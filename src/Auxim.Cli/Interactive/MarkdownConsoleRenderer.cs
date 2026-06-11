namespace Auxim.Cli.Interactive;

internal static class MarkdownConsoleRenderer
{
    public static void Render(string markdown)
    {
        foreach (var line in RenderToLines(markdown))
        {
            Console.WriteLine(line);
        }
    }

    public static IReadOnlyList<string> RenderToLines(string markdown)
    {
        var inCodeBlock = false;
        var codeLanguage = "";
        var lines = new List<string>();

        foreach (var rawLine in SplitLines(markdown))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                codeLanguage = inCodeBlock ? line[3..].Trim() : "";
                lines.Add(inCodeBlock
                    ? $"{ConsoleTheme.Badge("code")} {Ansi.Muted(codeLanguage.Length == 0 ? "text" : codeLanguage)}"
                    : ConsoleTheme.Badge("end"));
                continue;
            }

            if (inCodeBlock)
            {
                lines.Add($"  {line}");
                continue;
            }

            lines.AddRange(RenderLine(line));
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderLine(string line)
    {
        if (line.Length == 0)
        {
            return [""];
        }

        var trimmed = line.TrimStart();
        var indent = line.Length - trimmed.Length;

        if (trimmed is "---" or "***" or "___")
        {
            return [ConsoleTheme.RuleText()];
        }

        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return RenderHeading(trimmed);
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return [$"{Ansi.Muted("|")} {RenderInline(trimmed[2..])}"];
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return [$"{new string(' ', indent)}{Ansi.Accent("-")} {RenderInline(trimmed[2..])}"];
        }

        var ordered = TryParseOrderedList(trimmed);
        if (ordered is not null)
        {
            return [$"{new string(' ', indent)}{Ansi.Accent(ordered.Value.Marker)} {RenderInline(ordered.Value.Text)}"];
        }

        return [RenderInline(line)];
    }

    private static IReadOnlyList<string> RenderHeading(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        var text = line[level..].Trim();
        if (text.Length == 0)
        {
            return [line];
        }

        if (level <= 2)
        {
            return ["", Ansi.Bold(Ansi.Accent(text)), ConsoleTheme.RuleText()];
        }

        return [Ansi.Bold(text)];
    }

    private static (string Marker, string Text)? TryParseOrderedList(string line)
    {
        var dot = line.IndexOf('.');
        if (dot <= 0 || dot + 1 >= line.Length || line[dot + 1] != ' ')
        {
            return null;
        }

        for (var index = 0; index < dot; index++)
        {
            if (!char.IsDigit(line[index]))
            {
                return null;
            }
        }

        return (line[..(dot + 1)], line[(dot + 2)..]);
    }

    private static string RenderInline(string text)
    {
        text = ReplaceDelimited(text, "`", value => Ansi.Accent(value));
        text = ReplaceDelimited(text, "**", value => Ansi.Bold(value));
        text = ReplaceDelimited(text, "__", value => Ansi.Bold(value));
        return text;
    }

    private static string ReplaceDelimited(string text, string delimiter, Func<string, string> render)
    {
        var output = new List<string>();
        var remaining = text;
        while (true)
        {
            var start = remaining.IndexOf(delimiter, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Add(remaining);
                break;
            }

            var contentStart = start + delimiter.Length;
            var end = remaining.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                output.Add(remaining);
                break;
            }

            output.Add(remaining[..start]);
            output.Add(render(remaining[contentStart..end]));
            remaining = remaining[(end + delimiter.Length)..];
        }

        return string.Concat(output);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
