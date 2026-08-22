# Auxim

[中文说明](docs/README.zh-CN.md)

Auxim is a trusted runtime for AI agents. It exposes real resources such as
files, network access, processes, tools, credentials, and sessions to agents
through controlled, authorizable, and auditable boundaries.

In short, Auxim is the trusted execution and resource-control layer for AI
agents. The agent should not need raw host paths, unrestricted shell access, or
untracked credentials. Auxim gives the agent a narrower resource surface with
explicit policy, approval, and logging.

Auxim is early-stage software, but the CLI, virtual filesystem, approval flow,
Gateway, SDK, and Telegram connector are usable today.

## What Auxim Controls

- Files through a Virtual Agent File System instead of raw host paths
- Processes through a restricted agent shell instead of direct shell strings
- Tools through a shared registry, schema, policy, and approval flow
- Credentials through local config and `.env` storage outside prompts
- Sessions through persistent local conversation state
- Network capabilities through explicit tools and future gateway policies
- External apps and bots through Gateway, SDK, and connector adapters

## Core Principles

- Least privilege: agents see only mounted resources and registered tools.
- Authorization: high-risk actions require approval or an allow-list.
- Auditability: tool calls, approvals, and runtime events are logged.
- Resource abstraction: agent-facing paths and tools stay stable while host
  details remain behind Auxim.
- Separation of concerns: CLI, Gateway, SDK, and connectors are frontends or
  adapters around the same runtime boundary.

## Install

Build from source and install a user-level `auxim` command:

```bash
./build-install-auxim.sh
```

Install from the latest GitHub Release:

```bash
./install-online-auxim.sh
```

Override the install directory:

```bash
AUXIM_INSTALL_DIR=/some/bin ./build-install-auxim.sh
```

Remove the installed command and local Auxim state:

```bash
./remove-auxim.sh
```

## Quick Start

Start the interactive terminal:

```bash
auxim
```

Run one agent turn:

```bash
auxim chat "hello"
```

Configure a model provider:

```bash
auxim model set
auxim auth set-api-key
auxim chat "Introduce yourself in one sentence."
```

Set an OpenAI-compatible provider directly:

```bash
auxim model set openai-api gpt-4o-mini https://api.openai.com/v1
```

Non-secret settings are stored in `~/.auxim/config.json`. API keys are stored
in `~/.auxim/.env`.

## Common Commands

```bash
auxim
auxim chat <message>
auxim model show
auxim model set
auxim auth status
auxim auth set-api-key [key]
auxim config show
auxim session list
auxim tool list
auxim tool run <name> [key=value ...]
auxim approval list
auxim sandbox show
auxim doctor
```

Inside the interactive terminal:

```text
/help
/status
/context
/history [count]
/model show
/sandbox show
/approval list
/exit
// <shell-command>
```

`//` runs a user-driven command in the real local shell. Model tool calls use
the restricted `shell.run` tool instead.

## Resource Boundaries

### VAFS

VAFS is Auxim's Virtual Agent File System. The agent sees virtual paths instead
of raw host paths:

```text
/workspace        configured workspace directory
/tmp              writable Auxim scratch directory
/volumes/<name>   explicitly mounted extra directories
```

Configure the workspace and mounts:

```bash
auxim sandbox workspace /home/project/code1
auxim sandbox mount code2 /home/project/code2
auxim sandbox mount docs /home/project/docs --read-only
```

VAFS rejects unknown absolute paths, lexical path escapes, symbolic-link
escapes, and writes to read-only mounts. It is a tool-level safety boundary,
not a replacement for Docker, a VM, or an operating-system sandbox when
running untrusted code.

### VAShell

`shell.run` uses VAShell from `Auxim.VAFS` instead of passing commands to
`/bin/bash -lc`. VAShell rejects pipes, redirects, substitutions, and command
chaining. Path arguments must use VAFS paths such as `/workspace`, `/tmp`, or
`/volumes/<name>`.

### Approval

The following built-in resource accesses remain approval-required:

```text
shell.run
file.write
file.patch
todo.done
```

Approval options are:

```text
Allow once
Always allow
Deny and give feedback
```

`Always allow` stores an exact `ResourceAction + ResourceUri` grant. Approval
grants are stored in `~/.auxim/approvals.json`, or
`$AUXIM_HOME/approvals.json` when `AUXIM_HOME` is set.

## Gateway

`Auxim.Gateway` exposes the trusted runtime over HTTP/SSE so apps, services,
connectors, and future frontends can interact with the same controlled resource
surface without embedding CLI code.

