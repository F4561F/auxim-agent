using Auxim.Core.Agent;
using Auxim.Core.Runtime;

namespace Auxim.Tools;

public static class AuximApplication
{
    public static IAuximRuntime CreateRuntime() =>
        new AuximRuntimeService(
            new AuximAgentRunner(
                DefaultAgentClientFactory.Create,
                BuiltInTools.CreateDefaultRegistry),
            BuiltInTools.CreateDefaultRegistry);
}
