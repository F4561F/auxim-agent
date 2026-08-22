# Auxim Architecture

Auxim is built around C#/.NET primitives for local agent workflows. The main
extension points are already in place: model clients, a tool registry, local
session state, tool approval, and runtime plugin loading.

```text
Auxim.Core
  Agent/      AuximAgent, message/result models, model client abstractions
  Approval/   ToolApprovalService and persistent always-allow decisions
  Config/     ~/.auxim config and .env loading
  Logging/    Local agent log helpers
  Plugins/    IAuximPlugin contract and DLL discovery
  Runtime/    IAuximRuntime, request/result types, runtime orchestration
  State/      Current session pointer and JSON session documents
  Tools/      ToolDefinition and ToolRegistry

Auxim.VAFS
  VAFS/       Virtual Agent File System path mapping
  VAShell/    Virtual Agent Shell for controlled command execution
  Utilities/  Shared command tokenization

Auxim.Cli
  Terminal frontend for chat, model setup, auth, config, sessions, tools,
  approvals, diagnostics, and the interactive dashboard

Auxim.Gateway
  HTTP/SSE runtime host with SDK client and built-in connector source modules.

Auxim.Tools
  Built-in tool registrations for files, search, git, web fetch, shell, todo,
  echo, and time. Shell execution is delegated to Auxim.VAFS.VAShell.
```

The intended dependency direction is:

```text
Auxim.Cli ───────┐
                   ├──> Auxim.Core.Runtime.IAuximRuntime
Auxim.Gateway ───┘          │
                              ├──> Auxim.Core.Agent/State/Config/Approval
                              ├──> Auxim.Tools
                              └──> Auxim.VAFS
```

CLI and Gateway are frontends. Every application operation passes through
`IAuximRuntime`, including configuration, credentials, approvals, sandbox
state, tools, sessions, chat, history, host commands, and external conversation
mapping. Neither frontend accesses application infrastructure directly.

## Runtime Flow

`auxim chat <message>` is a CLI frontend operation. It delegates to
`IAuximRuntime`, which loads `~/.auxim/config.json`, reads API keys from
`~/.auxim/.env`, opens or creates the current session, creates the default
tool registry, and runs `AuximAgent`.

The agent starts each turn with a short system message, appends previous
non-system session messages, and then appends the new user message. If the
selected client supports tool calling, the agent sends tool schemas to the model
and loops until the model returns a final assistant response or the configured
maximum iteration count is reached.

## Model Clients

Auxim currently has two model client paths:

- `EchoAgentClient` for the default local/placeholder mode.
- `OpenAiCompatibleAgentClient` for OpenAI-compatible
  `/chat/completions` endpoints, including tool call serialization.

The CLI provider picker stores a provider id, model id, and base URL in config.
Provider-specific API key names live in `ProviderCatalog`, and
`DefaultAgentClientFactory` creates the default model client for runtime users.
Shell environment variables can override config for one-off runs.

## Tools and Approval

Tools are registered as `ToolDefinition` instances in a shared `ToolRegistry`.
Each tool exposes a stable name, toolset, description, parameter schema, async
handler, and an optional argument-specific resource resolver. The resolver
produces `ResourceAccess` values containing a `ResourceAction` and
`ResourceUri`. Current built-ins preserve the existing approval behavior for
`shell.run`, `file.write`, `file.patch`, and `todo.done`, but policy no longer
identifies them from a hard-coded Tool-name set.

Approval uses `IApprovalHandler.RequestAsync`. Every request has a unique ID
and receives the active `CancellationToken`. CLI provides an interactive
handler; Gateway provides a non-interactive handler. Remembered decisions are
exact action/resource grants. Resource declarations drive approval and audit,
but do not sandbox a Tool handler.

## VAFS

VAFS, the Virtual Agent File System, is the path boundary between the model and
the host machine. The agent-facing filesystem exposes `/workspace` and mounted
`/volumes/<name>` roots while rejecting lexical and symbolic-link escapes from
physical mount roots. Real host paths are used only inside Auxim. This is a
Tool-level boundary rather than process isolation.

Default mapping:

```text
/workspace -> Environment.CurrentDirectory
```

Optional environment overrides:

```text
AUXIM_WORKSPACE=/host/project
AUXIM_VAFS_MOUNTS="code2=/host/code2;docs=/host/docs:ro"
```

The normal user-facing path is the CLI:

