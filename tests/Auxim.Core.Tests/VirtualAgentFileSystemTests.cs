using Auxim.VAFS;
using Auxim.Core.Config;
using Auxim.Tools;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class VirtualAgentFileSystemTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _home;
    private readonly string _tmp;
    private readonly string? _previousWorkspace;
    private readonly string? _previousMounts;
    private readonly string? _previousHome;
    private readonly string? _previousTmp;

    public VirtualAgentFileSystemTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "auxim-vafs-tests", Guid.NewGuid().ToString("N"));
        _home = Path.Combine(Path.GetTempPath(), "auxim-vafs-home", Guid.NewGuid().ToString("N"));
        _tmp = Path.Combine(Path.GetTempPath(), "auxim-vafs-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_workspace, "README.md"), "hello");
        File.WriteAllText(Path.Combine(_workspace, "notes.txt"), "one two\nthree four\nfive\n");
        File.WriteAllText(Path.Combine(_workspace, ".hidden"), "secret");
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        File.WriteAllText(Path.Combine(_workspace, "src", "App.cs"), "class App {}");

        _previousWorkspace = Environment.GetEnvironmentVariable("AUXIM_WORKSPACE");
        _previousMounts = Environment.GetEnvironmentVariable("AUXIM_VAFS_MOUNTS");
        _previousHome = Environment.GetEnvironmentVariable("AUXIM_HOME");
        _previousTmp = Environment.GetEnvironmentVariable("AUXIM_TMP");
        Environment.SetEnvironmentVariable("AUXIM_WORKSPACE", _workspace);
        Environment.SetEnvironmentVariable("AUXIM_VAFS_MOUNTS", null);
        Environment.SetEnvironmentVariable("AUXIM_HOME", _home);
        Environment.SetEnvironmentVariable("AUXIM_TMP", _tmp);
    }

    [Fact]
    public void ResolvesWorkspacePathWithoutExposingHostPath()
    {
        var vafs = VirtualAgentFileSystem.FromEnvironment();

        var hostPath = vafs.ResolveToHostPath("/workspace/README.md");
        var virtualPath = vafs.ToVirtualPath(hostPath);

        Assert.Equal(Path.Combine(_workspace, "README.md"), hostPath);
        Assert.Equal("/workspace/README.md", virtualPath);
    }

    [Fact]
    public void ProvidesWritableTmpMount()
    {
        var vafs = VirtualAgentFileSystem.FromEnvironment();

        var hostPath = vafs.ResolveToHostPath("/tmp/generated.txt", requireWritable: true);
        var virtualPath = vafs.ToVirtualPath(hostPath);

        Assert.Equal(Path.Combine(_tmp, "generated.txt"), hostPath);
        Assert.Equal("/tmp/generated.txt", virtualPath);
        Assert.Contains(vafs.ListMounts(), mount => mount.VirtualPath == "/tmp" && !mount.ReadOnly);
    }

    [Fact]
    public void RejectsPathsOutsideVirtualFilesystem()
    {
        var vafs = VirtualAgentFileSystem.FromEnvironment();

        Assert.Throws<VirtualPathException>(() => vafs.ResolveToHostPath("/etc/passwd"));
        Assert.Throws<VirtualPathException>(() => vafs.ResolveToHostPath("../../../etc/passwd"));
    }

    [Fact]
    public void RejectsSymbolicLinksThatEscapeMount()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = Path.Combine(Path.GetTempPath(), "auxim-vafs-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "secret.txt");
        var link = Path.Combine(_workspace, "outside-link.txt");
        File.WriteAllText(outsideFile, "secret");
        File.CreateSymbolicLink(link, outsideFile);
        try
        {
            var vafs = VirtualAgentFileSystem.FromEnvironment();

            Assert.Throws<VirtualPathException>(() =>
                vafs.ResolveToHostPath("/workspace/outside-link.txt"));
        }
        finally
        {
            File.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void RewritesOnlyHostPathBoundaries()
    {
        var vafs = VirtualAgentFileSystem.FromEnvironment();
        var sibling = _workspace + "0";

        var output = vafs.RewriteHostPathsToVirtual($"{_workspace}/README.md {sibling}/README.md");

        Assert.Contains("/workspace/README.md", output);
        Assert.Contains($"{sibling}/README.md", output);
        Assert.DoesNotContain("/workspace0", output);
    }

    [Fact]
    public async Task FileListReturnsVirtualPaths()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var output = await registry.InvokeAsync(
            "file.list",
            new Dictionary<string, object?> { ["path"] = "/" });

        Assert.Contains("/workspace/", output);
        Assert.Contains("/tmp/", output);
        Assert.DoesNotContain(_workspace, output);
        Assert.DoesNotContain(_tmp, output);
    }

    [Fact]
    public async Task ShellPwdReturnsVirtualWorkspace()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var output = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "ls /" });

        Assert.Contains("/workspace", output);
        Assert.Contains("/tmp", output);
        Assert.DoesNotContain(_workspace, output);
        Assert.DoesNotContain(_tmp, output);
    }

    [Fact]
    public async Task ShellRejectsUnknownAbsolutePath()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "rg hello /etc" }));
    }

    [Fact]
    public async Task ShellRejectsShellSyntaxAndDangerousExternalCommands()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "echo hi && git status" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "git checkout main" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "dotnet publish" }));
    }

    [Fact]
    public async Task ShellSupportsReadOnlyBuiltIns()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var head = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "head -n 2 /workspace/notes.txt" });
        var tail = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "tail -n1 /workspace/notes.txt" });
        var wc = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "wc -l /workspace/notes.txt" });

        Assert.Contains("one two", head);
        Assert.Contains("three four", head);
        Assert.DoesNotContain("five", head);
        Assert.Contains("five", tail);
        Assert.Contains("3 /workspace/notes.txt", wc);
    }

    [Fact]
    public async Task ShellListHidesDotfilesUnlessRequested()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var normal = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "ls /workspace" });
        var all = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "ls -a /workspace" });

        Assert.DoesNotContain("/workspace/.hidden", normal);
        Assert.Contains("/workspace/.hidden", all);
    }

    [Fact]
    public async Task ShellSupportsFindGrepStatAndTest()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var found = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "find /workspace -type f -name *.cs" });
        var grep = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "grep -n class /workspace/src" });
        var stat = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "stat /workspace/src/App.cs" });
        var test = await registry.InvokeAsync(
            "shell.run",
            new Dictionary<string, object?> { ["command"] = "test -f /workspace/src/App.cs" });

        Assert.Contains("/workspace/src/App.cs", found);
        Assert.Contains("/workspace/src/App.cs:1:class App {}", grep);
        Assert.Contains("file", stat);
        Assert.Contains("/workspace/src/App.cs", stat);
        Assert.Contains("exit_code: 0", test);
        Assert.DoesNotContain(_workspace, found);
        Assert.DoesNotContain(_workspace, grep);
        Assert.DoesNotContain(_workspace, stat);
    }

    [Fact]
    public void LoadsWorkspaceAndMountsFromConfig()
    {
        var home = Path.Combine(Path.GetTempPath(), "auxim-vafs-home", Guid.NewGuid().ToString("N"));
        var mounted = Path.Combine(Path.GetTempPath(), "auxim-vafs-mounted", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(mounted);
        try
        {
            Environment.SetEnvironmentVariable("AUXIM_WORKSPACE", null);
            Environment.SetEnvironmentVariable("AUXIM_HOME", home);
            ConfigLoader.Save(new AuximConfig
            {
                Sandbox = new SandboxConfig
                {
                    Workspace = _workspace,
                    Mounts =
                    [
                        new SandboxMountConfig
                        {
                            Name = "extra",
                            HostPath = mounted,
                            ReadOnly = true,
                        },
                    ],
                },
            });

            var vafs = VirtualAgentFileSystem.FromEnvironment();

            Assert.Equal(_workspace, vafs.ResolveToHostPath("/workspace"));
            Assert.Equal(mounted, vafs.ResolveToHostPath("/volumes/extra"));
            Assert.Throws<VirtualPathException>(() => vafs.ResolveToHostPath("/volumes/extra/file.txt", requireWritable: true));
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }

            if (Directory.Exists(mounted))
            {
                Directory.Delete(mounted, recursive: true);
            }
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUXIM_WORKSPACE", _previousWorkspace);
        Environment.SetEnvironmentVariable("AUXIM_VAFS_MOUNTS", _previousMounts);
        Environment.SetEnvironmentVariable("AUXIM_HOME", _previousHome);
        Environment.SetEnvironmentVariable("AUXIM_TMP", _previousTmp);
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }

        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }
}
