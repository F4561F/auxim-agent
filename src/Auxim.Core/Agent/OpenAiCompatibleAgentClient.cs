using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auxim.Core.Runtime;
using Auxim.Core.Tools;

namespace Auxim.Core.Agent;

public sealed class OpenAiCompatibleAgentClient : IStreamingToolCallingAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OpenAiCompatibleAgentClient(string baseUrl, string apiKey, string model, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        _model = model;
        var endpoint = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient = httpClient ?? CreateHttpClient(endpoint);
        _httpClient.BaseAddress = endpoint;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken)
    {
        var completion = await CompleteWithToolsAsync(messages, [], cancellationToken);
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new InvalidOperationException("Model response did not include assistant content.");
        }

        return completion.Content;
    }

    public async Task<AgentClientResponse> CompleteWithToolsAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest(
            _model,
            messages.Select(ToChatMessage).ToArray(),
            tools.Select(ToToolSchema).ToArray());

        var json = JsonSerializer.Serialize(request, JsonOptions());
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Model request failed with HTTP {(int)response.StatusCode}: {responseBody}");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions());
        var message = completion?.Choices.FirstOrDefault()?.Message;
        if (message is null)
        {
            throw new InvalidOperationException("Model response did not include a message.");
        }

        var toolCalls = message.ToolCalls?
            .Where(call => call.Function is not null)
            .Select(call => new ToolCallRequest(
                call.Id,
                call.Function!.Name,
                call.Function.Arguments ?? "{}"))
            .ToArray()
            ?? [];

        return new AgentClientResponse(message.Content ?? "", toolCalls);
    }

    public async Task<AgentClientResponse> CompleteWithToolsStreamingAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        Func<string, CancellationToken, ValueTask> contentDeltaSink,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest(
            _model,
            messages.Select(ToChatMessage).ToArray(),
            tools.Select(ToToolSchema).ToArray(),
            Stream: true);

        var json = JsonSerializer.Serialize(request, JsonOptions());
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = content,
        };
        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Model request failed with HTTP {(int)response.StatusCode}: {responseBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var assistantContent = new StringBuilder();
        var toolCalls = new Dictionary<int, ToolCallBuilder>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0 || line.StartsWith(':'))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions());
            var delta = chunk?.Choices.FirstOrDefault()?.Delta;
            if (delta is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(delta.Content))
            {
                assistantContent.Append(delta.Content);
                await contentDeltaSink(delta.Content, cancellationToken);
            }

            foreach (var toolCall in delta.ToolCalls ?? [])
            {
                var builder = GetToolCallBuilder(toolCalls, toolCall.Index);
                if (!string.IsNullOrWhiteSpace(toolCall.Id))
                {
                    builder.Id = toolCall.Id;
                }

                if (!string.IsNullOrWhiteSpace(toolCall.Function?.Name))
                {
                    builder.Name.Append(toolCall.Function.Name);
                }

                if (!string.IsNullOrEmpty(toolCall.Function?.Arguments))
                {
                    builder.Arguments.Append(toolCall.Function.Arguments);
                }
            }
        }

        var completedToolCalls = toolCalls
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToRequest(pair.Key))
            .Where(call => !string.IsNullOrWhiteSpace(call.Name))
            .ToArray();

        return new AgentClientResponse(assistantContent.ToString(), completedToolCalls);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static HttpClient CreateHttpClient(Uri endpoint)
    {
        if (!endpoint.IsLoopback)
        {
            return new HttpClient();
        }

        return new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
        });
    }

    private static ChatMessage ToChatMessage(AgentMessage message)
    {
        return new ChatMessage(
            message.Role,
            message.Content,
            message.Name,
            message.ToolCallId,
            message.ToolCalls?.Select(call => new ChatToolCall(
                call.Id,
                "function",
                new ChatToolCallFunction(call.Name, call.ArgumentsJson))).ToArray());
    }

    private static ToolSchema ToToolSchema(ToolDefinition tool)
    {
        return new ToolSchema(
            "function",
            new FunctionSchema(tool.SchemaName, tool.Description, tool.ParametersSchema));
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<ToolSchema>? Tools = null,
        bool? Stream = null);

    private sealed record ChatMessage(
        string Role,
        string? Content,
        string? Name = null,
        string? ToolCallId = null,
        IReadOnlyList<ChatToolCall>? ToolCalls = null);

    private sealed record ChatToolCall(
        string Id,
        string Type,
        ChatToolCallFunction Function);

    private sealed record ChatToolCallFunction(string Name, string? Arguments);

    private sealed record ToolSchema(string Type, FunctionSchema Function);

    private sealed record FunctionSchema(
        string Name,
        string Description,
        IReadOnlyDictionary<string, object?> Parameters);

    private sealed record ChatCompletionResponse(IReadOnlyList<Choice> Choices);

    private sealed record Choice(ChatMessage Message);

    private sealed record ChatCompletionChunk(IReadOnlyList<StreamChoice> Choices);

    private sealed record StreamChoice(StreamDelta Delta);

    private sealed record StreamDelta(
        string? Content = null,
        IReadOnlyList<StreamToolCall>? ToolCalls = null);

    private sealed record StreamToolCall(
        int Index,
        string? Id = null,
        string? Type = null,
        ChatToolCallFunction? Function = null);

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();

        public ToolCallRequest ToRequest(int index)
        {
            return new ToolCallRequest(
                Id ?? $"call-{index}",
                Name.ToString(),
                Arguments.Length == 0 ? "{}" : Arguments.ToString());
        }
    }

    private static ToolCallBuilder GetToolCallBuilder(
        Dictionary<int, ToolCallBuilder> toolCalls,
        int index)
    {
        if (toolCalls.TryGetValue(index, out var builder))
        {
            return builder;
        }

        builder = new ToolCallBuilder();
        toolCalls[index] = builder;
        return builder;
    }
}
