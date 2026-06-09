namespace Auxim.Core.Config;

public static class DotEnvStore
{
    public static void LoadIntoEnvironment(string? path = null)
    {
        path ??= ConfigLoader.GetEnvPath();
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = Unquote(trimmed[(separator + 1)..].Trim());
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static void SetValue(string key, string value, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace) || key.Contains('='))
        {
            throw new ArgumentException("Invalid environment variable name.", nameof(key));
        }

        value = value.Replace("\r", "").Replace("\n", "");
        path ??= ConfigLoader.GetEnvPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var replacement = $"{key}={QuoteIfNeeded(value)}";
        var updated = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{key}=", StringComparison.Ordinal))
            {
                lines[i] = replacement;
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            lines.Add(replacement);
        }

        File.WriteAllLines(path, lines);
        Environment.SetEnvironmentVariable(key, value);
    }

    public static bool HasValue(string key, string? path = null)
    {
        LoadIntoEnvironment(path);
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key));
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Any(char.IsWhiteSpace) || value.Contains('"'))
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return value;
    }
}
