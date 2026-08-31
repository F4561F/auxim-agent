using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Core.Approval;
using Auxim.Core.Resources;
using Auxim.VAFS;

namespace Auxim.Core.Runtime;

public sealed partial class AuximRuntimeService : IAuximRuntime
{
    private readonly IAgentRunner _agentRunner;
    private readonly IRuntimeToolService _tools;
    private readonly Func<SessionStore> _sessionStoreFactory;
    private readonly Func<AuximConfig> _configLoader;
    private readonly Func<string> _homeDirectory;
    private readonly Func<string> _environmentDescription;
    private readonly object _externalConversationGate = new();

    public AuximRuntimeService(
        IAgentRunner agentRunner,
        IRuntimeToolService? tools = null,
        Func<SessionStore>? sessionStoreFactory = null,
        Func<AuximConfig>? configLoader = null,
        Func<string>? homeDirectory = null,
        Func<string>? environmentDescription = null)
    {
        _agentRunner = agentRunner;
        _tools = tools ?? EmptyRuntimeToolService.Instance;
        _sessionStoreFactory = sessionStoreFactory ?? (() => new SessionStore());
        _configLoader = configLoader ?? (() => ConfigLoader.Load());
        _homeDirectory = homeDirectory ?? ConfigLoader.GetAuximHome;
        _environmentDescription = environmentDescription
            ?? (() => VirtualAgentFileSystem.FromEnvironment().DescribeForAgent());
    }

    public AuximRuntimeStatus GetStatus()
    {
        var config = _configLoader();
        var currentSessionId = _sessionStoreFactory().GetCurrentSessionId();
        return new AuximRuntimeStatus(
            _homeDirectory(),
            config.Model.Provider,
            config.Model.Name,
            config.Model.BaseUrl,
            config.Agent.MaxIterations,
            currentSessionId,
            config.Sandbox.Workspace ?? Environment.CurrentDirectory,
            config.Sandbox.Mounts.Count);
    }

    public IReadOnlyList<AuximRuntimeTool> ListTools() => _tools.ListTools();

    public async Task<string> InvokeToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var runId = AuximRunId.New();
        return await _tools.InvokeAsync(
            runId,
            $"direct:{Guid.NewGuid():N}",
            name,
            arguments,
            _homeDirectory(),
            options?.ApprovalHandler ?? NonInteractiveApprovalHandler.Instance,
            CreateEventSink(options?.EventSink),
            cancellationToken);
    }

    public IReadOnlyList<ResourceAccess> ResolveToolResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return _tools.ResolveResourceAccesses(name, arguments);
    }

    public IReadOnlyList<AuximRuntimeSessionSummary> ListSessions()
    {
        var sessions = _sessionStoreFactory();
        var currentId = sessions.GetCurrentSessionId();
        return sessions.List()
            .Select(session => new AuximRuntimeSessionSummary(
                session.Id,
                session.Title,
                session.CreatedAt,
                session.UpdatedAt,
                string.Equals(session.Id, currentId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public IReadOnlyList<AuximRuntimeSessionSummary> SearchSessions(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var sessions = _sessionStoreFactory();
        var currentId = sessions.GetCurrentSessionId();
        return sessions.List()
            .Select(record => sessions.TryLoad(record.Id))
            .Where(session => session is not null
                && (session.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || session.Messages.Any(message =>
                        message.Content.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .Select(session => ToSummary(session!, currentId))
            .ToArray();
    }

    public AuximRuntimeSession GetOrCreateCurrentSession()
    {
        var sessions = _sessionStoreFactory();
        var session = sessions.GetOrCreateCurrent();
        return ToRuntimeSession(session, session.Id);
    }

    public AuximRuntimeSession? GetSession(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var sessions = _sessionStoreFactory();
        var session = sessions.TryLoad(id);
        return session is null
            ? null
            : ToRuntimeSession(session, sessions.GetCurrentSessionId());
    }

    public AuximRuntimeSession CreateSession(string? title = null, bool makeCurrent = true)
    {
        var sessions = _sessionStoreFactory();
        var session = sessions.NewSession(title, makeCurrent);
        return ToRuntimeSession(
            session,
            makeCurrent ? session.Id : sessions.GetCurrentSessionId());
    }

    public AuximRuntimeSession? UseSession(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var sessions = _sessionStoreFactory();
        var session = sessions.TryLoad(id);
        if (session is null)
        {
            return null;
        }

        sessions.SetCurrent(id);
        return ToRuntimeSession(session, id);
    }

    public void ClearCurrentSession() =>
        _sessionStoreFactory().ClearCurrent();

    public async Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var config = _configLoader();
        var runId = AuximRunId.New();
        var eventSink = CreateEventSink(options?.EventSink);
        var sessions = _sessionStoreFactory();
        var session = ResolveSession(sessions, request);
        var runRequest = new AgentRunRequest(
            runId,
            session.Id,
            request.Prompt,
            session.Messages.ToArray(),
            config,
            _homeDirectory(),
            _environmentDescription(),
            options?.ApprovalHandler ?? NonInteractiveApprovalHandler.Instance,
            eventSink);

        await eventSink.PublishAsync(
            new RuntimeRunStartedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                session.Id),
            cancellationToken);
        try
        {
            var result = await _agentRunner.RunAsync(runRequest, cancellationToken);
            if (request.AppendToSession)
            {
                sessions.AppendTurn(session, request.Prompt, result.FinalResponse);
            }

            await eventSink.PublishAsync(
                new RuntimeRunCompletedEvent(
                    RuntimeEventFactory.NewEventId(),
                    RuntimeEventFactory.Now(),
                    runId,
                    session.Id,
                    result.FinalResponse),
                cancellationToken);

            return new AuximChatResult(result.FinalResponse, result.Messages, session.Id, runId);
        }
        catch (Exception exception)
        {
            await eventSink.PublishAsync(
                new RuntimeRunFailedEvent(
                    RuntimeEventFactory.NewEventId(),
                    RuntimeEventFactory.Now(),
                    runId,
                    session.Id,
                    exception.Message),
                CancellationToken.None);
            throw;
        }
    }

    private IRuntimeEventSink CreateEventSink(IRuntimeEventSink? frontendSink)
    {
        var logSink = new RuntimeLogEventSink(_homeDirectory());
        return frontendSink is null
            ? logSink
            : new CompositeRuntimeEventSink(logSink, frontendSink);
    }

    private static SessionDocument ResolveSession(SessionStore sessions, AuximChatRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            return sessions.TryLoad(request.SessionId)
                ?? throw new InvalidOperationException($"Session not found: {request.SessionId}");
        }

        return request.UseCurrentSession
            ? sessions.GetOrCreateCurrent()
            : sessions.NewSession();
    }

    private static AuximRuntimeSessionSummary ToSummary(
        SessionDocument session,
        string currentId) =>
        new(
            session.Id,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            string.Equals(session.Id, currentId, StringComparison.OrdinalIgnoreCase));

    private static AuximRuntimeSession ToRuntimeSession(
        SessionDocument session,
        string currentId) =>
        new(
            session.Id,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            string.Equals(session.Id, currentId, StringComparison.OrdinalIgnoreCase),
            session.Messages.ToArray());
}
