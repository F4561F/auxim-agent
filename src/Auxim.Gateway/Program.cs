using System.Text.Json;
using System.Threading.Channels;
using Auxim.Core.Approval;
using Auxim.Core.Runtime;
using Auxim.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var gatewayToken = Environment.GetEnvironmentVariable("AUXIM_GATEWAY_TOKEN") ?? "";
var corsOrigins = ParseCsv(Environment.GetEnvironmentVariable("AUXIM_GATEWAY_CORS_ORIGINS"));
var telegramSettings = TelegramConnectorSettings.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IAuximRuntime>(_ => AuximApplication.CreateRuntime());
builder.Services.AddSingleton<IApprovalHandler, NonInteractiveGatewayApprovalHandler>();
if (telegramSettings.IsEnabled)
{
    builder.Services.AddSingleton(telegramSettings);
    builder.Services.AddHostedService<TelegramConnectorService>();
}
builder.Services.AddCors(options =>
{
    options.AddPolicy("gateway", policy =>
    {
        if (corsOrigins.Count > 0)
        {
            policy.WithOrigins(corsOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();
if (corsOrigins.Count > 0)
{
    app.UseCors("gateway");
}

app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(gatewayToken)
        || string.Equals(context.Request.Path, "/health", StringComparison.OrdinalIgnoreCase)
        || IsAuthorized(context.Request, gatewayToken))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(
        new GatewayError("unauthorized", "A valid bearer token is required."));
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "auxim-gateway",
}));

app.MapGet("/v1/status", (IAuximRuntime runtime) =>
{
    var status = runtime.GetStatus();
    return Results.Ok(new
    {
        service = "auxim-gateway",
        model = new
        {
            provider = status.ModelProvider,
            name = status.ModelName,
            baseUrl = status.ModelBaseUrl,
        },
        agent = new
        {
            maxIterations = status.MaxIterations,
        },
        approval = new
        {
            mode = "non-interactive",
            protectedResourcesRequireGrant = true,
        },
        auth = new
        {
            enabled = !string.IsNullOrWhiteSpace(gatewayToken),
        },
        cors = new
        {
            origins = corsOrigins,
        },
        connectors = new
        {
            telegram = telegramSettings.IsEnabled,
        },
    });
});

app.MapGet("/v1/tools", (IAuximRuntime runtime) =>
    Results.Ok(runtime.ListTools()));

app.MapGet("/v1/sessions", (IAuximRuntime runtime) =>
    Results.Ok(runtime.ListSessions()));

app.MapGet("/v1/sessions/current", (IAuximRuntime runtime) =>
    Results.Ok(runtime.GetOrCreateCurrentSession()));

app.MapGet("/v1/sessions/{id}", (string id, IAuximRuntime runtime) =>
{
    var session = runtime.GetSession(id);
    if (session is null)
    {
        return Results.NotFound(new GatewayError("session_not_found", "Session not found."));
    }

    return Results.Ok(session);
});

app.MapPost("/v1/sessions", (GatewayCreateSessionRequest request, IAuximRuntime runtime) =>
    Results.Ok(runtime.CreateSession(request.Title)));

app.MapPost("/v1/sessions/{id}/use", (string id, IAuximRuntime runtime) =>
{
    var session = runtime.UseSession(id);
    if (session is null)
    {
        return Results.NotFound(new GatewayError("session_not_found", "Session not found."));
    }

    return Results.Ok(session);
});

app.MapDelete("/v1/sessions/current", (IAuximRuntime runtime) =>
{
    runtime.ClearCurrentSession();
    return Results.Ok(new { currentSessionId = "" });
});

app.MapGet("/v1/message-conversations", (IAuximRuntime runtime) =>
    Results.Ok(runtime.ListExternalConversations()));

app.MapPost("/v1/messages", async (
    GatewayMessageRequest request,
    IAuximRuntime runtime,
    IApprovalHandler approvalHandler,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Platform))
    {
        return Results.BadRequest(new GatewayError("platform_required", "Platform is required."));
    }

    if (string.IsNullOrWhiteSpace(request.ConversationId))
    {
        return Results.BadRequest(new GatewayError(
            "conversation_id_required",
            "Conversation id is required."));
    }

    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return Results.BadRequest(new GatewayError("user_id_required", "User id is required."));
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new GatewayError("text_required", "Message text is required."));
    }

    if (!IsValidMessageScope(request.Scope))
    {
        return Results.BadRequest(new GatewayError(
            "invalid_scope",
            "Scope must be either 'participant' or 'conversation'."));
    }

    try
    {
        var result = await runtime.SendExternalMessageAsync(
            request.ToRuntimeRequest(),
            new AuximRuntimeOptions { ApprovalHandler = approvalHandler },
            cancellationToken: cancellationToken);
        return Results.Ok(GatewayMessageResponse.FromRuntimeResult(result));
    }
    catch (Exception exception)
    {
        return Results.Problem(
            title: "Message request failed.",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/v1/chat", async (
    GatewayChatRequest request,
    IAuximRuntime runtime,
    IApprovalHandler approvalHandler,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest(new GatewayError("prompt_required", "Prompt is required."));
    }

    try
    {
        var result = await runtime.ChatAsync(
            request.ToRuntimeRequest(),
            new AuximRuntimeOptions { ApprovalHandler = approvalHandler },
            cancellationToken);

        return Results.Ok(new GatewayChatResponse(
            result.SessionId,
            result.FinalResponse,
            result.RunId.Value));
    }
    catch (Exception exception)
    {
        return Results.Problem(
            title: "Chat request failed.",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/v1/chat/stream", async (
    GatewayChatRequest request,
    IAuximRuntime runtime,
    IApprovalHandler approvalHandler,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(
            new GatewayError("prompt_required", "Prompt is required."),
            cancellationToken);
        return;
    }

    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    httpContext.Response.ContentType = "text/event-stream";

    var events = Channel.CreateUnbounded<RuntimeEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    var chatTask = Task.Run(async () =>
    {
        try
        {
            _ = await runtime.ChatAsync(
                request.ToRuntimeRequest(),
                new AuximRuntimeOptions
                {
                    ApprovalHandler = approvalHandler,
                    EventSink = new DelegateRuntimeEventSink((runtimeEvent, _) =>
                    {
                        events.Writer.TryWrite(runtimeEvent);
                        return ValueTask.CompletedTask;
                    }),
                },
                cancellationToken);
            events.Writer.TryComplete();
        }
        catch (Exception)
        {
            events.Writer.TryComplete();
        }
    }, cancellationToken);

    try
    {
        await foreach (var runtimeEvent in events.Reader.ReadAllAsync(cancellationToken))
        {
            await WriteSseEventAsync(
                httpContext.Response,
                GatewayStreamEvent.FromRuntimeEvent(runtimeEvent),
                cancellationToken);
        }

        await chatTask;
    }
    catch (OperationCanceledException)
    {
        // Client disconnected or request was cancelled.
    }
});

