using Auxim.Core.Runtime;

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

public interface IStreamingToolCallingAgentClient : IToolCallingAgentClient
{
    Task<AgentClientResponse> CompleteWithToolsStreamingAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<Tools.ToolDefinition> tools,
        Func<string, CancellationToken, ValueTask> contentDeltaSink,
        CancellationToken cancellationToken);
}
