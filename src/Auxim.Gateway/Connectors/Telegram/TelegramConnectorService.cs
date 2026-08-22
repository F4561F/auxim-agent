using System.Net.Http.Json;
using System.Text.Json;
using Auxim.Core.Runtime;
using Auxim.Core.Approval;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class TelegramConnectorService : BackgroundService
{
    private readonly IAuximRuntime _runtime;
    private readonly IApprovalHandler _approvalHandler;
    private readonly TelegramConnectorSettings _settings;
    private readonly ILogger<TelegramConnectorService> _logger;

    public TelegramConnectorService(
        IAuximRuntime runtime,
        IApprovalHandler approvalHandler,
        TelegramConnectorSettings settings,
        ILogger<TelegramConnectorService> logger)
    {
        _runtime = runtime;
        _approvalHandler = approvalHandler;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var telegramHttp = new HttpClient
        {
            BaseAddress = new Uri($"https://api.telegram.org/bot{_settings.BotToken}/"),
            Timeout = TimeSpan.FromSeconds(_settings.PollTimeoutSeconds + 15),
        };

        _logger.LogInformation(
            "Telegram connector started with {Scope} conversation scope.",
            _settings.Scope);

        var offset = 0L;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await GetUpdatesAsync(telegramHttp, offset, stoppingToken);
                foreach (var update in updates)
                {
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await HandleUpdateAsync(update, telegramHttp, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Telegram connector polling failed.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Telegram connector stopped.");
    }

    private async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        HttpClient http,
        long offset,
        CancellationToken cancellationToken)
    {
        var url = $"getUpdates?timeout={_settings.PollTimeoutSeconds}&offset={offset}&allowed_updates=%5B%22message%22%5D";
        var response = await http.GetFromJsonAsync<TelegramApiResponse<List<TelegramUpdate>>>(
            url,
            TelegramJson.Options,
            cancellationToken);

        if (response is null)
        {
            return [];
        }

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Description ?? "Telegram getUpdates failed.");
        }

        return response.Result ?? [];
    }

    private async Task HandleUpdateAsync(
        TelegramUpdate update,
        HttpClient telegramHttp,
        CancellationToken cancellationToken)
    {
        var message = update.Message;
        if (message is null || message.Chat is null || message.From is null)
        {
            return;
        }

        var text = ExtractText(message);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!_settings.IsAllowed(message))
        {
            _logger.LogWarning(
                "Ignored unauthorized Telegram message from user {UserId} in chat {ChatId}.",
                message.From.Id,
                message.Chat.Id);
            return;
        }

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await SendTelegramMessageAsync(
                telegramHttp,
                message.Chat.Id,
                "Auxim connector is online.",
                message.MessageId,
                cancellationToken: cancellationToken);
            return;
        }

        if (_settings.RequireMention && !MentionsBot(text, _settings.BotUsername))
        {
            return;
        }

        var cleanText = StripBotMention(text, _settings.BotUsername).Trim();
        if (string.IsNullOrWhiteSpace(cleanText))
        {
            return;
        }

        await SendChatActionAsync(telegramHttp, message.Chat.Id, cancellationToken);

        AuximExternalMessageResult response;
        try
        {
            response = await _runtime.SendExternalMessageAsync(
                new AuximExternalMessageRequest(
                    Platform: "telegram",
                    ConversationId: ConversationId(message),
                    UserId: message.From.Id.ToString(),
                    Text: cleanText,
                    Scope: _settings.Scope,
                    DisplayName: DisplayName(message.From),
                    MessageId: message.MessageId.ToString(),
                    Metadata: MessageMetadata(message)),
                new AuximRuntimeOptions { ApprovalHandler = _approvalHandler },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Auxim failed to process Telegram message {MessageId}.",
                message.MessageId);
            await SendTelegramMessageAsync(
                telegramHttp,
                message.Chat.Id,
                "Auxim could not process this request.",
                message.MessageId,
                cancellationToken);
            return;
        }

        await SendTelegramMessageAsync(
            telegramHttp,
            message.Chat.Id,
            response.FinalResponse,
            message.MessageId,
            cancellationToken);
    }

    private static string ExtractText(TelegramMessage message) =>
        !string.IsNullOrWhiteSpace(message.Text) ? message.Text! : message.Caption ?? "";

    private static string ConversationId(TelegramMessage message)
    {
        var topic = message.MessageThreadId is null ? "" : $":thread:{message.MessageThreadId}";
        return $"{message.Chat?.Id}{topic}";
    }

    private static string DisplayName(TelegramUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            return $"@{user.Username}";
        }

        return string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static IReadOnlyDictionary<string, string> MessageMetadata(TelegramMessage message)
    {
        var metadata = new Dictionary<string, string>
        {
            ["chatType"] = message.Chat?.Type ?? "",
            ["chatTitle"] = message.Chat?.Title ?? "",
        };

        if (message.MessageThreadId is not null)
        {
            metadata["messageThreadId"] = message.MessageThreadId.Value.ToString();
        }

        return metadata;
    }

    private static bool MentionsBot(string text, string? botUsername) =>
        string.IsNullOrWhiteSpace(botUsername)
        || text.Contains($"@{botUsername}", StringComparison.OrdinalIgnoreCase);

    private static string StripBotMention(string text, string? botUsername) =>
        string.IsNullOrWhiteSpace(botUsername)
            ? text
            : text.Replace($"@{botUsername}", "", StringComparison.OrdinalIgnoreCase);

    private static async Task SendChatActionAsync(
        HttpClient http,
        long chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "sendChatAction",
                new { chat_id = chatId, action = "typing" },
                TelegramJson.Options,
                cancellationToken);
            _ = response.IsSuccessStatusCode;
        }
        catch
        {
            // Typing indicators are best-effort.
        }
    }

    private static async Task SendTelegramMessageAsync(
        HttpClient http,
        long chatId,
        string text,
        int? replyToMessageId,
        CancellationToken cancellationToken)
    {
        foreach (var chunk in ChunkTelegramText(text))
        {
            using var response = await http.PostAsJsonAsync(
                "sendMessage",
                new
                {
                    chat_id = chatId,
                    text = chunk,
                    reply_to_message_id = replyToMessageId,
                    disable_web_page_preview = true,
                },
                TelegramJson.Options,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Telegram sendMessage failed: {body}");
            }
        }
    }

    private static IEnumerable<string> ChunkTelegramText(string text)
    {
        const int limit = 3900;
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return "(empty response)";
            yield break;
        }

        for (var index = 0; index < text.Length; index += limit)
        {
            yield return text.Substring(index, Math.Min(limit, text.Length - index));
        }
    }
}

