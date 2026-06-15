namespace Auxim.Cli.Interactive;

internal static class ConsoleTheme
{
    private const int MaxWidth = 96;

    public static void Title(string title, string subtitle)
    {
        WriteWrapped($"{Ansi.Bold(Ansi.Accent(title))} {Ansi.Muted(subtitle)}", ConsoleWidth());
        Console.WriteLine(RuleText());
    }

    public static void Banner(string title, string subtitle)
    {
        Console.WriteLine();
        var width = Math.Min(ConsoleWidth(), MaxWidth);
        Console.WriteLine(Ansi.Accent($"{TerminalGlyphs.TopLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.TopRight}"));
        WriteBoxLines($"{Ansi.Bold(title.ToUpperInvariant())}  {Ansi.Muted(subtitle)}", width, Ansi.Accent);
        WriteBoxLines(Ansi.Muted("portable local agent for workspace automation"), width, Ansi.Accent);
        Console.WriteLine(Ansi.Accent($"{TerminalGlyphs.BottomLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.BottomRight}"));
    }

    public static void StatusBar(params (string Label, string Value)[] items)
    {
        Console.WriteLine();
        var width = ConsoleWidth();
        var line = $"  {string.Join("  ", items.Select(item => Chip(item.Label, item.Value)))}";
        if (VisibleLength(line) <= width)
        {
            Console.WriteLine(line);
            return;
        }

        foreach (var item in items)
        {
            var prefix = $"  {item.Label.PadRight(8)} ";
            var valueWidth = Math.Max(8, width - prefix.Length);
            var valueLines = WrapVisible(item.Value, valueWidth);
            Console.WriteLine($"{Ansi.Muted(prefix)}{Ansi.SoftBackground($" {valueLines[0]} ")}");
            foreach (var continuation in valueLines.Skip(1))
            {
                Console.WriteLine($"{new string(' ', prefix.Length)}{Ansi.SoftBackground($" {continuation} ")}");
            }
        }
    }

    public static void Section(string title)
    {
        Console.WriteLine();
        WriteWrapped($"{Ansi.Cyan(TerminalGlyphs.Section)} {Ansi.Bold(title)}", ConsoleWidth());
    }

    public static void Pair(string label, string value)
    {
        WriteWrapped($"{Ansi.Muted(label.PadRight(12))} {value}", ConsoleWidth(), "             ");
    }

    public static void Hint(params string[] hints)
    {
        WriteWrapped(Ansi.Muted(string.Join("  ", hints)), ConsoleWidth());
    }

    public static void Panel(string title, IEnumerable<(string Label, string Value)> rows)
    {
        Console.WriteLine();
        WriteWrapped($"{Ansi.Magenta(TerminalGlyphs.Section)} {Ansi.Bold(title)}", ConsoleWidth());
        var width = Math.Min(ConsoleWidth(), MaxWidth);
        var columns = width >= 88 ? 2 : 1;
        var materialized = rows.ToArray();
        var split = columns == 2 ? (materialized.Length + 1) / 2 : materialized.Length;
        var columnWidth = columns == 2 ? 39 : Math.Max(12, width - 2);
        for (var index = 0; index < split; index++)
        {
            var left = FormatPanelRows(materialized[index], columnWidth);
            if (columns == 1)
            {
                foreach (var line in left)
                {
                    Console.WriteLine(line);
                }

                continue;
            }

            var right = index + split < materialized.Length
                ? FormatPanelRows(materialized[index + split], columnWidth)
                : [PadVisible("", columnWidth)];
            var rowHeight = Math.Max(left.Count, right.Count);
            for (var rowIndex = 0; rowIndex < rowHeight; rowIndex++)
            {
                var leftLine = rowIndex < left.Count ? left[rowIndex] : PadVisible("", columnWidth);
                var rightLine = rowIndex < right.Count ? right[rowIndex] : PadVisible("", columnWidth);
                Console.WriteLine($"{leftLine}  {rightLine}");
            }
        }
    }

