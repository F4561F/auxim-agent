using Auxim.Core.Approval;

namespace Auxim.Core.Agent;

public sealed class AgentOptions
{
    public string Provider { get; init; } = "local";
    public string Model { get; init; } = "placeholder";
    public int MaxIterations { get; init; } = 90;
    public IReadOnlyList<string> EnabledToolsets { get; init; } = ["core"];
    public Action<ToolEvent>? ToolEventSink { get; init; }
    public Action<string>? ContentDeltaSink { get; init; }
    public ApprovalUIPrompt? ApprovalPrompt { get; init; }
}
