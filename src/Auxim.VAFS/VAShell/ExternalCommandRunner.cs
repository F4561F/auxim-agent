using System.Diagnostics;

namespace Auxim.VAFS;

internal sealed class ExternalCommandRunner
{
    private readonly VirtualAgentFileSystem _vafs;
    private readonly ExternalCommandPolicy _policy;

    public ExternalCommandRunner(VirtualAgentFileSystem vafs)
    {
        _vafs = vafs;
        _policy = new ExternalCommandPolicy(vafs);
    }

    public async Task<string> RunAsync(
        IReadOnlyList<string> tokens,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var plan = _policy.Review(tokens);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = _vafs.ResolveToHostPath("/workspace"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in plan.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {plan.Executable}.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var stdout = _vafs.RewriteHostPathsToVirtual(await stdoutTask);
            var stderr = _vafs.RewriteHostPathsToVirtual(await stderrTask);
            return FormatResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FormatResult(124, "", $"{plan.Executable}: timed out after {timeoutSeconds} seconds\n");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return FormatResult(127, "", $"{plan.Executable}: {exception.Message}\n");
        }
    }

    private static string FormatResult(int exitCode, string stdout, string stderr) =>
        $"exit_code: {exitCode}\nstdout:\n{EnsureTrailingNewline(stdout)}stderr:\n{stderr}";

    private static string EnsureTrailingNewline(string value) =>
        string.IsNullOrEmpty(value) || value.EndsWith('\n') ? value : value + Environment.NewLine;
}