public sealed class TelegramConnectorSettings
{
    public string BotToken { get; init; } = "";
    public string? BotUsername { get; init; }
    public string Scope { get; init; } = "participant";
    public bool RequireMention { get; init; }
    public int PollTimeoutSeconds { get; init; } = 30;
    public HashSet<string> AllowedUsers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AllowedChats { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEnabled => !string.IsNullOrWhiteSpace(BotToken);

    public static TelegramConnectorSettings FromEnvironment() =>
        new()
        {
            BotToken = Environment.GetEnvironmentVariable("AUXIM_TELEGRAM_BOT_TOKEN") ?? "",
            BotUsername = TrimAt(Environment.GetEnvironmentVariable("AUXIM_TELEGRAM_BOT_USERNAME")),
            Scope = NormalizeScope(Environment.GetEnvironmentVariable("AUXIM_TELEGRAM_SCOPE")),
            RequireMention = ReadBool("AUXIM_TELEGRAM_REQUIRE_MENTION"),
            PollTimeoutSeconds = ReadInt("AUXIM_TELEGRAM_POLL_TIMEOUT", 30),
            AllowedUsers = CsvSet(Environment.GetEnvironmentVariable("AUXIM_TELEGRAM_ALLOWED_USERS")),
            AllowedChats = CsvSet(Environment.GetEnvironmentVariable("AUXIM_TELEGRAM_ALLOWED_CHATS")),
        };

    public bool IsAllowed(TelegramMessage message)
    {
        var user = message.From;
        var chat = message.Chat;
        if (user is null || chat is null)
        {
            return false;
        }

        var userAllowed = AllowedUsers.Count == 0
            || AllowedUsers.Contains(user.Id.ToString())
            || (!string.IsNullOrWhiteSpace(user.Username)
                && TrimAt(user.Username) is { } username
                && AllowedUsers.Contains(username));
        var chatAllowed = AllowedChats.Count == 0 || AllowedChats.Contains(chat.Id.ToString());
        return userAllowed && chatAllowed;
    }

    private static HashSet<string> CsvSet(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TrimAt)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? TrimAt(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.TrimStart('@');
    }

    private static string NormalizeScope(string? raw) =>
        string.Equals(raw, "conversation", StringComparison.OrdinalIgnoreCase)
            ? "conversation"
            : "participant";

    private static bool ReadBool(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;
}

public sealed record TelegramApiResponse<T>(
    bool Ok,
    T? Result,
    string? Description);

public sealed record TelegramUpdate(
    int UpdateId,
    TelegramMessage? Message);

public sealed record TelegramMessage(
    int MessageId,
    TelegramUser? From,
    TelegramChat? Chat,
    string? Text,
    string? Caption,
    int? MessageThreadId);

public sealed record TelegramUser(
    long Id,
    bool IsBot,
    string? FirstName,
    string? LastName,
    string? Username);

public sealed record TelegramChat(
    long Id,
    string Type,
    string? Title,
    string? Username);

public static class TelegramJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
