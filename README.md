# Auxim

Auxim is a portable C#/.NET AI agent for local workspaces. It provides an
OpenAI-compatible model client, tool calling, local sessions, an interactive
terminal interface, runtime plugins, and a virtual filesystem boundary that
maps real host paths into agent-facing paths such as `/workspace`, `/tmp`, and
`/volumes/<name>`.

The project is still early, but it is already useful as a local CLI agent. The
main design goal is to let the model operate through explicit tools and virtual
paths instead of exposing raw host filesystem details.

## Features

- Interactive terminal dashboard when running `auxim`
- OpenAI-compatible chat completions with tool calling and streaming transport
- Local echo mode for smoke testing without an API key
- Session storage under `~/.auxim/sessions`
- Slash commands for model, auth, config, sessions, tools, approvals, sandbox,
  diagnostics, and history navigation
- Terminal Markdown rendering for assistant responses
- Mouse wheel history scrolling, `/resume`, and `Esc` to return to dashboard
- Unified keyboard/mouse input policy for prompt, history, and approval screens
- Interactive approval UI for high-risk tools
- Persistent and revocable always-allowed tool approvals
- VAFS, the Virtual Agent File System, with `/workspace`, `/tmp`, and mounted
  `/volumes/<name>` paths
- Restricted `auxim-shell` for `shell.run`
- Runtime DLL plugin discovery from `./plugins` and `~/.auxim/plugins`
- Placeholder `Gateway` project for future UI/platform integration

## Install

Build and install a user-level `auxim` command:

```bash
./install-auxim.sh
```

The installer publishes `src/Auxim.Cli` to `dist/auxim`, links the
executable to `~/.local/bin/auxim`, and attempts to add that directory to your
shell profile when it is not already on `PATH`.

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

Start the interactive agent:

```bash
auxim
```

Run one chat turn:

```bash
auxim chat "hello"
```

Configure a real OpenAI-compatible provider:

```bash
auxim model set
auxim auth set-api-key
auxim chat "Introduce yourself in one sentence."
```

Set a provider directly:

```bash
auxim model set openai-api gpt-4o-mini https://api.openai.com/v1
```

Non-secret settings are stored in `~/.auxim/config.json`. API keys are stored
in `~/.auxim/.env`. Environment variables can override config for one-off
runs.

## Interactive Shell

Running `auxim` with no arguments opens Auxim's alternate-screen terminal
interface. It behaves like a small workspace console: the dashboard shows
runtime state and common actions, the prompt accepts normal conversation, and
slash commands control local agent features.

In a narrow terminal, the interface reflows content instead of dropping it:
status rows split across lines, runtime values wrap under their labels, and
action descriptions stay inside the action panel.

Common commands:

```text
/help                 command reference
/status               runtime overview
/context              current session statistics
/history [count]      open conversation history
/show <turn>          render one turn
/jump <turn>          replay history from a turn
/tail [count]         replay recent turns
/resume               return to dashboard
/model show           active model config
/sandbox show         VAFS mappings
/approval list        always-allowed tools
/clear                clear and redraw
/exit                 quit
// <shell-command>    run a user-driven host shell command
```

Input features:

- `Tab` completes slash commands.
- `Up` and `Down` browse prompt history.
- `Left`, `Right`, `Home`, and `End` edit the current input.
- `Ctrl+C` cancels the active model/tool/shell turn.
- `Ctrl+D` exits from an empty prompt.
- End a line with `\` to continue on the next prompt.
- Use `/paste`, then finish the pasted block with a single `.` line.
- Use the mouse wheel to scroll rendered conversation history.
- Use `Esc` or `/resume` to return from history to the dashboard.

Prefix a line with `//` to run the rest of the line in your real shell:

```text
// git status --short
// dotnet test
```

Shell escape commands are explicitly user-driven local commands. They are not
sent to the model as prompts and are separate from the restricted `shell.run`
tool.

Disable alternate screen mode if you prefer inline output:

```bash
AUXIM_NO_ALT_SCREEN=true auxim
```

## Approval

High-risk tools require approval:

```text
shell.run
file.write
file.patch
todo.done
```

The approval screen accepts only explicit keyboard choices:

