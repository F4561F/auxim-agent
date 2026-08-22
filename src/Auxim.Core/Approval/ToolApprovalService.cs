using System.Text.Json;
using Auxim.Core.Config;
using Auxim.Core.Resources;
using Auxim.Core.Runtime;

namespace Auxim.Core.Approval;

public sealed class ToolApprovalService
{
    private readonly string _storePath;

    public ToolApprovalService(string? home = null)
    {
        home ??= ConfigLoader.GetAuximHome();
        _storePath = Path.Combine(home, "approvals.json");
    }

    public async Task<ApprovalResponse> ReviewAsync(
        AuximRunId runId,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        IReadOnlyList<ResourceAccess> resourceAccesses,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink,
        CancellationToken cancellationToken)
    {
        var protectedAccesses = resourceAccesses
            .Where(access => access.RequiresApproval)
            .ToArray();
        if (protectedAccesses.Length == 0)
        {
            return ApprovalResponse.Allow();
        }

        var store = LoadStore();
        if (IsGranted(store, toolName, protectedAccesses))
        {
            return ApprovalResponse.Allow();
        }

        var request = new ApprovalRequest(
            Guid.NewGuid().ToString("N"),
            runId.Value,
            toolName,
            arguments,
            protectedAccesses);
        await eventSink.PublishAsync(
            new RuntimeApprovalRequestedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                request),
            cancellationToken);

        var response = await approvalHandler.RequestAsync(request, cancellationToken);
        if (response.Approved && response.Remember)
        {
            foreach (var access in protectedAccesses)
            {
                if (store.Grants.Any(grant => Matches(grant, access)))
                {
                    continue;
                }

                store.Grants.Add(new ApprovalGrant(
                    Guid.NewGuid().ToString("N"),
                    access.Action,
                    access.Resource,
                    toolName));
            }

            SaveStore(store);
        }

        await eventSink.PublishAsync(
            new RuntimeApprovalResolvedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                request.RequestId,
                response.Approved,
                response.Approved && response.Remember,
                response.Reason),
            cancellationToken);
        return response;
    }

    public IReadOnlyList<ApprovalGrant> ListGrants()
    {
        var store = LoadStore();
        var grants = store.Grants.ToList();
        grants.AddRange(store.AlwaysAllowedTools.Select(tool => new ApprovalGrant(
            $"legacy:{tool}",
            new ResourceAction("legacy-tool"),
            ResourceUri.Opaque("tool", tool),
            tool)));
        return grants
            .OrderBy(grant => grant.Action.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(grant => grant.Resource.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ClearGrants() => SaveStore(new ApprovalStore());

    public bool RevokeGrant(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var store = LoadStore();
        var removed = store.Grants.RemoveAll(
            grant => string.Equals(grant.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (id.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = id["legacy:".Length..];
            removed |= store.AlwaysAllowedTools.RemoveAll(
                tool => string.Equals(tool, toolName, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            SaveStore(store);
        }

        return removed;
    }

    private static bool IsGranted(
        ApprovalStore store,
        string toolName,
        IReadOnlyList<ResourceAccess> accesses)
    {
        if (store.AlwaysAllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return accesses.All(access => store.Grants.Any(grant => Matches(grant, access)));
    }

    private static bool Matches(ApprovalGrant grant, ResourceAccess access) =>
        string.Equals(grant.Action.Value, access.Action.Value, StringComparison.OrdinalIgnoreCase)
        && string.Equals(grant.Resource.Value, access.Resource.Value, StringComparison.OrdinalIgnoreCase);

    private ApprovalStore LoadStore()
    {
        if (!File.Exists(_storePath))
        {
            return new ApprovalStore();
        }

        try
        {
            return JsonSerializer.Deserialize<ApprovalStore>(
                File.ReadAllText(_storePath),
                JsonOptions()) ?? new ApprovalStore();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ApprovalStore();
        }
    }

    private void SaveStore(ApprovalStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath) ?? ".");
        File.WriteAllText(
            _storePath,
            JsonSerializer.Serialize(store, JsonOptions()) + Environment.NewLine);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}

public sealed class ApprovalStore
{
    public List<ApprovalGrant> Grants { get; set; } = [];

    // Read-only compatibility for approvals.json created before resource grants.
    public List<string> AlwaysAllowedTools { get; set; } = [];
}
