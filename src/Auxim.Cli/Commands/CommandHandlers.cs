using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Core.Approval;
using Auxim.VAFS;
using Auxim.Tools;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    private static readonly ProviderChoice[] ProviderChoices =
    [
        new(
            "openai-api",
            "OpenAI API",
            "https://api.openai.com/v1",
            ["gpt-5.5", "gpt-5.5-pro", "gpt-5.4", "gpt-5.4-mini", "gpt-4.1", "gpt-4o", "gpt-4o-mini"],
            "OPENAI_API_KEY"),
        new(
            "openrouter",
            "OpenRouter",
            "https://openrouter.ai/api/v1",
            ["anthropic/claude-opus-4.8", "anthropic/claude-sonnet-4.6", "openai/gpt-5.5", "google/gemini-3-pro-preview", "deepseek/deepseek-v4-flash", "moonshotai/kimi-k2.6", "z-ai/glm-5.1"],
            "OPENROUTER_API_KEY"),
        new(
            "nous",
            "Nous Portal",
            "https://inference.nousresearch.com/v1",
            ["anthropic/claude-opus-4.8", "anthropic/claude-sonnet-4.6", "openai/gpt-5.5", "google/gemini-3.5-flash", "moonshotai/kimi-k2.6", "z-ai/glm-5.1"],
            "NOUS_API_KEY"),
        new(
            "novita",
            "NovitaAI",
            "https://api.novita.ai/openai/v1",
            ["moonshotai/kimi-k2.5", "minimax/minimax-m2.7", "zai-org/glm-5", "deepseek/deepseek-v3-0324", "qwen/qwen3-235b-a22b-fp8"],
            "NOVITA_API_KEY"),
        new(
            "ollama",
            "Ollama local OpenAI-compatible API",
            "http://127.0.0.1:11434/v1",
            ["llama3.2", "qwen2.5-coder", "mistral", "gemma3", "deepseek-r1"],
            ""),
        new(
            "lmstudio",
            "LM Studio local server",
            "http://127.0.0.1:1234/v1",
            ["local-model"],
            ""),
        new(
            "deepseek",
            "DeepSeek",
            "https://api.deepseek.com/v1",
            ["deepseek-v4-pro", "deepseek-v4-flash", "deepseek-chat", "deepseek-reasoner"],
            "DEEPSEEK_API_KEY"),
        new(
            "zai",
            "Z.AI / GLM",
            "https://api.z.ai/api/paas/v4",
            ["glm-5.1", "glm-5", "glm-5v-turbo", "glm-5-turbo", "glm-4.7", "glm-4.5", "glm-4.5-flash"],
            "ZAI_API_KEY"),
        new(
            "kimi-coding",
            "Kimi / Moonshot",
            "https://api.moonshot.ai/v1",
            ["kimi-k2.6", "kimi-k2.5", "kimi-for-coding", "kimi-k2-thinking", "kimi-k2-thinking-turbo", "kimi-k2-turbo-preview"],
            "KIMI_API_KEY"),
        new(
            "kimi-coding-cn",
            "Kimi / Moonshot China",
            "https://api.moonshot.cn/v1",
            ["kimi-k2.6", "kimi-k2.5", "kimi-k2-thinking", "kimi-k2-turbo-preview"],
            "KIMI_CN_API_KEY"),
        new(
            "xai",
            "xAI Grok",
            "https://api.x.ai/v1",
            ["grok-4.3", "grok-4.20-0309-reasoning", "grok-4.20-0309-non-reasoning", "grok-4.20-multi-agent-0309"],
            "XAI_API_KEY"),
        new(
            "gemini-openai",
            "Google Gemini OpenAI-compatible",
            "https://generativelanguage.googleapis.com/v1beta/openai",
            ["gemini-3.1-pro-preview", "gemini-3-pro-preview", "gemini-3.5-flash", "gemini-3.1-flash-lite-preview"],
            "GEMINI_API_KEY"),
        new(
            "nvidia",
            "NVIDIA NIM",
            "https://integrate.api.nvidia.com/v1",
            ["nvidia/nemotron-3-super-120b-a12b", "nvidia/nemotron-3-nano-30b-a3b", "deepseek-ai/deepseek-v3.2", "moonshotai/kimi-k2.6", "openai/gpt-oss-120b"],
            "NVIDIA_API_KEY"),
        new(
            "alibaba",
            "Qwen Cloud / DashScope",
            "https://dashscope-intl.aliyuncs.com/compatible-mode/v1",
            ["qwen3.7-max", "qwen3.6-plus", "qwen3.5-plus", "qwen3-coder-plus", "qwen3-coder-next", "kimi-k2.5", "glm-5"],
            "DASHSCOPE_API_KEY"),
        new(
            "xiaomi",
            "Xiaomi MiMo",
            "https://api.xiaomimimo.com/v1",
            ["mimo-v2.5-pro", "mimo-v2.5", "mimo-v2-pro", "mimo-v2-omni", "mimo-v2-flash"],
            "MIMO_API_KEY"),
        new(
            "huggingface",
            "Hugging Face Inference Providers",
            "https://router.huggingface.co/v1",
            ["moonshotai/Kimi-K2.5", "Qwen/Qwen3.5-397B-A17B", "deepseek-ai/DeepSeek-V3.2", "MiniMaxAI/MiniMax-M2.5", "zai-org/GLM-5"],
            "HF_TOKEN"),
        new(
            "gmi",
            "GMI Cloud",
            "https://api.gmi-serving.com/v1",
            ["zai-org/GLM-5.1-FP8", "deepseek-ai/DeepSeek-V3.2", "moonshotai/Kimi-K2.5", "google/gemini-3.1-flash-lite-preview", "openai/gpt-5.4"],
            "GMI_API_KEY"),
        new(
            "kilocode",
            "Kilo Code",
            "https://api.kilo.ai/api/gateway",
            ["anthropic/claude-opus-4.6", "anthropic/claude-sonnet-4.6", "openai/gpt-5.4", "google/gemini-3-pro-preview", "google/gemini-3-flash-preview"],
            "KILOCODE_API_KEY"),
        new(
            "opencode-zen",
            "OpenCode Zen",
            "https://opencode.ai/zen/v1",
            ["kimi-k2.5", "gpt-5.4-pro", "gpt-5.4", "claude-sonnet-4-6", "gemini-3-pro", "minimax-m2.7", "glm-5", "qwen3-coder"],
            "OPENCODE_API_KEY"),
        new(
            "opencode-go",
            "OpenCode Go",
            "https://opencode.ai/zen/go/v1",
            ["kimi-k2.6", "kimi-k2.5", "glm-5.1", "mimo-v2.5-pro", "minimax-m2.7", "qwen3.7-max"],
            "OPENCODE_API_KEY"),
        new(
            "ollama-cloud",
            "Ollama Cloud",
            "https://ollama.com/v1",
            ["gpt-oss:120b", "deepseek-v3.1:671b", "qwen3-coder:480b", "llama3.3:70b"],
            "OLLAMA_API_KEY"),
        new(
            "arcee",
            "Arcee AI",
            "https://api.arcee.ai/api/v1",
            ["trinity-large-thinking", "trinity-large-preview", "trinity-mini"],
            "ARCEEAI_API_KEY"),
    ];

    private static int PrintPath(string path)
    {
        Console.WriteLine(path);
        return 0;
    }

    private static IReadOnlyDictionary<string, object?> ParseKeyValueArgs(IEnumerable<string> args)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            var separator = arg.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            result[arg[..separator]] = arg[(separator + 1)..];
        }

        return result;
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? "";
        }

        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(chars.ToArray());
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }
    }

    private static string PromptWithDefault(string label, string defaultValue)
    {
        var suffix = string.IsNullOrWhiteSpace(defaultValue) ? "" : $" [{defaultValue}]";
        Console.Write($"{label}{suffix}: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static ProviderChoice SelectProvider(string currentProvider)
    {
        Console.WriteLine("Choose a provider:");
        var options = ProviderChoices.ToList();
        var current = ProviderChoices.FirstOrDefault(provider =>
            string.Equals(provider.Id, currentProvider, StringComparison.OrdinalIgnoreCase));

        for (var i = 0; i < options.Count; i++)
        {
            var marker = current is not null && options[i].Id == current.Id ? "  <- current" : "";
            Console.WriteLine($"  {i + 1}. {options[i].Label}{marker}");
        }

        Console.WriteLine($"  {options.Count + 1}. Custom provider");
        Console.WriteLine();

        var selected = PromptForChoice("Provider", current is not null ? Array.IndexOf(ProviderChoices, current) + 1 : 1, options.Count + 1);
        if (selected <= options.Count)
        {
            return options[selected - 1];
        }

        var id = PromptWithDefault("Provider id", currentProvider == "local" ? "custom" : currentProvider);
        var baseUrl = PromptWithDefault("Base URL", "https://api.example.com/v1");
        return new ProviderChoice(id, $"Custom ({id})", baseUrl, [], IsCustom: true);
    }

    public static string ApiKeyNameForProvider(string provider)
    {
        return ProviderCatalog.ApiKeyNameForProvider(provider);
    }

    public static bool RequiresApiKey(string provider)
    {
        return ProviderCatalog.RequiresApiKey(provider);
    }

    private static string FormatApiKeyStatus(string provider, string keyName)
    {
        if (!RequiresApiKey(provider))
        {
            return "not required";
        }

        return DotEnvStore.HasValue(keyName) || DotEnvStore.HasValue("AUXIM_API_KEY")
            ? $"set ({keyName})"
            : $"not set ({keyName})";
    }

    private static string SelectModel(ProviderChoice provider, string currentModel)
    {
        if (provider.Models.Count == 0)
        {
            Console.WriteLine();
            return PromptWithDefault("Model id", currentModel == "placeholder" ? "" : currentModel);
        }

        Console.WriteLine();
        Console.WriteLine($"Choose a model for {provider.Label}:");
        var models = provider.Models.ToList();
        for (var i = 0; i < models.Count; i++)
        {
            var marker = string.Equals(models[i], currentModel, StringComparison.OrdinalIgnoreCase) ? "  <- current" : "";
            Console.WriteLine($"  {i + 1}. {models[i]}{marker}");
        }

        Console.WriteLine($"  {models.Count + 1}. Custom model");
        Console.WriteLine();

        var defaultChoice = 1;
        var currentIndex = models.FindIndex(model => string.Equals(model, currentModel, StringComparison.OrdinalIgnoreCase));
        if (currentIndex >= 0)
        {
            defaultChoice = currentIndex + 1;
        }

        var selected = PromptForChoice("Model", defaultChoice, models.Count + 1);
        if (selected <= models.Count)
        {
            return models[selected - 1];
        }

        return PromptWithDefault("Model id", currentModel == "placeholder" ? "" : currentModel);
    }

    private static int PromptForChoice(string label, int defaultChoice, int maxChoice)
    {
        while (true)
        {
            Console.Write($"{label} [1-{maxChoice}, default {defaultChoice}]: ");
            var raw = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultChoice;
            }

            if (int.TryParse(raw.Trim(), out var choice) && choice >= 1 && choice <= maxChoice)
            {
                return choice;
            }

            Console.WriteLine($"Choose a number from 1 to {maxChoice}.");
        }
    }

    private static bool IsYes(string value)
    {
        return value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string WorkspaceHostPath(AuximConfig config)
    {
        var environment = Environment.GetEnvironmentVariable("AUXIM_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return Path.GetFullPath(ExpandHome(environment));
        }

        return string.IsNullOrWhiteSpace(config.Sandbox.Workspace)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(ExpandHome(config.Sandbox.Workspace));
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }

    private static bool IsValidMountName(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            && name is not "." and not "..";
    }

    private static void PrintPanel(string title, IReadOnlyList<string> lines)
    {
        var width = Math.Max(title.Length + 4, lines.Select(line => line.Length).DefaultIfEmpty(0).Max() + 4);
        var top = "+" + new string('-', width - 2) + "+";
        Console.WriteLine(top);
        Console.WriteLine("| " + title.PadRight(width - 4) + " |");
        Console.WriteLine("|" + new string('-', width - 2) + "|");
        foreach (var line in lines)
        {
            Console.WriteLine("| " + line.PadRight(width - 4) + " |");
        }
        Console.WriteLine(top);
        Console.WriteLine();
    }

    private sealed record ProviderChoice(
        string Id,
        string Label,
        string BaseUrl,
        IReadOnlyList<string> Models,
        string ApiKeyEnv = "AUXIM_API_KEY",
        bool IsCustom = false);
}
