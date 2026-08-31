using Auxim.Core.Runtime;
using Auxim.Core.Tools;

namespace Auxim.Agent;

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
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken);
}

public interface IStreamingToolCallingAgentClient : IToolCallingAgentClient
{
    Task<AgentClientResponse> CompleteWithToolsStreamingAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        Func<string, CancellationToken, ValueTask> contentDeltaSink,
        CancellationToken cancellationToken);
}
