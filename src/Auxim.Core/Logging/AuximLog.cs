using Auxim.Core.Config;

namespace Auxim.Core.Logging;

public static class AuximLog
{
    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warning(string message)
    {
        Write("WARN", message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            var logDir = Path.Combine(ConfigLoader.GetAuximHome(), "logs");
            Directory.CreateDirectory(logDir);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logDir, "agent.log"), line);
        }
        catch
        {
            // Logging must never break agent execution.
        }
    }
}
