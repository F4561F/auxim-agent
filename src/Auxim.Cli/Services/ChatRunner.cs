using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Cli.Interactive;
using Auxim.Tools;

namespace Auxim.Cli.Services;

public sealed class ChatRunner
{
    private readonly Action<ToolEvent>? _toolEventSink;
    private readonly Action<string>? _contentDeltaSink;

    public ChatRunner(
        Action<ToolEvent>? toolEventSink = null,
        Action<string>? contentDeltaSink = null)
    {
        _toolEventSink = toolEventSink;
        _contentDeltaSink = contentDeltaSink;
    }

    public async Task<AgentResult> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var config = ConfigLoader.Load();
        var options = new AgentOptions
        {
            Provider = config.Model.Provider,
            Model = config.Model.Name,
            MaxIterations = config.Agent.MaxIterations,
            ToolEventSink = _toolEventSink,
            ContentDeltaSink = _contentDeltaSink,
            ApprovalPrompt = ApprovalRenderer.Prompt,
        };

        var sessions = new SessionStore();
        var session = sessions.GetOrCreateCurrent();
        var agent = new AuximAgent(
            AgentClientFactory.Create(config),
            BuiltInTools.CreateDefaultRegistry(),
            options);
        var result = await agent.RunConversationAsync(prompt, session.Messages, cancellationToken);
        sessions.AppendTurn(session, prompt, result.FinalResponse);
        return result;
    }
}
