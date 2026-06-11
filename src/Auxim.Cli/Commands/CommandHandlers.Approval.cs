using Auxim.Core.Approval;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleApproval(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "list";
        var service = new ToolApprovalService();
        return subcommand switch
        {
            "clear" => ClearApprovals(service),
            "list" => ListApprovals(service),
            "revoke" => RevokeApproval(service, args.Skip(1).FirstOrDefault()),
            _ => PrintApprovalHelp(),
        };
    }

    private static int ListApprovals(ToolApprovalService service)
    {
        var tools = service.ListAlwaysAllowedTools();
        if (tools.Count == 0)
        {
            Console.WriteLine("No tools are always allowed.");
            return 0;
        }

        foreach (var tool in tools)
        {
            Console.WriteLine(tool);
        }

        return 0;
    }

    private static int ClearApprovals(ToolApprovalService service)
    {
        service.ClearAlwaysAllowedTools();
        Console.WriteLine("Cleared always-allowed tool approvals.");
        return 0;
    }

    private static int RevokeApproval(ToolApprovalService service, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            Console.WriteLine("Usage: auxim approval revoke <tool-name>");
            return 1;
        }

        if (!service.RevokeAlwaysAllowedTool(toolName))
        {
            Console.WriteLine($"Tool is not always allowed: {toolName}");
            return 1;
        }

        Console.WriteLine($"Revoked always-allowed approval for: {toolName}");
        return 0;
    }

    private static int PrintApprovalHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim approval list");
        Console.WriteLine("  auxim approval revoke <tool-name>");
        Console.WriteLine("  auxim approval clear");
        return 1;
    }
}