    public static void ActionList(IEnumerable<InteractiveCommand> commands)
    {
        var width = Math.Max(20, Math.Min(ConsoleWidth() - 2, MaxWidth));
        var columns = width >= 88 ? 2 : 1;
        var rows = commands.ToArray();
        var split = columns == 2 ? (rows.Length + 1) / 2 : rows.Length;
        var innerWidth = width - 4;

        Console.WriteLine($"  {Ansi.Muted($"{TerminalGlyphs.TopLeft}{TerminalGlyphs.HorizontalLine(innerWidth)}{TerminalGlyphs.TopRight}")}");
        for (var index = 0; index < split; index++)
        {
            var cellWidth = columns == 2 ? 39 : Math.Max(8, innerWidth - 2);
            var left = FormatActionLines(rows[index], index + 1, cellWidth);
            if (columns == 1)
            {
                foreach (var line in left)
                {
                    Console.WriteLine($"  {Ansi.Muted(TerminalGlyphs.Vertical)} {line}{Ansi.Muted($" {TerminalGlyphs.Vertical}")}");
                }

                continue;
            }

            var right = index + split < rows.Length
                ? FormatActionLines(rows[index + split], index + split + 1, cellWidth)
                : [PadVisible("", cellWidth)];
            var rowHeight = Math.Max(left.Count, right.Count);
            for (var rowIndex = 0; rowIndex < rowHeight; rowIndex++)
            {
                var leftLine = rowIndex < left.Count ? left[rowIndex] : PadVisible("", cellWidth);
                var rightLine = rowIndex < right.Count ? right[rowIndex] : PadVisible("", cellWidth);
                Console.WriteLine($"  {Ansi.Muted(TerminalGlyphs.Vertical)} {leftLine} {Ansi.Muted(TerminalGlyphs.Vertical)} {rightLine} {Ansi.Muted(TerminalGlyphs.Vertical)}");
            }
        }
        Console.WriteLine($"  {Ansi.Muted($"{TerminalGlyphs.BottomLeft}{TerminalGlyphs.HorizontalLine(innerWidth)}{TerminalGlyphs.BottomRight}")}");
    }

    public static string Pill(string text) => Ansi.Reverse($" {text} ");

    public static string Chip(string label, string value)
    {
        return $"{Ansi.Muted(label)} {Ansi.SoftBackground($" {value} ")}";
    }

    private static IReadOnlyList<string> FormatActionLines(InteractiveCommand command, int number, int width)
    {
        var index = Ansi.Muted(number.ToString("00"));
        var commandLabel = Ansi.Accent(command.Name);
        var firstPrefix = $"{index}  ";
        var first = $"{firstPrefix}{commandLabel}";
        var lines = new List<string> { PadVisible(first, width) };
        var descriptionWidth = Math.Max(8, width - 4);
        foreach (var line in WrapVisible(Ansi.Muted(command.Description), descriptionWidth))
        {
            lines.Add(PadVisible($"    {line}", width));
        }

        return lines;
    }

    private static IReadOnlyList<string> FormatPanelRows((string Label, string Value) row, int width)
    {
        var prefix = $"  {Ansi.Muted(row.Label.PadRight(12))} ";
        var valueWidth = Math.Max(8, width - 15);
        var valueLines = WrapVisible(row.Value, valueWidth).ToArray();
        if (valueLines.Length == 0)
        {
            return [PadVisible(prefix, width)];
        }

        var lines = new List<string>
        {
            PadVisible(prefix + valueLines[0], width),
        };
        foreach (var line in valueLines.Skip(1))
        {
            lines.Add(PadVisible(new string(' ', 15) + line, width));
        }

        return lines;
    }

