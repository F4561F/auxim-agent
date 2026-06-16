namespace Auxim.Core.Config;

public static class ProviderCatalog
{
    private static readonly Dictionary<string, string> ApiKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai-api"] = "OPENAI_API_KEY",
        ["openrouter"] = "OPENROUTER_API_KEY",
        ["nous"] = "NOUS_API_KEY",
        ["novita"] = "NOVITA_API_KEY",
        ["ollama"] = "",
        ["lmstudio"] = "",
        ["deepseek"] = "DEEPSEEK_API_KEY",
        ["zai"] = "ZAI_API_KEY",
        ["kimi-coding"] = "KIMI_API_KEY",
        ["kimi-coding-cn"] = "KIMI_CN_API_KEY",
        ["xai"] = "XAI_API_KEY",
        ["gemini-openai"] = "GEMINI_API_KEY",
        ["nvidia"] = "NVIDIA_API_KEY",
        ["alibaba"] = "DASHSCOPE_API_KEY",
        ["xiaomi"] = "MIMO_API_KEY",
        ["huggingface"] = "HF_TOKEN",
        ["gmi"] = "GMI_API_KEY",
        ["kilocode"] = "KILOCODE_API_KEY",
        ["opencode-zen"] = "OPENCODE_API_KEY",
        ["opencode-go"] = "OPENCODE_API_KEY",
        ["ollama-cloud"] = "OLLAMA_API_KEY",
        ["arcee"] = "ARCEEAI_API_KEY",
    };

    public static string ApiKeyNameForProvider(string provider) =>
        ApiKeyNames.TryGetValue(provider, out var keyName) && !string.IsNullOrWhiteSpace(keyName)
            ? keyName
            : "AUXIM_API_KEY";

    public static bool RequiresApiKey(string provider) =>
        !ApiKeyNames.TryGetValue(provider, out var keyName) || !string.IsNullOrWhiteSpace(keyName);
}
