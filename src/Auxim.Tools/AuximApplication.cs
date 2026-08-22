using Auxim.Core.Runtime;

namespace Auxim.Tools;

public static class AuximApplication
{
    public static IAuximRuntime CreateRuntime() =>
        new AuximRuntimeService(
            DefaultAgentClientFactory.Create,
            BuiltInTools.CreateDefaultRegistry);
}
