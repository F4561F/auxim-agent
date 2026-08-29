using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.Runtime;

namespace Auxim.Core.State;

public sealed class SessionStore
{
    private readonly string _sessionsDir;
    private readonly string _currentSessionPath;

    public SessionStore(string? home = null)
    {
        home ??= ConfigLoader.GetAuximHome();
        _sessionsDir = Path.Combine(home, "sessions");
        _currentSessionPath = Path.Combine(home, "current_session");
        Directory.CreateDirectory(_sessionsDir);
    }

    public SessionDocument GetOrCreateCurrent()
    {
        var currentId = GetCurrentSessionId();
        if (!string.IsNullOrWhiteSpace(currentId))
        {
            var existing = TryLoad(currentId);
            if (existing is not null)
            {
                return existing;
            }
        }

        return NewSession();
    }

    public SessionDocument NewSession(string? title = null, bool makeCurrent = true)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new SessionDocument
        {
            Id = $"session-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..33],
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled session" : title,
            CreatedAt = now,
            UpdatedAt = now,
            Messages = [],
        };

        Save(session);
        if (makeCurrent)
        {
            SetCurrent(session.Id);
        }

        return session;
    }

    public IReadOnlyList<SessionRecord> List()
    {
        return Directory.GetFiles(_sessionsDir, "*.json")
            .Select(path => TryLoad(Path.GetFileNameWithoutExtension(path)))
            .Where(session => session is not null)
            .Select(session => new SessionRecord(
                session!.Id,
                session.CreatedAt,
                session.UpdatedAt,
                session.Title))
            .OrderByDescending(record => record.UpdatedAt)
            .ToArray();
    }

    public SessionDocument? TryLoad(string id)
    {
        var path = SessionPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SessionDocument>(json, JsonOptions());
    }

    public void Save(SessionDocument session)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(session, JsonOptions());
        File.WriteAllText(SessionPath(session.Id), json + Environment.NewLine);
    }

    public void AppendTurn(SessionDocument session, string userMessage, string assistantMessage)
    {
        if (session.Messages.Count == 0 && session.Title == "Untitled session")
        {
            session.Title = userMessage.Length <= 60 ? userMessage : userMessage[..60];
        }

        session.Messages.Add(new AgentMessage("user", userMessage));
        session.Messages.Add(new AgentMessage("assistant", assistantMessage));
        Save(session);
    }

    public void SetCurrent(string id)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_currentSessionPath) ?? ".");
        File.WriteAllText(_currentSessionPath, id + Environment.NewLine);
    }

    public string GetCurrentSessionId()
    {
        return File.Exists(_currentSessionPath)
            ? File.ReadAllText(_currentSessionPath).Trim()
            : "";
    }

    public void ClearCurrent()
    {
        if (File.Exists(_currentSessionPath))
        {
            File.Delete(_currentSessionPath);
        }
    }

    private string SessionPath(string id)
    {
        var safeId = string.Concat(id.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'));
        return Path.Combine(_sessionsDir, safeId + ".json");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}

public sealed class SessionDocument
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Untitled session";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AgentMessage> Messages { get; set; } = [];
}
