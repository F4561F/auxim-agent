# Auxim Architecture

Auxim is built around C#/.NET primitives for local agent workflows. The
The main
extension points are already in place: model clients, a tool registry, local
session state, tool approval, and runtime plugin loading.

```text
Auxim.Core
  Agent/      AuximAgent, message/result models, model client abstractions
  Approval/   ToolApprovalService and persistent always-allow decisions
  Config/     ~/.auxim config and .env loading
  Logging/    Local agent log helpers
  Plugins/    IAuximPlugin contract and DLL discovery
  State/      Current session pointer and JSON session documents
  Tools/      ToolDefinition and ToolRegistry
  Vafs/       Virtual Agent File System path mapping

Auxim.Cli
  Command-line host for chat, model setup, auth, config, sessions, tools,
  approvals, and diagnostics

Auxim.Gateway
  Messaging gateway host with per-platform adapters. The console adapter is
  currently a placeholder.

Auxim.Tools
  Built-in tool registrations for files, search, git, web fetch, shell, todo,
  echo, and time
```

## Runtime Flow

`auxim chat <message>` loads `~/.auxim/config.json`, reads API keys from
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
Provider-specific API key names are resolved by the CLI; shell environment
variables can override config for one-off runs.

## Tools and Approval

Tools are registered as `ToolDefinition` instances in a shared `ToolRegistry`.
Each tool exposes a stable name, toolset, description, JSON-schema-like
parameter schema, and async handler. Tool names are converted to schema-safe
function names by replacing dots with underscores.

High-risk tools are reviewed by `ToolApprovalService` before execution:
`shell.run`, `file.write`, `file.patch`, and `todo.done`. In an interactive
terminal, the user can allow once, always allow that tool, or deny with
feedback. In non-interactive runs, approval-required tools are denied.

## VAFS

VAFS, the Virtual Agent File System, is the path boundary between the model and
the host machine. The agent-facing filesystem exposes `/workspace` and mounted
`/volumes/<name>` roots. Real host paths are used only inside Auxim.

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

`shell.run` uses a restricted `auxim-shell` implementation instead of passing
commands to `/bin/bash -lc`. It is still disabled unless
`AUXIM_ALLOW_SHELL=true` is set.

The shell parser rejects pipes, redirects, shell substitutions, and command
chaining. The default command allowlist is `pwd`, `ls`, `cat`, `head`, `tail`,
`rg`, `git`, and `dotnet`; it can be overridden with `AUXIM_SHELL_COMMANDS`.
Path arguments are resolved through VAFS and output is rewritten to virtual
paths.

## State

Auxim stores state under `~/.auxim` by default, or `AUXIM_HOME` when set:

- `config.json` - non-secret model, agent, and display settings.
- `.env` - provider API keys and other secrets.
- `sessions/*.json` - session documents.
- `current_session` - the active session id.
- `approvals.json` - always-allowed high-risk tools.
- `todos.json` - todo tool state.
- `logs/agent.log` - agent/tool log output.

## Plugins

`PluginLoader` scans `./plugins` and `~/.auxim/plugins` for DLLs. Any concrete
type implementing `IAuximPlugin` is instantiated and asked to register tools.
This keeps external tool packages separate from the built-in `Auxim.Tools`
assembly.

## Current Gaps

- The gateway has only a console placeholder adapter.
- Skills are reserved in the repository layout but not implemented yet.
- Provider metadata is currently kept in the CLI picker instead of a dedicated
  provider registry.
- Tests are minimal and currently cover only basic tool invocation.
