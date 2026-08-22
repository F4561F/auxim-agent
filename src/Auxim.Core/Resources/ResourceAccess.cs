namespace Auxim.Core.Resources;

public sealed record ResourceAction(string Value)
{
    public static ResourceAction Read { get; } = new("read");
    public static ResourceAction Write { get; } = new("write");
    public static ResourceAction Execute { get; } = new("execute");

    public override string ToString() => Value;
}

public sealed record ResourceUri
{
    public ResourceUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOf(':') <= 0)
        {
            throw new ArgumentException("Resource URI must include a scheme.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ResourceUri Vafs(string virtualPath) =>
        new($"vafs:{NormalizeVirtualPath(virtualPath)}");

    public static ResourceUri Opaque(string scheme, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(value);
        return new ResourceUri($"{scheme}:{Uri.EscapeDataString(value)}");
    }

    public override string ToString() => Value;

    private static string NormalizeVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = $"/workspace/{normalized}";
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException("Resource path escapes its virtual root.", nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var result = "/" + string.Join('/', segments);
        if (result is "/" or "/volumes"
            || result == "/workspace"
            || result.StartsWith("/workspace/", StringComparison.Ordinal)
            || result == "/tmp"
            || result.StartsWith("/tmp/", StringComparison.Ordinal)
            || result.StartsWith("/volumes/", StringComparison.Ordinal))
        {
            return result;
        }

        throw new ArgumentException(
            "VAFS resource must be under /workspace, /tmp, or /volumes.",
            nameof(path));
    }
}

public sealed record ResourceAccess(
    ResourceAction Action,
    ResourceUri Resource,
    bool RequiresApproval = false);
