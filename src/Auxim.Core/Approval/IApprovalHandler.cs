using Auxim.Core.Resources;

namespace Auxim.Core.Approval;

public interface IApprovalHandler
{
    Task<ApprovalResponse> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken);
}

public sealed record ApprovalRequest(
    string RequestId,
    string RunId,
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyList<ResourceAccess> ResourceAccesses);

public sealed record ApprovalResponse(
    bool Approved,
    string Reason,
    bool Remember = false)
{
    public static ApprovalResponse Allow(bool remember = false) =>
        new(true, "", remember);

    public static ApprovalResponse Deny(string reason) =>
        new(false, reason, false);
}

public sealed class NonInteractiveApprovalHandler : IApprovalHandler
{
    public static NonInteractiveApprovalHandler Instance { get; } = new();

    private NonInteractiveApprovalHandler()
    {
    }

    public Task<ApprovalResponse> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApprovalResponse.Deny(
            "Resource approval is required, but this runtime frontend is non-interactive."));
    }
}

public sealed record ApprovalGrant(
    string Id,
    ResourceAction Action,
    ResourceUri Resource,
    string? ToolName = null);
