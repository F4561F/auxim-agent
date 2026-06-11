using System.Text.Json;
using Auxim.Core.Config;

namespace Auxim.Core.Approval;

/// <summary>
/// Delegate that prompts the user for tool approval in an interactive terminal.
/// Returns the approval decision together with a flag indicating whether the
/// user chose "always allow" (so the store can persist it).
/// </summary>
public delegate (ToolApprovalDecision Decision, bool AlwaysAllow) ApprovalUIPrompt(
    string toolName,
    IReadOnlyDictionary<string, object?> arguments);

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
    private readonly ApprovalUIPrompt? _uiPrompt;

    /// <param name="uiPrompt">
    /// Optional interactive prompt. When null the service operates in
    /// non-interactive mode and always denies high-risk tools.
    /// </param>
    public ToolApprovalService(ApprovalUIPrompt? uiPrompt = null, string? home = null)
    {
        _uiPrompt = uiPrompt;
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

        if (_uiPrompt is not null)
        {
            var (decision, alwaysAllow) = _uiPrompt(toolName, arguments);
            if (alwaysAllow)
            {
                store.AlwaysAllowedTools.Add(toolName);
                SaveStore(store);
            }

            return decision;
        }

        // Non-interactive fallback: always deny high-risk tools when no UI is attached.
        return ToolApprovalDecision.Deny(
            "Tool approval is required, but the process is not attached to an interactive terminal.");
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

    public bool RevokeAlwaysAllowedTool(string toolName)
    {
        var store = LoadStore();
        var removed = store.AlwaysAllowedTools.RemoveAll(
            tool => string.Equals(tool, toolName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            SaveStore(store);
        }

        return removed;
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
