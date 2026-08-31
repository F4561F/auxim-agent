using Auxim.Core.Runtime;
using Auxim.Core.Tools;
using Auxim.Tools;

namespace Auxim.Agent;

public static class AuximApplication
{
    public static IAuximRuntime CreateRuntime()
    {
        Func<ToolRegistry> tools = BuiltInTools.CreateDefaultRegistry;
        var runtimeTools = new RuntimeToolService(tools);
        var agentRunner = new AuximAgentRunner(
            DefaultAgentClientFactory.Create,
            tools,
            runtimeTools);
        return new AuximRuntimeService(agentRunner, runtimeTools);
    }
}
