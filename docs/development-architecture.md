# Development Architecture

This document is for contributors adding features or new frontends. The short
rule is: keep frontend code at the edge, keep agent orchestration behind
`IAuximRuntime`, and keep host filesystem access behind VAFS.

## Mental Model

```text
Auxim.Core        kernel-like runtime layer
IAuximRuntime    stable syscall-like application boundary
Auxim.Cli        terminal frontend
Auxim.Gateway    HTTP/SSE host with SDK and connector source modules
Auxim.Tools      built-in capability adapters
Auxim.VAFS       virtual filesystem and controlled agent shell
```

`Auxim.Cli` and `Auxim.Gateway` should be peers. They should both call
`IAuximRuntime`; neither should depend on the other.

## Module Responsibilities

### Auxim.Core

Core owns shared runtime concepts:

- `Agent/`: agent loop, messages, results, model client abstractions, OpenAI
  compatible client.
- `Runtime/`: `IAuximRuntime`, chat request/result types, and
  `AuximRuntimeService`.
- `Config/`: config files, `.env`, provider API-key naming, runtime mode.
- `State/`: session documents and current session pointer.
- `Approval/`: asynchronous frontend approval contract and persisted resource grants.
- `Resources/`: stable `ResourceAction`, `ResourceUri`, and access declarations.
- `Tools/`: `ToolDefinition` and `ToolRegistry` abstractions.
- `Plugins/`: runtime plugin contract and DLL discovery.
- `Logging/`: local log helpers.

Core should not depend on CLI, Gateway, or concrete UI. It may expose callbacks
or interfaces that frontends implement.

### Auxim.VAFS

VAFS owns the agent-visible filesystem boundary:

- `/workspace`, `/tmp`, and `/volumes/<name>` mapping.
- host-path rewriting back to virtual paths.
- path escape prevention.
- VAShell command parsing, built-ins, and external command planning.

Tools and runtime code should not expose raw host paths to the model. New
file-like features should resolve paths through VAFS.

### Auxim.Tools

Tools owns built-in capabilities exposed to the agent:

- file read/write/list/patch
- search
- git read operations
- web fetch
- shell adapter
- todo state
- core utilities such as time and echo

Tools should use `ToolDefinition` and `ToolRegistry` from Core. Tools may depend
on VAFS when they touch files or commands. Every resource-using Tool should add
an argument-specific `ResourceAccessResolver`. The declaration is approval and
audit metadata, not a sandbox. Tools should not depend on CLI UI.

### Auxim.Cli

CLI owns terminal concerns:

- argument parsing in `Program.cs`
- slash commands
- interactive dashboard
- prompt editor and terminal input
- approval UI
- terminal Markdown rendering

CLI must call `IAuximRuntime` for every application operation, including
configuration, credentials, approvals, sandbox state, tools, sessions, chat,
input history, and host commands. Terminal parsing, prompting, selection, and
rendering remain in CLI; the `//` syntax is terminal UI, but command execution
belongs to the runtime.

### Auxim.Gateway

Gateway is the HTTP/SSE boundary for non-terminal platform adapters. A future
Web frontend should connect to Gateway over HTTP or Server-Sent Events.
Gateway translates requests into `IAuximRuntime` calls and streams runtime
events back to the client.

Gateway exposes status, tool discovery, session management, blocking chat, and
SSE streaming chat endpoints. Browser-facing apps can opt into bearer-token
auth with `AUXIM_GATEWAY_TOKEN` and explicit CORS origins with
`AUXIM_GATEWAY_CORS_ORIGINS`.

Gateway route handlers must not instantiate or access configuration stores,
credential stores, approval stores, VAFS, `ToolRegistry`, `SessionStore`, or
other application infrastructure. External conversation mapping and its
persistence also belong to `IAuximRuntime`.

Messaging integrations should use the generic `/v1/messages` connector
boundary first. External adapters can use that HTTP API, while built-in
connectors live under `Auxim.Gateway/Connectors` and call
`IAuximRuntime.SendExternalMessageAsync` directly. Each connector owns its
platform credentials, allow-lists, polling or webhook mechanics, and reply
formatting. The runtime owns the stable mapping from external conversations to
Auxim sessions.

Gateway should not depend on `Auxim.Cli`.

### Gateway SDK module

The `Auxim.Gateway/SDK` source module should stay client-only and continue to
model Gateway's public protocol rather than Core runtime services. It shares
the Gateway project and assembly for now, while its `Auxim.SDK` namespace keeps
the client API distinct from host internals.

