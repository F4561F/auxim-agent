using Auxim.Core.Config;
using Auxim.Core.Tools;

namespace Auxim.Core.Agent;

public sealed class AuximAgentRunner : IAgentRunner
{
    private readonly Func<AuximConfig, IAgentClient> _agentClientFactory;
    private readonly Func<ToolRegistry> _toolRegistryFactory;

    public AuximAgentRunner(
        Func<AuximConfig, IAgentClient> agentClientFactory,
        Func<ToolRegistry> toolRegistryFactory)
    {
        _agentClientFactory = agentClientFactory;
        _toolRegistryFactory = toolRegistryFactory;
    }

    public Task<AgentResult> RunAsync(
        AuximConfig config,
        AgentOptions options,
        string message,
        IReadOnlyList<AgentMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var agent = new AuximAgent(
            _agentClientFactory(config),
            _toolRegistryFactory(),
            options);
        return agent.RunConversationAsync(message, history, cancellationToken);
    }
}
