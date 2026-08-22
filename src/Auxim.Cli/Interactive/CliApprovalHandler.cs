using Auxim.Core.Approval;

namespace Auxim.Cli.Interactive;

internal sealed class CliApprovalHandler : IApprovalHandler
{
    public Task<ApprovalResponse> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Console.IsInputRedirected
            ? Task.FromResult(ApprovalResponse.Deny(
                "CLI approval requires an interactive terminal."))
            : ApprovalRenderer.PromptAsync(request, cancellationToken);
    }
}
