namespace Auxim.Core.Vafs;

public sealed record VirtualMount(
    string Name,
    string VirtualPath,
    string HostPath,
    bool ReadOnly);
