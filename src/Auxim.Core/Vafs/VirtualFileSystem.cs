using System.Text;
using System.Text.RegularExpressions;

namespace Auxim.Core.Vafs;

public sealed class VirtualFileSystem
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly IReadOnlyList<VirtualMount> _mounts;

    public VirtualFileSystem(VirtualFileSystemOptions options)
    {
        var mounts = new List<VirtualMount>
        {
            NormalizeMount(new VirtualMount(
                "workspace",
                "/workspace",
                options.WorkspaceHostPath,
                ReadOnly: false)),
        };

        foreach (var mount in options.Mounts)
        {
            mounts.Add(NormalizeMount(mount));
        }

        _mounts = mounts
            .OrderByDescending(mount => mount.VirtualPath.Length)
            .ToArray();
    }

    public static VirtualFileSystem FromEnvironment() =>
        new(VirtualFileSystemOptions.FromEnvironment());

    public IReadOnlyList<VirtualMount> ListMounts() => _mounts;

    public string ResolveToHostPath(string path, bool requireWritable = false)
    {
        var virtualPath = NormalizeVirtualPath(path);
        var mount = FindMountForVirtualPath(virtualPath)
            ?? throw new VirtualPathException(
                $"Path '{path}' is outside Auxim VAFS. Use /workspace or mounted /volumes paths.");

        if (requireWritable && mount.ReadOnly)
        {
            throw new VirtualPathException($"Virtual mount '{mount.VirtualPath}' is read-only.");
        }

        var relative = RelativeVirtualPath(mount.VirtualPath, virtualPath);
        var hostPath = Path.GetFullPath(Path.Combine(mount.HostPath, relative));
        if (!IsSameOrChildPath(mount.HostPath, hostPath))
        {
            throw new VirtualPathException($"Path '{path}' escapes virtual mount '{mount.VirtualPath}'.");
        }

        return hostPath;
    }

    public string ToVirtualPath(string hostPath)
    {
        var fullPath = Path.GetFullPath(hostPath);
        foreach (var mount in _mounts.OrderByDescending(mount => mount.HostPath.Length))
        {
            if (!IsSameOrChildPath(mount.HostPath, fullPath))
            {
                continue;
            }

            var relative = Path.GetRelativePath(mount.HostPath, fullPath);
            if (relative == ".")
            {
                return mount.VirtualPath;
            }

            return mount.VirtualPath.TrimEnd('/') + "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        throw new VirtualPathException("Host path is outside Auxim VAFS.");
    }

    public string RewriteHostPathsToVirtual(string text)
    {
        var rewritten = text;
        foreach (var mount in _mounts.OrderByDescending(mount => mount.HostPath.Length))
        {
            var pattern = Regex.Escape(mount.HostPath)
                + @"(?=$|[\/\\\s:\)\]\}',""])";
            rewritten = Regex.Replace(
                rewritten,
                pattern,
                mount.VirtualPath,
                PathComparison == StringComparison.OrdinalIgnoreCase
                    ? RegexOptions.IgnoreCase
                    : RegexOptions.None);
        }

        return rewritten;
    }

    public string DescribeForAgent()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Available virtual filesystem mounts:");
        foreach (var mount in _mounts.OrderBy(mount => mount.VirtualPath, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {mount.VirtualPath}{(mount.ReadOnly ? " (read-only)" : "")}");
        }

        return builder.ToString().TrimEnd();
    }

    private static VirtualMount NormalizeMount(VirtualMount mount)
    {
        if (string.IsNullOrWhiteSpace(mount.Name))
        {
            throw new VirtualPathException("Virtual mount name is required.");
        }

        if (mount.Name.Contains('/') || mount.Name.Contains('\\') || mount.Name is "." or "..")
        {
            throw new VirtualPathException($"Invalid virtual mount name '{mount.Name}'.");
        }

        var virtualPath = NormalizeVirtualPath(mount.VirtualPath);
        if (virtualPath != "/workspace" && !virtualPath.StartsWith("/volumes/", StringComparison.Ordinal))
        {
            throw new VirtualPathException("Virtual mounts must live at /workspace or /volumes/<name>.");
        }

        return mount with
        {
            VirtualPath = virtualPath,
            HostPath = Path.GetFullPath(mount.HostPath),
        };
    }

    private static string NormalizeVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".")
        {
            return "/workspace";
        }

        path = path.Replace('\\', '/').Trim();
        if (path.StartsWith('~') || HasWindowsDrivePrefix(path))
        {
            throw new VirtualPathException("Use VAFS paths such as /workspace or /volumes/<name>.");
        }

        var basePath = path.StartsWith('/') ? "" : "/workspace";
        var parts = (basePath + "/" + path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case ".":
                    continue;
                case "..":
                    if (stack.Count == 0)
                    {
                        throw new VirtualPathException("Path escapes Auxim VAFS.");
                    }

                    stack.RemoveAt(stack.Count - 1);
                    break;
                default:
                    stack.Add(part);
                    break;
            }
        }

        return "/" + string.Join('/', stack);
    }

    private VirtualMount? FindMountForVirtualPath(string virtualPath)
    {
        return _mounts.FirstOrDefault(mount =>
            string.Equals(virtualPath, mount.VirtualPath, StringComparison.Ordinal)
            || virtualPath.StartsWith(mount.VirtualPath + "/", StringComparison.Ordinal));
    }

    private static string RelativeVirtualPath(string mountPath, string virtualPath)
    {
        if (string.Equals(mountPath, virtualPath, StringComparison.Ordinal))
        {
            return ".";
        }

        return virtualPath[(mountPath.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsSameOrChildPath(string parent, string path)
    {
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(parent, path, PathComparison)
            || path.StartsWith(parent + Path.DirectorySeparatorChar, PathComparison)
            || path.StartsWith(parent + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static bool HasWindowsDrivePrefix(string path) =>
        path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
}
