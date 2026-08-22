using Auxim.Core.Config;

namespace Auxim.Core.Logging;

public static class AuximLog
{
    private static readonly object WriteGate = new();

    public static void Info(string message, string? homeDirectory = null)
    {
        Write("INFO", message, homeDirectory);
    }

    public static void Warning(string message, string? homeDirectory = null)
    {
        Write("WARN", message, homeDirectory);
    }

    private static void Write(string level, string message, string? homeDirectory)
    {
        try
        {
            lock (WriteGate)
            {
                var logDir = Path.Combine(
                    homeDirectory ?? ConfigLoader.GetAuximHome(),
                    "logs");
                Directory.CreateDirectory(logDir);
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(logDir, "agent.log"), line);
            }
        }
        catch
        {
            // Logging must never break agent execution.
        }
    }
}
