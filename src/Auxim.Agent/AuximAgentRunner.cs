using Auxim.Core.Config;
using Auxim.Core.Runtime;
using Auxim.Core.Tools;

namespace Auxim.Agent;

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
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = new AgentOptions
        {
            Provider = request.Configuration.Model.Provider,
            Model = request.Configuration.Model.Name,
            MaxIterations = request.Configuration.Agent.MaxIterations,
            RunId = request.RunId,
            HomeDirectory = request.HomeDirectory,
            ApprovalHandler = request.ApprovalHandler,
            EventSink = request.EventSink,
        };
        var agent = new AuximAgent(
            _agentClientFactory(request.Configuration),
            _toolRegistryFactory(),
            options);
        return agent.RunConversationAsync(
            request.UserInput,
            request.SessionContext,
            cancellationToken);
    }
}
