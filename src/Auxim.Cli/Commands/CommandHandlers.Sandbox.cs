using Auxim.Core.Config;
using Auxim.Core.Vafs;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleSandbox(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "show" => ShowSandbox(),
            "workspace" => SetSandboxWorkspace(args.Skip(1).FirstOrDefault()),
            "mount" => MountSandboxVolume(args.Skip(1).ToArray()),
            "unmount" => UnmountSandboxVolume(args.Skip(1).FirstOrDefault()),
            _ => PrintSandboxHelp(),
        };
    }

    private static int ShowSandbox()
    {
        var config = ConfigLoader.Load();
        var vfs = VirtualFileSystem.FromEnvironment();
        Console.WriteLine("Auxim Sandbox");
        Console.WriteLine($"Config:    {ConfigLoader.GetConfigPath()}");
        Console.WriteLine($"Workspace: /workspace -> {WorkspaceHostPath(config)}");
        Console.WriteLine("Mounts:");

        var mounts = config.Sandbox.Mounts
            .OrderBy(mount => mount.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mounts.Length == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var mount in mounts)
            {
                Console.WriteLine($"  /volumes/{mount.Name} -> {mount.HostPath}{(mount.ReadOnly ? " (read-only)" : "")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(vfs.DescribeForAgent());
        return 0;
    }

    private static int SetSandboxWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Usage: auxim sandbox workspace <host-path>");
            return 1;
        }

        var fullPath = Path.GetFullPath(ExpandHome(path));
        if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"Directory not found: {fullPath}");
            return 1;
        }

        var config = ConfigLoader.Load();
        var updated = new AuximConfig
        {
            Model = config.Model,
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = new SandboxConfig
            {
                Workspace = fullPath,
                Mounts = config.Sandbox.Mounts,
                Shell = config.Sandbox.Shell,
            },
        };
        ConfigLoader.Save(updated);
        Console.WriteLine($"Mapped /workspace -> {fullPath}");
        return 0;
    }

    private static int MountSandboxVolume(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return PrintSandboxHelp();
        }

        var name = args[0].Trim();
        if (!IsValidMountName(name))
        {
            Console.Error.WriteLine("Mount name must contain only letters, digits, '-', or '_'.");
            return 1;
        }

        var hostPath = Path.GetFullPath(ExpandHome(args[1]));
        if (!Directory.Exists(hostPath))
        {
            Console.Error.WriteLine($"Directory not found: {hostPath}");
            return 1;
        }

        var readOnly = args.Skip(2).Any(arg =>
            arg.Equals("--read-only", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--ro", StringComparison.OrdinalIgnoreCase));
        var config = ConfigLoader.Load();
        var mounts = config.Sandbox.Mounts
            .Where(mount => !mount.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        mounts.Add(new SandboxMountConfig
        {
            Name = name,
            HostPath = hostPath,
            ReadOnly = readOnly,
        });

        ConfigLoader.Save(new AuximConfig
        {
            Model = config.Model,
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = new SandboxConfig
            {
                Workspace = config.Sandbox.Workspace,
                Mounts = mounts,
                Shell = config.Sandbox.Shell,
            },
        });
        Console.WriteLine($"Mounted /volumes/{name} -> {hostPath}{(readOnly ? " (read-only)" : "")}");
        return 0;
    }

    private static int UnmountSandboxVolume(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Usage: auxim sandbox unmount <name>");
            return 1;
        }

        var config = ConfigLoader.Load();
        var mounts = config.Sandbox.Mounts
            .Where(mount => !mount.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mounts.Count == config.Sandbox.Mounts.Count)
        {
            Console.Error.WriteLine($"Mount not found: {name}");
            return 1;
        }

        ConfigLoader.Save(new AuximConfig
        {
            Model = config.Model,
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = new SandboxConfig
            {
                Workspace = config.Sandbox.Workspace,
                Mounts = mounts,
                Shell = config.Sandbox.Shell,
            },
        });
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
