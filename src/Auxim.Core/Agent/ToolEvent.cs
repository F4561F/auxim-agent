namespace Auxim.Core.Agent;

public sealed record ToolEvent(
    string Kind,
    string Name,
    string Detail);
