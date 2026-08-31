using Auxim.Core.Runtime;
using Auxim.Core.Tools;
using Auxim.Tools;

namespace Auxim.Agent;

public static class AuximApplication
{
    public static IAuximRuntime CreateRuntime()
    {
        Func<ToolRegistry> tools = BuiltInTools.CreateDefaultRegistry;
        var agentRunner = new AuximAgentRunner(DefaultAgentClientFactory.Create, tools);
        var runtimeTools = new RuntimeToolService(tools);
        return new AuximRuntimeService(agentRunner, runtimeTools);
    }
}
