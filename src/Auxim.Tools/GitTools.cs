using System.Diagnostics;
using Auxim.Core.Tools;
using Auxim.Core.Vafs;

namespace Auxim.Tools;

public static class GitTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "git.status",
            "git",
            "Returns git status --short.",
            (_, cancellationToken) => RunGitAsync(["status", "--short"], cancellationToken)));

        registry.Register(new ToolDefinition(
            "git.diff",
            "git",
            "Returns git diff. Optionally pass staged=true for staged changes.",
            (args, cancellationToken) =>
            {
                var staged = args.TryGetValue("staged", out var value)
                    && bool.TryParse(value?.ToString(), out var parsed)
                    && parsed;
                return RunGitAsync(staged ? ["diff", "--cached"] : ["diff"], cancellationToken);
            })
        {
            ParametersSchema = FileTools.ObjectSchema(("staged", "boolean", "Show staged diff when true.")),
        });
    }

    private static async Task<string> RunGitAsync(string[] args, CancellationToken cancellationToken)
    {
        var vfs = FileTools.Vfs();
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = vfs.ResolveToHostPath("/workspace"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\nstderr:\n{stderr}";
        return vfs.RewriteHostPathsToVirtual(output);
    }
}
