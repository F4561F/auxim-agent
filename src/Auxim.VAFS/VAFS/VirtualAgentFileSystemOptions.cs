using System.Text.Json;

namespace Auxim.VAFS;

public sealed class VirtualAgentFileSystemOptions
{
    public string WorkspaceHostPath { get; init; } = Environment.CurrentDirectory;
    public string TmpHostPath { get; init; } = Path.Combine(GetAuximHome(), "tmp");
    public IReadOnlyList<VirtualMount> Mounts { get; init; } = [];

    public static VirtualAgentFileSystemOptions FromEnvironment()
    {
        var config = LoadSandboxConfig();
        var workspace = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_WORKSPACE"),
            config.Workspace,
            Environment.CurrentDirectory);
        var tmp = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_TMP"),
            Path.Combine(GetAuximHome(), "tmp"));

        return new VirtualAgentFileSystemOptions
        {
            WorkspaceHostPath = workspace,
            TmpHostPath = tmp,
            Mounts = MergeMounts(
                config.Mounts.Select(ToVirtualMount),
                ParseMounts(Environment.GetEnvironmentVariable("AUXIM_VAFS_MOUNTS"))),
        };
    }

    private static VirtualMount ToVirtualMount(SandboxMount mount)
    {
        return new VirtualMount(
            mount.Name,
            $"/volumes/{mount.Name}",
            mount.HostPath,
            mount.ReadOnly);
    }

    private static IReadOnlyList<VirtualMount> MergeMounts(
        IEnumerable<VirtualMount> configured,
        IEnumerable<VirtualMount> environment)
    {
        var merged = new Dictionary<string, VirtualMount>(StringComparer.OrdinalIgnoreCase);
        foreach (var mount in configured.Concat(environment))
        {
            if (string.IsNullOrWhiteSpace(mount.Name))
            {
                continue;
            }

            merged[mount.Name] = mount;
        }

        return merged.Values.ToArray();
    }

    private static IReadOnlyList<VirtualMount> ParseMounts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var mounts = new List<VirtualMount>();
        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw new VirtualPathException(
                    "Invalid AUXIM_VAFS_MOUNTS entry. Use name=/host/path or name=/host/path:ro.");
            }

            var name = entry[..separator].Trim();
            var hostPath = entry[(separator + 1)..].Trim();
            var readOnly = false;
            if (hostPath.EndsWith(":ro", StringComparison.OrdinalIgnoreCase))
            {
                readOnly = true;
                hostPath = hostPath[..^3];
            }

            mounts.Add(new VirtualMount(
                name,
                $"/volumes/{name}",
                hostPath,
                readOnly));
        }

        return mounts;
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

    private static SandboxConfigSnapshot LoadSandboxConfig()
    {
        var path = Path.Combine(GetAuximHome(), "config.json");
        if (!File.Exists(path))
        {
            return new SandboxConfigSnapshot(null, []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("sandbox", out var sandbox))
            {
                return new SandboxConfigSnapshot(null, []);
            }

            var workspace = sandbox.TryGetProperty("workspace", out var workspaceElement)
                ? workspaceElement.GetString()
                : null;
            var mounts = new List<SandboxMount>();
            if (sandbox.TryGetProperty("mounts", out var mountsElement)
                && mountsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var mount in mountsElement.EnumerateArray())
                {
                    var name = mount.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                    var hostPath = mount.TryGetProperty("hostPath", out var pathElement) ? pathElement.GetString() ?? "" : "";
                    var readOnly = mount.TryGetProperty("readOnly", out var readOnlyElement)
                        && readOnlyElement.ValueKind == JsonValueKind.True;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(hostPath))
                    {
                        mounts.Add(new SandboxMount(name, hostPath, readOnly));
                    }
                }
            }

            return new SandboxConfigSnapshot(workspace, mounts);
        }
        catch (JsonException)
        {
            return new SandboxConfigSnapshot(null, []);
        }
        catch (IOException)
        {
            return new SandboxConfigSnapshot(null, []);
        }
        catch (UnauthorizedAccessException)
        {
            return new SandboxConfigSnapshot(null, []);
        }
    }

    private static string GetAuximHome()
    {
        var configured = Environment.GetEnvironmentVariable("AUXIM_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".auxim");
    }

    private sealed record SandboxConfigSnapshot(string? Workspace, IReadOnlyList<SandboxMount> Mounts);

    private sealed record SandboxMount(string Name, string HostPath, bool ReadOnly);
}
