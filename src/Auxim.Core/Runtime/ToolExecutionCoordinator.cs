using Auxim.Core.Approval;
using Auxim.Core.Resources;
using Auxim.Core.Tools;

namespace Auxim.Core.Runtime;

internal sealed class ToolExecutionCoordinator
{
    private readonly ToolRegistry _tools;
    private readonly ToolApprovalService _approvals;
    private readonly IApprovalHandler _approvalHandler;
    private readonly IRuntimeEventSink _eventSink;

    public ToolExecutionCoordinator(
        ToolRegistry tools,
        string homeDirectory,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink)
    {
        _tools = tools;
        _approvals = new ToolApprovalService(homeDirectory);
        _approvalHandler = approvalHandler;
        _eventSink = eventSink;
    }

    public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments) =>
        _tools.Get(toolName).ResolveResourceAccesses(arguments);

    public async Task<ToolExecutionResult> ExecuteAsync(
        AuximRunId runId,
        string toolCallId,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var definition = _tools.Get(toolName);
        var accesses = definition.ResolveResourceAccesses(arguments);
        await _eventSink.PublishAsync(
            new RuntimeToolStartedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                toolCallId,
                toolName,
                accesses),
            cancellationToken);

        var approval = await _approvals.ReviewAsync(
            runId,
            toolName,
            arguments,
            accesses,
            _approvalHandler,
            _eventSink,
            cancellationToken);
        if (!approval.Approved)
        {
            await PublishCompletedAsync(runId, toolCallId, toolName, "denied", 0, cancellationToken);
            return ToolExecutionResult.Denied(toolName, approval.Reason);
        }

        try
        {
            var content = await definition.Handler(arguments, cancellationToken);
            await PublishCompletedAsync(
                runId,
                toolCallId,
                toolName,
                "succeeded",
                content.Length,
                cancellationToken);
            return ToolExecutionResult.Succeeded(toolName, content);
        }
        catch
        {
            await PublishCompletedAsync(runId, toolCallId, toolName, "failed", 0, cancellationToken);
            throw;
        }
    }

    private ValueTask PublishCompletedAsync(
        AuximRunId runId,
        string toolCallId,
        string toolName,
        string outcome,
        int outputLength,
        CancellationToken cancellationToken) =>
        _eventSink.PublishAsync(
            new RuntimeToolCompletedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                toolCallId,
                toolName,
                outcome,
                outputLength),
            cancellationToken);
}

internal sealed record ToolExecutionResult(
    string ToolName,
    string Content,
    bool WasDenied,
    string Feedback)
{
    public static ToolExecutionResult Succeeded(string toolName, string content) =>
        new(toolName, content, false, "");

    public static ToolExecutionResult Denied(string toolName, string feedback) =>
        new(toolName, "", true, feedback);
}
