using Auxim.Core.Logging;

namespace Auxim.Core.Runtime;

public interface IRuntimeEventSink
{
    ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken);
}

public sealed class DelegateRuntimeEventSink : IRuntimeEventSink
{
    private readonly Func<RuntimeEvent, CancellationToken, ValueTask> _handler;

    public DelegateRuntimeEventSink(Func<RuntimeEvent, CancellationToken, ValueTask> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken) =>
        _handler(runtimeEvent, cancellationToken);
}

internal sealed class NullRuntimeEventSink : IRuntimeEventSink
{
    public static NullRuntimeEventSink Instance { get; } = new();

    private NullRuntimeEventSink()
    {
    }

    public ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

internal sealed class CompositeRuntimeEventSink : IRuntimeEventSink
{
    private readonly IReadOnlyList<IRuntimeEventSink> _sinks;

    public CompositeRuntimeEventSink(params IRuntimeEventSink[] sinks)
    {
        _sinks = sinks;
    }

    public async ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks)
        {
            await sink.PublishAsync(runtimeEvent, cancellationToken);
        }
    }
}

internal sealed class RuntimeLogEventSink : IRuntimeEventSink
{
    private readonly string _homeDirectory;

    public RuntimeLogEventSink(string homeDirectory)
    {
        _homeDirectory = homeDirectory;
    }

    public ValueTask PublishAsync(RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = runtimeEvent switch
        {
            RuntimeToolStartedEvent started =>
                $"runtime.event kind={started.Kind} run={started.RunId} tool={started.ToolName} resources={started.ResourceAccesses.Count}",
            RuntimeToolCompletedEvent completed =>
                $"runtime.event kind={completed.Kind} run={completed.RunId} tool={completed.ToolName} outcome={completed.Outcome} chars={completed.OutputLength}",
            RuntimeApprovalRequestedEvent requested =>
                $"runtime.event kind={requested.Kind} run={requested.RunId} request={requested.Request.RequestId} tool={requested.Request.ToolName}",
            RuntimeApprovalResolvedEvent resolved =>
                $"runtime.event kind={resolved.Kind} run={resolved.RunId} request={resolved.RequestId} approved={resolved.Approved} remembered={resolved.Remembered}",
            RuntimeRunFailedEvent failed =>
                $"runtime.event kind={failed.Kind} run={failed.RunId} session={failed.SessionId} error={failed.Error}",
            _ => $"runtime.event kind={runtimeEvent.Kind} run={runtimeEvent.RunId}",
        };

        if (runtimeEvent is RuntimeRunFailedEvent
            || runtimeEvent is RuntimeApprovalResolvedEvent { Approved: false })
        {
            AuximLog.Warning(message, _homeDirectory);
        }
        else if (runtimeEvent is not RuntimeContentDeltaEvent)
        {
            AuximLog.Info(message, _homeDirectory);
        }

        return ValueTask.CompletedTask;
    }
}
