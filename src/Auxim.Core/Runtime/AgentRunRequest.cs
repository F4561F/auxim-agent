using Auxim.Core.Approval;
using Auxim.Core.Config;

namespace Auxim.Core.Runtime;

public sealed record AgentRunRequest(
    AuximRunId RunId,
    string SessionId,
    string UserInput,
    IReadOnlyList<AgentMessage> SessionContext,
    AuximConfig Configuration,
    string HomeDirectory,
    string EnvironmentDescription,
    IApprovalHandler ApprovalHandler,
    IRuntimeEventSink EventSink);
