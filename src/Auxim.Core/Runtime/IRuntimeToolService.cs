using Auxim.Core.Approval;
using Auxim.Core.Resources;
using Auxim.Core.Tools;

namespace Auxim.Core.Runtime;

public interface IRuntimeToolService
{
    IReadOnlyList<AuximRuntimeTool> ListTools();

    IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments);

    Task<string> InvokeAsync(
        AuximRunId runId,
        string toolCallId,
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        string homeDirectory,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink,
        CancellationToken cancellationToken);
}

public sealed class ToolApprovalDeniedException : InvalidOperationException
{
    public ToolApprovalDeniedException(string toolName, string reason)
        : base($"Tool '{toolName}' was denied: {reason}")
    {
        ToolName = toolName;
        Reason = reason;
    }

    public string ToolName { get; }

    public string Reason { get; }
}

public sealed class RuntimeToolService : IRuntimeToolService
{
    private readonly Func<ToolRegistry> _toolRegistryFactory;

    public RuntimeToolService(Func<ToolRegistry> toolRegistryFactory)
    {
        _toolRegistryFactory = toolRegistryFactory;
    }

    public IReadOnlyList<AuximRuntimeTool> ListTools() =>
        _toolRegistryFactory()
            .List()
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tool => new AuximRuntimeTool(
                tool.Name,
                tool.SchemaName,
                tool.Toolset,
                tool.Description,
                tool.ParametersSchema,
                tool.ResourceAccessResolver is not null))
            .ToArray();

    public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments) =>
        _toolRegistryFactory().Get(name).ResolveResourceAccesses(arguments);

    public async Task<string> InvokeAsync(
        AuximRunId runId,
        string toolCallId,
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        string homeDirectory,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink,
        CancellationToken cancellationToken)
    {
        var execution = await new ToolExecutionCoordinator(
            _toolRegistryFactory(),
            homeDirectory,
            approvalHandler,
            eventSink).ExecuteAsync(
                runId,
                toolCallId,
                name,
                arguments,
                cancellationToken);
        if (execution.WasDenied)
        {
            throw new ToolApprovalDeniedException(name, execution.Feedback);
        }

        return execution.Content;
    }
}

internal sealed class EmptyRuntimeToolService : IRuntimeToolService
{
    public static EmptyRuntimeToolService Instance { get; } = new();

    private EmptyRuntimeToolService()
    {
    }

    public IReadOnlyList<AuximRuntimeTool> ListTools() => [];

    public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments) =>
        throw NotRegistered(name);

    public Task<string> InvokeAsync(
        AuximRunId runId,
        string toolCallId,
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        string homeDirectory,
        IApprovalHandler approvalHandler,
        IRuntimeEventSink eventSink,
        CancellationToken cancellationToken) =>
        Task.FromException<string>(NotRegistered(name));

    private static InvalidOperationException NotRegistered(string name) =>
        new($"Tool '{name}' is not registered.");
}
