using System.Text.Json;
using Auxim.Core.Approval;

namespace Auxim.Cli.Interactive;

internal static class ApprovalRenderer
{
    public static async Task<ApprovalResponse> PromptAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scrollSuspension = TerminalMouse.SuspendAlternateScroll();
        var width = Math.Max(20, Math.Min(ConsoleTheme.ConsoleWidth(), 96));

        Console.WriteLine();
        Console.WriteLine(Ansi.Warning($"{TerminalGlyphs.TopLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.TopRight}"));
        WriteBoxLines(
            $"{Ansi.Bold("SAFETY REVIEW")}  {Ansi.Muted("Tool approval required")}",
            width);
        WriteBoxLines(
            $"{ConsoleTheme.Chip("tool", request.ToolName)}  {Ansi.WarningBackground(" resource access ")}",
            width);
        WriteBoxLines($"{Ansi.Muted("request")}  {request.RequestId}", width);
        WriteBoxLines(
            $"{Ansi.Muted("risk")}  This tool can modify files, run commands, or change state.",
            width);
        Console.WriteLine(Ansi.Warning($"{TerminalGlyphs.TeeLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.TeeRight}"));
        WriteBoxLines($"{Ansi.Cyan(TerminalGlyphs.Section)} {Ansi.Bold("Arguments")}", width);
        foreach (var arg in FormatArgumentsForDisplay(request.Arguments))
        {
            WriteBoxLines($"  {Ansi.Muted(arg.Key.PadRight(14))} {arg.Value}", width);
        }

        WriteBoxLines($"{Ansi.Cyan(TerminalGlyphs.Section)} {Ansi.Bold("Resources")}", width);
        foreach (var access in request.ResourceAccesses)
        {
            WriteBoxLines($"  {access.Action}  {access.Resource}", width);
        }

        Console.WriteLine(Ansi.Warning($"{TerminalGlyphs.TeeLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.TeeRight}"));
        WriteBoxLines($"{Ansi.Cyan(TerminalGlyphs.Section)} {Ansi.Bold("Decision")}", width);
        var options = new[]
        {
            (Label: "Allow once", Tone: "success", Value: 1),
            (Label: "Always allow", Tone: "warning", Value: 2),
            (Label: "Deny and give feedback", Tone: "danger", Value: 3),
        };

        var optionsTop = Console.CursorTop;
        var selected = 0;

        RenderOptions(options, selected, width);
        RenderHint(width);
        Console.WriteLine(Ansi.Warning($"{TerminalGlyphs.BottomLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.BottomRight}"));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = await TerminalInput.ReadAsync(
                TerminalInputPolicy.Approval,
                cancellationToken);
            if (input.Kind != TerminalInputEventKind.Key)
            {
                continue;
            }

