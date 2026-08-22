namespace Auxim.Core.Runtime;

public sealed record AuximApplicationPaths(
    string HomeDirectory,
    string ConfigPath,
    string SecretsPath,
    string HistoryPath,
    string LogPath);

public sealed record AuximModelSettings(
    string Provider,
    string Model,
    string? BaseUrl,
    string ConfigPath,
    string SecretsPath);

public sealed record AuximCredentialStatus(
    string Provider,
    string EnvironmentVariable,
    bool Required,
    bool Configured,
    string SecretsPath);

public sealed record AuximSandboxMount(
    string Name,
    string VirtualPath,
    string HostPath,
    bool ReadOnly);

public sealed record AuximSandboxStatus(
    string ConfigPath,
    string WorkspaceHostPath,
    string TempHostPath,
    IReadOnlyList<AuximSandboxMount> Mounts,
    string AgentDescription);

public sealed record AuximDiagnostics(
    AuximApplicationPaths Paths,
    bool ConfigExists,
    bool SecretsExist,
    AuximModelSettings Model,
    AuximCredentialStatus Credential,
    int ToolCount,
    string WorkspaceVirtualPath,
    int MountCount,
    int SessionCount,
    string ShellPolicy);

public sealed record AuximExternalMessageRequest(
    string Platform,
    string ConversationId,
    string UserId,
    string Text,
    string Scope = "participant",
    string? DisplayName = null,
    string? MessageId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuximExternalMessageResult(
    string Platform,
    string ConversationId,
    string UserId,
    string Scope,
    string ConversationKey,
    string SessionId,
    string FinalResponse,
    AuximRunId RunId);

public sealed record AuximExternalConversation(
    string Key,
    string Platform,
    string ConversationId,
    string UserId,
    string Scope,
    string SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
