using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleAuth(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        var subcommand = args.FirstOrDefault() ?? "status";
        return subcommand switch
        {
            "path" => PrintPath(runtime.GetApplicationPaths().SecretsPath),
            "status" => AuthStatus(runtime),
            "set-api-key" => SetApiKey(args.Skip(1).FirstOrDefault(), runtime),
            _ => PrintAuthHelp(),
        };
    }

    private static int SetApiKey(string? key, IAuximRuntime runtime)
    {
        var status = runtime.GetCredentialStatus();
        if (!status.Required)
        {
            Console.WriteLine($"{status.Provider} does not require an API key by default.");
            return 0;
        }

        key = string.IsNullOrWhiteSpace(key) ? ReadSecret("API key: ") : key;
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("No API key provided.");
            return 1;
        }

        runtime.SetApiKey(status.Provider, key);
        Console.WriteLine($"{status.EnvironmentVariable} saved to {status.SecretsPath}");
        return 0;
    }

    private static int AuthStatus(IAuximRuntime runtime)
    {
        var status = runtime.GetCredentialStatus();
        Console.WriteLine($"Env file: {status.SecretsPath}");
        Console.WriteLine($"Provider: {status.Provider}");
        Console.WriteLine($"API key:  {FormatApiKeyStatus(status)}");
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
