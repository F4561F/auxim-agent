using Auxim.Tools;
using Auxim.Core.Resources;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public async Task InvokeAsyncRunsRegisteredTool()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var result = await registry.InvokeAsync(
            "echo",
            new Dictionary<string, object?> { ["text"] = "hello" });

        Assert.Equal("hello", result);
    }

    [Fact]
    public void BuiltInToolsResolveArgumentSpecificResourceAccess()
    {
        var registry = BuiltInTools.CreateDefaultRegistry();

        var fileAccess = Assert.Single(registry.Get("file.write").ResolveResourceAccesses(
            new Dictionary<string, object?> { ["path"] = "/workspace/output.txt" }));
        var shellAccess = Assert.Single(registry.Get("shell.run").ResolveResourceAccesses(
            new Dictionary<string, object?> { ["command"] = "cat /workspace/README.md" }));

        Assert.Equal(ResourceAction.Write, fileAccess.Action);
        Assert.Equal("vafs:/workspace/output.txt", fileAccess.Resource.Value);
        Assert.True(fileAccess.RequiresApproval);
        Assert.Equal(ResourceAction.Execute, shellAccess.Action);
        Assert.StartsWith("vashell:", shellAccess.Resource.Value);
        Assert.True(shellAccess.RequiresApproval);
        Assert.Throws<ArgumentException>(() => ResourceUri.Vafs("/workspace/../../etc/passwd"));
    }

}