## Runtime Boundary

The runtime boundary currently starts with:

```csharp
public interface IAuximRuntime
{
    AuximApplicationPaths GetApplicationPaths();
    string GetConfigJson();
    void SetConfigValue(...);
    AuximModelSettings GetModelSettings();
    AuximModelSettings SetModelSettings(...);
    AuximCredentialStatus GetCredentialStatus(...);
    void SetApiKey(...);
    AuximSandboxStatus GetSandboxStatus();
    AuximDiagnostics GetDiagnostics();
    IReadOnlyList<string> LoadInputHistory();
    void SaveInputHistory(...);
    Task<int> RunHostCommandAsync(...);
    AuximRuntimeStatus GetStatus();
    IReadOnlyList<AuximRuntimeTool> ListTools();
    Task<string> InvokeToolAsync(...);
    IReadOnlyList<ResourceAccess> ResolveToolResourceAccesses(...);
    IReadOnlyList<AuximRuntimeSessionSummary> ListSessions();
    AuximRuntimeSession GetOrCreateCurrentSession();
    AuximRuntimeSession? GetSession(string id);
    AuximRuntimeSession CreateSession(...);
    AuximRuntimeSession? UseSession(string id);
    void ClearCurrentSession();
    IReadOnlyList<AuximExternalConversation> ListExternalConversations();
    Task<AuximExternalMessageResult> SendExternalMessageAsync(...);
    Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

`AuximRuntimeService` owns the shared operations:

1. Read and update configuration, credentials, approvals, and sandbox state.
2. Own CLI history and host-command execution requested by terminal syntax.
3. Discover and invoke tools through the configured registry factory.
4. List, search, create, select, and clear sessions.
5. Persist external conversation mappings and dispatch external messages.
6. Create the model client and `AgentOptions`.
7. Run `AuximAgent` and append turns to session state.

`AuximRuntimeOptions` accepts one `IRuntimeEventSink` and one asynchronous
`IApprovalHandler`. Content deltas, Tool lifecycle, approval lifecycle, and Run
lifecycle all use the same structured event stream. Runtime logging is another
consumer of that stream rather than a separate Agent callback.

`AuximRunId` identifies one execution. It is distinct from a conversation
Session ID. Runtime events are transient and are not appended to Session
documents; this reserves a clean boundary for a future Run model without
implementing a Run Engine now.

## Adding A New Frontend

For a Web UI, do not call CLI code. Connect to Gateway or add an adapter under
Gateway that:

1. Accepts HTTP/SSE requests.
2. Converts them to `AuximChatRequest`.
3. Calls `IAuximRuntime.ChatAsync`.
4. Converts structured `RuntimeEvent` values to protocol messages and implements
   an asynchronous approval handler when the frontend supports interaction.
5. Sends the final `AuximChatResult` back to the client.

The dependency direction should look like this:

```text
Web UI -> Auxim.Gateway -> IAuximRuntime -> Core/Tools/VAFS
```

## Adding A New Tool

Add built-in tools under `Auxim.Tools` unless the tool belongs to a plugin.
Register it from `BuiltInTools`.

Guidelines:

- Use VAFS for all file paths.
- Return virtual paths, not host paths.
- Resolve actual `ResourceAction + ResourceUri` values after arguments are known.
- Mark the relevant access declaration approval-required when preserving or
  adding current policy behavior.
- Keep parameter schemas explicit and narrow.
- Add focused tests for path safety and error behavior.

Native DLL plugins are trusted in-process extensions. Their handlers execute
with Auxim's host permissions and can bypass VAFS unless plugin code explicitly
uses it. Never describe DLL plugins as sandboxed; resource declarations do not
enforce process isolation.

## Adding Runtime Features

If a feature should be available to CLI, Gateway, and future frontends, put the
orchestration behind Core runtime APIs instead of adding it directly to CLI.

Examples:

- chat execution
- streaming event surfaces
- session replay
- approval protocols
- model selection status
- tool listing and invocation APIs

CLI can still own human-friendly commands and terminal rendering, but the
business operation should be reusable.

## Current Design Notes

- `Auxim.Core` still contains both core primitives and agent runtime. If the
  agent loop grows significantly, a future `Auxim.Agent` project could split
  agent orchestration out of Core.
- `Auxim.Gateway` exposes the runtime over HTTP/SSE and contains its SDK and
  built-in connector source modules in one project.
- Provider API-key naming is shared in Core. The rich interactive provider menu
  still lives in CLI because it is terminal UX.
