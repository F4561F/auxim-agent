using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleDoctor(IAuximRuntime runtime)
    {
        var diagnostics = runtime.GetDiagnostics();
        Console.WriteLine("Auxim Doctor");
        Console.WriteLine($"Home:        {diagnostics.Paths.HomeDirectory}");
        Console.WriteLine($"Config:      {diagnostics.Paths.ConfigPath} {(diagnostics.ConfigExists ? "ok" : "missing")}");
        Console.WriteLine($"Env:         {diagnostics.Paths.SecretsPath} {(diagnostics.SecretsExist ? "ok" : "missing")}");
        Console.WriteLine($"Provider:    {diagnostics.Model.Provider}");
        Console.WriteLine($"Model:       {diagnostics.Model.Model}");
        Console.WriteLine($"Base URL:    {diagnostics.Model.BaseUrl ?? "(default)"}");
        Console.WriteLine($"API key:     {FormatApiKeyStatus(diagnostics.Credential)}");
        Console.WriteLine($"Tools:       {diagnostics.ToolCount}");
        Console.WriteLine($"Workspace:   {diagnostics.WorkspaceVirtualPath}");
        Console.WriteLine($"Mounts:      {diagnostics.MountCount}");
        Console.WriteLine($"Shell:       {diagnostics.ShellPolicy}");
        Console.WriteLine($"Sessions:    {diagnostics.SessionCount}");
        Console.WriteLine($"Log file:    {diagnostics.Paths.LogPath}");
        return 0;
    }
}
