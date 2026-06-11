using System.Globalization;
using System.Text;

namespace Auxim.Cli.Interactive;

internal sealed class LineEditor
{
    private readonly IReadOnlyList<string> _history;
    private readonly Func<string, IReadOnlyList<string>> _completionProvider;
    private readonly Action<int, Action>? _scrollHandler;
    private int _lastRenderWidth;
    private int _lastRenderLines = 1;
    private int _lastCursorLine;

    public LineEditor(
        IReadOnlyList<string> history,
        Func<string, IReadOnlyList<string>> completionProvider,
        Action<int, Action>? scrollHandler = null)
    {
        _history = history;
        _completionProvider = completionProvider;
        _scrollHandler = scrollHandler;
    }

    public string? ReadLine(string prompt, string visiblePrompt, Func<bool>? escapeReturnsImmediately = null)
    {
        if (Console.IsInputRedirected)
        {
            Console.Write(prompt);
            return Console.ReadLine();
        }

        Console.Write(prompt);
        _lastRenderWidth = DisplayWidth(visiblePrompt);
        _lastRenderLines = 1;
        _lastCursorLine = 0;
        using var inputScope = TerminalInput.Apply(TerminalInputPolicy.LineEditor);
        var buffer = new StringBuilder();
        var cursor = 0;
        var historyIndex = _history.Count;

        while (true)
        {
            var input = TerminalInput.Read(TerminalInputPolicy.LineEditor);
            if (input.Kind == TerminalInputEventKind.Ignored)
            {
                continue;
            }

            if (input.Kind == TerminalInputEventKind.MouseWheel)
            {
                _scrollHandler?.Invoke(input.WheelDelta, () =>
                {
                    ResetRenderState();
                    Render(prompt, visiblePrompt, buffer, cursor);
                });
                continue;
            }

            var key = input.Key;
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    if (escapeReturnsImmediately?.Invoke() == true)
                    {
                        return "/resume";
                    }

                    break;
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();
                case ConsoleKey.D when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    if (buffer.Length == 0)
                    {
                        Console.WriteLine();
                        return null;
                    }

                    break;
                case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    Console.WriteLine();
                    return "/exit";
                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.Remove(cursor - 1, 1);
                        cursor--;
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.Delete:
                    if (cursor < buffer.Length)
                    {
                        buffer.Remove(cursor, 1);
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.LeftArrow:
                    if (cursor > 0)
                    {
                        cursor--;
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length)
                    {
                        cursor++;
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.Home:
                    cursor = 0;
                    Render(prompt, visiblePrompt, buffer, cursor);
                    break;
                case ConsoleKey.End:
                    cursor = buffer.Length;
                    Render(prompt, visiblePrompt, buffer, cursor);
                    break;
                case ConsoleKey.UpArrow:
                    if (_history.Count > 0 && historyIndex > 0)
                    {
                        historyIndex--;
                        ReplaceBuffer(buffer, _history[historyIndex], ref cursor);
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.DownArrow:
                    if (historyIndex < _history.Count)
                    {
                        historyIndex++;
                        ReplaceBuffer(buffer, historyIndex == _history.Count ? "" : _history[historyIndex], ref cursor);
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
                case ConsoleKey.Tab:
                    Complete(prompt, visiblePrompt, buffer, ref cursor);
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                        Render(prompt, visiblePrompt, buffer, cursor);
                    }

                    break;
            }
        }
    }

    private void Complete(string prompt, string visiblePrompt, StringBuilder buffer, ref int cursor)
    {
        var input = buffer.ToString();
        var matches = _completionProvider(input);
        if (matches.Count == 0)
        {
            return;
        }

        if (matches.Count == 1)
        {
            ReplaceBuffer(buffer, matches[0] + (input.Contains(' ') ? "" : " "), ref cursor);
            Render(prompt, visiblePrompt, buffer, cursor);
            return;
        }

        Console.WriteLine();
        Console.WriteLine(string.Join("  ", matches.Select(Ansi.Accent)));
        // Cursor is now 2 lines below the input — update tracking so the
        // next Render moves back up past suggestions + old input line.
        _lastCursorLine += 2;
        _lastRenderLines += 2;
        // Don't call Render — suggestions remain visible until next keystroke.
    }

    private static void ReplaceBuffer(StringBuilder buffer, string value, ref int cursor)
    {
        buffer.Clear();
        buffer.Append(value);
        cursor = buffer.Length;
    }

    private void Render(string prompt, string visiblePrompt, StringBuilder buffer, int cursor)
    {
        var text = buffer.ToString();
        var terminalWidth = TerminalWidth();
        var fullWidth = DisplayWidth(visiblePrompt) + DisplayWidth(text);
        var newLines = Math.Max(1, (fullWidth + terminalWidth - 1) / terminalWidth);

        if (Ansi.ControlSequencesEnabled)
        {
            // Move cursor to start of previous render area
            // _lastCursorLine is the 0-based line within the previous block where the cursor sits
            if (_lastCursorLine > 0)
            {
                Console.Write($"\u001b[{_lastCursorLine}A");
            }

            Console.Write("\r");
            // Clear from cursor to end of screen — clears all wrapped lines
            Console.Write("\u001b[0J");

            Console.Write(prompt);
            Console.Write(text);

            // Compute cursor position within the newly rendered block
            var cursorOffset = DisplayWidth(visiblePrompt) + DisplayWidth(text[..cursor]);
            var cursorLine = cursorOffset / terminalWidth;
            var cursorCol = cursorOffset % terminalWidth;

            // Move cursor from end-of-text back to correct position
            var linesFromEnd = newLines - 1 - cursorLine;
            if (linesFromEnd > 0)
            {
                Console.Write($"\u001b[{linesFromEnd}A");
            }

            Console.Write("\r");
            if (cursorCol > 0)
            {
                Console.Write($"\u001b[{cursorCol}C");
            }

            _lastRenderWidth = fullWidth;
            _lastRenderLines = newLines;
            _lastCursorLine = cursorLine;
            return;
        }

        // Non-ANSI fallback — best-effort single-line redraw
        Console.Write('\r');
        Console.Write(prompt);
        Console.Write(text);
        if (_lastRenderWidth > fullWidth)
        {
            Console.Write(new string(' ', _lastRenderWidth - fullWidth));
        }

        Console.Write('\r');
        Console.Write(prompt);
        if (cursor > 0)
        {
            Console.Write(text[..cursor]);
        }

        _lastRenderWidth = fullWidth;
    }

    private void ResetRenderState()
    {
        _lastRenderWidth = 0;
        _lastRenderLines = 1;
        _lastCursorLine = 0;
    }

    private static int TerminalWidth()
    {
        try
        {
            return Math.Max(Console.WindowWidth, 1);
        }
        catch (IOException)
        {
            return 80;
        }
    }

    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneWidth(rune);
        }

        return width;
    }