await app.RunAsync();

static IReadOnlyList<string> ParseCsv(string? raw) =>
    string.IsNullOrWhiteSpace(raw)
        ? []
        : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();

static bool IsAuthorized(HttpRequest request, string token)
{
    var header = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(header[prefix.Length..].Trim(), token, StringComparison.Ordinal);
}

static bool IsValidMessageScope(string? scope) =>
    string.Equals(scope, "participant", StringComparison.OrdinalIgnoreCase)
    || string.Equals(scope, "conversation", StringComparison.OrdinalIgnoreCase);

static async Task WriteSseEventAsync(
    HttpResponse response,
    GatewayStreamEvent gatewayEvent,
    CancellationToken cancellationToken)
{
    await response.WriteAsync($"event: {gatewayEvent.Type}\n", cancellationToken);
    await response.WriteAsync(
        $"data: {JsonSerializer.Serialize(gatewayEvent.Payload, JsonSerializerOptions.Web)}\n\n",
        cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public sealed record GatewayChatRequest(
    string Prompt,
    bool UseCurrentSession = true,
    bool AppendToSession = true,
    string? SessionId = null)
{
    public AuximChatRequest ToRuntimeRequest() =>
        new(Prompt, UseCurrentSession, AppendToSession, SessionId);
}

public sealed record GatewayChatResponse(
    string SessionId,
    string FinalResponse,
    string RunId);

public sealed record GatewayCreateSessionRequest(
    string? Title = null);

public sealed record GatewayMessageRequest(
    string Platform,
    string ConversationId,
    string UserId,
    string Text,
    string Scope = "participant",
    string? DisplayName = null,
    string? MessageId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public AuximExternalMessageRequest ToRuntimeRequest() =>
        new(Platform, ConversationId, UserId, Text, Scope, DisplayName, MessageId, Metadata);
}

public sealed record GatewayMessageResponse(
    string Platform,
    string ConversationId,
    string UserId,
    string Scope,
    string ConversationKey,
    string SessionId,
    string FinalResponse,
    string RunId)
{
    public static GatewayMessageResponse FromRuntimeResult(AuximExternalMessageResult result) =>
        new(
            result.Platform,
            result.ConversationId,
            result.UserId,
            result.Scope,
            result.ConversationKey,
            result.SessionId,
            result.FinalResponse,
            result.RunId.Value);
}

public sealed record GatewayError(
    string Code,
    string Message);

public sealed record GatewayStreamEvent(
    string Type,
    object Payload)
{
    public static GatewayStreamEvent FromRuntimeEvent(RuntimeEvent runtimeEvent) =>
        runtimeEvent switch
        {
            RuntimeContentDeltaEvent content => new("content_delta", new { delta = content.Delta }),
            RuntimeToolStartedEvent started => new("tool_event", new
            {
                kind = "start",
                name = started.ToolName,
                detail = $"{started.ResourceAccesses.Count} resource access declarations",
            }),
            RuntimeToolCompletedEvent completed => new("tool_event", new
            {
                kind = completed.Outcome,
                name = completed.ToolName,
                detail = $"{completed.OutputLength} chars",
            }),
            RuntimeRunCompletedEvent completed => new("final", new
            {
                sessionId = completed.SessionId,
                finalResponse = completed.FinalResponse,
                runId = completed.RunId.Value,
            }),
            RuntimeRunFailedEvent failed => new("error", new { message = failed.Error, runId = failed.RunId.Value }),
            _ => new("runtime_event", runtimeEvent),
        };
}
