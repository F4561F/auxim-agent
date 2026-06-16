using Auxim.Core.Agent;
using Auxim.Core.Runtime;
using Auxim.Cli.Interactive;
using Auxim.Tools;

namespace Auxim.Cli.Services;

public sealed class ChatRunner
{
    private readonly IAuximRuntime _runtime;
    private readonly Action<ToolEvent>? _toolEventSink;
    private readonly Action<string>? _contentDeltaSink;

    public ChatRunner(
        Action<ToolEvent>? toolEventSink = null,
        Action<string>? contentDeltaSink = null,
        IAuximRuntime? runtime = null)
    {
        _runtime = runtime ?? new AuximRuntimeService(
            DefaultAgentClientFactory.Create,
            BuiltInTools.CreateDefaultRegistry);
        _toolEventSink = toolEventSink;
        _contentDeltaSink = contentDeltaSink;
    }

    public async Task<AgentResult> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var result = await _runtime.ChatAsync(
            new AuximChatRequest(prompt),
            new AuximRuntimeOptions
        {
            ToolEventSink = _toolEventSink,
            ContentDeltaSink = _contentDeltaSink,
            ApprovalPrompt = ApprovalRenderer.Prompt,
        },
            cancellationToken);
        return result.ToAgentResult();
    }
}
