using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleConfig(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "path" => PrintPath(runtime.GetApplicationPaths().ConfigPath),
            "show" => ShowConfig(runtime),
            "set" => SetConfig(args.Skip(1).ToArray(), runtime),
            _ => PrintConfigHelp(),
        };
    }

    private static int ShowConfig(IAuximRuntime runtime)
    {
        Console.WriteLine(runtime.GetConfigJson());
        return 0;
    }

    private static int SetConfig(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        if (args.Count < 2)
        {
            return PrintConfigHelp();
        }

        runtime.SetConfigValue(args[0], string.Join(' ', args.Skip(1)));
        Console.WriteLine($"Saved {args[0]} to {runtime.GetApplicationPaths().ConfigPath}");
        return 0;
    }

    private static int PrintConfigHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim config show");
        Console.WriteLine("  auxim config path");
        Console.WriteLine("  auxim config set <key.path> <value>");
        return 1;
    }
}
