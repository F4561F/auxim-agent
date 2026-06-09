using Auxim.Core.Config;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleAuth(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "status";
        return subcommand switch
        {
            "path" => PrintPath(ConfigLoader.GetEnvPath()),
            "status" => AuthStatus(),
            "set-api-key" => SetApiKey(args.Skip(1).FirstOrDefault()),
            _ => PrintAuthHelp(),
        };
    }

    private static int SetApiKey(string? key)
    {
        var config = ConfigLoader.Load();
        var keyName = ApiKeyNameForProvider(config.Model.Provider);
        if (!RequiresApiKey(config.Model.Provider))
        {
            Console.WriteLine($"{config.Model.Provider} does not require an API key by default.");
            return 0;
        }

        key = string.IsNullOrWhiteSpace(key) ? ReadSecret("API key: ") : key;
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("No API key provided.");
            return 1;
        }

        DotEnvStore.SetValue(keyName, key);
        Console.WriteLine($"{keyName} saved to {ConfigLoader.GetEnvPath()}");
        return 0;
    }

    private static int AuthStatus()
    {
        var config = ConfigLoader.Load();
        var keyName = ApiKeyNameForProvider(config.Model.Provider);
        Console.WriteLine($"Env file: {ConfigLoader.GetEnvPath()}");
        Console.WriteLine($"Provider: {config.Model.Provider}");
        Console.WriteLine($"API key:  {FormatApiKeyStatus(config.Model.Provider, keyName)}");
        return 0;
    }

    private static int PrintAuthHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim auth status");
        Console.WriteLine("  auxim auth path");
        Console.WriteLine("  auxim auth set-api-key [key]");
        return 1;
    }
}
