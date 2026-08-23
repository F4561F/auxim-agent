using Auxim.Core.Runtime;

namespace Auxim.Core.Agent;

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);
}
