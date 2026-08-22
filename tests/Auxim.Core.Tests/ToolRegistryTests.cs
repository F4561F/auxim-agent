using Auxim.Tools;
using Auxim.Core.Agent;
using Auxim.Core.Resources;
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
    public void BuiltInToolsResolveArgumentSpecificResourceAccess()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var fileAccess = Assert.Single(registry.Get("file.write").ResolveResourceAccesses(
            new Dictionary<string, object?> { ["path"] = "/workspace/output.txt" }));
        var shellAccess = Assert.Single(registry.Get("shell.run").ResolveResourceAccesses(
            new Dictionary<string, object?> { ["command"] = "cat /workspace/README.md" }));

        Assert.Equal(ResourceAction.Write, fileAccess.Action);
        Assert.Equal("vafs:/workspace/output.txt", fileAccess.Resource.Value);
        Assert.True(fileAccess.RequiresApproval);
        Assert.Equal(ResourceAction.Execute, shellAccess.Action);
        Assert.StartsWith("vashell:", shellAccess.Resource.Value);
        Assert.True(shellAccess.RequiresApproval);
        Assert.Throws<ArgumentException>(() => ResourceUri.Vafs("/workspace/../../etc/passwd"));
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
            (delta, _) =>
            {
                deltas.Add(delta);
                return ValueTask.CompletedTask;
            },
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
            (_, _) => ValueTask.CompletedTask,
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
