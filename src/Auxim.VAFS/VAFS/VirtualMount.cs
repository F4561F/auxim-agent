namespace Auxim.VAFS;

public sealed record VirtualMount(
    string Name,
    string VirtualPath,
    string HostPath,
    bool ReadOnly);
