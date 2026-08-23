using Auxim.Core.Config;

namespace Auxim.Core.Agent;

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        AuximConfig config,
        AgentOptions options,
        string message,
        IReadOnlyList<AgentMessage>? history = null,
        CancellationToken cancellationToken = default);
}
