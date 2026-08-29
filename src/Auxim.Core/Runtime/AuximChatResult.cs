namespace Auxim.Core.Runtime;

public sealed record AuximChatResult(
    string FinalResponse,
    IReadOnlyList<AgentMessage> Messages,
    string SessionId,
    AuximRunId RunId);
