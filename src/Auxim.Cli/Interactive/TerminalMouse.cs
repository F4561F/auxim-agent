using System.Diagnostics;

namespace Auxim.Cli.Interactive;

internal static class TerminalMouse
{
    private static readonly object Sync = new();
    private static bool _trackingEnabled;
    private const int MouseSequenceTimeoutMs = 50;

    public static void SetTracking(bool enabled)
    {
        if (!Ansi.ControlSequencesEnabled)
        {
            return;
        }

        lock (Sync)
        {
            if (enabled == _trackingEnabled)
            {
                return;
            }

            WriteAndFlush(enabled
                ? "\u001b[?1000h\u001b[?1006h"
                : "\u001b[?1006l\u001b[?1000l");
            _trackingEnabled = enabled;
        }
    }

    public static IDisposable UseTracking(bool enabled)
    {
        bool restore;
        lock (Sync)
        {
            restore = _trackingEnabled;
        }

        SetTracking(enabled);
        return new TrackingScope(restore);
    }

    public static bool TryConsumeMouseEvent(ConsoleKeyInfo key, out int wheelDelta)
    {
        wheelDelta = 0;
        if (key.Key == ConsoleKey.Escape)
        {
            return TryReadMouseSequence(first: null, out var text) && TryParseMouseSequence(text, out wheelDelta);
        }

        // Some terminals can leave the ESC byte behind when input is read while
        // wheel events are still arriving. Consume the orphan SGR mouse tail.
        if (key.KeyChar == '[')
        {
            return TryReadMouseSequence(first: '[', out var text) && TryParseMouseSequence(text, out wheelDelta);
        }

        return false;
    }

    private static bool TryReadMouseSequence(char? first, out string text)
    {
        text = "";
        var sequence = new List<char>();
        if (first is { } firstCharacter)
        {
            sequence.Add(firstCharacter);
        }
        else
        {
            if (!WaitForKey(MouseSequenceTimeoutMs))
            {
                return false;
            }

            sequence.Add(Console.ReadKey(intercept: true).KeyChar);
        }

        if (sequence[0] != '[')
        {
            return false;
        }

        if (!WaitForKey(MouseSequenceTimeoutMs))
        {
            return false;
        }

        sequence.Add(Console.ReadKey(intercept: true).KeyChar);
        if (sequence[1] != '<')
        {
            return false;
        }

        while (WaitForKey(MouseSequenceTimeoutMs) && sequence.Count < 32)
        {
            var next = Console.ReadKey(intercept: true).KeyChar;
            sequence.Add(next);
            if (next is 'm' or 'M')
            {
                text = new string(sequence.ToArray());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMouseSequence(string text, out int wheelDelta)
    {
        wheelDelta = 0;
        if (!text.StartsWith("[<", StringComparison.Ordinal))
        {
            return false;
        }

        var end = text.IndexOf(';');
        if (end < 2 || !int.TryParse(text[2..end], out var button))
        {
            return true;
        }

        wheelDelta = button switch
        {
            64 => -1,
            65 => 1,
            _ => 0,
        };
        return true;
    }

    private static bool WaitForKey(int milliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            if (Console.KeyAvailable)
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return Console.KeyAvailable;
    }

    private static void WriteAndFlush(string text)
    {
        try
        {
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch
        {
            // stderr/stdout may already be closed during shutdown.
        }
    }

    private sealed class TrackingScope(bool restore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (restore)
            {
                SetTracking(enabled: true);
            }

            _disposed = true;
        }
    }
}
