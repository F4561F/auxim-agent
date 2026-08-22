using System.Net;
using System.Text;
using Auxim.SDK;
using Xunit;

namespace Auxim.SDK.Tests;

public sealed class AuximGatewayClientTests
{
    [Fact]
    public async Task GetStatusAsync_SendsBearerToken()
    {
        using var client = CreateClient(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret", request.Headers.Authorization?.Parameter);
            Assert.Equal("/v1/status", request.RequestUri?.AbsolutePath);

            return JsonResponse("""
                {
                  "service": "auxim-gateway",
                  "model": { "provider": "local", "name": "echo", "baseUrl": "" },
                  "agent": { "maxIterations": 4 },
                  "approval": { "mode": "non-interactive", "protectedResourcesRequireGrant": true },
                  "auth": { "enabled": true },
                  "cors": { "origins": ["http://localhost:5173"] },
                  "connectors": { "telegram": true }
                }
                """);
        });

        var status = await client.GetStatusAsync();

        Assert.Equal("auxim-gateway", status.Service);
        Assert.Equal("local", status.Model.Provider);
        Assert.True(status.Auth.Enabled);
        Assert.True(status.Approval.ProtectedResourcesRequireGrant);
        Assert.True(status.Connectors.Telegram);
    }

    [Fact]
    public async Task ChatAsync_PostsRequestAndReadsResponse()
    {
        using var client = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/chat", request.RequestUri?.AbsolutePath);

            return JsonResponse("""
                {
                  "sessionId": "session-1",
                  "finalResponse": "hello from auxim"
                }
                """);
        });

        var response = await client.ChatAsync("hello", useCurrentSession: false, appendToSession: false);

        Assert.Equal("session-1", response.SessionId);
        Assert.Equal("hello from auxim", response.FinalResponse);
    }

    [Fact]
    public async Task SendMessageAsync_PostsConnectorMessage()
    {
        using var client = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/messages", request.RequestUri?.AbsolutePath);

            return JsonResponse("""
                {
                  "platform": "slack",
                  "conversationId": "C123",
                  "userId": "U456",
                  "scope": "participant",
                  "conversationKey": "slack:c123:u456",
                  "sessionId": "session-1",
                  "finalResponse": "adapter reply"
                }
                """);
        });

        var response = await client.SendMessageAsync(
            platform: "slack",
            conversationId: "C123",
            userId: "U456",
            text: "hello from slack",
            displayName: "Ada");

        Assert.Equal("slack:c123:u456", response.ConversationKey);
        Assert.Equal("adapter reply", response.FinalResponse);
    }

    [Fact]
    public async Task StreamChatAsync_ParsesSseEvents()
    {
        using var client = CreateClient(request =>
        {
            Assert.Equal("text/event-stream", request.Headers.Accept.Single().MediaType);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    event: content_delta
                    data: {"delta":"hello"}

                    event: tool_event
                    data: {"kind":"started","name":"time.now","detail":"running"}

                    event: final
                    data: {"sessionId":"session-1","finalResponse":"hello"}

                    """, Encoding.UTF8, "text/event-stream"),
            };
        });

        var events = new List<AuximGatewayStreamEvent>();
        await foreach (var streamEvent in client.StreamChatAsync("hello"))
        {
            events.Add(streamEvent);
        }

        var delta = Assert.IsType<AuximContentDeltaEvent>(events[0]);
        Assert.Equal("hello", delta.Delta);

        var tool = Assert.IsType<AuximToolEventEvent>(events[1]);
        Assert.Equal("time.now", tool.ToolEvent.Name);

        var final = Assert.IsType<AuximFinalEvent>(events[2]);
        Assert.Equal("session-1", final.SessionId);
    }

    [Fact]
    public async Task GatewayError_ThrowsTypedException()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"code":"unauthorized","message":"A valid bearer token is required."}""",
                Encoding.UTF8,
                "application/json"),
        });

        var exception = await Assert.ThrowsAsync<AuximGatewayException>(() => client.GetStatusAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("unauthorized", exception.Error?.Code);
    }

    private static AuximGatewayClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("http://localhost:5055"),
        };

        return new AuximGatewayClient(httpClient, new AuximGatewayClientOptions
        {
            Token = "secret",
        });
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
