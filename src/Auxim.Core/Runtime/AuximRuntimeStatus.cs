namespace Auxim.Core.Runtime;

public sealed record AuximRuntimeStatus(
    string HomeDirectory,
    string ModelProvider,
    string ModelName,
    string? ModelBaseUrl,
    int MaxIterations,
    string CurrentSessionId,
    string Workspace,
    int MountCount);
