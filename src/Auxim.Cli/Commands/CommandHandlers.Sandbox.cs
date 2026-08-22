using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleSandbox(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "show" => ShowSandbox(runtime),
            "workspace" => SetSandboxWorkspace(args.Skip(1).FirstOrDefault(), runtime),
            "mount" => MountSandboxVolume(args.Skip(1).ToArray(), runtime),
            "unmount" => UnmountSandboxVolume(args.Skip(1).FirstOrDefault(), runtime),
            _ => PrintSandboxHelp(),
        };
    }

    private static int ShowSandbox(IAuximRuntime runtime)
    {
        var sandbox = runtime.GetSandboxStatus();
        Console.WriteLine("Auxim Sandbox");
        Console.WriteLine($"Config:    {sandbox.ConfigPath}");
        Console.WriteLine($"Workspace: /workspace -> {sandbox.WorkspaceHostPath}");
        Console.WriteLine($"Temp:      /tmp -> {sandbox.TempHostPath}");
        Console.WriteLine("Mounts:");
        if (sandbox.Mounts.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var mount in sandbox.Mounts)
            {
                Console.WriteLine($"  {mount.VirtualPath} -> {mount.HostPath}{(mount.ReadOnly ? " (read-only)" : "")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(sandbox.AgentDescription);
        return 0;
    }

    private static int SetSandboxWorkspace(string? path, IAuximRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Usage: auxim sandbox workspace <host-path>");
            return 1;
        }

        var sandbox = runtime.SetSandboxWorkspace(path);
        Console.WriteLine($"Mapped /workspace -> {sandbox.WorkspaceHostPath}");
        return 0;
    }

    private static int MountSandboxVolume(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        if (args.Count < 2)
        {
            return PrintSandboxHelp();
        }

        var readOnly = args.Skip(2).Any(arg =>
            arg.Equals("--read-only", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--ro", StringComparison.OrdinalIgnoreCase));
        var sandbox = runtime.MountSandboxVolume(args[0].Trim(), args[1], readOnly);
        var mount = sandbox.Mounts.Single(item => item.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"Mounted {mount.VirtualPath} -> {mount.HostPath}{(readOnly ? " (read-only)" : "")}");
        return 0;
    }

    private static int UnmountSandboxVolume(string? name, IAuximRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Usage: auxim sandbox unmount <name>");
            return 1;
        }

        if (!runtime.UnmountSandboxVolume(name))
        {
            Console.Error.WriteLine($"Mount not found: {name}");
            return 1;
        }

        Console.WriteLine($"Unmounted /volumes/{name}");
        return 0;
    }

    private static int PrintSandboxHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim sandbox show");
        Console.WriteLine("  auxim sandbox workspace <host-path>");
        Console.WriteLine("  auxim sandbox mount <name> <host-path> [--read-only]");
        Console.WriteLine("  auxim sandbox unmount <name>");
        return 1;
    }
}
