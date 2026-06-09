namespace Auxim.Core.Agent;

public sealed record AgentResult(string FinalResponse, IReadOnlyList<AgentMessage> Messages);
