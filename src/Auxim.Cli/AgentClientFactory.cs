using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static class AgentClientFactory
{
    public static IAgentClient Create(AuximConfig config) =>
        DefaultAgentClientFactory.Create(config);
}