Gateway is one project. Its HTTP host, typed SDK source, and built-in connectors
live under the same `src/Auxim.Gateway/Auxim.Gateway.csproj`:

```text
src/Auxim.Gateway/
  Program.cs
  SDK/
  Connectors/Telegram/
  Auxim.Gateway.csproj
```

Gateway and CLI use `Auxim.Core.Runtime.IAuximRuntime` as their only
application boundary. Configuration, credentials, approvals, sandbox state,
tools, sessions, chat, input history, host commands, and external conversation
mapping are all owned by the runtime. The frontends retain only terminal,
HTTP/SSE, and platform transport concerns.

```bash
dotnet run --project src/Auxim.Gateway/Auxim.Gateway.csproj --urls http://127.0.0.1:5055
```

Optional app-facing settings:

```bash
AUXIM_GATEWAY_TOKEN=local-secret
AUXIM_GATEWAY_CORS_ORIGINS=http://localhost:5173,http://127.0.0.1:5173
```

When `AUXIM_GATEWAY_TOKEN` is set, all endpoints except `/health` require
`Authorization: Bearer <token>`. `AUXIM_GATEWAY_CORS_ORIGINS` enables browser
clients from the listed origins.

Current endpoints:

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
POST /v1/chat/stream   Server-Sent Events
```

Gateway uses an asynchronous non-interactive approval handler. Resource access
that requires approval must match a prior resource grant; otherwise it is
denied without attempting terminal UI.

## SDK

The `SDK/` module inside `Auxim.Gateway` provides a typed .NET client under the
`Auxim.SDK` namespace. It is built into the same Gateway assembly and handles
bearer auth, JSON requests, session APIs, connector messages, and Server-Sent
Events parsing.

```csharp
using Auxim.SDK;

using var auxim = new AuximGatewayClient(new AuximGatewayClientOptions
{
    BaseAddress = new Uri("http://127.0.0.1:5055"),
    Token = "local-secret",
});

var response = await auxim.ChatAsync("Summarize this workspace.");
Console.WriteLine(response.FinalResponse);

await foreach (var streamEvent in auxim.StreamChatAsync("Show progress as you work."))
{
    if (streamEvent is AuximContentDeltaEvent delta)
    {
        Console.Write(delta.Delta);
    }
}
```

## Connectors

Connectors translate external app events into Auxim's controlled runtime
protocol. `POST /v1/messages` is the generic connector boundary for chat apps
and bots. Adapters for Slack, Telegram, Discord, Feishu, or other platforms can
translate native events into a common Auxim message envelope.

The built-in `Connectors/Telegram/` module uses Telegram Bot API long polling.
When configured, it runs as a Gateway background service and passes messages
through the same internal message service used by `/v1/messages`.

```bash
AUXIM_TELEGRAM_BOT_TOKEN=<bot-token> \
AUXIM_TELEGRAM_BOT_USERNAME=<bot-username> \
AUXIM_TELEGRAM_ALLOWED_USERS=<telegram-user-id-or-username> \
dotnet run --project src/Auxim.Gateway/Auxim.Gateway.csproj --urls http://127.0.0.1:5055
```

Optional settings:

```text
AUXIM_TELEGRAM_ALLOWED_CHATS      comma-separated chat ids
AUXIM_TELEGRAM_REQUIRE_MENTION    true/false
AUXIM_TELEGRAM_SCOPE              participant or conversation
AUXIM_TELEGRAM_POLL_TIMEOUT       seconds, default 30
```

## Plugins

Auxim discovers plugin DLLs in:

```text
./plugins
~/.auxim/plugins
```

A plugin implements `IAuximPlugin` and registers additional tools with the
shared `ToolRegistry`.

Native DLL plugins are **trusted in-process extensions**. They execute with the
same operating-system permissions as Auxim and are not automatically confined
by VAFS, VAShell, resource declarations, or approval policy. Install only code
you trust. Resource declarations improve approval and audit visibility; they
are not a sandbox.

## Documentation

- [Architecture](docs/architecture.md)
- [Development architecture](docs/development-architecture.md)
- [Chinese development architecture](docs/development-architecture.zh-CN.md)

## Development

Build:

```bash
dotnet build Auxim.sln --nologo
```

Run tests:

```bash
dotnet test Auxim.sln --nologo
```

In restricted environments, VSTest/MSBuild may need permission to create local
test communication sockets or pipes.

## Status

Auxim is early-stage software. The trusted execution boundary is taking shape;
Gateway, connectors, long-term memory, and packaged skills are still evolving.

## License

Apache-2.0. See [LICENSE](LICENSE).
