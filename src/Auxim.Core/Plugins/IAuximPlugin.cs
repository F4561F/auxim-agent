using Auxim.Core.Tools;

namespace Auxim.Core.Plugins;

/// <summary>
/// Trusted in-process extension contract. A native DLL plugin runs with the
/// Auxim host process permissions and is not automatically constrained by
/// VAFS, VAShell, resource declarations, or approval policy.
/// </summary>
public interface IAuximPlugin
{
    string Name { get; }
    void Register(ToolRegistry tools);
}
