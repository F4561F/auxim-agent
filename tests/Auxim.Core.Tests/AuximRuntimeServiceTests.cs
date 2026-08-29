using Auxim.Core.Approval;
using Auxim.Core.Config;
using Auxim.Core.Resources;
using Auxim.Core.Runtime;
using Auxim.Core.State;
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
    public async Task ChatAsyncRunsInjectedRunnerAndAppendsSession()
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
        var runner = new FakeAgentRunner("fake response");
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
            sessionStoreFactory: () => new SessionStore(_home),
            configLoader: () => config,
            homeDirectory: () => _home);

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
        Assert.Empty(runtime.ListTools());
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
        var runtime = CreateRuntime(
            new AuximConfig
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
            },
            new FakeRuntimeToolService());

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

    private AuximRuntimeService CreateRuntime(
        AuximConfig? config = null,
        IRuntimeToolService? tools = null) =>
        new(
            new FakeAgentRunner(),
            tools,
            () => new SessionStore(_home),
            () => config ?? new AuximConfig(),
            () => _home);

    private AuximRuntimeService CreatePersistentRuntime()
    {
        var path = Path.Combine(_home, "config.json");
        return new AuximRuntimeService(
            new FakeAgentRunner(),
            sessionStoreFactory: () => new SessionStore(_home),
            configLoader: () => ConfigLoader.Load(path),
            homeDirectory: () => _home);
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

    private sealed class FakeRuntimeToolService : IRuntimeToolService
    {
        public IReadOnlyList<AuximRuntimeTool> ListTools() =>
            [new AuximRuntimeTool(
                "echo",
                "echo",
                "test",
                "Returns text.",
                new Dictionary<string, object?>(),
                false)];

        public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
            string name,
            IReadOnlyDictionary<string, object?> arguments) => [];

        public async Task<string> InvokeAsync(
            AuximRunId runId,
            string toolCallId,
            string name,
            IReadOnlyDictionary<string, object?> arguments,
            string homeDirectory,
            IApprovalHandler approvalHandler,
            IRuntimeEventSink eventSink,
            CancellationToken cancellationToken)
        {
            await eventSink.PublishAsync(
                new RuntimeToolStartedEvent(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    runId,
                    toolCallId,
                    name,
                    []),
                cancellationToken);
            var result = arguments["text"]?.ToString() ?? "";
            await eventSink.PublishAsync(
                new RuntimeToolCompletedEvent(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    runId,
                    toolCallId,
                    name,
                    "succeeded",
                    result.Length),
                cancellationToken);
            return result;
        }
    }

    private sealed class FakeAgentRunner(string? response = null) : IAgentRunner
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
            var finalResponse = response ?? $"Auxim received: {request.UserInput}";
            return Task.FromResult(new AgentResult(
                finalResponse,
                [.. request.SessionContext, new AgentMessage("user", request.UserInput), new AgentMessage("assistant", finalResponse)]));
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
