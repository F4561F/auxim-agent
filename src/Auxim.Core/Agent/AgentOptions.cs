using Auxim.Core.Approval;
using Auxim.Core.Runtime;

namespace Auxim.Core.Agent;

public sealed class AgentOptions
{
    public string Provider { get; init; } = "local";
    public string Model { get; init; } = "placeholder";
    public int MaxIterations { get; init; } = 90;
    public IReadOnlyList<string> EnabledToolsets { get; init; } = ["core"];
    public AuximRunId RunId { get; init; } = AuximRunId.New();
    public string HomeDirectory { get; init; } = "";
    public IApprovalHandler ApprovalHandler { get; init; } = NonInteractiveApprovalHandler.Instance;
    public IRuntimeEventSink? EventSink { get; init; }
}
