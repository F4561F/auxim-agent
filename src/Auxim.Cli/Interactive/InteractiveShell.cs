using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.VAFS;
using Auxim.Cli.Services;
using System.Text;

namespace Auxim.Cli.Interactive;

internal static class InteractiveShell
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Now;
    private static CancellationTokenSource? _currentTurn;
    private static IReadOnlyList<string> _transcriptLines = [];
    private static int _transcriptOffset;
    private static bool _transcriptMode;

    public static async Task<int> RunAsync()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        using var screen = InteractiveScreen.Enter();
        try
        {
            var history = InteractiveHistory.Load();
            var editor = new LineEditor(history.Entries, InteractiveCommandCatalog.Complete, ScrollTranscript);
            PrintDashboard();

            while (true)
            {
                var input = ReadUserInput(editor);
                if (input is null)
                {
                    Console.WriteLine();
                    history.Save();
                    return 0;
                }

                input = input.Trim();
                if (input.Length == 0)
                {
                    continue;
                }

                if (input.StartsWith("//", StringComparison.Ordinal))
                {
                    history.Add(input);
                    history.Save();
                    await RunShellEscapeAsync(input[2..].TrimStart());
                    continue;
                }

                if (input.StartsWith('/'))
                {
                    history.Add(input);
                    var exitCode = await RunSlashCommandAsync(input);
                    history.Save();
                    if (exitCode == ExitRequested)
                    {
                        return 0;
                    }

                    continue;
                }

                history.Add(input);
                history.Save();
                using var turn = new CancellationTokenSource();
                _currentTurn = turn;
                var streamedContent = false;
                var toolEvents = 0;
                var started = DateTimeOffset.Now;
                var streamedResponse = new StringBuilder();
                try
                {
                    PrintTurnHeader("assistant", $"thinking  {started:HH:mm:ss}");
                    var runner = new ChatRunner(
                        toolEvent =>
                        {
                            toolEvents++;
                            PrintToolEvent(toolEvent);
                        },
                        delta =>
                        {
                            streamedContent = true;
                            streamedResponse.Append(delta);
                        });
                    var result = await runner.RunAsync(input, turn.Token);
                    AppendAssistantResponse(streamedContent ? streamedResponse.ToString() : result.FinalResponse);
                    PrintTurnFooter(started, toolEvents);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine();
                    Console.WriteLine(Ansi.Warning("Cancelled current turn."));
                    Console.WriteLine();
                }
                catch (Exception exception)
                {
                    Console.WriteLine();
                    Console.WriteLine(Ansi.Error($"Error: {exception.Message}"));
                    Console.WriteLine();
                }
                finally
                {
                    _currentTurn = null;
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        if (_currentTurn is not null)
        {
            args.Cancel = true;
            _currentTurn.Cancel();
            return;
        }
        
        args.Cancel = true;
        InteractiveScreen.EmergencyCleanup();
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Exiting (Ctrl+C).");
        Environment.Exit(0);
    }

    private static void PrintHeader()
    {
        var config = ConfigLoader.Load();
        var currentSession = new SessionStore().GetCurrentSessionId();
        ConsoleTheme.Banner("Auxim", "portable C# AI agent");
        ConsoleTheme.StatusBar(
            ("model", $"{config.Model.Provider}/{config.Model.Name}"),
            ("session", string.IsNullOrWhiteSpace(currentSession) ? "(new)" : currentSession),
            ("mode", InteractiveScreen.IsAlternateScreenActive ? "alternate" : "inline"));
        Console.WriteLine();
    }

    private static void PrintDashboard()
    {
        PrintHeader();
        ConsoleTheme.Section("Dashboard");
        PrintRuntimePanel();
        ConsoleTheme.Section("Actions");
        ConsoleTheme.ActionList(
        [
            new("/help", "command and input reference"),
            new("/status", "current interface and runtime"),
            new("/context", "session usage snapshot"),
            new("/history", "open the conversation history"),
            new("/jump 1", "start history replay from turn 1"),
            new("/model show", "active model config"),
            new("/sandbox show", "VAFS mappings"),
            new("// git status", "run host shell"),
        ]);
        Console.WriteLine();
        ConsoleTheme.Hint("Type naturally to talk with the agent. Use /paste for multiline input or // for a real shell command.");
        Console.WriteLine();
    }

    private static void PrintToolEvent(ToolEvent toolEvent)
    {
        var detail = toolEvent.Detail.Length <= 160
            ? toolEvent.Detail
            : toolEvent.Detail[..160] + "...";
        Console.WriteLine();
        var kind = toolEvent.Kind == "done" ? Ansi.Success(toolEvent.Kind) : Ansi.Accent(toolEvent.Kind);
        Console.WriteLine($"{ConsoleTheme.Badge($"tool:{toolEvent.Name}")} {kind} {Ansi.Muted(detail)}");
    }

    private const int ExitRequested = 42;

    private static async Task<int> RunSlashCommandAsync(string input)
    {
        var parts = SplitCommand(input[1..]);
        if (parts.Count == 0)
        {
            return 0;
        }

        var command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();
        switch (command)
        {
            case "exit":
            case "quit":
            case "q":
                return ExitRequested;
            case "help":
            case "?":
                PrintHelp();
                return 0;
            case "new":
                return CommandHandlers.HandleSession(["new", ..args]);
            case "context":
            case "usage":
                PrintContext();
                return 0;
            case "history":
                PrintHistory(args);
                return 0;
            case "show":
                ShowTurn(args);
                return 0;
            case "jump":
            case "goto":
                JumpToTurn(args);
                return 0;
            case "tail":
                TailTurns(args);
                return 0;
            case "resume":
                ResumeDashboard();
                return 0;
            case "status":
                PrintRuntimePanel();
                return 0;
            case "shortcuts":
                PrintShortcuts();
                return 0;
            case "paste":
                PrintStatus("/paste is only available from the main prompt.");
                return 0;
            case "model":
                return CommandHandlers.HandleModel(args);
            case "auth":
                return CommandHandlers.HandleAuth(args);
            case "config":
                return CommandHandlers.HandleConfig(args);
            case "session":
                return CommandHandlers.HandleSession(args);
            case "sessions":
                return CommandHandlers.HandleSession(["list"]);
            case "tools":
                return await CommandHandlers.HandleTool(["list"]);
            case "tool":
                return await CommandHandlers.HandleTool(args);
            case "approval":
                return CommandHandlers.HandleApproval(args);
            case "sandbox":
                return CommandHandlers.HandleSandbox(args);
            case "doctor":
                return CommandHandlers.HandleDoctor();
            case "clear":
            case "redraw":
                InteractiveScreen.Clear();
                PrintDashboard();
                return 0;
            case "welcome":
                PrintDashboard();
                return 0;
            default:
                Console.WriteLine(Ansi.Warning($"Unknown command: /{command}"));
                ConsoleTheme.Hint("Type /help for available commands.");
                return 1;
        }
    }

    private static void PrintHelp()
    {
        ConsoleTheme.Section("Commands");
        ConsoleTheme.CommandGrid(InteractiveCommandCatalog.Commands);

        ConsoleTheme.Section("Input");
        ConsoleTheme.Hint("Up/Down history", "Left/Right cursor", "Tab completion", "Ctrl+D exit");
        ConsoleTheme.Hint("mouse wheel scrolls history in place", "line \\ continues input", "/paste ends with .", "// runs real shell");
        ConsoleTheme.Hint("/history opens history", "/show <n> opens one turn", "/jump <n> replays from a turn", "Esc or /resume returns");
        Console.WriteLine();
    }

    private static void PrintContext()
    {
        var store = new SessionStore();
        var session = store.GetOrCreateCurrent();
        var userMessages = session.Messages.Count(message => message.Role == "user");
        var assistantMessages = session.Messages.Count(message => message.Role == "assistant");
        ConsoleTheme.Section("Context");
        ConsoleTheme.Pair("session", session.Id);
        ConsoleTheme.Pair("title", session.Title);
        ConsoleTheme.Pair("messages", session.Messages.Count.ToString());
        ConsoleTheme.Pair("user turns", userMessages.ToString());
        ConsoleTheme.Pair("assistant", assistantMessages.ToString());
        ConsoleTheme.Pair("chars", session.Messages.Sum(message => message.Content.Length).ToString());
    }

    private static void PrintHistory(IReadOnlyList<string> args)
    {
        var turns = GetCurrentTurns();
        if (turns.Count == 0)
        {
            ConsoleTheme.Hint("No turns in the current session.");
            return;
        }

        var count = ParsePositiveInt(args.FirstOrDefault(), defaultValue: Math.Min(20, turns.Count));
        var start = Math.Max(0, turns.Count - count);
        OpenTranscript(
            $"Conversation History ({turns.Count} turns)",
            RenderHistorySummaryToLines(turns.Skip(start)),
            startAtBottom: false);
    }

    private static IReadOnlyList<string> RenderHistorySummaryToLines(IEnumerable<SessionTurn> turns)
    {
        var lines = new List<string>();
        foreach (var turn in turns)
        {
            var user = OneLine(turn.User.Content, 72);
            var assistant = OneLine(turn.Assistant?.Content ?? "(no assistant response)", 72);
            lines.Add($"{Ansi.Accent(turn.Number.ToString().PadLeft(3))} {Ansi.Bold("you")}       {user}");
            lines.Add($"    {Ansi.Muted("assistant")} {Ansi.Muted(assistant)}");
            lines.Add("");
        }

        lines.Add(ConsoleTheme.RuleText());
        lines.Add(Ansi.Muted("/show <n> shows one turn  /jump <n> replays from that turn  /tail <n> replays recent turns  Esc or /resume returns"));
        return lines;
    }

    private static void ShowTurn(IReadOnlyList<string> args)
    {
        var turn = FindTurn(args.FirstOrDefault());
        if (turn is null)
        {
            return;
        }

        RenderTurn(turn);
    }

    private static void JumpToTurn(IReadOnlyList<string> args)
    {
        var turns = GetCurrentTurns();
        if (!TryParseTurnNumber(args.FirstOrDefault(), turns.Count, out var turnNumber))
        {
            return;
        }

        OpenTranscript(
            $"History from turn {turnNumber}",
            RenderTurnsToLines(turns.Where(turn => turn.Number >= turnNumber)),
            startAtBottom: false);
    }

    private static void TailTurns(IReadOnlyList<string> args)
    {
        var turns = GetCurrentTurns();
        var count = ParsePositiveInt(args.FirstOrDefault(), defaultValue: Math.Min(5, turns.Count));
        var start = Math.Max(1, turns.Count - count + 1);
        JumpToTurn([start.ToString()]);
    }

    private static void OpenConversationScrollback(Action? redrawPrompt = null)
    {
        var turns = GetCurrentTurns();
        if (turns.Count == 0)
        {
            PrintStatus("No conversation history in the current session.");
            redrawPrompt?.Invoke();
            return;
        }

        OpenTranscript("Conversation history", RenderTurnsToLines(turns), startAtBottom: true, redrawPrompt);
    }

    private static SessionTurn? FindTurn(string? rawNumber)
    {
        var turns = GetCurrentTurns();
        if (!TryParseTurnNumber(rawNumber, turns.Count, out var turnNumber))
        {
            return null;
        }

        return turns.First(turn => turn.Number == turnNumber);
    }

    private static bool TryParseTurnNumber(string? rawNumber, int maxTurn, out int turnNumber)
    {
        turnNumber = 0;
        if (!int.TryParse(rawNumber, out var parsed) || parsed < 1 || parsed > maxTurn)
        {
            Console.WriteLine(Ansi.Warning(maxTurn == 0
                ? "Current session has no turns."
                : $"Usage: /show <1-{maxTurn}> or /jump <1-{maxTurn}>"));
            return false;
        }

        turnNumber = parsed;
        return true;
    }

    private static int ParsePositiveInt(string? raw, int defaultValue)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }

    private static IReadOnlyList<SessionTurn> GetCurrentTurns()
    {
        var session = new SessionStore().GetOrCreateCurrent();
        var turns = new List<SessionTurn>();
        AgentMessage? pendingUser = null;
        foreach (var message in session.Messages)
        {
            if (message.Role == "user")
            {
                if (pendingUser is not null)
                {
                    turns.Add(new SessionTurn(turns.Count + 1, pendingUser, null));
                }

                pendingUser = message;
                continue;
            }

            if (message.Role == "assistant" && pendingUser is not null)
            {
                turns.Add(new SessionTurn(turns.Count + 1, pendingUser, message));
                pendingUser = null;
            }
        }

        if (pendingUser is not null)
        {
            turns.Add(new SessionTurn(turns.Count + 1, pendingUser, null));
        }

        return turns;
    }

    private static void RenderTurn(SessionTurn turn)
    {
        OpenTranscript($"Turn {turn.Number}", RenderTurnsToLines([turn]), startAtBottom: false);
    }

    private static void AppendAssistantResponse(string response)
    {
        var turns = GetCurrentTurns();
        if (turns.Count == 0)
        {
            PrintStatus("No conversation to append to.");
            return;
        }

        OpenTranscript(
            "Conversation history",
            RenderTurnsToLines(turns),
            startAtBottom: true);
    }

    private static void OpenTranscript(string title, IReadOnlyList<string> lines, bool startAtBottom, Action? redrawPrompt = null)
    {
        _transcriptLines = BuildTranscriptFrame(title, lines);
        _transcriptOffset = startAtBottom ? Math.Max(0, _transcriptLines.Count - TranscriptPageHeight()) : 0;
        _transcriptMode = true;
        RenderTranscriptViewport(redrawPrompt);
    }

    private static void ScrollTranscript(int delta, Action redrawPrompt)
    {
        if (!_transcriptMode)
        {
            OpenConversationScrollback(redrawPrompt);
            return;
        }

        _transcriptOffset += delta;
        RenderTranscriptViewport(redrawPrompt);
    }

    private static void ResumeDashboard()
    {
        _transcriptMode = false;
        _transcriptLines = [];
        _transcriptOffset = 0;
        InteractiveScreen.Clear();
        PrintDashboard();
    }

    private static void RenderTranscriptViewport(Action? redrawPrompt = null)
    {
        var height = TranscriptPageHeight();
        _transcriptOffset = Math.Clamp(_transcriptOffset, 0, Math.Max(0, _transcriptLines.Count - height));
        InteractiveScreen.Clear();
        foreach (var line in _transcriptLines.Skip(_transcriptOffset).Take(height))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(ConsoleTheme.RuleText());
        ConsoleTheme.Hint(
            $"lines {_transcriptOffset + 1}-{Math.Min(_transcriptLines.Count, _transcriptOffset + height)} / {_transcriptLines.Count}",
            "mouse wheel scrolls history in place",
            "Esc or /resume returns");
        Console.WriteLine();

        redrawPrompt?.Invoke();
    }

    private static IReadOnlyList<string> BuildTranscriptFrame(string title, IReadOnlyList<string> lines)
    {
        var framed = new List<string>
        {
            $"{ConsoleTheme.Badge("history")} {Ansi.Bold(title)}",
            ConsoleTheme.RuleText(),
        };
        framed.AddRange(lines);
        return framed;
    }

    private static int TranscriptPageHeight()
    {
        if (Console.IsOutputRedirected)
        {
            return int.MaxValue;
        }

        try
        {
            return Math.Max(8, Console.WindowHeight - 6);
        }
        catch (IOException)
        {
            return 20;
        }
    }

    private static IReadOnlyList<string> RenderTurnsToLines(IEnumerable<SessionTurn> turns)
    {
        var lines = new List<string>();
        foreach (var turn in turns)
        {
            lines.Add("");
            lines.Add($"{ConsoleTheme.Badge($"turn:{turn.Number}")} {Ansi.Bold("you")}");
            lines.Add(ConsoleTheme.RuleText());
            lines.AddRange(MarkdownConsoleRenderer.RenderToLines(turn.User.Content));
            lines.Add("");
            lines.Add($"{ConsoleTheme.Badge($"turn:{turn.Number}")} {Ansi.Bold("assistant")}");
            lines.Add(ConsoleTheme.RuleText());
            lines.AddRange(turn.Assistant is null
                ? [Ansi.Muted("(no assistant response)")]
                : MarkdownConsoleRenderer.RenderToLines(turn.Assistant.Content));
            lines.Add(ConsoleTheme.RuleText());
        }

        return lines;
    }

    private static string OneLine(string text, int maxLength)
    {
        var value = text.ReplaceLineEndings(" ").Trim();
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static void PrintRuntimePanel()
    {
        var config = ConfigLoader.Load();
        var session = new SessionStore().GetOrCreateCurrent();
        var screenMode = InteractiveScreen.IsAlternateScreenActive ? "alternate" : "inline";
        ConsoleTheme.Panel("Runtime", [
            ("model", $"{config.Model.Provider}/{config.Model.Name}"),
            ("session", $"{session.Id} ({session.Messages.Count} messages)"),
            ("workspace", config.Sandbox.Workspace ?? Environment.CurrentDirectory),
            ("mounts", config.Sandbox.Mounts.Count.ToString()),
            ("agent shell", "VAShell policy"),
            ("ui", screenMode),
            ("uptime", FormatDuration(DateTimeOffset.Now - StartedAt)),
        ]);
    }

    private static void PrintShortcuts()
    {
        ConsoleTheme.Section("Shortcuts");
        ConsoleTheme.Pair("Mouse wheel", "scroll history without leaving the prompt");
        ConsoleTheme.Pair("Up/Down", "browse input history");
        ConsoleTheme.Pair("Left/Right", "move cursor");
        ConsoleTheme.Pair("Home/End", "jump to line boundary");
        ConsoleTheme.Pair("Tab", "complete slash commands");
        ConsoleTheme.Pair("Ctrl+C", "cancel active model/tool/shell turn");
        ConsoleTheme.Pair("Ctrl+D", "exit from an empty prompt");
        ConsoleTheme.Pair("\\", "continue input on the next line");
        ConsoleTheme.Pair("//", "run the rest of the line in the real shell");
        ConsoleTheme.Pair("/history", "list turn numbers in the current session");
        ConsoleTheme.Pair("/jump n", "clear and replay conversation from turn n");
        ConsoleTheme.Pair("Esc", "return to dashboard from history");
        ConsoleTheme.Pair("/resume", "return to dashboard");
    }

    private static string? ReadUserInput(LineEditor editor)
    {
        var input = editor.ReadLine(Prompt(), PromptVisible(), escapeReturnsImmediately: () => _transcriptMode);
        if (input is null)
        {
            return null;
        }

        if (input.Trim() == "/paste")
        {
            return ReadPasteBlock(editor);
        }

        if (!input.EndsWith('\\'))
        {
            return input;
        }

        var lines = new List<string> { input[..^1] };
        while (true)
        {
            var continuation = editor.ReadLine(ContinuationPrompt(), ContinuationPromptVisible());
            if (continuation is null)
            {
                return string.Join(Environment.NewLine, lines);
            }

            if (continuation.EndsWith('\\'))
            {
                lines.Add(continuation[..^1]);
                continue;
            }

            lines.Add(continuation);
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static string ReadPasteBlock(LineEditor editor)
    {
        ConsoleTheme.Section("Paste");
        ConsoleTheme.Hint("Finish with a single . on its own line.");
        var lines = new List<string>();
        while (true)
        {
            var line = editor.ReadLine(ContinuationPrompt(), ContinuationPromptVisible());
            if (line is null || line == ".")
            {
                break;
            }

            lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Prompt() => $"{Ansi.Accent("auxim")}{Ansi.Muted(" >")} ";

    private static string PromptVisible() => "auxim > ";

    private static string ContinuationPrompt() => $"{Ansi.Muted("     >")} ";

    private static string ContinuationPromptVisible() => "     > ";

    private static void PrintStatus(string text)
    {
        Console.WriteLine($"{ConsoleTheme.Badge("status")} {Ansi.Muted(text)}");
    }

    private static void PrintTurnHeader(string role, string status)
    {
        Console.WriteLine();
        Console.WriteLine($"{ConsoleTheme.Badge(role)} {Ansi.Muted(status)}");
        ConsoleTheme.Rule();
    }

    private static void PrintTurnFooter(DateTimeOffset started, int toolEvents)
    {
        Console.WriteLine();
        ConsoleTheme.Rule();
        ConsoleTheme.Hint($"done in {FormatDuration(DateTimeOffset.Now - started)}", $"{toolEvents} tool events");
        Console.WriteLine();
    }

    private static async Task RunShellEscapeAsync(string command)
    {
        using var turn = new CancellationTokenSource();
        _currentTurn = turn;
        var started = DateTimeOffset.Now;
        try
        {
            Console.WriteLine();
            Console.WriteLine($"{ConsoleTheme.Badge("shell")} {Ansi.Muted(command)} {Ansi.Muted(started.ToString("HH:mm:ss"))}");
            ConsoleTheme.Rule();
            await ShellEscapeRunner.RunAsync(command, turn.Token);
            ConsoleTheme.Rule();
            ConsoleTheme.Hint($"done in {FormatDuration(DateTimeOffset.Now - started)}");
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine(Ansi.Warning("Cancelled shell command."));
            Console.WriteLine();
        }
        catch (Exception exception)
        {
            Console.WriteLine(Ansi.Error($"Shell error: {exception.Message}"));
            Console.WriteLine();
        }
        finally
        {
            _currentTurn = null;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:N0} ms"
            : $"{duration.TotalSeconds:N1} s";
    }

    private static IReadOnlyList<string> SplitCommand(string input)
    {
        return CommandTokenizer.Tokenize(input);
    }
}

internal sealed record SessionTurn(
    int Number,
    AgentMessage User,
    AgentMessage? Assistant);
