# Auxim

[中文说明](docs/README.zh-CN.md)

Auxim is a portable C#/.NET local AI agent runtime for real workspaces. It
combines OpenAI-compatible models, tool calling, local sessions, approval
controls, plugins, a terminal UI, and a Virtual Agent File System (VAFS) that
keeps model-facing paths separate from raw host filesystem paths.

Auxim is still early, but the CLI is usable today for local workspace
tasks.

## Features

- Interactive terminal UI with slash commands and conversation history
- OpenAI-compatible chat completions with tool calling
- Local echo mode for smoke testing without an API key
- Session storage under `~/.auxim/sessions`
- Approval flow for high-risk tools such as shell and file writes
- VAFS paths: `/workspace`, `/tmp`, and `/volumes/<name>`
- Restricted VAShell for controlled `shell.run` execution
- Built-in tools for files, search, git, web fetch, shell, time, echo, and todo
- Runtime plugin discovery from `./plugins` and `~/.auxim/plugins`

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

Run one chat turn:

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

Inside the interactive terminal, common slash commands include:

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

## VAFS

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

VAFS is a tool-level safety boundary. It rejects unknown absolute paths, path
escapes, and writes to read-only mounts. It is not a replacement for Docker, a
VM, or an operating-system sandbox when running untrusted code.

## VAShell

`shell.run` uses VAShell from `Auxim.VAFS` instead of passing commands to
`/bin/bash -lc`. VAShell rejects pipes, redirects, substitutions, and command
chaining. Path arguments must use VAFS paths such as `/workspace`, `/tmp`, or
`/volumes/<name>`.

## Approval

High-risk tools require approval:

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

Approval state is stored in `~/.auxim/approvals.json`, or
`$AUXIM_HOME/approvals.json` when `AUXIM_HOME` is set.

## Plugins

Auxim discovers plugin DLLs in:

```text
./plugins
~/.auxim/plugins
```

A plugin implements `IAuximPlugin` and registers additional tools with the
shared `ToolRegistry`.

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

Auxim is early-stage software. The CLI is usable, while Gateway, long-term
memory, and packaged skills are still evolving.

## License

Apache-2.0. See [LICENSE](LICENSE).
