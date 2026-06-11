using Auxim.Core.Vafs;
using Auxim.Core.Config;
using Auxim.Tools;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class VirtualFileSystemTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _home;
    private readonly string _tmp;
    private readonly string? _previousWorkspace;
    private readonly string? _previousMounts;
    private readonly string? _previousHome;
    private readonly string? _previousTmp;

    public VirtualFileSystemTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "auxim-vafs-tests", Guid.NewGuid().ToString("N"));
        _home = Path.Combine(Path.GetTempPath(), "auxim-vafs-home", Guid.NewGuid().ToString("N"));
        _tmp = Path.Combine(Path.GetTempPath(), "auxim-vafs-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_workspace, "README.md"), "hello");
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
        var vfs = VirtualFileSystem.FromEnvironment();

        var hostPath = vfs.ResolveToHostPath("/workspace/README.md");
        var virtualPath = vfs.ToVirtualPath(hostPath);

        Assert.Equal(Path.Combine(_workspace, "README.md"), hostPath);
        Assert.Equal("/workspace/README.md", virtualPath);
    }

    [Fact]
    public void ProvidesWritableTmpMount()
    {
        var vfs = VirtualFileSystem.FromEnvironment();

        var hostPath = vfs.ResolveToHostPath("/tmp/generated.txt", requireWritable: true);
        var virtualPath = vfs.ToVirtualPath(hostPath);

        Assert.Equal(Path.Combine(_tmp, "generated.txt"), hostPath);
        Assert.Equal("/tmp/generated.txt", virtualPath);
        Assert.Contains(vfs.ListMounts(), mount => mount.VirtualPath == "/tmp" && !mount.ReadOnly);
    }

    [Fact]
    public void RejectsPathsOutsideVirtualFilesystem()
    {
        var vfs = VirtualFileSystem.FromEnvironment();

        Assert.Throws<VirtualPathException>(() => vfs.ResolveToHostPath("/etc/passwd"));
        Assert.Throws<VirtualPathException>(() => vfs.ResolveToHostPath("../../../etc/passwd"));
    }

    [Fact]
    public void RewritesOnlyHostPathBoundaries()
    {
        var vfs = VirtualFileSystem.FromEnvironment();
        var sibling = _workspace + "0";

        var output = vfs.RewriteHostPathsToVirtual($"{_workspace}/README.md {sibling}/README.md");

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
        var previousShell = Environment.GetEnvironmentVariable("AUXIM_ALLOW_SHELL");
        Environment.SetEnvironmentVariable("AUXIM_ALLOW_SHELL", "true");
        try
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
        finally
        {
            Environment.SetEnvironmentVariable("AUXIM_ALLOW_SHELL", previousShell);
        }
    }

    [Fact]
    public async Task ShellRejectsUnknownAbsolutePath()
    {
        var previousShell = Environment.GetEnvironmentVariable("AUXIM_ALLOW_SHELL");
        Environment.SetEnvironmentVariable("AUXIM_ALLOW_SHELL", "true");
        try
        {
            var registry = BuiltInTools.CreateDefaultRegistry();

            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InvokeAsync(
                "shell.run",
                new Dictionary<string, object?> { ["command"] = "rg hello /etc" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUXIM_ALLOW_SHELL", previousShell);
        }
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

            var vfs = VirtualFileSystem.FromEnvironment();

            Assert.Equal(_workspace, vfs.ResolveToHostPath("/workspace"));
            Assert.Equal(mounted, vfs.ResolveToHostPath("/volumes/extra"));
            Assert.Throws<VirtualPathException>(() => vfs.ResolveToHostPath("/volumes/extra/file.txt", requireWritable: true));
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
