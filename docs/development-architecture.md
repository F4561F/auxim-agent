# Development Architecture

This document is for contributors adding features or new frontends. The short
rule is: keep frontend code at the edge, keep agent orchestration behind
`IAuximRuntime`, and keep host filesystem access behind VAFS.

## Mental Model

```text
Auxim.Core        kernel-like runtime layer
IAuximRuntime    stable syscall-like application boundary
Auxim.Cli        terminal frontend
Auxim.Gateway    future HTTP/WebSocket/platform frontend
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
- `Approval/`: high-risk tool approval policy and persisted allow-list.
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
on VAFS when they touch files or commands. Tools should not depend on CLI UI.

### Auxim.Cli

CLI owns terminal concerns:

- argument parsing in `Program.cs`
- slash commands
- interactive dashboard
- prompt editor and terminal input
- approval UI
- terminal Markdown rendering

CLI should call `IAuximRuntime` for chat-like work. It should not duplicate
agent/session/tool orchestration. Terminal-only actions, such as shell escape
with `//`, can stay in CLI.

### Auxim.Gateway

Gateway is the place for non-terminal platform adapters. A future Web frontend
should connect to Gateway over HTTP or WebSocket. Gateway should translate
requests into `IAuximRuntime` calls and stream runtime events back to the
client.

Gateway should not depend on `Auxim.Cli`.

## Runtime Boundary

The runtime boundary currently starts with:

```csharp
public interface IAuximRuntime
{
    Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

`AuximRuntimeService` performs the common orchestration:

1. Load config.
2. Create the model client.
3. Create the tool registry.
4. Open or create the current session.
5. Build `AgentOptions`.
6. Run `AuximAgent`.
7. Append the turn to session state.

Frontends can provide callbacks in `AuximRuntimeOptions` for content deltas,
tool events, and approvals.

## Adding A New Frontend

For a Web UI, do not call CLI code. Add an adapter under Gateway that:

1. Accepts HTTP/WebSocket requests.
2. Converts them to `AuximChatRequest`.
3. Calls `IAuximRuntime.ChatAsync`.
4. Converts content deltas, tool events, and approval prompts to protocol
   messages.
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
- Add an approval requirement in `ToolApprovalService` if the tool writes,
  runs commands, changes state, or can cause external side effects.
- Keep parameter schemas explicit and narrow.
- Add focused tests for path safety and error behavior.

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
- `Auxim.Gateway` is still a placeholder. The runtime boundary is the first
  step toward making Gateway and Web frontends straightforward.
- Provider API-key naming is shared in Core. The rich interactive provider menu
  still lives in CLI because it is terminal UX.