```text
auxim sandbox show
auxim sandbox workspace /host/project
auxim sandbox mount code2 /host/code2
auxim sandbox mount docs /host/docs --read-only
auxim sandbox unmount docs
```

These commands persist settings under `sandbox` in `~/.auxim/config.json`.
Environment variables are still honored as one-off overrides. All file-like
tools resolve model-provided paths through VAFS and rewrite host paths in
outputs back to virtual paths. Attempts to use unknown absolute paths or escape
a mount are rejected.

## Auxim Shell

`shell.run` uses VAShell from `Auxim.VAFS` instead of passing commands to
`/bin/bash -lc`. The shell parser rejects pipes, redirects, shell
substitutions, and command chaining. Built-in commands operate on VAFS paths,
and approved external command subsets such as `rg`, safe `git` reads, and
selected `dotnet` commands are planned before execution. Path arguments are
resolved through VAFS and output is rewritten to virtual paths.

## State

Auxim stores state under `~/.auxim` by default, or `AUXIM_HOME` when set:

- `config.json` - non-secret model, agent, and display settings.
- `.env` - provider API keys and other secrets.
- `sessions/*.json` - session documents.
- `current_session` - the active session id.
- `approvals.json` - remembered action/resource approval grants.
- `todos.json` - todo tool state.
- `logs/agent.log` - agent/tool log output.

## Plugins

`PluginLoader` scans `./plugins` and `~/.auxim/plugins` for DLLs. Any concrete
type implementing `IAuximPlugin` is instantiated and asked to register tools.
This keeps external tool packages separate from the built-in `Auxim.Tools`
assembly.

Native DLL plugins are trusted in-process extensions. They have the host
process permissions and are not automatically constrained by VAFS, VAShell,
resource declarations, or approval policy.

## Gateway API

`Auxim.Gateway` exposes the shared runtime without depending on CLI:

```text
GET  /health
GET  /v1/status
GET  /v1/tools
GET  /v1/sessions
GET  /v1/sessions/current
GET  /v1/sessions/{id}
POST /v1/sessions
POST /v1/sessions/{id}/use
DELETE /v1/sessions/current
GET  /v1/message-conversations
POST /v1/messages
POST /v1/chat
POST /v1/chat/stream
```

The Gateway route layer only handles HTTP concerns. Status, tools, sessions,
chat, and connector conversation mapping are delegated to `IAuximRuntime`,
matching the CLI execution path.

Set `AUXIM_GATEWAY_TOKEN` to require `Authorization: Bearer <token>` for every
endpoint except `/health`. Set `AUXIM_GATEWAY_CORS_ORIGINS` to a comma-separated
origin list when browser clients need cross-origin access.

`/v1/chat/stream` uses Server-Sent Events derived from the same structured
`RuntimeEvent` stream consumed by CLI and runtime logging. Gateway runs with an
asynchronous non-interactive approval handler, so protected resource accesses
are denied unless an exact grant already exists.

`/v1/messages` is Auxim's lightweight messaging connector boundary. External
adapters send a common envelope with `platform`, `conversationId`, `userId`,
`text`, and an optional scope. The runtime persists the conversation-to-session
mapping in `gateway-conversations.json` under Auxim home.

The built-in Telegram connector lives under `Auxim.Gateway/Connectors`, uses
Telegram Bot API long polling, and calls
`IAuximRuntime.SendExternalMessageAsync`, as does `/v1/messages`. It starts as a
hosted service only when its bot token is set.

## SDK

The `Auxim.Gateway/SDK` module is the typed .NET client for Gateway. It is built
into the Gateway assembly but retains the `Auxim.SDK` namespace as a distinct
public API. The module owns HTTP request construction, bearer auth headers,
typed response models, Gateway error exceptions, messaging connector calls,
and SSE event parsing.

## Run And Session Identity

`AuximRunId` identifies one live execution and is attached to `RuntimeEvent`
and runtime results. A conversation Session stores user input and final answers;
it does not store transient Tool or approval state. This separates the future
Run model from conversation history without implementing a Run Engine or
persistent Run store.

## Current Gaps

- Skills are reserved in the repository layout but not implemented yet.
- The CLI provider picker still owns the interactive provider/model menu, while
  shared API-key naming lives in Core.
- `IRuntimeEventSink` is push-based today; a future consumer may justify an
  async-enumerable transport without changing the event model.
