namespace Auxim.Cli.Interactive;

internal enum TerminalInputEventKind
{
    Ignored,
    Key,
    MouseWheel,
}

internal readonly record struct TerminalInputEvent(
    TerminalInputEventKind Kind,
    ConsoleKeyInfo Key,
    int WheelDelta);

internal sealed class TerminalInputPolicy
{
    private readonly Func<ConsoleKeyInfo, bool> _acceptKey;

    private TerminalInputPolicy(bool acceptsMouseWheel, Func<ConsoleKeyInfo, bool> acceptKey)
    {
        AcceptsMouseWheel = acceptsMouseWheel;
        _acceptKey = acceptKey;
    }

    public bool AcceptsMouseWheel { get; }

    public bool AcceptsKey(ConsoleKeyInfo key) => _acceptKey(key);

    public static TerminalInputPolicy LineEditor { get; } = new(
        acceptsMouseWheel: true,
        acceptKey: _ => true);

    public static TerminalInputPolicy Approval { get; } = new(
        acceptsMouseWheel: false,
        acceptKey: key => key.Key is
            ConsoleKey.UpArrow
            or ConsoleKey.DownArrow
            or ConsoleKey.Enter
            or ConsoleKey.D1
            or ConsoleKey.D2
            or ConsoleKey.D3
            or ConsoleKey.NumPad1
            or ConsoleKey.NumPad2
            or ConsoleKey.NumPad3);
}

internal static class TerminalInput
{
    private static readonly Queue<ConsoleKeyInfo> PendingKeys = new();

    public static IDisposable Apply(TerminalInputPolicy policy)
    {
        return TerminalMouse.UseTracking(policy.AcceptsMouseWheel);
    }

    public static TerminalInputEvent Read(TerminalInputPolicy policy)
    {
        var key = ReadRawKey();
        if (TerminalMouse.TryConsumeMouseEvent(key, out var wheelDelta))
        {
            wheelDelta += DrainMouseWheelBurst();
            return policy.AcceptsMouseWheel && wheelDelta != 0
                ? new TerminalInputEvent(TerminalInputEventKind.MouseWheel, key, wheelDelta)
                : new TerminalInputEvent(TerminalInputEventKind.Ignored, key, 0);
        }

        return policy.AcceptsKey(key)
            ? new TerminalInputEvent(TerminalInputEventKind.Key, key, 0)
            : new TerminalInputEvent(TerminalInputEventKind.Ignored, key, 0);
    }

    private static ConsoleKeyInfo ReadRawKey()
    {
        return PendingKeys.Count > 0
            ? PendingKeys.Dequeue()
            : Console.ReadKey(intercept: true);
    }

    private static int DrainMouseWheelBurst()
    {
        var delta = 0;
        while (Console.KeyAvailable)
        {
            var next = Console.ReadKey(intercept: true);
            if (!TerminalMouse.TryConsumeMouseEvent(next, out var nextDelta))
            {
                PendingKeys.Enqueue(next);
                break;
            }

            delta += nextDelta;
        }

        return delta;
    }
}
