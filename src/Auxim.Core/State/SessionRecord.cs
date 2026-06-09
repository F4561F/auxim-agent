namespace Auxim.Core.State;

public sealed record SessionRecord(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Title);
