using Auxim.Core.Resources;

namespace Auxim.Core.Tools;

public sealed record ToolDefinition(
    string Name,
    string Toolset,
    string Description,
    Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<string>> Handler)
{
    public string SchemaName => Name.Replace('.', '_');

    public IReadOnlyDictionary<string, object?> ParametersSchema { get; init; } =
        new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
            ["additionalProperties"] = true,
        };

    /// <summary>
    /// Declares argument-specific resource access for approval and audit. This
    /// declaration does not sandbox the handler or reduce host process rights.
    /// </summary>
    public Func<IReadOnlyDictionary<string, object?>, IReadOnlyList<ResourceAccess>>? ResourceAccessResolver { get; init; }

    public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
        IReadOnlyDictionary<string, object?> arguments) =>
        ResourceAccessResolver?.Invoke(arguments) ?? [];
}
