using Auxim.Core.Agent;

namespace Auxim.Core.Runtime;

public sealed record AuximChatResult(
    string FinalResponse,
    IReadOnlyList<AgentMessage> Messages,
    string SessionId)
{
    public AgentResult ToAgentResult() => new(FinalResponse, Messages);
}
