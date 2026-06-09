using System.Text.Json;
using Auxim.Core.Config;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleConfig(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "path" => PrintPath(ConfigLoader.GetConfigPath()),
            "show" => ShowConfig(),
            "set" => SetConfig(args.Skip(1).ToArray()),
            _ => PrintConfigHelp(),
        };
    }

    private static int ShowConfig()
    {
        var path = ConfigLoader.GetConfigPath();
        if (!File.Exists(path))
        {
            Console.WriteLine(JsonSerializer.Serialize(new AuximConfig(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
            return 0;
        }

        Console.WriteLine(File.ReadAllText(path));
        return 0;
    }

    private static int SetConfig(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return PrintConfigHelp();
        }

        ConfigLoader.SetValue(args[0], string.Join(' ', args.Skip(1)));
        Console.WriteLine($"Saved {args[0]} to {ConfigLoader.GetConfigPath()}");
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
