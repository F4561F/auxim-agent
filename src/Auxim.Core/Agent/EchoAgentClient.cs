using Auxim.Core.Runtime;

namespace Auxim.Core.Agent;

public sealed class EchoAgentClient : IAgentClient
{
    public Task<string> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken)
    {
        var lastUserMessage = messages.LastOrDefault(message => message.Role == "user")?.Content ?? "";
        return Task.FromResult($"Auxim received: {lastUserMessage}");
    }
}
