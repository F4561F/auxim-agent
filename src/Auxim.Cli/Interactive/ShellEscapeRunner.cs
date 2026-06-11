using System.Diagnostics;

namespace Auxim.Cli.Interactive;

internal static class ShellEscapeRunner
{
    public static async Task<int> RunAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.WriteLine($"{ConsoleTheme.Badge("shell")} {Ansi.Muted("usage: // <shell-command>")}");
            return 0;
        }

        using var process = Process.Start(CreateStartInfo(command))
            ?? throw new InvalidOperationException("Failed to start shell.");

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"{ConsoleTheme.Badge("shell")} {Ansi.Warning($"exit code {process.ExitCode}")}");
            }

            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Environment.CurrentDirectory,
                ArgumentList = { "/C", command },
            };
        }

        return new ProcessStartInfo
        {
            FileName = ResolveUnixShell(),
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Environment.CurrentDirectory,
            ArgumentList = { "-lc", command },
        };
    }

    private static string ResolveUnixShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        return string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell;
    }
}
