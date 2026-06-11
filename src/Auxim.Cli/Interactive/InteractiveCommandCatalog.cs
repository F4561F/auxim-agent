namespace Auxim.Cli.Interactive;

internal static class InteractiveCommandCatalog
{
    public static readonly IReadOnlyList<InteractiveCommand> Commands =
    [
        new("/help", "Show this help"),
        new("/exit", "Quit"),
        new("/new", "Start a new session"),
        new("/session", "Manage sessions"),
        new("/sessions", "List sessions"),
        new("/model", "Show or configure model"),
        new("/auth", "Manage API keys"),
        new("/config", "Show or edit config"),
        new("/tools", "List tools"),
        new("/tool", "Run a tool directly"),
        new("/sandbox", "Show or edit VAFS mappings"),
        new("/approval", "Show or clear approvals"),
        new("/doctor", "Show diagnostics"),
        new("/context", "Show current session context stats"),
        new("/history", "Open the conversation history"),
        new("/show", "Show a single turn by number"),
        new("/jump", "Replay history from a turn number"),
        new("/tail", "Replay recent turns"),
        new("/resume", "Return to the dashboard"),
        new("/status", "Show interface and runtime status"),
        new("/shortcuts", "Show keyboard shortcuts"),
        new("/paste", "Enter multiline input"),
        new("/clear", "Clear screen"),
        new("/redraw", "Redraw the interface"),
        new("/welcome", "Show the start dashboard"),
        new("//", "Run a command in your real shell"),
    ];

    public static string[] Complete(string input)
    {
        if (!input.StartsWith('/'))
        {
            return [];
        }

        var commandPart = input.Split(' ', 2)[0];
        return Commands
            .Select(command => command.Name)
            .Where(name => name.StartsWith(commandPart, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

internal sealed record InteractiveCommand(string Name, string Description);
