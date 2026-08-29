namespace Auxim.Core.Runtime;

public sealed record AuximRuntimeSessionSummary(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent);

public sealed record AuximRuntimeSession(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent,
    IReadOnlyList<AgentMessage> Messages);
