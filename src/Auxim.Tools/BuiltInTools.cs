using Auxim.Core.Tools;
using Auxim.Core.Plugins;

namespace Auxim.Tools;

public static class BuiltInTools
{
    public static ToolRegistry CreateDefaultRegistry()
    {
        var registry = new ToolRegistry();
        RegisterCoreTools(registry);
        return registry;
    }

    public static void RegisterCoreTools(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "time.now",
            "core",
            "Returns the current local time.",
            (_, _) => Task.FromResult(DateTimeOffset.Now.ToString("O"))));

        registry.Register(new ToolDefinition(
            "echo",
            "core",
            "Returns the provided text argument.",
            (args, _) => Task.FromResult(args.TryGetValue("text", out var text) ? text?.ToString() ?? "" : ""))
        {
            ParametersSchema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["text"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "Text to echo back.",
                    },
                },
                ["required"] = new[] { "text" },
                ["additionalProperties"] = false,
            },
        });
        FileTools.Register(registry);
        SearchTools.Register(registry);
        GitTools.Register(registry);
        WebTools.Register(registry);
        ShellTools.Register(registry);
        TodoTools.Register(registry);
        PluginLoader.DiscoverAndRegister(registry);
    }
}
