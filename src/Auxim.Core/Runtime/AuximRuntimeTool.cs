namespace Auxim.Core.Runtime;

public sealed record AuximRuntimeTool(
    string Name,
    string SchemaName,
    string Toolset,
    string Description,
    IReadOnlyDictionary<string, object?> ParametersSchema,
    bool DeclaresResourceAccess);
