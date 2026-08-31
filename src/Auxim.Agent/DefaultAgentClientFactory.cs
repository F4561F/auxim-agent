using Auxim.Core.Config;

namespace Auxim.Agent;

public static class DefaultAgentClientFactory
{
    public static IAgentClient Create(AuximConfig config)
    {
        DotEnvStore.LoadIntoEnvironment();

        var provider = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_PROVIDER"),
            config.Model.Provider);

        if (string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "placeholder", StringComparison.OrdinalIgnoreCase))
        {
            return new EchoAgentClient();
        }

        var keyName = ProviderCatalog.ApiKeyNameForProvider(provider);
        var apiKey = ProviderCatalog.RequiresApiKey(provider)
            ? FirstNonEmpty(
                Environment.GetEnvironmentVariable(keyName),
                Environment.GetEnvironmentVariable("AUXIM_API_KEY"))
            : "dummy-local-key";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "AUXIM_API_KEY is required. Run: auxim auth set-api-key");
        }

        var baseUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_BASE_URL"),
            config.Model.BaseUrl,
            "https://api.openai.com/v1");

        var model = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_MODEL"),
            config.Model.Name);

        if (string.Equals(model, "placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Set AUXIM_MODEL or config.model.name before using an OpenAI-compatible provider.");
        }

        return new OpenAiCompatibleAgentClient(baseUrl, apiKey, model);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
