using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleApproval(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        var subcommand = args.FirstOrDefault() ?? "list";
        return subcommand switch
        {
            "clear" => ClearApprovals(runtime),
            "list" => ListApprovals(runtime),
            "revoke" => RevokeApproval(runtime, args.Skip(1).FirstOrDefault()),
            _ => PrintApprovalHelp(),
        };
    }

    private static int ListApprovals(IAuximRuntime runtime)
    {
        var grants = runtime.ListApprovalGrants();
        if (grants.Count == 0)
        {
            Console.WriteLine("No resource approval grants are stored.");
            return 0;
        }

        foreach (var grant in grants)
        {
            Console.WriteLine($"{grant.Id}  {grant.Action}  {grant.Resource}");
        }

        return 0;
    }

    private static int ClearApprovals(IAuximRuntime runtime)
    {
        runtime.ClearApprovalGrants();
        Console.WriteLine("Cleared resource approval grants.");
        return 0;
    }

    private static int RevokeApproval(IAuximRuntime runtime, string? grantId)
    {
        if (string.IsNullOrWhiteSpace(grantId))
        {
            Console.WriteLine("Usage: auxim approval revoke <grant-id>");
            return 1;
        }

        if (!runtime.RevokeApprovalGrant(grantId))
        {
            Console.WriteLine($"Approval grant not found: {grantId}");
            return 1;
        }

        Console.WriteLine($"Revoked approval grant: {grantId}");
        return 0;
    }

    private static int PrintApprovalHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim approval list");
        Console.WriteLine("  auxim approval revoke <grant-id>");
        Console.WriteLine("  auxim approval clear");
        return 1;
    }
}
