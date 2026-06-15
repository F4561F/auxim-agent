namespace Auxim.Cli.Interactive;

internal static class TerminalGlyphs
{
    public static bool UnicodeEnabled => !ShouldUseAscii();

    public static string TopLeft => UnicodeEnabled ? "╭" : "+";
    public static string TopRight => UnicodeEnabled ? "╮" : "+";
    public static string BottomLeft => UnicodeEnabled ? "╰" : "+";
    public static string BottomRight => UnicodeEnabled ? "╯" : "+";
    public static string TeeLeft => UnicodeEnabled ? "├" : "+";
    public static string TeeRight => UnicodeEnabled ? "┤" : "+";
    public static string Horizontal => UnicodeEnabled ? "─" : "-";
    public static string Vertical => UnicodeEnabled ? "│" : "|";
    public static string Section => UnicodeEnabled ? "▌" : "|";
    public static string Ellipsis => UnicodeEnabled ? "…" : "...";

    public static string HorizontalLine(int width) => new(Horizontal[0], Math.Max(0, width));

    private static bool ShouldUseAscii()
    {
        var forced = Environment.GetEnvironmentVariable("AUXIM_ASCII_UI");
        if (IsTruthy(forced))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TERM_PROGRAM"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSYSTEM"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANSICON"))
            && !IsTruthy(Environment.GetEnvironmentVariable("ConEmuANSI"))
            && !string.Equals(Environment.GetEnvironmentVariable("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
