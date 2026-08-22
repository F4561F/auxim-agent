namespace Auxim.Core.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ToolDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Tool name is required.", nameof(definition));
        }

        _tools[definition.Name] = definition;
    }

    public IReadOnlyCollection<ToolDefinition> List() => _tools.Values.ToArray();

    public ToolDefinition Get(string name)
    {
        if (!_tools.TryGetValue(name, out var definition))
        {
            throw new InvalidOperationException($"Tool '{name}' is not registered.");
        }

        return definition;
    }

    public async Task<string> InvokeAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        return await Get(name).Handler(arguments, cancellationToken);
    }
}
