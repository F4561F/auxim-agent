using System.Text.Json;

namespace Auxim.Core.Runtime;

public sealed partial class AuximRuntimeService
{
    public IReadOnlyList<AuximExternalConversation> ListExternalConversations()
    {
        lock (_externalConversationGate)
        {
            return LoadExternalConversations().Values
                .OrderByDescending(record => record.UpdatedAt)
                .ToArray();
        }
    }

    public async Task<AuximExternalMessageResult> SendExternalMessageAsync(
        AuximExternalMessageRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExternalMessage(request);
        var conversation = GetOrCreateExternalConversation(request);
        var result = await ChatAsync(
            new AuximChatRequest(
                BuildExternalPrompt(request),
                UseCurrentSession: false,
                AppendToSession: true,
                SessionId: conversation.SessionId),
            options ?? new AuximRuntimeOptions(),
            cancellationToken);
        TouchExternalConversation(conversation.Key);

        return new AuximExternalMessageResult(
            request.Platform,
            request.ConversationId,
            request.UserId,
            NormalizeExternalScope(request.Scope),
            conversation.Key,
            result.SessionId,
            result.FinalResponse,
            result.RunId);
    }

    private AuximExternalConversation GetOrCreateExternalConversation(AuximExternalMessageRequest request)
    {
        lock (_externalConversationGate)
        {
            var records = LoadExternalConversations();
            var key = BuildExternalConversationKey(request);
            if (records.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var session = CreateSession(
                $"gateway:{NormalizeExternalToken(request.Platform)}:{TrimExternalTitle(request.ConversationId)}",
                makeCurrent: false);
            var created = new AuximExternalConversation(
                key,
                request.Platform,
                request.ConversationId,
                request.UserId,
                NormalizeExternalScope(request.Scope),
                session.Id,
                session.Title,
                session.CreatedAt,
                session.UpdatedAt);
            records[key] = created;
            SaveExternalConversations(records);
            return created;
        }
    }

    private void TouchExternalConversation(string key)
    {
        lock (_externalConversationGate)
        {
            var records = LoadExternalConversations();
            if (records.TryGetValue(key, out var existing))
            {
                records[key] = existing with { UpdatedAt = DateTimeOffset.UtcNow };
                SaveExternalConversations(records);
            }
        }
    }

    private Dictionary<string, AuximExternalConversation> LoadExternalConversations()
    {
        var path = ExternalConversationPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, AuximExternalConversation>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var records = JsonSerializer.Deserialize<List<AuximExternalConversation>>(
                File.ReadAllText(path),
                ApplicationJsonOptions) ?? [];
            return records.ToDictionary(record => record.Key, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, AuximExternalConversation>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveExternalConversations(Dictionary<string, AuximExternalConversation> records)
    {
        var path = ExternalConversationPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(records.Values.OrderBy(record => record.Key), ApplicationJsonOptions)
            + Environment.NewLine);
    }

    private string ExternalConversationPath() =>
        Path.Combine(_homeDirectory(), "gateway-conversations.json");

    private static void ValidateExternalMessage(AuximExternalMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        if (request.Scope is not null
            && !string.Equals(request.Scope, "participant", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Scope, "conversation", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Scope must be either 'participant' or 'conversation'.", nameof(request));
        }
    }

    private static string BuildExternalPrompt(AuximExternalMessageRequest request)
    {
        var display = string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.UserId
            : $"{request.DisplayName} ({request.UserId})";
        var lines = new List<string>
        {
            "[External message]",
            $"Platform: {request.Platform}",
            $"Conversation: {request.ConversationId}",
            $"User: {display}",
        };
        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            lines.Add($"MessageId: {request.MessageId}");
        }

        if (request.Metadata is { Count: > 0 })
        {
            lines.AddRange(request.Metadata
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"Metadata.{item.Key}: {item.Value}"));
        }

        lines.Add("");
        lines.Add(request.Text);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildExternalConversationKey(AuximExternalMessageRequest request)
    {
        var parts = NormalizeExternalScope(request.Scope) == "conversation"
            ? new[] { NormalizeExternalToken(request.Platform), NormalizeExternalToken(request.ConversationId) }
            : [NormalizeExternalToken(request.Platform), NormalizeExternalToken(request.ConversationId), NormalizeExternalToken(request.UserId)];
        return string.Join(":", parts);
    }

    private static string NormalizeExternalScope(string? scope) =>
        string.Equals(scope, "conversation", StringComparison.OrdinalIgnoreCase)
            ? "conversation"
            : "participant";

    private static string NormalizeExternalToken(string value)
    {
        var normalized = string.Concat(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-'));
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string TrimExternalTitle(string value)
    {
        var normalized = NormalizeExternalToken(value);
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }
}
