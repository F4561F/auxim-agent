using Auxim.Core.Agent;
using Auxim.Core.Approval;
using Auxim.Core.Config;
using Auxim.Core.Resources;
using Auxim.Core.Runtime;
using Auxim.Core.State;
using Auxim.Core.Tools;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class AuximRuntimeServiceTests : IDisposable
{
    private readonly string _home;

    public AuximRuntimeServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "auxim-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    [Fact]
    public async Task ChatAsyncRunsAgentAndAppendsSession()
    {
        var runtime = CreateRuntime();

        var result = await runtime.ChatAsync(new AuximChatRequest("hello runtime"));

        Assert.Contains("hello runtime", result.FinalResponse);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionId));

        var store = new SessionStore(_home);
        var session = Assert.Single(store.List());
        var document = store.TryLoad(session.Id);
        Assert.NotNull(document);
        Assert.Equal(2, document!.Messages.Count);
    }

    [Fact]
    public async Task RuntimeOwnsStatusAndToolOperations()
    {
        var runtime = CreateRuntime(new AuximConfig
        {
            Model = new ModelConfig
            {
                Provider = "test-provider",
                Name = "test-model",
                BaseUrl = "https://example.test/v1",
            },
            Agent = new AgentConfig { MaxIterations = 12 },
            Sandbox = new SandboxConfig
            {
                Workspace = "/workspace",
                Mounts = [new SandboxMountConfig { Name = "data", HostPath = "/data" }],
            },
        });

        var status = runtime.GetStatus();
        var tool = Assert.Single(runtime.ListTools());
        var result = await runtime.InvokeToolAsync(
            "echo",
            new Dictionary<string, object?> { ["text"] = "hello" });

        Assert.Equal(_home, status.HomeDirectory);
        Assert.Equal("test-provider", status.ModelProvider);
        Assert.Equal("test-model", status.ModelName);
        Assert.Equal(12, status.MaxIterations);
        Assert.Equal("/workspace", status.Workspace);
        Assert.Equal(1, status.MountCount);
        Assert.Equal("echo", tool.Name);
        Assert.Equal("hello", result);
        Assert.True(File.Exists(Path.Combine(_home, "logs", "agent.log")));
    }

    [Fact]
    public async Task RuntimeOwnsSessionLifecycleAndSearch()
    {
        var runtime = CreateRuntime();
        var background = runtime.CreateSession("external conversation", makeCurrent: false);

        Assert.False(background.IsCurrent);
        Assert.Empty(runtime.GetStatus().CurrentSessionId);

        await runtime.ChatAsync(new AuximChatRequest(
            "searchable message",
            UseCurrentSession: false,
            AppendToSession: true,
            SessionId: background.Id));

        var current = runtime.CreateSession("current conversation");
        var records = runtime.ListSessions();
        var match = Assert.Single(runtime.SearchSessions("searchable"));

        Assert.True(current.IsCurrent);
        Assert.Equal(2, records.Count);
        Assert.Equal(background.Id, match.Id);
        Assert.True(runtime.GetSession(current.Id)?.IsCurrent);
        Assert.True(runtime.UseSession(background.Id)?.IsCurrent);

        runtime.ClearCurrentSession();
        Assert.Empty(runtime.GetStatus().CurrentSessionId);
    }

    [Fact]
    public void RuntimeOwnsConfigurationAndInputHistory()
    {
        var runtime = CreatePersistentRuntime();

        var updated = runtime.SetModelSettings(
            "test-provider",
            "test-model",
            "https://example.test/v1/");
        runtime.SetConfigValue("agent.maxIterations", "17");
        runtime.SaveInputHistory(["first", "second"]);

        Assert.Equal("test-provider", updated.Provider);
        Assert.Equal("test-model", runtime.GetModelSettings().Model);
        Assert.Equal("https://example.test/v1", runtime.GetModelSettings().BaseUrl);
        Assert.Contains("\"maxIterations\": 17", runtime.GetConfigJson());
        Assert.Equal(["first", "second"], runtime.LoadInputHistory());
        Assert.StartsWith(_home, runtime.GetApplicationPaths().ConfigPath);
    }

    [Fact]
    public async Task RuntimeOwnsExternalConversationMapping()
    {
        var runtime = CreatePersistentRuntime();
        var request = new AuximExternalMessageRequest(
            "telegram",
            "group-1",
            "user-1",
            "hello external");

        var first = await runtime.SendExternalMessageAsync(request);
        var second = await runtime.SendExternalMessageAsync(request with { Text = "again" });
        var conversation = Assert.Single(runtime.ListExternalConversations());

        Assert.Equal(first.ConversationKey, second.ConversationKey);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(first.SessionId, conversation.SessionId);
        Assert.Contains("hello external", first.FinalResponse);
    }

    [Fact]
    public async Task ToolApprovalUsesUniqueRequestsResourcesAndOneEventStream()
    {
        var runtime = CreateProtectedRuntime();
        var handler = new RecordingApprovalHandler(remember: true);
        var events = new List<RuntimeEvent>();
        var options = new AuximRuntimeOptions
        {
            ApprovalHandler = handler,
            EventSink = new DelegateRuntimeEventSink((runtimeEvent, _) =>
            {
                events.Add(runtimeEvent);
                return ValueTask.CompletedTask;
            }),
        };

        await runtime.InvokeToolAsync(
            "protected.write",
            new Dictionary<string, object?> { ["path"] = "/workspace/one.txt" },
            options);
        await runtime.InvokeToolAsync(
            "protected.write",
            new Dictionary<string, object?> { ["path"] = "/workspace/one.txt" },
            options);
        await runtime.InvokeToolAsync(
            "protected.write",
            new Dictionary<string, object?> { ["path"] = "/workspace/two.txt" },
            options);

        Assert.Equal(2, handler.Requests.Count);
        Assert.NotEqual(handler.Requests[0].RequestId, handler.Requests[1].RequestId);
        Assert.Equal("vafs:/workspace/one.txt", handler.Requests[0].ResourceAccesses.Single().Resource.Value);
        Assert.Equal("vafs:/workspace/two.txt", handler.Requests[1].ResourceAccesses.Single().Resource.Value);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeToolStartedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeApprovalRequestedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeApprovalResolvedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeToolCompletedEvent { Outcome: "succeeded" });
        Assert.Equal(2, runtime.ListApprovalGrants().Count);
    }

    [Fact]
    public async Task ApprovalHandlerReceivesCancellation()
    {
        var runtime = CreateProtectedRuntime();
        var handler = new BlockingApprovalHandler();
        using var cancellation = new CancellationTokenSource();
        var invocation = runtime.InvokeToolAsync(
            "protected.write",
            new Dictionary<string, object?> { ["path"] = "/workspace/cancelled.txt" },
            new AuximRuntimeOptions { ApprovalHandler = handler },
            cancellation.Token);

        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
        Assert.True(handler.ObservedToken.CanBeCanceled);
    }

    private AuximRuntimeService CreateRuntime(AuximConfig? config = null) =>
        new(
            _ => new EchoAgentClient(),
            CreateToolRegistry,
            () => new SessionStore(_home),
            () => config ?? new AuximConfig(),
            () => _home);

    private AuximRuntimeService CreatePersistentRuntime()
    {
        var path = Path.Combine(_home, "config.json");
        return new AuximRuntimeService(
            _ => new EchoAgentClient(),
            CreateToolRegistry,
            () => new SessionStore(_home),
            () => ConfigLoader.Load(path),
            () => _home);
    }

    private AuximRuntimeService CreateProtectedRuntime() =>
        new(
            _ => new EchoAgentClient(),
            CreateProtectedToolRegistry,
            () => new SessionStore(_home),
            () => new AuximConfig(),
            () => _home);

    private static ToolRegistry CreateToolRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition(
            "echo",
            "test",
            "Returns text.",
            (arguments, _) => Task.FromResult(arguments["text"]?.ToString() ?? "")));
        return registry;
    }

    private static ToolRegistry CreateProtectedToolRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition(
            "protected.write",
            "test",
            "Writes a protected resource.",
            (_, _) => Task.FromResult("written"))
        {
            ResourceAccessResolver = arguments =>
                [new ResourceAccess(
                    ResourceAction.Write,
                    ResourceUri.Vafs(arguments["path"]?.ToString() ?? ""),
                    RequiresApproval: true)],
        });
        return registry;
    }

    private sealed class RecordingApprovalHandler(bool remember) : IApprovalHandler
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public Task<ApprovalResponse> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(ApprovalResponse.Allow(remember));
        }
    }

    private sealed class BlockingApprovalHandler : IApprovalHandler
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        public async Task<ApprovalResponse> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ApprovalResponse.Allow();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
