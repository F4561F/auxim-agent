using Auxim.Core.Approval;
using Auxim.Core.Resources;

namespace Auxim.Core.Runtime;

public readonly record struct AuximRunId(string Value)
{
    public static AuximRunId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

public abstract record RuntimeEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId)
{
    public abstract string Kind { get; }
}

public sealed record RuntimeRunStartedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string SessionId) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "run.started";
}

public sealed record RuntimeRunCompletedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string SessionId,
    string FinalResponse) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "run.completed";
}

public sealed record RuntimeRunFailedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string SessionId,
    string Error) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "run.failed";
}

public sealed record RuntimeContentDeltaEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string Delta) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "content.delta";
}

public sealed record RuntimeToolStartedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string ToolCallId,
    string ToolName,
    IReadOnlyList<ResourceAccess> ResourceAccesses) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "tool.started";
}

public sealed record RuntimeToolCompletedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string ToolCallId,
    string ToolName,
    string Outcome,
    int OutputLength) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "tool.completed";
}

public sealed record RuntimeApprovalRequestedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    ApprovalRequest Request) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "approval.requested";
}

public sealed record RuntimeApprovalResolvedEvent(
    string EventId,
    DateTimeOffset OccurredAt,
    AuximRunId RunId,
    string RequestId,
    bool Approved,
    bool Remembered,
    string Reason) : RuntimeEvent(EventId, OccurredAt, RunId)
{
    public override string Kind => "approval.resolved";
}

internal static class RuntimeEventFactory
{
    public static string NewEventId() => Guid.NewGuid().ToString("N");

    public static DateTimeOffset Now() => DateTimeOffset.UtcNow;
}