    private static int RuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format
            or UnicodeCategory.Control)
        {
            return 0;
        }

        return IsWide(rune.Value) ? 2 : 1;
    }

    private static bool IsWide(int value)
    {
        return value is >= 0x1100 and <= 0x115f
            or >= 0x231a and <= 0x231b
            or >= 0x2329 and <= 0x232a
            or >= 0x23e9 and <= 0x23ec
            or >= 0x23f0 and <= 0x23f0
            or >= 0x23f3 and <= 0x23f3
            or >= 0x25fd and <= 0x25fe
            or >= 0x2614 and <= 0x2615
            or >= 0x2648 and <= 0x2653
            or >= 0x267f and <= 0x267f
            or >= 0x2693 and <= 0x2693
            or >= 0x26a1 and <= 0x26a1
            or >= 0x26aa and <= 0x26ab
            or >= 0x26bd and <= 0x26be
            or >= 0x26c4 and <= 0x26c5
            or >= 0x26ce and <= 0x26ce
            or >= 0x26d4 and <= 0x26d4
            or >= 0x26ea and <= 0x26ea
            or >= 0x26f2 and <= 0x26f3
            or >= 0x26f5 and <= 0x26f5
            or >= 0x26fa and <= 0x26fa
            or >= 0x26fd and <= 0x26fd
            or >= 0x2705 and <= 0x2705
            or >= 0x270a and <= 0x270b
            or >= 0x2728 and <= 0x2728
            or >= 0x274c and <= 0x274c
            or >= 0x274e and <= 0x274e
            or >= 0x2753 and <= 0x2755
            or >= 0x2757 and <= 0x2757
            or >= 0x2795 and <= 0x2797
            or >= 0x27b0 and <= 0x27b0
            or >= 0x27bf and <= 0x27bf
            or >= 0x2b1b and <= 0x2b1c
            or >= 0x2b50 and <= 0x2b50
            or >= 0x2b55 and <= 0x2b55
            or >= 0x2e80 and <= 0xa4cf
            or >= 0xac00 and <= 0xd7a3
            or >= 0xf900 and <= 0xfaff
            or >= 0xfe10 and <= 0xfe19
            or >= 0xfe30 and <= 0xfe6f
            or >= 0xff00 and <= 0xff60
            or >= 0xffe0 and <= 0xffe6
            or >= 0x1f300 and <= 0x1f64f
            or >= 0x1f900 and <= 0x1f9ff
            or >= 0x20000 and <= 0x3fffd;
    }
}
