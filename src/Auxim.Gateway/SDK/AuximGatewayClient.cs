using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Auxim.SDK;

public sealed class AuximGatewayClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AuximGatewayClient(AuximGatewayClientOptions? options = null)
        : this(new HttpClient(), AsOwnedOptions(options))
    {
    }

    public AuximGatewayClient(HttpClient httpClient, AuximGatewayClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        options ??= new AuximGatewayClientOptions();
        _httpClient = httpClient;
        _ownsHttpClient = options.OwnsHttpClient;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = options.BaseAddress;
        }

        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.Token);
        }
    }

    public Task<AuximHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<AuximHealth>("/health", cancellationToken);

    public Task<AuximGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<AuximGatewayStatus>("/v1/status", cancellationToken);

    public Task<IReadOnlyList<AuximToolInfo>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<IReadOnlyList<AuximToolInfo>>("/v1/tools", cancellationToken);

    public Task<IReadOnlyList<AuximSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<IReadOnlyList<AuximSessionRecord>>("/v1/sessions", cancellationToken);

    public Task<IReadOnlyList<AuximMessageConversationRecord>> ListMessageConversationsAsync(
        CancellationToken cancellationToken = default) =>
        GetJsonAsync<IReadOnlyList<AuximMessageConversationRecord>>(
            "/v1/message-conversations",
            cancellationToken);

    public Task<AuximSessionDocument> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<AuximSessionDocument>("/v1/sessions/current", cancellationToken);

    public Task<AuximSessionDocument> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return GetJsonAsync<AuximSessionDocument>(
            $"/v1/sessions/{Uri.EscapeDataString(sessionId)}",
            cancellationToken);
    }

    public Task<AuximSessionDocument> CreateSessionAsync(
        string? title = null,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<AuximCreateSessionRequest, AuximSessionDocument>(
            "/v1/sessions",
            new AuximCreateSessionRequest(title),
            cancellationToken);

    public Task<AuximSessionDocument> UseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return PostJsonAsync<object, AuximSessionDocument>(
            $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/use",
            new { },
            cancellationToken);
    }

    public async Task ClearCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync("/v1/sessions/current", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AuximChatResponse> ChatAsync(
        string prompt,
        bool useCurrentSession = true,
        bool appendToSession = true,
        CancellationToken cancellationToken = default) =>
        ChatAsync(new AuximChatRequest(prompt, useCurrentSession, appendToSession), cancellationToken);

    public Task<AuximChatResponse> ChatAsync(
        AuximChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        return PostJsonAsync<AuximChatRequest, AuximChatResponse>(
            "/v1/chat",
            request,
            cancellationToken);
    }

    public Task<AuximMessageResponse> SendMessageAsync(
        AuximMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);

        return PostJsonAsync<AuximMessageRequest, AuximMessageResponse>(
            "/v1/messages",
            request,
            cancellationToken);
    }

    public Task<AuximMessageResponse> SendMessageAsync(
        string platform,
        string conversationId,
        string userId,
        string text,
        string scope = "participant",
        string? displayName = null,
        string? messageId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(
            new AuximMessageRequest(
                platform,
                conversationId,
                userId,
                text,
                scope,
                displayName,
                messageId,
                metadata),
            cancellationToken);

    public async IAsyncEnumerable<AuximGatewayStreamEvent> StreamChatAsync(
        string prompt,
        bool useCurrentSession = true,
        bool appendToSession = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var streamEvent in StreamChatAsync(
                           new AuximChatRequest(prompt, useCurrentSession, appendToSession),
                           cancellationToken))
        {
            yield return streamEvent;
        }
    }

    public async IAsyncEnumerable<AuximGatewayStreamEvent> StreamChatAsync(
        AuximChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/stream")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventType = null;
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (eventType is not null && data.Length > 0)
                {
                    yield return ParseStreamEvent(eventType, data.ToString());
                }

                eventType = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].TrimStart());
            }
        }

        if (eventType is not null && data.Length > 0)
        {
            yield return ParseStreamEvent(eventType, data.ToString());
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            path,
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Gateway returned an empty response body.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        AuximGatewayError? error = null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer.Deserialize<AuximGatewayError>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Preserve the raw response text in the exception message below.
            }
        }

        var message = error is null
            ? $"Auxim Gateway request failed with HTTP {(int)response.StatusCode}: {body}"
            : $"Auxim Gateway request failed with HTTP {(int)response.StatusCode}: {error.Code} - {error.Message}";

        throw new AuximGatewayException(response.StatusCode, error, message);
    }

    private static AuximGatewayStreamEvent ParseStreamEvent(string eventType, string data)
    {
        using var document = JsonDocument.Parse(data);
        var payload = document.RootElement.Clone();

        return eventType switch
        {
            "content_delta" => new AuximContentDeltaEvent(
                payload.GetProperty("delta").GetString() ?? ""),
            "tool_event" => new AuximToolEventEvent(
                payload.Deserialize<AuximToolEvent>(JsonOptions)
                    ?? throw new JsonException("Invalid tool event payload.")),
            "final" => new AuximFinalEvent(
                payload.GetProperty("sessionId").GetString() ?? "",
                payload.GetProperty("finalResponse").GetString() ?? "",
                payload.TryGetProperty("runId", out var runId)
                    ? runId.GetString() ?? ""
                    : ""),
            "error" => new AuximStreamErrorEvent(
                payload.GetProperty("message").GetString() ?? ""),
            _ => new AuximUnknownStreamEvent(eventType, payload),
        };
    }

    private static AuximGatewayClientOptions AsOwnedOptions(AuximGatewayClientOptions? options) =>
        new()
        {
            BaseAddress = options?.BaseAddress ?? new Uri("http://127.0.0.1:5055"),
            Token = options?.Token,
            OwnsHttpClient = true,
        };
}