            var key = input.Key;
            switch (key.Key)
            {
                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    selected = 0;
                    RedrawOptions(options, optionsTop, selected, width);
                    break;
                case ConsoleKey.D2:
                case ConsoleKey.NumPad2:
                    selected = 1;
                    RedrawOptions(options, optionsTop, selected, width);
                    break;
                case ConsoleKey.D3:
                case ConsoleKey.NumPad3:
                    selected = 2;
                    RedrawOptions(options, optionsTop, selected, width);
                    break;
                case ConsoleKey.UpArrow:
                    if (selected > 0)
                    {
                        selected--;
                        RedrawOptions(options, optionsTop, selected, width);
                    }

                    break;
                case ConsoleKey.DownArrow:
                    if (selected < options.Length - 1)
                    {
                        selected++;
                        RedrawOptions(options, optionsTop, selected, width);
                    }

                    break;
                case ConsoleKey.Enter:
                    return CompleteSelection(selected, options.Length, optionsTop);
            }
        }
    }

    private static ApprovalResponse CompleteSelection(
        int selected,
        int optionCount,
        int optionsTop)
    {
        ClearOptionsBlock(optionCount, optionsTop);
        switch (selected)
        {
            case 0:
                Console.WriteLine(Ansi.Success("Allowed. Continuing."));
                Console.WriteLine();
                return ApprovalResponse.Allow();
            case 1:
                Console.WriteLine(Ansi.Success("Always allowed. Future calls to this tool will not prompt."));
                Console.WriteLine();
                return ApprovalResponse.Allow(remember: true);
            default:
                Console.Write(Ansi.Warning("Feedback for the model: "));
                var reason = Console.ReadLine();
                reason = string.IsNullOrWhiteSpace(reason)
                    ? "User denied this tool call."
                    : reason.Trim();
                Console.WriteLine(Ansi.Error("Denied. Feedback was sent to the model."));
                Console.WriteLine();
                return ApprovalResponse.Deny(reason);
        }
    }

    private static void RenderOptions((string Label, string Tone, int Value)[] options, int selected, int width)
    {
        for (var i = 0; i < options.Length; i++)
        {
            var marker = i == selected ? ">" : " ";
            var label = $"  {marker}  {i + 1}  {options[i].Label.PadRight(34)}";
            var rendered = i == selected
                ? SelectedOption(label, options[i].Tone)
                : Ansi.Muted(label);
            Console.WriteLine(BoxLine(rendered, width));
        }
    }

    private static void RedrawOptions((string Label, string Tone, int Value)[] options, int optionsTop, int selected, int width)
    {
        ClearOptionsBlock(options.Length, optionsTop);
        RenderOptions(options, selected, width);
        RenderHint(width);
        Console.WriteLine(Ansi.Warning($"{TerminalGlyphs.BottomLeft}{TerminalGlyphs.HorizontalLine(width - 2)}{TerminalGlyphs.BottomRight}"));
    }

    private static void ClearOptionsBlock(int optionCount, int optionsTop)
    {
        if (Ansi.ControlSequencesEnabled)
        {
            Console.Write("\r");
            Console.Write($"\u001b[{optionCount + 2}A");
            Console.Write("\u001b[0J");
            return;
        }

        try
        {
            Console.SetCursorPosition(0, optionsTop);
        }
        catch
        {
            Console.WriteLine();
        }
    }

    private static void RenderHint()
    {
        RenderHint(Math.Min(ConsoleTheme.ConsoleWidth(), 96));
    }

    private static void RenderHint(int width)
    {
        WriteBoxLines(Ansi.Muted("  Up/Down select   Enter confirm   1/2/3 select then Enter"), width);
    }

    private static string SelectedOption(string text, string tone)
    {
        return tone switch
        {
            "success" => Ansi.AccentBackground(text),
            "warning" => Ansi.WarningBackground(text),
            "danger" => Ansi.ErrorBackground(text),
            _ => Ansi.Reverse(text),
        };
    }

    private static string Pad(string text, int width)
    {
        return text.Length >= width ? text[..width] : text + new string(' ', width - text.Length);
    }

    private static string BoxLine(string content, int width)
    {
        var available = width - 4;
        var fitted = ConsoleTheme.FitVisible(content, available);
        var visible = StripAnsi(fitted);
        var padded = visible.Length >= available ? fitted : fitted + new string(' ', available - visible.Length);
        return $"{Ansi.Warning(TerminalGlyphs.Vertical)} {padded} {Ansi.Warning(TerminalGlyphs.Vertical)}";
    }

    private static void WriteBoxLines(string content, int width)
    {
        foreach (var line in ConsoleTheme.WrapVisible(content, width - 4))
        {
            Console.WriteLine(BoxLine(line, width));
        }
    }

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

    private static IReadOnlyList<(string Key, string Value)> FormatArgumentsForDisplay(
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = new List<(string Key, string Value)>();
        foreach (var (key, value) in arguments)
        {
            if (value is null)
            {
                result.Add((key, Ansi.Muted("(null)")));
                continue;
            }

            var displayKey = FormatKeyName(key);
            var displayValue = FormatValue(value);
            result.Add((displayKey, displayValue));
        }

        return result;
    }

    private static string FormatKeyName(string key)
    {
        return key switch
        {
            "command" => "command",
            "timeoutSeconds" => "timeoutSeconds",
            "path" => "path",
            "content" => "content",
            "oldText" => "oldText",
            "newText" => "newText",
            "text" => "text",
            "maxResults" => "maxResults",
            "pattern" => "pattern",
            "url" => "url",
            "name" => "name",
            "description" => "description",
            "arguments" => "arguments",
            "id" => "ID",
            "message" => "message",
            "reason" => "reason",
            "feedback" => "feedback",
            "instruction" => "instruction",
            _ => key,
        };
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return Ansi.Muted("(null)");
        }

        if (value is string str)
        {
            if (str.Length == 0)
            {
                return Ansi.Muted("(empty string)");
            }

            if (str.Length <= 120)
            {
                return Clean(str);
            }

            return $"{Clean(str[..120])}{Ansi.Muted("...")} ({str.Length} chars)";
        }

        if (value is bool b)
        {
            return b ? Ansi.Success("true") : Ansi.Error("false");
        }

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => FormatValue(je.GetString()),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => Ansi.Success("true"),
                JsonValueKind.False => Ansi.Error("false"),
                JsonValueKind.Object => je.GetRawText(),
                JsonValueKind.Array => $"[{string.Join(", ", je.EnumerateArray().Select(e => FormatValue(e)))}]",
                JsonValueKind.Null => Ansi.Muted("(null)"),
                _ => je.GetRawText(),
            };
        }

        var text = value.ToString() ?? "";
        if (text.Length <= 120)
        {
            return text;
        }

        return $"{text[..120]}{Ansi.Muted("...")} ({text.Length} chars)";
    }

    private static string Clean(string s)
    {
        return s
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
