using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Core.Vafs;
using Auxim.Tools;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleDoctor()
    {
        var config = ConfigLoader.Load();
        var registry = BuiltInTools.CreateDefaultRegistry();
        var keyName = ApiKeyNameForProvider(config.Model.Provider);
        Console.WriteLine("Auxim Doctor");
        Console.WriteLine($"Home:        {ConfigLoader.GetAuximHome()}");
        Console.WriteLine($"Config:      {ConfigLoader.GetConfigPath()} {(File.Exists(ConfigLoader.GetConfigPath()) ? "ok" : "missing")}");
        Console.WriteLine($"Env:         {ConfigLoader.GetEnvPath()} {(File.Exists(ConfigLoader.GetEnvPath()) ? "ok" : "missing")}");
        Console.WriteLine($"Provider:    {config.Model.Provider}");
        Console.WriteLine($"Model:       {config.Model.Name}");
        Console.WriteLine($"Base URL:    {config.Model.BaseUrl ?? "(default)"}");
        Console.WriteLine($"API key:     {FormatApiKeyStatus(config.Model.Provider, keyName)}");
        Console.WriteLine($"Tools:       {registry.List().Count}");
        Console.WriteLine($"Workspace:   {VirtualFileSystem.FromEnvironment().ListMounts().First(mount => mount.Name == "workspace").VirtualPath}");
        Console.WriteLine($"Mounts:      {VirtualFileSystem.FromEnvironment().ListMounts().Count - 1}");
        Console.WriteLine($"Shell:       {(Environment.GetEnvironmentVariable("AUXIM_ALLOW_SHELL") == "true" ? "enabled" : "disabled")}");
        Console.WriteLine($"Sessions:    {new SessionStore().List().Count}");
        Console.WriteLine($"Log file:    {Path.Combine(ConfigLoader.GetAuximHome(), "logs", "agent.log")}");
        return 0;
    }
}
