using Auxim.Tools;
using Auxim.Core.Agent;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public async Task InvokeAsyncRunsRegisteredTool()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var result = await registry.InvokeAsync(
            "echo",
            new Dictionary<string, object?> { ["text"] = "hello" });

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task OpenAiCompatibleClientSerializesEmptyMessageContent()
    {
        var handler = new CaptureHandler();
        var http = new HttpClient(handler);
        var client = new OpenAiCompatibleAgentClient("https://example.test/v1", "test-key", "test-model", http);

        await client.CompleteWithToolsAsync(
            [
                new AgentMessage("assistant", "")
                {
                    ToolCalls =
                    [
                        new ToolCallRequest("call-1", "echo", "{}"),
                    ],
                },
            ],
            [],
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBody);
        var message = document.RootElement.GetProperty("messages")[0];
        Assert.True(message.TryGetProperty("content", out var content));
        Assert.Equal("", content.GetString());
    }

    [Fact]
    public async Task OpenAiCompatibleClientStreamsContentDeltas()
    {
        var handler = new CaptureHandler("""
            data: {"choices":[{"delta":{"content":"hel"}}]}

            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: [DONE]

            """);
        var http = new HttpClient(handler);
        var client = new OpenAiCompatibleAgentClient("https://example.test/v1", "test-key", "test-model", http);
        var deltas = new List<string>();

        var result = await client.CompleteWithToolsStreamingAsync(
            [new AgentMessage("user", "hello")],
            [],
            deltas.Add,
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBody);
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(["hel", "lo"], deltas);
        Assert.Equal("hello", result.Content);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public async Task OpenAiCompatibleClientStreamsToolCallDeltas()
    {
        var handler = new CaptureHandler("""
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"file."}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"read","arguments":"{\"path\":"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"/workspace/README.md\"}"}}]}}]}

            data: [DONE]

            """);
        var http = new HttpClient(handler);
        var client = new OpenAiCompatibleAgentClient("https://example.test/v1", "test-key", "test-model", http);

        var result = await client.CompleteWithToolsStreamingAsync(
            [new AgentMessage("user", "read README")],
            [],
            _ => { },
            CancellationToken.None);

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("call-1", call.Id);
        Assert.Equal("file.read", call.Name);
        Assert.Equal("{\"path\":\"/workspace/README.md\"}", call.ArgumentsJson);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CaptureHandler(string? responseBody = null)
        {
            _responseBody = responseBody ?? """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "done"
                      }
                    }
                  ]
                }
                """;
        }

        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody),
            };
        }
    }
}
