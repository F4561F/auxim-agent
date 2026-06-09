using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.Logging;

namespace Auxim.Core.Approval;

public sealed class ToolApprovalService
{
    private static readonly HashSet<string> HighRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "shell.run",
        "file.write",
        "file.patch",
        "todo.done",
    };

    private readonly string _storePath;

    public ToolApprovalService(string? home = null)
    {
        home ??= ConfigLoader.GetAuximHome();
        _storePath = Path.Combine(home, "approvals.json");
    }

    public ToolApprovalDecision Review(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        if (!HighRiskTools.Contains(toolName))
        {
            return ToolApprovalDecision.Allow;
        }

        var store = LoadStore();
        if (store.AlwaysAllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return ToolApprovalDecision.Allow;
        }

        if (Console.IsInputRedirected)
        {
            return ToolApprovalDecision.Deny(
                "Tool approval is required, but the process is not attached to an interactive terminal.");
        }

        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| Auxim Safety Review                            |");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"Tool: {toolName}");
        Console.WriteLine("Risk: may modify files, mark state, or execute commands.");
        Console.WriteLine("Arguments:");
        Console.WriteLine(JsonSerializer.Serialize(arguments, JsonOptions()));
        Console.WriteLine();
        Console.WriteLine("1. Allow once");
        Console.WriteLine("2. Always allow this tool");
        Console.WriteLine("3. Deny and give feedback");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Choice [1/2/3]: ");
            var choice = Console.ReadLine()?.Trim();
            switch (choice)
            {
                case "1":
                    AuximLog.Info($"approval.allow_once tool={toolName}");
                    return ToolApprovalDecision.Allow;
                case "2":
                    store.AlwaysAllowedTools.Add(toolName);
                    SaveStore(store);
                    AuximLog.Info($"approval.always_allow tool={toolName}");
                    return ToolApprovalDecision.Allow;
                case "3":
                    Console.Write("Reason or suggestion for the model: ");
                    var reason = Console.ReadLine();
                    reason = string.IsNullOrWhiteSpace(reason)
                        ? "User denied this tool call."
                        : reason.Trim();
                    AuximLog.Warning($"approval.denied tool={toolName} reason={reason}");
                    return ToolApprovalDecision.Deny(reason);
                default:
                    Console.WriteLine("Choose 1, 2, or 3.");
                    break;
            }
        }
    }

    public IReadOnlyList<string> ListAlwaysAllowedTools()
    {
        return LoadStore().AlwaysAllowedTools
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ClearAlwaysAllowedTools()
    {
        SaveStore(new ApprovalStore());
    }

    private ApprovalStore LoadStore()
    {
        if (!File.Exists(_storePath))
        {
            return new ApprovalStore();
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<ApprovalStore>(json, JsonOptions()) ?? new ApprovalStore();
        }
        catch
        {
            return new ApprovalStore();
        }
    }

    private void SaveStore(ApprovalStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath) ?? ".");
        File.WriteAllText(_storePath, JsonSerializer.Serialize(store, JsonOptions()) + Environment.NewLine);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}

public sealed record ToolApprovalDecision(bool Approved, string Reason)
{
    public static ToolApprovalDecision Allow { get; } = new(true, "");
    public static ToolApprovalDecision Deny(string reason) => new(false, reason);
}

public sealed class ApprovalStore
{
    public List<string> AlwaysAllowedTools { get; set; } = [];
}
