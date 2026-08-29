namespace Auxim.Core.Runtime;

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);
}
