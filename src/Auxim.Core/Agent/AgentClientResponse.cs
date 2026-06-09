namespace Auxim.Core.Agent;

public sealed record AgentClientResponse(
    string Content,
    IReadOnlyList<ToolCallRequest> ToolCalls)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}

public interface IToolCallingAgentClient : IAgentClient
{
    Task<AgentClientResponse> CompleteWithToolsAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<Tools.ToolDefinition> tools,
        CancellationToken cancellationToken);
}
