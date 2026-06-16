using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Core.Tools;

namespace Auxim.Core.Runtime;

public sealed class AuximRuntimeService : IAuximRuntime
{
    private readonly Func<AuximConfig, IAgentClient> _agentClientFactory;
    private readonly Func<ToolRegistry> _toolRegistryFactory;
    private readonly Func<SessionStore> _sessionStoreFactory;
    private readonly Func<AuximConfig> _configLoader;

    public AuximRuntimeService(
        Func<AuximConfig, IAgentClient> agentClientFactory,
        Func<ToolRegistry> toolRegistryFactory,
        Func<SessionStore>? sessionStoreFactory = null,
        Func<AuximConfig>? configLoader = null)
    {
        _agentClientFactory = agentClientFactory;
        _toolRegistryFactory = toolRegistryFactory;
        _sessionStoreFactory = sessionStoreFactory ?? (() => new SessionStore());
        _configLoader = configLoader ?? (() => ConfigLoader.Load());
    }

    public async Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var config = _configLoader();
        var agentOptions = new AgentOptions
        {
            Provider = config.Model.Provider,
            Model = config.Model.Name,
            MaxIterations = config.Agent.MaxIterations,
            ToolEventSink = options?.ToolEventSink,
            ContentDeltaSink = options?.ContentDeltaSink,
            ApprovalPrompt = options?.ApprovalPrompt,
        };

        var sessions = _sessionStoreFactory();
        var session = request.UseCurrentSession
            ? sessions.GetOrCreateCurrent()
            : sessions.NewSession();
        var agent = new AuximAgent(
            _agentClientFactory(config),
            _toolRegistryFactory(),
            agentOptions);

        var result = await agent.RunConversationAsync(request.Prompt, session.Messages, cancellationToken);
        if (request.AppendToSession)
        {
            sessions.AppendTurn(session, request.Prompt, result.FinalResponse);
        }

        return new AuximChatResult(result.FinalResponse, result.Messages, session.Id);
    }
}
