namespace Auxim.Cli.Interactive;

internal static class TerminalMouse
{
    private static readonly object Sync = new();
    private static bool _alternateScrollEnabled;

    public static void Reset()
    {
        if (!Ansi.ControlSequencesEnabled)
        {
            return;
        }

        lock (Sync)
        {
            WriteAndFlush("\u001b[?1007l\u001b[?1006l\u001b[?1003l\u001b[?1002l\u001b[?1000l");
            _alternateScrollEnabled = false;
        }
    }

    public static void EnableAlternateScroll()
    {
        if (!Ansi.ControlSequencesEnabled)
        {
            return;
        }

        lock (Sync)
        {
            if (_alternateScrollEnabled)
            {
                return;
            }

            // 1007 translates wheel movement into cursor keys without taking
            // click and drag events away from the terminal's text selection.
            WriteAndFlush("\u001b[?1003l\u001b[?1002l\u001b[?1000l\u001b[?1006l\u001b[?1007h");
            _alternateScrollEnabled = true;
        }
    }

    public static void DisableAlternateScroll()
    {
        if (!Ansi.ControlSequencesEnabled)
        {
            return;
        }

        lock (Sync)
        {
            if (!_alternateScrollEnabled)
            {
                return;
            }

            WriteAndFlush("\u001b[?1007l");
            _alternateScrollEnabled = false;
        }
    }

    public static IDisposable SuspendAlternateScroll()
    {
        bool restore;
        lock (Sync)
        {
            restore = _alternateScrollEnabled;
        }

        DisableAlternateScroll();
        return new AlternateScrollSuspension(restore);
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
            // stdout may already be closed during shutdown.
        }
    }

    private sealed class AlternateScrollSuspension(bool restore) : IDisposable
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
                EnableAlternateScroll();
            }

            _disposed = true;
        }
    }
}
