using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.Runtime;
using Auxim.Core.State;
using Auxim.Core.Tools;
using Xunit;

namespace Auxim.Core.Tests;

public sealed class AuximRuntimeServiceTests : IDisposable
{
    private readonly string _home;

    public AuximRuntimeServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "auxim-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    [Fact]
    public async Task ChatAsyncRunsAgentAndAppendsSession()
    {
        var runtime = new AuximRuntimeService(
            _ => new EchoAgentClient(),
            () => new ToolRegistry(),
            () => new SessionStore(_home),
            () => new AuximConfig());

        var result = await runtime.ChatAsync(new AuximChatRequest("hello runtime"));

        Assert.Contains("hello runtime", result.FinalResponse);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionId));

        var store = new SessionStore(_home);
        var session = Assert.Single(store.List());
        var document = store.TryLoad(session.Id);
        Assert.NotNull(document);
        Assert.Equal(2, document!.Messages.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
