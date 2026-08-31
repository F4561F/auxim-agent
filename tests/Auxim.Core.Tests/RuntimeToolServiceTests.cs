using Auxim.Core.Approval;
using Auxim.Core.Resources;
using Auxim.Core.Runtime;
using Auxim.Core.Tools;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class RuntimeToolServiceTests : IDisposable
{
    private readonly string _home;

    public RuntimeToolServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "auxim-runtime-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    [Fact]
    public async Task ToolApprovalUsesUniqueRequestsResourcesAndOneEventStream()
    {
        var tools = new RuntimeToolService(CreateProtectedToolRegistry);
        var handler = new RecordingApprovalHandler(remember: true);
        var events = new List<RuntimeEvent>();
        var eventSink = new DelegateRuntimeEventSink((runtimeEvent, _) =>
        {
            events.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        });

        await InvokeAsync(tools, "/workspace/one.txt", handler, eventSink);
        await InvokeAsync(tools, "/workspace/one.txt", handler, eventSink);
        await InvokeAsync(tools, "/workspace/two.txt", handler, eventSink);

        Assert.Equal(2, handler.Requests.Count);
        Assert.NotEqual(handler.Requests[0].RequestId, handler.Requests[1].RequestId);
        Assert.Equal("vafs:/workspace/one.txt", handler.Requests[0].ResourceAccesses.Single().Resource.Value);
        Assert.Equal("vafs:/workspace/two.txt", handler.Requests[1].ResourceAccesses.Single().Resource.Value);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeToolStartedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeApprovalRequestedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeApprovalResolvedEvent);
        Assert.Contains(events, runtimeEvent => runtimeEvent is RuntimeToolCompletedEvent { Outcome: "succeeded" });
        Assert.Equal(2, new ToolApprovalService(_home).ListGrants().Count);
    }

    [Fact]
    public async Task ApprovalHandlerReceivesCancellation()
    {
        var tools = new RuntimeToolService(CreateProtectedToolRegistry);
        var handler = new BlockingApprovalHandler();
        using var cancellation = new CancellationTokenSource();
        var invocation = InvokeAsync(
            tools,
            "/workspace/cancelled.txt",
            handler,
            NullEventSink.Instance,
            cancellation.Token);

        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
        Assert.True(handler.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task DeniedApprovalThrowsBoundaryExceptionAndPublishesDeniedEvents()
    {
        var tools = new RuntimeToolService(CreateProtectedToolRegistry);
        var handler = new DenyingApprovalHandler("not approved");
        var events = new List<RuntimeEvent>();
        var eventSink = new DelegateRuntimeEventSink((runtimeEvent, _) =>
        {
            events.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        });

        var exception = await Assert.ThrowsAsync<ToolApprovalDeniedException>(() =>
            InvokeAsync(tools, "/workspace/denied.txt", handler, eventSink));

        Assert.Equal("protected.write", exception.ToolName);
        Assert.Equal("not approved", exception.Reason);
        Assert.Equal(
            ["tool.started", "approval.requested", "approval.resolved", "tool.completed"],
            events.Select(runtimeEvent => runtimeEvent.Kind));
        Assert.Contains(events, runtimeEvent =>
            runtimeEvent is RuntimeApprovalResolvedEvent
            {
                Approved: false,
                Reason: "not approved",
            });
        Assert.Contains(events, runtimeEvent =>
            runtimeEvent is RuntimeToolCompletedEvent { Outcome: "denied", OutputLength: 0 });
    }

    private Task<string> InvokeAsync(
        IRuntimeToolService tools,
        string path,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink,
        CancellationToken cancellationToken = default) =>
        tools.InvokeAsync(
            AuximRunId.New(),
            $"test:{Guid.NewGuid():N}",
            "protected.write",
            new Dictionary<string, object?> { ["path"] = path },
            _home,
            approvalHandler,
            eventSink,
            cancellationToken);

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

    private sealed class DenyingApprovalHandler(string reason) : IApprovalHandler
    {
        public Task<ApprovalResponse> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApprovalResponse.Deny(reason));
        }
    }

    private sealed class NullEventSink : IRuntimeEventSink
    {
        public static NullEventSink Instance { get; } = new();

        public ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
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
