using Auxim.Core.Runtime;
using Auxim.Cli.Interactive;

namespace Auxim.Cli.Services;

public sealed class ChatRunner
{
    private readonly IAuximRuntime _runtime;
    private readonly IRuntimeEventSink? _eventSink;

    public ChatRunner(
        IAuximRuntime runtime,
        IRuntimeEventSink? eventSink = null)
    {
        _runtime = runtime;
        _eventSink = eventSink;
    }

    public async Task<AuximChatResult> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var result = await _runtime.ChatAsync(
            new AuximChatRequest(prompt),
            new AuximRuntimeOptions
        {
            EventSink = _eventSink,
            ApprovalHandler = new CliApprovalHandler(),
        },
            cancellationToken);
        return result;
    }
}
