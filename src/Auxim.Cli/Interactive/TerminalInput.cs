namespace Auxim.Cli.Interactive;

internal enum TerminalInputEventKind
{
    Ignored,
    Key,
}

internal readonly record struct TerminalInputEvent(
    TerminalInputEventKind Kind,
    ConsoleKeyInfo Key);

internal sealed class TerminalInputPolicy
{
    private readonly Func<ConsoleKeyInfo, bool> _acceptKey;

    private TerminalInputPolicy(Func<ConsoleKeyInfo, bool> acceptKey)
    {
        _acceptKey = acceptKey;
    }

    public bool AcceptsKey(ConsoleKeyInfo key) => _acceptKey(key);

    public static TerminalInputPolicy LineEditor { get; } = new(_ => true);

    public static TerminalInputPolicy Approval { get; } = new(
        key => key.Key is
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
    public static TerminalInputEvent Read(TerminalInputPolicy policy)
    {
        var key = Console.ReadKey(intercept: true);

        return policy.AcceptsKey(key)
            ? new TerminalInputEvent(TerminalInputEventKind.Key, key)
            : new TerminalInputEvent(TerminalInputEventKind.Ignored, key);
    }

    public static async ValueTask<TerminalInputEvent> ReadAsync(
        TerminalInputPolicy policy,
        CancellationToken cancellationToken)
    {
        while (!Console.KeyAvailable)
        {
            await Task.Delay(25, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Read(policy);
    }
}
