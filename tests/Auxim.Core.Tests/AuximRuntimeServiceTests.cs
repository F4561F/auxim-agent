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
    public async Task ChatAsyncUsesInjectedAgentRunner()
    {
        var runner = new FakeAgentRunner();
        var approvalHandler = new RecordingApprovalHandler(remember: false);
        var events = new List<RuntimeEvent>();
        var config = new AuximConfig
        {
            Model = new ModelConfig { Provider = "fake-provider", Name = "fake-model" },
            Agent = new AgentConfig { MaxIterations = 7 },
        };
        using var cancellation = new CancellationTokenSource();
        var runtime = new AuximRuntimeService(
            runner,
            CreateToolRegistry,
            () => new SessionStore(_home),
            () => config,
            () => _home);

        var result = await runtime.ChatAsync(
            new AuximChatRequest("run through fake"),
            new AuximRuntimeOptions
            {
                ApprovalHandler = approvalHandler,
                EventSink = new DelegateRuntimeEventSink((runtimeEvent, _) =>
                {
                    events.Add(runtimeEvent);
                    return ValueTask.CompletedTask;
                }),
            },
            cancellation.Token);

        Assert.Equal("fake response", result.FinalResponse);
        Assert.NotNull(runner.Request);
        Assert.Equal("run through fake", runner.Request.UserInput);
        Assert.Equal(result.RunId, runner.Request.RunId);
        Assert.Equal(result.SessionId, runner.Request.SessionId);
        Assert.Equal(_home, runner.Request.HomeDirectory);
        Assert.Same(config, runner.Request.Configuration);
        Assert.Empty(runner.Request.SessionContext);
        Assert.Same(approvalHandler, runner.Request.ApprovalHandler);
        Assert.NotNull(runner.Request.EventSink);
        Assert.Equal(cancellation.Token, runner.CancellationToken);
        Assert.Contains(events, runtimeEvent =>
            runtimeEvent is RuntimeRunStartedEvent started && started.RunId == result.RunId);
        Assert.Contains(events, runtimeEvent =>
            runtimeEvent is RuntimeRunCompletedEvent completed && completed.RunId == result.RunId);

        var session = Assert.Single(new SessionStore(_home).List());
        var document = new SessionStore(_home).TryLoad(session.Id);
        Assert.Equal("fake response", document!.Messages.Last().Content);
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
            CreateAgentRunner(),
            CreateToolRegistry,
            () => new SessionStore(_home),
            () => config ?? new AuximConfig(),
            () => _home);

    private AuximRuntimeService CreatePersistentRuntime()
    {
        var path = Path.Combine(_home, "config.json");
        return new AuximRuntimeService(
            CreateAgentRunner(),
            CreateToolRegistry,
            () => new SessionStore(_home),
            () => ConfigLoader.Load(path),
            () => _home);
    }

    private AuximRuntimeService CreateProtectedRuntime() =>
        new(
            CreateAgentRunner(CreateProtectedToolRegistry),
            CreateProtectedToolRegistry,
            () => new SessionStore(_home),
            () => new AuximConfig(),
            () => _home);

    private static IAgentRunner CreateAgentRunner(Func<ToolRegistry>? toolRegistryFactory = null) =>
        new AuximAgentRunner(
            _ => new EchoAgentClient(),
            toolRegistryFactory ?? CreateToolRegistry);

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

    private sealed class FakeAgentRunner : IAgentRunner
    {
        public AgentRunRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<AgentResult> RunAsync(
            AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(new AgentResult(
                "fake response",
                [.. request.SessionContext, new AgentMessage("user", request.UserInput), new AgentMessage("assistant", "fake response")]));
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
