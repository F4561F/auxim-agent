using System.Text.Json;
using Auxim.Core.Approval;
using Auxim.Core.Config;
using Auxim.Core.Resources;
using Auxim.Core.Runtime;
using Auxim.Core.Tools;
using Xunit;

namespace Auxim.Agent.Tests;

public sealed class AuximAgentToolExecutionTests
{
    [Fact]
    public async Task RunnerUsesRuntimeToolServiceAndReturnsDenialFeedbackToAgent()
    {
        const string environmentDescription = "runtime-provided VAFS environment";
        var client = new DeniedToolCallAgentClient();
        var runtimeTools = new DenyingRuntimeToolService("user rejected this operation");
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition(
            "protected.write",
            "test",
            "Writes a protected resource.",
            (_, _) => Task.FromResult("should not run")));
        var runner = new AuximAgentRunner(_ => client, () => registry, runtimeTools);
        var runId = AuximRunId.New();
        var home = Path.Combine(
            Path.GetTempPath(),
            "auxim-agent-tool-tests",
            Guid.NewGuid().ToString("N"));

        var result = await runner.RunAsync(new AgentRunRequest(
            runId,
            "test-session",
            "write the file",
            [],
            new AuximConfig
            {
                Model = new ModelConfig { Provider = "test", Name = "test" },
                Agent = new AgentConfig { MaxIterations = 2 },
            },
            home,
            environmentDescription,
            NonInteractiveApprovalHandler.Instance,
            NullEventSink.Instance));

        Assert.Equal("continued without the tool", result.FinalResponse);
        Assert.Equal(runId, runtimeTools.RunId);
        Assert.Equal("call-1", runtimeTools.ToolCallId);
        Assert.Equal("protected.write", runtimeTools.ToolName);
        Assert.Equal("/workspace/file.txt", runtimeTools.Arguments["path"]?.ToString());
        Assert.Equal(home, runtimeTools.HomeDirectory);

        var systemPrompt = Assert.Single(
            client.Requests[0],
            message => message.Role == "system");
        Assert.Contains(environmentDescription, systemPrompt.Content);

        var followUp = Assert.Single(client.Requests.Skip(1));
        var toolResult = Assert.Single(followUp, message => message.Role == "tool");
        using var document = JsonDocument.Parse(toolResult.Content);
        Assert.True(document.RootElement.GetProperty("denied").GetBoolean());
        Assert.Equal("protected.write", document.RootElement.GetProperty("tool").GetString());
        Assert.Equal(
            "user rejected this operation",
            document.RootElement.GetProperty("userFeedback").GetString());
        Assert.False(document.RootElement.TryGetProperty("error", out _));
        Assert.Contains(followUp, message =>
            message.Role == "user"
            && message.Content.Contains(
                "user rejected this operation",
                StringComparison.Ordinal));
    }

    private sealed class DeniedToolCallAgentClient : IToolCallingAgentClient
    {
        public List<IReadOnlyList<AgentMessage>> Requests { get; } = [];

        public Task<string> CompleteAsync(
            IReadOnlyList<AgentMessage> messages,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentClientResponse> CompleteWithToolsAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());
            return Task.FromResult(Requests.Count == 1
                ? new AgentClientResponse(
                    "",
                    [new ToolCallRequest(
                        "call-1",
                        "protected_write",
                        "{\"path\":\"/workspace/file.txt\"}")])
                : new AgentClientResponse("continued without the tool", []));
        }
    }

    private sealed class DenyingRuntimeToolService(string reason) : IRuntimeToolService
    {
        public AuximRunId RunId { get; private set; }
        public string ToolCallId { get; private set; } = "";
        public string ToolName { get; private set; } = "";
        public IReadOnlyDictionary<string, object?> Arguments { get; private set; } =
            new Dictionary<string, object?>();
        public string HomeDirectory { get; private set; } = "";

        public IReadOnlyList<AuximRuntimeTool> ListTools() => [];

        public IReadOnlyList<ResourceAccess> ResolveResourceAccesses(
            string name,
            IReadOnlyDictionary<string, object?> arguments) => [];

        public Task<string> InvokeAsync(
            AuximRunId runId,
            string toolCallId,
            string name,
            IReadOnlyDictionary<string, object?> arguments,
            string homeDirectory,
            IApprovalHandler approvalHandler,
            IRuntimeEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunId = runId;
            ToolCallId = toolCallId;
            ToolName = name;
            Arguments = arguments;
            HomeDirectory = homeDirectory;
            return Task.FromException<string>(new ToolApprovalDeniedException(name, reason));
        }
    }

    private sealed class NullEventSink : IRuntimeEventSink
    {
        public static NullEventSink Instance { get; } = new();

        public ValueTask PublishAsync(
            RuntimeEvent runtimeEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
