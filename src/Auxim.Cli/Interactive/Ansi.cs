namespace Auxim.Cli.Interactive;

internal static class Ansi
{
    public static bool Enabled =>
        ControlSequencesEnabled
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    public static bool ControlSequencesEnabled =>
        !Console.IsOutputRedirected
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    public static string Bold(string text) => Wrap("1", text);

    public static string Dim(string text) => Wrap("2", text);

    public static string Accent(string text) => Wrap("36", text);

    public static string Success(string text) => Wrap("32", text);

    public static string Warning(string text) => Wrap("33", text);

    public static string Error(string text) => Wrap("31", text);

    public static string Muted(string text) => Wrap("90", text);

    public static string Reverse(string text) => Wrap("7", text);

    public static string AccentBackground(string text) => Wrap("30;46", text);

    public static string WarningBackground(string text) => Wrap("30;43", text);

    public static string ErrorBackground(string text) => Wrap("97;41", text);

    public static string Cyan(string text) => Wrap("96", text);

    public static string Blue(string text) => Wrap("34", text);

    public static string Magenta(string text) => Wrap("35", text);

    public static string SoftBackground(string text) => Wrap("30;47", text);

    private static string Wrap(string code, string text)
    {
        return Enabled ? $"\u001b[{code}m{text}\u001b[0m" : text;
    }
}
