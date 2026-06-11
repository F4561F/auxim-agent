using System.Runtime.InteropServices;

namespace Auxim.Cli.Interactive;

internal sealed class InteractiveScreen : IDisposable
{
    private readonly bool _enabled;
    private bool _disposed;

    static InteractiveScreen()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FullCleanup();

        RegisterPosixSignal(PosixSignal.SIGTERM, 15);
        RegisterPosixSignal(PosixSignal.SIGHUP, 1);
        RegisterPosixSignal(PosixSignal.SIGINT, 2);
    }

    private InteractiveScreen(bool enabled)
    {
        _enabled = enabled;
    }

    public static InteractiveScreen Enter()
    {
        var enabled = ShouldUseAlternateScreen();
        if (!enabled)
        {
            IsAlternateScreenActive = false;
            return new InteractiveScreen(enabled: false);
        }

        WriteAndFlush("\u001b[?1049h\u001b[?25h");
        WriteAndFlush("\u001b]0;Auxim\u0007");
        TerminalMouse.SetTracking(enabled: true);

        Clear();
        IsAlternateScreenActive = true;
        return new InteractiveScreen(enabled: true);
    }

    public static bool IsAlternateScreenActive { get; private set; }

    public static void Clear()
    {
        if (Ansi.ControlSequencesEnabled)
        {
            WriteAndFlush("\u001b[2J\u001b[H");
        }
        else
        {
            Console.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_enabled)
        {
            TerminalMouse.SetTracking(enabled: false);

            WriteAndFlush("\u001b[?25h\u001b[?1049l");
            IsAlternateScreenActive = false;
        }

        _disposed = true;
    }

    internal static void EmergencyCleanup()
    {
        if (!IsAlternateScreenActive || !Ansi.ControlSequencesEnabled)
        {
            return;
        }

        TerminalMouse.SetTracking(enabled: false);
        WriteAndFlush("\u001b[?25h\u001b[?1049l");
        IsAlternateScreenActive = false;
    }

    private static void FullCleanup()
    {
        if (!Ansi.ControlSequencesEnabled)
        {
            return;
        }

        WriteAndFlush("\u001b[?1006l\u001b[?1000l\u001b[?25h\u001b[?1049l");
        IsAlternateScreenActive = false;
    }

    private static void RegisterPosixSignal(PosixSignal signal, int signum)
    {
        try
        {
            PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                FullCleanup();
                Console.ResetColor();
                Environment.Exit(128 + signum);
            });
        }
        catch (NotSupportedException)
        {
            // Some platforms, such as Windows, do not support these signals.
        }
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

    private static bool ShouldUseAlternateScreen()
    {
        if (!Ansi.ControlSequencesEnabled || Console.IsInputRedirected)
        {
            return false;
        }

        var disabled = Environment.GetEnvironmentVariable("AUXIM_NO_ALT_SCREEN");
        return !string.Equals(disabled, "1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(disabled, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
