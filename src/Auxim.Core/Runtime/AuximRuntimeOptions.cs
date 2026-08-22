using Auxim.Core.Approval;

namespace Auxim.Core.Runtime;

public sealed class AuximRuntimeOptions
{
    public IApprovalHandler? ApprovalHandler { get; init; }

    public IRuntimeEventSink? EventSink { get; init; }
}
