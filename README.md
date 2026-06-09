# Auxim

Auxim is a portable C#/.NET AI agent for local workspaces.
It is currently an early local-agent implementation with an OpenAI-compatible
model client, tool calling, local sessions, tool approval, runtime plugins, and
a virtual filesystem boundary for file-facing tools.

The project is intentionally small. The useful path today is a local CLI agent
that can talk to an OpenAI-compatible endpoint and operate on a mapped
`/workspace` instead of exposing real host paths to the model.

## Current Capabilities

- OpenAI-compatible `/chat/completions` model transport with tool calls
- Local echo mode for smoke testing without an API key
- Session storage under `~/.auxim/sessions`
- Model, auth, config, session, tool, approval, sandbox, and doctor commands
- Built-in tools for files, search, git, web fetch, shell, todo, echo, and time
- VAFS, the Virtual Agent File System, mapping host paths to `/workspace` and
  `/volumes/<name>`
- Restricted `auxim-shell` instead of direct `/bin/bash -lc`
- Interactive approval for high-risk tools
- Runtime DLL plugin discovery from `./plugins` and `~/.auxim/plugins`

Auxim is not yet a mature agent product. The gateway is still a placeholder,
there is no TUI/Web UI, and memory/context management is minimal.

## Install

Build and install a user-level `auxim` command:

```bash
./install-auxim.sh
```

The installer publishes `src/Auxim.Cli` to `dist/auxim`, links the executable
to `~/.local/bin/auxim`, and adds that directory to your shell profile if it is
not already on `PATH`.

Override the install directory:

```bash
AUXIM_INSTALL_DIR=/some/bin ./install-auxim.sh
```

Remove the installed command and local Auxim state:

```bash
./remove-auxim.sh
```

Non-interactive removal:

```bash
./remove-auxim.sh --yes --remove-path-entry
```

## Quick Start

Use local echo mode:

```bash
auxim chat "hello"
```

Configure a real OpenAI-compatible provider:

```bash
auxim model set
auxim auth set-api-key
auxim chat "Introduce yourself in one sentence."
```

`auxim model set` opens a terminal picker for common OpenAI-compatible
providers and models. You can also set a provider directly:

```bash
auxim model set openai-api gpt-4o-mini https://api.openai.com/v1
```

Non-secret settings are stored in `~/.auxim/config.json`. API keys are stored
in `~/.auxim/.env`. Environment variables can override config for one-off
runs.

## Commands

```bash
auxim chat <message>
auxim model show
auxim model set
auxim auth status
auxim auth path
auxim auth set-api-key [key]
auxim config show
auxim config path
auxim config set <key.path> <value>
auxim session list
auxim session show [session-id]
auxim session search <query>
auxim session new [title]
auxim session use <session-id>
auxim session clear
auxim tool list
auxim tool run <name> [key=value ...]
auxim approval list
auxim approval clear
auxim sandbox show
auxim sandbox workspace <host-path>
auxim sandbox mount <name> <host-path> [--read-only]
auxim sandbox unmount <name>
auxim doctor
```

## VAFS

VAFS is Auxim's Virtual Agent File System. The model should see virtual paths,
not host paths:

```text
/workspace        -> configured workspace directory
/volumes/auxim   -> explicitly mounted extra directory
```

Set the workspace:

```bash
auxim sandbox workspace /home/project/code1
```

Mount another directory:

```bash
auxim sandbox mount code2 /home/project/code2
auxim sandbox mount docs /home/project/docs --read-only
```

After that, the agent can use:

```text
/workspace
/volumes/code2
/volumes/docs
```

Unknown absolute paths such as `/etc/passwd` are rejected. Write-capable file
tools reject read-only mounts. Tool output rewrites host paths back to virtual
paths.

One-off overrides are still available:

```bash
AUXIM_WORKSPACE=/home/project/code1 \
AUXIM_VAFS_MOUNTS="code2=/home/project/code2;docs=/home/project/docs:ro" \
  auxim chat "compare /workspace with /volumes/code2"
```

VAFS is a tool-level safety boundary. It does not replace Docker, a VM, or OS
sandboxing for untrusted local code.

## Auxim Shell

`shell.run` is disabled by default. Enable it explicitly:

```bash
AUXIM_ALLOW_SHELL=true auxim tool run shell.run command=pwd
```

When enabled, `shell.run` uses `auxim-shell`, not `/bin/bash -lc`.

Default allowed commands:

```text
pwd, ls, cat, head, tail, rg, git, dotnet
```

The parser rejects shell operators, pipes, redirects, substitutions, and command
chaining. Path arguments must use `/workspace`, `/volumes/<name>`, or relative
paths. Output paths are rewritten back to VAFS paths.

Customize the allowlist:

```bash
AUXIM_SHELL_COMMANDS="pwd,ls,rg,git,dotnet" auxim chat "run tests"
```

## Built-In Tools

```text
time.now
echo
file.list
file.read
file.write
file.patch
file.search
git.status
git.diff
web.fetch
shell.run
todo.add
todo.list
todo.done
```

High-risk tools require approval:

```text
shell.run
file.write
file.patch
todo.done
```

If a high-risk tool is denied, the user's feedback is sent back to the model as
both a tool result and a follow-up user message.

## Plugins

Auxim discovers plugin DLLs in:

```text
./plugins
~/.auxim/plugins
```

A plugin implements `IAuximPlugin` and registers additional tools with the
shared `ToolRegistry`.

## Project Layout

```text
src/Auxim.Core
  Agent/       agent loop, model clients, messages/results
  Approval/    tool approval
  Config/      ~/.auxim config and .env support
  Logging/     local logs
  Plugins/     runtime plugin contract and DLL discovery
  State/       session storage
  Tools/       tool registry and definitions
  Vafs/        virtual filesystem mapping

src/Auxim.Cli
  Program.cs
  AgentClientFactory.cs
  Commands/    CLI command handlers split by domain

src/Auxim.Tools
  Built-in tool implementations

src/Auxim.Gateway
  Gateway shape and current console placeholder

tests
  xUnit tests for core behavior and VAFS
```

## Development

Build:

```bash
dotnet build Auxim.sln --nologo
```

Run tests:

```bash
dotnet test Auxim.sln --nologo
```

In restricted environments, VSTest may need permission to open its local test
communication socket. The code itself builds without network access when
dependencies are already restored.

## Configuration Paths

```text
~/.auxim/config.json       non-secret config
~/.auxim/.env              API keys and secrets
~/.auxim/sessions/         session documents
~/.auxim/current_session   active session id
~/.auxim/approvals.json    always-allowed tool approvals
~/.auxim/todos.json        todo state
~/.auxim/logs/agent.log    agent and tool logs
```

Override Auxim home:

```bash
AUXIM_HOME=/some/path auxim doctor
```
