using System.Diagnostics;
using Auxim.Core.Tools;
using Auxim.Core.Resources;
using Auxim.VAFS;

namespace Auxim.Tools;

public static class SearchTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "file.search",
            "files",
            "Searches files under /workspace, /tmp, or mounted /volumes paths. Uses ripgrep when available.",
            async (args, cancellationToken) =>
            {
                var vafs = FileTools.Vafs();
                var pattern = FileTools.Required(args, "pattern");
                var path = args.TryGetValue("path", out var rawPath) ? rawPath?.ToString() ?? "/workspace" : "/workspace";
                var hostPath = vafs.ResolveToHostPath(path);
                var maxResults = 100;
                if (args.TryGetValue("maxResults", out var rawMax)
                    && int.TryParse(rawMax?.ToString(), out var parsedMax)
                    && parsedMax > 0)
                {
                    maxResults = Math.Min(parsedMax, 500);
                }

                return await RunRipgrepAsync(vafs, pattern, hostPath, maxResults, cancellationToken)
                    ?? FallbackSearch(vafs, pattern, hostPath, maxResults);
            })
        {
            ParametersSchema = FileTools.ObjectSchema(
                [
                    ("pattern", "string", "Text or regex pattern to search for."),
                    ("path", "string", "Virtual path to search. Defaults to /workspace."),
                    ("maxResults", "integer", "Maximum results to return."),
                ],
                ["pattern"]),
            ResourceAccessResolver = args =>
            {
                var path = args.TryGetValue("path", out var value)
                    ? value?.ToString() ?? "/workspace"
                    : "/workspace";
                return [new ResourceAccess(ResourceAction.Read, ResourceUri.Vafs(path))];
            },
        });
    }

    private static async Task<string?> RunRipgrepAsync(
        VirtualAgentFileSystem vafs,
        string pattern,
        string hostPath,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (!CommandExists("rg"))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "rg",
            WorkingDirectory = vafs.ResolveToHostPath("/workspace"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--line-number");
        startInfo.ArgumentList.Add("--hidden");
        startInfo.ArgumentList.Add("--glob");
        startInfo.ArgumentList.Add("!.git");
        startInfo.ArgumentList.Add("--max-count");
        startInfo.ArgumentList.Add(maxResults.ToString());
        startInfo.ArgumentList.Add(pattern);
        startInfo.ArgumentList.Add(hostPath);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(stdout) ? "No matches." : vafs.RewriteHostPathsToVirtual(stdout);
    }

    private static string FallbackSearch(VirtualAgentFileSystem vafs, string pattern, string hostPath, int maxResults)
    {
        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(hostPath, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add($"{vafs.ToVirtualPath(file)}:{lineNumber}:{line}");
                    if (matches.Count >= maxResults)
                    {
                        return string.Join(Environment.NewLine, matches);
                    }
                }
            }
        }

        return matches.Count == 0 ? "No matches." : string.Join(Environment.NewLine, matches);
    }

    private static bool CommandExists(string command)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return paths.Any(path => File.Exists(Path.Combine(path, command)));
    }
}
