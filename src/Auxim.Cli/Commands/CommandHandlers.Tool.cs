using Auxim.Tools;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static async Task<int> HandleTool(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "list";
        var registry = BuiltInTools.CreateDefaultRegistry();
        switch (subcommand)
        {
            case "list":
                foreach (var tool in registry.List().OrderBy(tool => tool.Name))
                {
                    Console.WriteLine($"{tool.Name} [{tool.Toolset}] - {tool.Description}");
                }

                return 0;
            case "run":
                if (args.Count < 2)
                {
                    return PrintToolHelp();
                }

                var toolName = args[1];
                var toolArgs = ParseKeyValueArgs(args.Skip(2));
                Console.WriteLine(await registry.InvokeAsync(toolName, toolArgs));
                return 0;
            default:
                return PrintToolHelp();
        }
    }

    private static int PrintToolHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim tool list");
        Console.WriteLine("  auxim tool run <name> [key=value ...]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  auxim tool run echo text=hello");
        return 1;
    }
}
