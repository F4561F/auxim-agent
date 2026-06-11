using Auxim.Core.Config;

namespace Auxim.Core.Vafs;

public sealed class VirtualFileSystemOptions
{
    public string WorkspaceHostPath { get; init; } = Environment.CurrentDirectory;
    public string TmpHostPath { get; init; } = Path.Combine(ConfigLoader.GetAuximHome(), "tmp");
    public IReadOnlyList<VirtualMount> Mounts { get; init; } = [];

    public static VirtualFileSystemOptions FromEnvironment()
    {
        var config = ConfigLoader.Load();
        var workspace = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_WORKSPACE"),
            config.Sandbox.Workspace,
            Environment.CurrentDirectory);
        var tmp = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AUXIM_TMP"),
            Path.Combine(ConfigLoader.GetAuximHome(), "tmp"));

        return new VirtualFileSystemOptions
        {
            WorkspaceHostPath = workspace,
            TmpHostPath = tmp,
            Mounts = MergeMounts(
                config.Sandbox.Mounts.Select(ToVirtualMount),
                ParseMounts(Environment.GetEnvironmentVariable("AUXIM_VAFS_MOUNTS"))),
        };
    }

    private static VirtualMount ToVirtualMount(SandboxMountConfig mount)
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
}
