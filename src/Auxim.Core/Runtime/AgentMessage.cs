namespace Auxim.Core.Runtime;

public sealed record AgentMessage(string Role, string Content)
{
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<ToolCallRequest>? ToolCalls { get; init; }
}

public sealed record ToolCallRequest(string Id, string Name, string ArgumentsJson);
