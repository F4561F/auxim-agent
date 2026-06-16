using Auxim.Core.Agent;
using Auxim.Core.Approval;

namespace Auxim.Core.Runtime;

public sealed class AuximRuntimeOptions
{
    public Action<ToolEvent>? ToolEventSink { get; init; }
    public Action<string>? ContentDeltaSink { get; init; }
    public ApprovalUIPrompt? ApprovalPrompt { get; init; }
}
