using System.Text.Json;

namespace Auxim.SDK;

public sealed record AuximHealth(
    string Status,
    string Service);

public sealed record AuximGatewayStatus(
    string Service,
    AuximGatewayModelStatus Model,
    AuximGatewayAgentStatus Agent,
    AuximGatewayApprovalStatus Approval,
    AuximGatewayAuthStatus Auth,
    AuximGatewayCorsStatus Cors,
    AuximGatewayConnectorStatus Connectors);

public sealed record AuximGatewayModelStatus(
    string Provider,
    string Name,
    string BaseUrl);

public sealed record AuximGatewayAgentStatus(
    int MaxIterations);

public sealed record AuximGatewayApprovalStatus(
    string Mode,
    bool ProtectedResourcesRequireGrant);

public sealed record AuximGatewayAuthStatus(
    bool Enabled);

public sealed record AuximGatewayCorsStatus(
    IReadOnlyList<string> Origins);

public sealed record AuximGatewayConnectorStatus(
    bool Telegram);

public sealed record AuximToolInfo(
    string Name,
    string SchemaName,
    string Toolset,
    string Description,
    JsonElement ParametersSchema,
    bool DeclaresResourceAccess = false);

public sealed record AuximSessionRecord(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent);

public sealed record AuximSessionDocument(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent,
    IReadOnlyList<AuximAgentMessage> Messages);

public sealed record AuximAgentMessage(
    string Role,
    string Content)
{
    public string? Name { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<AuximToolCallRequest>? ToolCalls { get; init; }
}

public sealed record AuximToolCallRequest(
    string Id,
    string Name,
    string ArgumentsJson);

public sealed record AuximCreateSessionRequest(
    string? Title = null);

public sealed record AuximChatRequest(
    string Prompt,
    bool UseCurrentSession = true,
    bool AppendToSession = true,
    string? SessionId = null);

public sealed record AuximChatResponse(
    string SessionId,
    string FinalResponse,
    string RunId = "");

public sealed record AuximMessageRequest(
    string Platform,
    string ConversationId,
    string UserId,
    string Text,
    string Scope = "participant",
    string? DisplayName = null,
    string? MessageId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuximMessageResponse(
    string Platform,
    string ConversationId,
    string UserId,
    string Scope,
    string ConversationKey,
    string SessionId,
    string FinalResponse,
    string RunId = "");

public sealed record AuximMessageConversationRecord(
    string Key,
    string Platform,
    string ConversationId,
    string UserId,
    string Scope,
    string SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AuximGatewayError(
    string Code,
    string Message);

public sealed record AuximToolEvent(
    string Kind,
    string Name,
    string Detail);

public abstract record AuximGatewayStreamEvent(string Type);

public sealed record AuximContentDeltaEvent(string Delta)
    : AuximGatewayStreamEvent("content_delta");

public sealed record AuximToolEventEvent(AuximToolEvent ToolEvent)
    : AuximGatewayStreamEvent("tool_event");

public sealed record AuximFinalEvent(string SessionId, string FinalResponse, string RunId = "")
    : AuximGatewayStreamEvent("final");

public sealed record AuximStreamErrorEvent(string Message)
    : AuximGatewayStreamEvent("error");

public sealed record AuximUnknownStreamEvent(string EventType, JsonElement Payload)
    : AuximGatewayStreamEvent(EventType);
