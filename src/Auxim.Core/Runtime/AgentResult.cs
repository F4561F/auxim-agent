namespace Auxim.Core.Runtime;

public sealed record AgentResult(string FinalResponse, IReadOnlyList<AgentMessage> Messages);
