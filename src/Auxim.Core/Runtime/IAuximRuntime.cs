using Auxim.Core.Approval;
using Auxim.Core.Resources;

namespace Auxim.Core.Runtime;

public interface IAuximRuntime
{
    AuximApplicationPaths GetApplicationPaths();

    string GetConfigJson();

    void SetConfigValue(string keyPath, string value);

    AuximModelSettings GetModelSettings();

    AuximModelSettings SetModelSettings(string provider, string model, string? baseUrl = null);

    AuximCredentialStatus GetCredentialStatus(string? provider = null);

    void SetApiKey(string provider, string apiKey);

    IReadOnlyList<ApprovalGrant> ListApprovalGrants();

    void ClearApprovalGrants();

    bool RevokeApprovalGrant(string grantId);

    AuximSandboxStatus GetSandboxStatus();

    AuximSandboxStatus SetSandboxWorkspace(string hostPath);

    AuximSandboxStatus MountSandboxVolume(string name, string hostPath, bool readOnly = false);

    bool UnmountSandboxVolume(string name);

    AuximDiagnostics GetDiagnostics();

    IReadOnlyList<string> LoadInputHistory();

    void SaveInputHistory(IReadOnlyList<string> entries);

    Task<int> RunHostCommandAsync(
        string command,
        CancellationToken cancellationToken = default);

    AuximRuntimeStatus GetStatus();

    IReadOnlyList<AuximRuntimeTool> ListTools();

    Task<string> InvokeToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<ResourceAccess> ResolveToolResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments);

    IReadOnlyList<AuximRuntimeSessionSummary> ListSessions();

    IReadOnlyList<AuximRuntimeSessionSummary> SearchSessions(string query);

    AuximRuntimeSession GetOrCreateCurrentSession();

    AuximRuntimeSession? GetSession(string id);

    AuximRuntimeSession CreateSession(string? title = null, bool makeCurrent = true);

    AuximRuntimeSession? UseSession(string id);

    void ClearCurrentSession();

    IReadOnlyList<AuximExternalConversation> ListExternalConversations();

    Task<AuximExternalMessageResult> SendExternalMessageAsync(
        AuximExternalMessageRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);
}
