using Auxim.Core.Config;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleModel(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "show" => ShowModel(),
            "set" => SetModel(args.Skip(1).ToArray()),
            _ => PrintModelHelp(),
        };
    }

    private static int ShowModel()
    {
        var config = ConfigLoader.Load();
        Console.WriteLine($"Provider: {config.Model.Provider}");
        Console.WriteLine($"Model:    {config.Model.Name}");
        Console.WriteLine($"Base URL: {config.Model.BaseUrl ?? "(default)"}");
        return 0;
    }

    private static int SetModel(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return SetModelInteractive();
        }

        if (args.Count < 2)
        {
            return PrintModelHelp();
        }

        var config = ConfigLoader.Load();
        var provider = args[0];
        var model = args[1];
        var baseUrl = args.Count >= 3 ? args[2] : config.Model.BaseUrl;

        var updated = new AuximConfig
        {
            Model = new ModelConfig
            {
                Provider = provider,
                Name = model,
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/'),
            },
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = config.Sandbox,
        };

        ConfigLoader.Save(updated);
        Console.WriteLine($"Model saved to {ConfigLoader.GetConfigPath()}");
        Console.WriteLine($"Provider: {updated.Model.Provider}");
        Console.WriteLine($"Model:    {updated.Model.Name}");
        Console.WriteLine($"Base URL: {updated.Model.BaseUrl ?? "(default)"}");
        return 0;
    }

    private static int SetModelInteractive()
    {
        var config = ConfigLoader.Load();
        var currentProvider = config.Model.Provider;
        var currentModel = config.Model.Name;
        var currentBaseUrl = config.Model.BaseUrl;

        PrintPanel(
            "Auxim Model Setup",
            [
                $"Config: {ConfigLoader.GetConfigPath()}",
                $"Secrets: {ConfigLoader.GetEnvPath()}",
                "",
                $"Current provider: {currentProvider}",
                $"Current model:    {currentModel}",
                $"Current base URL: {config.Model.BaseUrl ?? "(default)"}",
            ]);

        var selectedProvider = SelectProvider(currentProvider);
        var provider = selectedProvider.Id;
        var model = SelectModel(selectedProvider, currentModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            Console.Error.WriteLine("Model is required.");
            return 1;
        }

        var defaultBaseUrl = selectedProvider.BaseUrl;
        if (!string.IsNullOrWhiteSpace(currentBaseUrl)
            && string.Equals(provider, currentProvider, StringComparison.OrdinalIgnoreCase))
        {
            defaultBaseUrl = currentBaseUrl;
        }

        var baseUrl = selectedProvider.IsCustom
            ? defaultBaseUrl
            : PromptWithDefault("Base URL", defaultBaseUrl);
        var updated = new AuximConfig
        {
            Model = new ModelConfig
            {
                Provider = provider,
                Name = model,
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/'),
            },
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = config.Sandbox,
        };

        ConfigLoader.Save(updated);
        Console.WriteLine();
        Console.WriteLine($"Model saved to {ConfigLoader.GetConfigPath()}");

        var keyName = ApiKeyNameForProvider(provider);
        var hasKey = !RequiresApiKey(provider)
            || DotEnvStore.HasValue(keyName)
            || DotEnvStore.HasValue("AUXIM_API_KEY");
        if (RequiresApiKey(provider) && !hasKey && IsYes(PromptWithDefault("Save API key now?", "yes")))
        {
            var key = ReadSecret($"{keyName}: ");
            if (!string.IsNullOrWhiteSpace(key))
            {
                DotEnvStore.SetValue(keyName, key);
                Console.WriteLine($"API key saved to {ConfigLoader.GetEnvPath()}");
            }
        }

        PrintPanel(
            "Saved",
            [
                $"Provider: {updated.Model.Provider}",
                $"Model:    {updated.Model.Name}",
                $"Base URL: {updated.Model.BaseUrl ?? "(default)"}",
                $"API key:  {FormatApiKeyStatus(provider, keyName)}",
                "",
                "Try: ./auxim chat \"hello\"",
            ]);

        return 0;
    }

    private static int PrintModelHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim model show");
        Console.WriteLine("  auxim model set");
        Console.WriteLine("  auxim model set <provider> <model> [base-url]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  auxim model set");
        Console.WriteLine("  auxim model set openai-compatible gpt-4o-mini https://api.openai.com/v1");
        return 1;
    }
}