    public static void CommandGrid(IEnumerable<InteractiveCommand> commands)
    {
        var width = Math.Min(ConsoleWidth(), MaxWidth);
        var columns = width >= 88 ? 2 : 1;
        var rows = commands
            .Select(command => $"{Ansi.Accent(command.Name.PadRight(16))} {command.Description}")
            .ToArray();

        if (columns == 1)
        {
            foreach (var row in rows)
            {
                WriteWrapped($"  {row}", width, "  ");
            }

            return;
        }

        var split = (rows.Length + 1) / 2;
        for (var index = 0; index < split; index++)
        {
            var left = StripAnsi(rows[index]);
            var leftRendered = rows[index];
            var rightRendered = index + split < rows.Length ? rows[index + split] : "";
            WriteWrapped($"  {leftRendered}{new string(' ', Math.Max(2, 44 - left.Length))}{rightRendered}", width, "  ");
        }
    }

    public static void Rule()
    {
        Console.WriteLine(RuleText());
    }

    public static string RuleText()
    {
        return Ansi.Muted(new string('-', Math.Min(ConsoleWidth(), MaxWidth)));
    }

    public static string Badge(string text)
    {
        return $"{Ansi.Muted("[")}{Ansi.Accent(text)}{Ansi.Muted("]")}";
    }

    public static int ConsoleWidth()
    {
        if (Console.IsOutputRedirected)
        {
            return 80;
        }

        try
        {
            return Math.Max(Console.WindowWidth - 1, 20);
        }
        catch (IOException)
        {
            return 80;
        }
    }

    public static string FitVisible(string text, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        var visible = StripAnsi(text);
        if (visible.Length <= width)
        {
            return text;
        }

        if (width == 1)
        {
            return visible[..1];
        }

        var suffix = TerminalGlyphs.Ellipsis;
        var prefixLength = Math.Max(0, width - suffix.Length);
        return visible[..Math.Min(visible.Length, prefixLength)] + suffix;
    }

    public static IReadOnlyList<string> WrapVisible(string text, int width)
    {
        if (width <= 0)
        {
            return [""];
        }

        if (VisibleLength(text) <= width)
        {
            return [text];
        }

        var visible = StripAnsi(text);
        var lines = new List<string>();
        var remaining = visible;
        while (remaining.Length > width)
        {
            var split = FindWrapPoint(remaining, width);
            lines.Add(remaining[..split].TrimEnd());
            remaining = remaining[split..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            lines.Add(remaining);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private static void WriteWrapped(string text, int width, string continuationPrefix = "")
    {
        var lines = WrapVisible(text, width);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index == 0 || continuationPrefix.Length == 0)
            {
                Console.WriteLine(lines[index]);
                continue;
            }

            WriteWrapped(continuationPrefix + lines[index], width);
        }
    }

    private static string PadVisible(string text, int width)
    {
        text = FitVisible(text, width);
        var visible = StripAnsi(text);
        return visible.Length >= width ? text : text + new string(' ', width - visible.Length);
    }

    private static string BoxLine(string content, int width)
    {
        var available = width - 4;
        return $"{Ansi.Accent(TerminalGlyphs.Vertical)} {PadVisible(content, available)} {Ansi.Accent(TerminalGlyphs.Vertical)}";
    }

    private static void WriteBoxLines(string content, int width, Func<string, string> border)
    {
        foreach (var line in WrapVisible(content, width - 4))
        {
            Console.WriteLine($"{border(TerminalGlyphs.Vertical)} {PadVisible(line, width - 4)} {border(TerminalGlyphs.Vertical)}");
        }
    }

    private static int FindWrapPoint(string text, int width)
    {
        var max = Math.Min(width, text.Length);
        for (var index = max; index > 0; index--)
        {
            if (char.IsWhiteSpace(text[index - 1]))
            {
                return index;
            }
        }

        return max;
    }

    private static int VisibleLength(string text) => StripAnsi(text).Length;

    private static string StripAnsi(string text)
    {
        var result = new List<char>();
        var inEscape = false;
        foreach (var character in text)
        {
            if (character == '\u001b')
            {
                inEscape = true;
                continue;
            }

            if (inEscape)
            {
                if (char.IsLetter(character))
                {
                    inEscape = false;
                }

                continue;
            }

            result.Add(character);
        }

        return new string(result.ToArray());
    }
}
