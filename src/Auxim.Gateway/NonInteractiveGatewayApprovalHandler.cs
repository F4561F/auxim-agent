using Auxim.Core.Approval;

public sealed class NonInteractiveGatewayApprovalHandler : IApprovalHandler
{
    public Task<ApprovalResponse> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApprovalResponse.Deny(
            "Gateway cannot approve resource access interactively. Add an explicit resource grant first."));
    }
}
