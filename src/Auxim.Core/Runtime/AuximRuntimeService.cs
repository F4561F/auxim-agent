using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Core.Tools;
using Auxim.Core.Approval;
using Auxim.Core.Resources;

namespace Auxim.Core.Runtime;

public sealed partial class AuximRuntimeService : IAuximRuntime
{
    private readonly Func<AuximConfig, IAgentClient> _agentClientFactory;
    private readonly Func<ToolRegistry> _toolRegistryFactory;
    private readonly Func<SessionStore> _sessionStoreFactory;
    private readonly Func<AuximConfig> _configLoader;
    private readonly Func<string> _homeDirectory;
    private readonly object _externalConversationGate = new();

    public AuximRuntimeService(
        Func<AuximConfig, IAgentClient> agentClientFactory,
        Func<ToolRegistry> toolRegistryFactory,
        Func<SessionStore>? sessionStoreFactory = null,
        Func<AuximConfig>? configLoader = null,
        Func<string>? homeDirectory = null)
    {
        _agentClientFactory = agentClientFactory;
        _toolRegistryFactory = toolRegistryFactory;
        _sessionStoreFactory = sessionStoreFactory ?? (() => new SessionStore());
        _configLoader = configLoader ?? (() => ConfigLoader.Load());
        _homeDirectory = homeDirectory ?? ConfigLoader.GetAuximHome;
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

    public IReadOnlyList<AuximRuntimeTool> ListTools() =>
        _toolRegistryFactory()
            .List()
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tool => new AuximRuntimeTool(
                tool.Name,
                tool.SchemaName,
                tool.Toolset,
                tool.Description,
                tool.ParametersSchema,
                tool.ResourceAccessResolver is not null))
            .ToArray();

    public async Task<string> InvokeToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var runId = AuximRunId.New();
        var execution = await CreateToolExecution(options).ExecuteAsync(
            runId,
            $"direct:{Guid.NewGuid():N}",
            name,
            arguments,
            cancellationToken);
        if (execution.WasDenied)
        {
            throw new ToolApprovalDeniedException(name, execution.Feedback);
        }

        return execution.Content;
    }

    public IReadOnlyList<ResourceAccess> ResolveToolResourceAccesses(
        string name,
        IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return _toolRegistryFactory().Get(name).ResolveResourceAccesses(arguments);
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
        var agentOptions = new AgentOptions
        {
            Provider = config.Model.Provider,
            Model = config.Model.Name,
            MaxIterations = config.Agent.MaxIterations,
            RunId = runId,
            HomeDirectory = _homeDirectory(),
            ApprovalHandler = options?.ApprovalHandler ?? NonInteractiveApprovalHandler.Instance,
            EventSink = eventSink,
        };

        var sessions = _sessionStoreFactory();
        var session = ResolveSession(sessions, request);
        var agent = new AuximAgent(
            _agentClientFactory(config),
            _toolRegistryFactory(),
            agentOptions);

        await eventSink.PublishAsync(
            new RuntimeRunStartedEvent(
                RuntimeEventFactory.NewEventId(),
                RuntimeEventFactory.Now(),
                runId,
                session.Id),
            cancellationToken);
        try
        {
            var result = await agent.RunConversationAsync(request.Prompt, session.Messages, cancellationToken);
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

    private ToolExecutionCoordinator CreateToolExecution(AuximRuntimeOptions? options) =>
        new(
            _toolRegistryFactory(),
            _homeDirectory(),
            options?.ApprovalHandler ?? NonInteractiveApprovalHandler.Instance,
            CreateEventSink(options?.EventSink));

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
