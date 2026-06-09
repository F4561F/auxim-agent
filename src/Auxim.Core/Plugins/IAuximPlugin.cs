using Auxim.Core.Tools;

namespace Auxim.Core.Plugins;

public interface IAuximPlugin
{
    string Name { get; }
    void Register(ToolRegistry tools);
}
