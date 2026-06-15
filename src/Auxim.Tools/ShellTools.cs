using Auxim.Core.Tools;
using Auxim.VAFS;

namespace Auxim.Tools;

public static class ShellTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "shell.run",
            "shell",
            "Runs a restricted Virtual Agent Shell command inside Auxim VAFS.",
            async (args, cancellationToken) =>
            {
                var command = FileTools.Required(args, "command");
                var timeoutSeconds = 30;
                if (args.TryGetValue("timeoutSeconds", out var rawTimeout)
                    && int.TryParse(rawTimeout?.ToString(), out var parsedTimeout)
                    && parsedTimeout > 0)
                {
                    timeoutSeconds = Math.Min(parsedTimeout, 300);
                }

                var shell = new VAShell(FileTools.Vafs());
                return await shell.RunAsync(command, timeoutSeconds, cancellationToken);
            })
        {
            ParametersSchema = FileTools.ObjectSchema(
                [
                    ("command", "string", "Restricted Virtual Agent Shell command to run. Built-ins include pwd, echo, ls, cat, head, tail, wc, find, grep, stat, and test. Paths must use /workspace, /tmp, or /volumes."),
                    ("timeoutSeconds", "integer", "Timeout in seconds, capped at 300."),
                ],
                ["command"]),
        });
    }
}
