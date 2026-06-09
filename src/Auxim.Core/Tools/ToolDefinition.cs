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
}
