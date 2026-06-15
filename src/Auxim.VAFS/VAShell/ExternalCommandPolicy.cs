namespace Auxim.VAFS;

internal sealed class ExternalCommandPolicy
{
    private static readonly HashSet<string> GitReadOnlySubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "branch",
        "diff",
        "log",
        "show",
        "status",
    };

    private static readonly HashSet<string> DotnetReadOnlySubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "--info",
        "--list-runtimes",
        "--list-sdks",
        "--version",
        "build",
        "test",
    };

    private readonly VirtualAgentFileSystem _vafs;

    public ExternalCommandPolicy(VirtualAgentFileSystem vafs)
    {
        _vafs = vafs;
    }

    public ExternalCommandPlan Review(IReadOnlyList<string> tokens)
    {
        var executable = tokens[0];
        var args = tokens.Skip(1).ToArray();
        return executable switch
        {
            "git" => ReviewGit(args),
            "rg" => ReviewRipgrep(args),
            "dotnet" => ReviewDotnet(args),
            _ => throw new InvalidOperationException($"VAShell: command '{executable}' is not allowed."),
        };
    }

    private ExternalCommandPlan ReviewGit(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            throw new InvalidOperationException("VAShell allows only explicit read-only git subcommands.");
        }

        if (!GitReadOnlySubcommands.Contains(args[0]))
        {
            throw new InvalidOperationException($"git {args[0]} is not allowed by VAShell.");
        }

        return new ExternalCommandPlan("git", ResolvePathArguments(args));
    }

    private ExternalCommandPlan ReviewRipgrep(IReadOnlyList<string> args)
    {
        var output = new List<string>();
        var patternSeen = false;
        foreach (var arg in args)
        {
            if (!patternSeen && !arg.StartsWith('-'))
            {
                patternSeen = true;
                output.Add(arg);
                continue;
            }

            RejectUnknownAbsolutePath(arg);
            if (!patternSeen || arg.StartsWith('-') || !LooksLikePath(arg))
            {
                output.Add(arg);
                continue;
            }

            output.Add(_vafs.ResolveToHostPath(arg));
        }

        return new ExternalCommandPlan("rg", output);
    }

    private ExternalCommandPlan ReviewDotnet(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            throw new InvalidOperationException("VAShell allows only explicit dotnet subcommands.");
        }

        if (!DotnetReadOnlySubcommands.Contains(args[0]))
        {
            throw new InvalidOperationException($"dotnet {args[0]} is not allowed by VAShell.");
        }

        return new ExternalCommandPlan("dotnet", ResolvePathArguments(args));
    }

    private IReadOnlyList<string> ResolvePathArguments(IReadOnlyList<string> args)
    {
        return args.Select(arg =>
        {
            RejectUnknownAbsolutePath(arg);
            if (arg.StartsWith('-') || !LooksLikePath(arg))
            {
                return arg;
            }

            return _vafs.ResolveToHostPath(arg);
        }).ToArray();
    }

    private static bool LooksLikePath(string arg) =>
        arg is "." or ".."
        || arg.StartsWith("/workspace", StringComparison.Ordinal)
        || arg.StartsWith("/tmp", StringComparison.Ordinal)
        || arg.StartsWith("/volumes", StringComparison.Ordinal)
        || arg.StartsWith("./", StringComparison.Ordinal)
        || arg.StartsWith("../", StringComparison.Ordinal)
        || (!arg.StartsWith('/') && arg.Contains('/'));

    private static void RejectUnknownAbsolutePath(string arg)
    {
        if (arg.StartsWith('/')
            && !arg.StartsWith("/workspace", StringComparison.Ordinal)
            && !arg.StartsWith("/tmp", StringComparison.Ordinal)
            && !arg.StartsWith("/volumes", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("VAShell only accepts virtual absolute paths under /workspace, /tmp, or /volumes.");
        }
    }
}

