using System.Diagnostics;
using Auxim.Core.Tools;

namespace Auxim.Tools;

public static class ShellTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "shell.run",
            "shell",
            "Runs a restricted auxim-shell command inside the virtual /workspace filesystem.",
            async (args, cancellationToken) =>
            {
                if (!IsShellAllowed())
                {
                    return "shell.run is disabled. Set AUXIM_ALLOW_SHELL=true to allow restricted auxim-shell commands.";
                }

                var command = FileTools.Required(args, "command");
                var timeoutSeconds = 30;
                if (args.TryGetValue("timeoutSeconds", out var rawTimeout)
                    && int.TryParse(rawTimeout?.ToString(), out var parsedTimeout)
                    && parsedTimeout > 0)
                {
                    timeoutSeconds = Math.Min(parsedTimeout, 300);
                }

                var shell = new AuximShell(FileTools.Vfs());
                return await shell.RunAsync(command, timeoutSeconds, cancellationToken);
            })
        {
            ParametersSchema = FileTools.ObjectSchema(
                [
                    ("command", "string", "Restricted auxim-shell command to run. Paths must use /workspace or /volumes."),
                    ("timeoutSeconds", "integer", "Timeout in seconds, capped at 300."),
                ],
                ["command"]),
        });
    }

    private static bool IsShellAllowed()
    {
        var value = Environment.GetEnvironmentVariable("AUXIM_ALLOW_SHELL") ?? "";
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
