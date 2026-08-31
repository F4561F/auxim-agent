using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.Tools;
using Auxim.Core.Runtime;

namespace Auxim.Agent;

public sealed class AuximAgent
{
    private readonly IAgentClient _client;
    private readonly ToolRegistry _tools;
    private readonly AgentOptions _options;
    private readonly IRuntimeEventSink _eventSink;
    private readonly IRuntimeToolService _runtimeTools;
    private readonly string _homeDirectory;

    public AuximAgent(
        IAgentClient client,
        ToolRegistry tools,
        IRuntimeToolService runtimeTools,
        AgentOptions? options = null)
    {
        _client = client;
        _tools = tools;
        _runtimeTools = runtimeTools;
        _options = options ?? new AgentOptions();
        _eventSink = _options.EventSink ?? NullAgentRuntimeEventSink.Instance;
        _homeDirectory = string.IsNullOrWhiteSpace(_options.HomeDirectory)
            ? ConfigLoader.GetAuximHome()
            : _options.HomeDirectory;
    }

    public async Task<string> ChatAsync(string message, CancellationToken cancellationToken = default)
    {
        var result = await RunConversationAsync(message, cancellationToken: cancellationToken);
        return result.FinalResponse;
    }

    public async Task<AgentResult> RunConversationAsync(
        string userMessage,
        IReadOnlyList<AgentMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<AgentMessage>
        {
            new("system", BuildSystemPrompt()),
        };
        if (history is not null)
        {
            messages.AddRange(history.Where(message => message.Role != "system"));
        }

        messages.Add(new("user", userMessage));

        string response;
        if (_client is IToolCallingAgentClient toolClient)
        {
            response = await RunToolCallingLoopAsync(toolClient, messages, cancellationToken);
        }
        else
        {
            response = await _client.CompleteAsync(messages, cancellationToken);
            messages.Add(new AgentMessage("assistant", response));
        }

        return new AgentResult(response, messages);
    }

    public IReadOnlyCollection<ToolDefinition> GetAvailableTools() => _tools.List();

    private string BuildSystemPrompt()
    {
        return string.Join(Environment.NewLine, [
            $"You are Auxim. Provider={_options.Provider}; Model={_options.Model}.",
            "Use Auxim VAFS paths only. Never assume or mention host filesystem paths.",
            "Use /tmp for temporary generated files and scratch output.",
            _options.EnvironmentDescription,
        ]);
    }

    private async Task<string> RunToolCallingLoopAsync(
        IToolCallingAgentClient client,
        List<AgentMessage> messages,
        CancellationToken cancellationToken)
    {
        var tools = _tools.List().ToArray();
        for (var iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            var completion = await CompleteWithToolsAsync(client, messages, tools, cancellationToken);
            if (!completion.HasToolCalls)
            {
                messages.Add(new AgentMessage("assistant", completion.Content));
                return completion.Content;
            }

            messages.Add(new AgentMessage("assistant", completion.Content)
            {
                ToolCalls = completion.ToolCalls,
            });

            var deniedCalls = new List<ToolInvocationResult>();
            foreach (var call in completion.ToolCalls)
            {
                var result = await InvokeToolCallAsync(call, cancellationToken);
                messages.Add(new AgentMessage("tool", result.Content)
                {
                    Name = call.Name,
                    ToolCallId = call.Id,
                });

                if (result.Denied)
                {
                    deniedCalls.Add(result);
                }
            }

            foreach (var denied in deniedCalls)
            {
                messages.Add(new AgentMessage(
                    "user",
                    $"I denied the `{denied.ToolName}` tool call. My feedback: {denied.Feedback}"));
            }
        }

        throw new InvalidOperationException($"Tool-calling loop exceeded {_options.MaxIterations} iterations.");
    }

    private Task<AgentClientResponse> CompleteWithToolsAsync(
        IToolCallingAgentClient client,
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        if (_options.EventSink is not null && client is IStreamingToolCallingAgentClient streamingClient)
        {
            return streamingClient.CompleteWithToolsStreamingAsync(
                messages,
                tools,
                (delta, token) => _eventSink.PublishAsync(
                    new RuntimeContentDeltaEvent(
                        Guid.NewGuid().ToString("N"),
                        DateTimeOffset.UtcNow,
                        _options.RunId,
                        delta),
                    token),
                cancellationToken);
        }

        return client.CompleteWithToolsAsync(messages, tools, cancellationToken);
    }

    private async Task<ToolInvocationResult> InvokeToolCallAsync(ToolCallRequest call, CancellationToken cancellationToken)
    {
        try
        {
            var args = ParseArguments(call.ArgumentsJson);
            var toolName = ResolveToolName(call.Name);
            var content = await _runtimeTools.InvokeAsync(
                _options.RunId,
                call.Id,
                toolName,
                args,
                _homeDirectory,
                _options.ApprovalHandler,
                _eventSink,
                cancellationToken);
            return ToolInvocationResult.Allowed(toolName, content);
        }
        catch (ToolApprovalDeniedException exception)
        {
            var content = JsonSerializer.Serialize(new
            {
                denied = true,
                tool = exception.ToolName,
                userFeedback = exception.Reason,
                instruction = "Respect the user's decision. Continue without this tool or propose a safer alternative.",
            });
            return ToolInvocationResult.DeniedResult(
                exception.ToolName,
                content,
                exception.Reason);
        }
        catch (Exception exception)
        {
            return ToolInvocationResult.Allowed(call.Name, JsonSerializer.Serialize(new
            {
                error = true,
                tool = call.Name,
                message = exception.Message,
            }));
        }
    }

    private string ResolveToolName(string schemaName)
    {
        var match = _tools.List().FirstOrDefault(tool =>
            string.Equals(tool.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool.Name, schemaName, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? schemaName;
    }

    private static IReadOnlyDictionary<string, object?> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>();
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        return parsed ?? new Dictionary<string, object?>();
    }

    private sealed class NullAgentRuntimeEventSink : IRuntimeEventSink
    {
        public static NullAgentRuntimeEventSink Instance { get; } = new();

        public ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ToolInvocationResult(
        string ToolName,
        string Content,
        bool Denied,
        string Feedback)
    {
        public static ToolInvocationResult Allowed(string toolName, string content) =>
            new(toolName, content, false, "");

        public static ToolInvocationResult DeniedResult(string toolName, string content, string feedback) =>
            new(toolName, content, true, feedback);
    }
}