- `Up` / `Down` move the selected option.
- `1` / `2` / `3` select an option.
- `Enter` confirms the selected option.
- Mouse events and unrelated keys are ignored.

Approval options:

```text
Allow once
Always allow
Deny and give feedback
```

List always-allowed tools:

```bash
auxim approval list
```

Revoke one always-allowed tool:

```bash
auxim approval revoke file.write
```

Clear all always-allowed approvals:

```bash
auxim approval clear
```

Approval state is stored in `~/.auxim/approvals.json`, or
`$AUXIM_HOME/approvals.json` when `AUXIM_HOME` is set.

## VAFS

VAFS is Auxim's Virtual Agent File System. The agent should see virtual paths,
not real host paths:

```text
/workspace        configured workspace directory
/tmp              writable Auxim scratch directory
/volumes/<name>   explicitly mounted extra directories
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

The agent can then use:

```text
/workspace
/tmp
/volumes/code2
/volumes/docs
```

Unknown absolute paths such as `/etc/passwd` are rejected by file-facing tools.
Write-capable tools reject read-only mounts. Tool output rewrites host paths
back to virtual paths.

`/tmp` is always writable and intended for generated files, scratch artifacts,
and intermediate outputs. By default it maps to `~/.auxim/tmp`. Override it
for one run:

```bash
AUXIM_TMP=/some/scratch/path auxim
```

One-off workspace and mount overrides:

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
chaining. Path arguments must use `/workspace`, `/tmp`, `/volumes/<name>`, or
relative paths. Output paths are rewritten back to VAFS paths.

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

## Plugins And Skills

Auxim discovers plugin DLLs in:

```text
./plugins
~/.auxim/plugins
```

A plugin implements `IAuximPlugin` and registers additional tools with the
shared `ToolRegistry`.

The `skills/` directory is reserved for packaged agent capabilities and project
conventions. It currently documents the intended location and is not yet a full
runtime skill loader.

## Commands

```bash
auxim
auxim chat <message>
auxim tools
auxim model show
auxim model set
auxim model set <provider> <model> [base-url]
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
auxim approval revoke <tool-name>
auxim approval clear
auxim sandbox show
auxim sandbox workspace <host-path>
auxim sandbox mount <name> <host-path> [--read-only]
auxim sandbox unmount <name>
auxim doctor
```

## Project Layout

```text
src/Auxim.Core
  Agent/       agent loop, model clients, messages, results, tool events
  Approval/    tool approval service and approval state
  Config/      ~/.auxim config and .env support
  Logging/     local logs
  Plugins/     runtime plugin contract and DLL discovery
  State/       session storage
  Tools/       tool registry and definitions
  Utilities/   shared utility code such as command tokenization
  Vafs/        virtual filesystem mapping

src/Auxim.Cli
  Program.cs
  AgentClientFactory.cs
  Commands/    CLI command handlers split by domain
  Interactive/ dashboard, prompt editor, history view, approval UI, input policy
  Services/    reusable CLI services such as ChatRunner

src/Auxim.Tools
  Built-in tool implementations and restricted auxim-shell

src/Auxim.Gateway
  Gateway shape and current console placeholder

plugins/
  Project-local runtime plugin directory

skills/
  Project-local agent skill convention directory

tests/
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

Publish the local CLI manually:

```bash
dotnet publish src/Auxim.Cli/Auxim.Cli.csproj \
  -c Release \
  -o dist/auxim \
  --self-contained false \
  -p:UseAppHost=true
```

## Configuration Paths

```text
~/.auxim/config.json       non-secret config
~/.auxim/.env              API keys and secrets
~/.auxim/sessions/         session documents
~/.auxim/history           interactive prompt history
~/.auxim/tmp/              default VAFS /tmp scratch directory
~/.auxim/current_session   active session id
~/.auxim/approvals.json    always-allowed tool approvals
~/.auxim/todos.json        todo state
~/.auxim/logs/agent.log    agent and tool logs
```

Override Auxim home:

```bash
AUXIM_HOME=/some/path auxim doctor
```

## Status

Auxim is not yet a mature agent product. The CLI path is usable, but the
gateway is still a placeholder, long-term memory is minimal, and plugin/skill
packaging is intentionally simple.
