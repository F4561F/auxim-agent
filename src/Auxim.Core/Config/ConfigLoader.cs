using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auxim.Core.Config;

public static class ConfigLoader
{
    public static AuximConfig Load(string? path = null)
    {
        path ??= Path.Combine(GetAuximHome(), "config.json");
        if (!File.Exists(path))
        {
            return new AuximConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AuximConfig>(json, JsonOptions()) ?? new AuximConfig();
    }

    public static void Save(AuximConfig config, string? path = null)
    {
        path ??= GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(config, JsonOptions());
        WriteAtomic(path, json + Environment.NewLine);
    }

    public static void SetValue(string keyPath, string value, string? path = null)
    {
        path ??= GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        JsonObject root;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            root = JsonNode.Parse(json)?.AsObject() ?? [];
        }
        else
        {
            root = [];
        }

        var parts = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Config key path is required.", nameof(keyPath));
        }

        var current = root;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            if (current[part] is not JsonObject next)
            {
                next = [];
                current[part] = next;
            }

            current = next;
        }

        current[parts[^1]] = ParseConfigValue(value);
        WriteAtomic(path, root.ToJsonString(JsonOptions()) + Environment.NewLine);
    }

    public static string GetConfigPath() => Path.Combine(GetAuximHome(), "config.json");

    public static string GetEnvPath() => Path.Combine(GetAuximHome(), ".env");

    public static string GetAuximHome()
    {
        var configured = Environment.GetEnvironmentVariable("AUXIM_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".auxim");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static JsonNode? ParseConfigValue(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        if (int.TryParse(value, out var integer))
        {
            return JsonValue.Create(integer);
        }

        return JsonValue.Create(value);
    }

    private static void WriteAtomic(string path, string content)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch (IOException) when (File.Exists(path) && File.Exists(tempPath))
        {
            File.Replace(tempPath, path, null);
        }
    }
}
