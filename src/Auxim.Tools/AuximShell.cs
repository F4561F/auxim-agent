using System.Diagnostics;
using Auxim.Core.Utilities;
using Auxim.Core.Vafs;

namespace Auxim.Tools;

internal sealed class AuximShell
{
    private static readonly HashSet<string> DefaultAllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "pwd",
        "ls",
        "cat",
        "head",
        "tail",
        "rg",
        "git",
        "dotnet",
    };

    private static readonly HashSet<string> ForbiddenGitSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "clean",
        "clone",
        "commit",
        "fetch",
        "merge",
        "pull",
        "push",
        "rebase",
        "reset",
        "restore",
        "switch",
        "checkout",
    };

    private readonly VirtualFileSystem _vfs;
    private readonly IReadOnlySet<string> _allowedCommands;

    public AuximShell(VirtualFileSystem vfs)
    {
        _vfs = vfs;
        _allowedCommands = AllowedCommandsFromEnvironment();
    }

    public async Task<string> RunAsync(string command, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var tokens = Parse(command);
        if (tokens.Count == 0)
        {
            return "auxim-shell: empty command";
        }

        var executable = tokens[0];
        if (!_allowedCommands.Contains(executable))
        {
            return $"auxim-shell: command '{executable}' is not allowed.";
        }

        return executable switch
        {
            "pwd" => "exit_code: 0\nstdout:\n/workspace\nstderr:\n",
            "ls" => RunList(tokens),
            "cat" => RunRead(tokens, maxLines: null),
            "head" => RunRead(tokens, maxLines: 10),
            "tail" => RunTail(tokens, maxLines: 10),
            "git" => await RunProcessAsync("git", ValidateGitArgs(tokens.Skip(1).ToArray()), timeoutSeconds, cancellationToken),
            "rg" => await RunProcessAsync("rg", ResolvePathArguments(tokens.Skip(1).ToArray()), timeoutSeconds, cancellationToken),
            "dotnet" => await RunProcessAsync("dotnet", ResolvePathArguments(tokens.Skip(1).ToArray()), timeoutSeconds, cancellationToken),
            _ => $"auxim-shell: command '{executable}' is not implemented.",
        };
    }

    private string RunList(IReadOnlyList<string> tokens)
    {
        var path = tokens.Count >= 2 ? tokens[1] : "/workspace";
        if (path == "/")
        {
            return FormatResult(0, "/workspace/\n/tmp/\n/volumes/\n", "");
        }

        if (path == "/volumes")
        {
            var volumes = _vfs.ListMounts()
                .Where(mount => mount.VirtualPath.StartsWith("/volumes/", StringComparison.Ordinal))
                .OrderBy(mount => mount.VirtualPath)
                .Select(mount => mount.VirtualPath + "/");
            return FormatResult(0, string.Join(Environment.NewLine, volumes) + Environment.NewLine, "");
        }

        var hostPath = _vfs.ResolveToHostPath(path);
        var entries = Directory.EnumerateFileSystemEntries(hostPath)
            .OrderBy(entry => entry)
            .Select(entry => Directory.Exists(entry)
                ? _vfs.ToVirtualPath(entry) + "/"
                : _vfs.ToVirtualPath(entry));
        return FormatResult(0, string.Join(Environment.NewLine, entries) + Environment.NewLine, "");
    }

    private string RunRead(IReadOnlyList<string> tokens, int? maxLines)
    {
        if (tokens.Count < 2)
        {
            return FormatResult(2, "", "usage: cat|head <virtual-path>\n");
        }

        var hostPath = _vfs.ResolveToHostPath(tokens[1]);
        var lines = File.ReadLines(hostPath);
        if (maxLines is not null)
        {
            lines = lines.Take(maxLines.Value);
        }

        return FormatResult(0, string.Join(Environment.NewLine, lines) + Environment.NewLine, "");
    }

    private string RunTail(IReadOnlyList<string> tokens, int maxLines)
    {
        if (tokens.Count < 2)
        {
            return FormatResult(2, "", "usage: tail <virtual-path>\n");
        }

        var hostPath = _vfs.ResolveToHostPath(tokens[1]);
        var queue = new Queue<string>();
        foreach (var line in File.ReadLines(hostPath))
        {
            queue.Enqueue(line);
            if (queue.Count > maxLines)
            {
                queue.Dequeue();
            }
        }

        return FormatResult(0, string.Join(Environment.NewLine, queue) + Environment.NewLine, "");
    }

    private async Task<string> RunProcessAsync(
        string executable,
        IReadOnlyList<string> args,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = _vfs.ResolveToHostPath("/workspace"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {executable}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        var stdout = _vfs.RewriteHostPathsToVirtual(await stdoutTask);
        var stderr = _vfs.RewriteHostPathsToVirtual(await stderrTask);
        return FormatResult(process.ExitCode, stdout, stderr);
    }

    private IReadOnlyList<string> ResolvePathArguments(IReadOnlyList<string> args)
    {
        return args.Select(arg =>
        {
            if (arg.StartsWith('/')
                && !arg.StartsWith("/workspace", StringComparison.Ordinal)
                && !arg.StartsWith("/tmp", StringComparison.Ordinal)
                && !arg.StartsWith("/volumes", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("auxim-shell only accepts virtual absolute paths under /workspace, /tmp, or /volumes.");
            }

            if (arg.StartsWith('-') || !LooksLikePath(arg))
            {
                return arg;
            }

            return _vfs.ResolveToHostPath(arg);
        }).ToArray();
    }

    private static IReadOnlyList<string> ValidateGitArgs(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && ForbiddenGitSubcommands.Contains(args[0]))
        {
            throw new InvalidOperationException($"git {args[0]} is not allowed by auxim-shell.");
        }

        return args;
    }

    private static IReadOnlySet<string> AllowedCommandsFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("AUXIM_SHELL_COMMANDS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultAllowedCommands;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Parse(string command)
    {
        if (command.Any(character => character is ';' or '|' or '&' or '>' or '<' or '$' or '`' or '\n' or '\r'))
        {
            throw new InvalidOperationException(
                "auxim-shell does not allow shell operators, pipes, redirects, substitutions, or command chaining.");
        }

        return CommandTokenizer.Tokenize(command, throwOnUnclosedQuote: true);
    }

    private static bool LooksLikePath(string arg) =>
        arg is "." or ".."
        || arg.StartsWith("/workspace", StringComparison.Ordinal)
        || arg.StartsWith("/tmp", StringComparison.Ordinal)
        || arg.StartsWith("/volumes", StringComparison.Ordinal)
        || arg.StartsWith("./", StringComparison.Ordinal)
        || arg.StartsWith("../", StringComparison.Ordinal)
        || (!arg.StartsWith('/') && arg.Contains('/'));

    private static string FormatResult(int exitCode, string stdout, string stderr) =>
        $"exit_code: {exitCode}\nstdout:\n{EnsureTrailingNewline(stdout)}stderr:\n{stderr}";

    private static string EnsureTrailingNewline(string value) =>
        string.IsNullOrEmpty(value) || value.EndsWith('\n') ? value : value + Environment.NewLine;
}
