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

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
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
                    """),
            };
        }
    }
}
