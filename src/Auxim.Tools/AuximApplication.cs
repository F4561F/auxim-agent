using Auxim.Core.Agent;
using Auxim.Core.Runtime;
using Auxim.Core.Tools;

namespace Auxim.Tools;

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
