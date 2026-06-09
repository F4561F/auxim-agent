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

    private static int PrintApprovalHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim approval list");
        Console.WriteLine("  auxim approval clear");
        return 1;
    }
}
