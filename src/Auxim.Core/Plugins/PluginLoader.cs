using System.Reflection;
using Auxim.Core.Config;
using Auxim.Core.Logging;
using Auxim.Core.Tools;

namespace Auxim.Core.Plugins;

public static class PluginLoader
{
    public static IReadOnlyList<string> DiscoverAndRegister(ToolRegistry tools)
    {
        var loaded = new List<string>();
        foreach (var dll in PluginDlls())
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(IAuximPlugin).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type) is not IAuximPlugin plugin)
                    {
                        continue;
                    }

                    plugin.Register(tools);
                    loaded.Add($"{plugin.Name} ({dll})");
                    AuximLog.Info($"plugin.loaded name={plugin.Name} path={dll}");
                }
            }
            catch (Exception exception)
            {
                AuximLog.Warning($"plugin.failed path={dll} message={exception.Message}");
            }
        }

        return loaded;
    }

    private static IEnumerable<string> PluginDlls()
    {
        foreach (var dir in PluginDirs().Where(Directory.Exists))
        {
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                yield return dll;
            }
        }
    }

    private static IEnumerable<string> PluginDirs()
    {
        yield return Path.Combine(Environment.CurrentDirectory, "plugins");
        yield return Path.Combine(ConfigLoader.GetAuximHome(), "plugins");
    }
}
