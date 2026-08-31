using Auxim.Core.Runtime;

namespace Auxim.Agent;

public interface IAgentClient
{
    Task<string> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken);
}
