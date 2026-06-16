# Auxim 开发架构文档

本文面向后续开发者，说明各模块职责、依赖方向，以及新增功能时应该放在哪里。

核心原则：

```text
前端代码留在边缘。
agent 编排放在 IAuximRuntime 后面。
宿主机文件访问必须经过 VAFS。
```

## 心智模型

```text
Auxim.Core        类似 kernel-like runtime layer
IAuximRuntime    类似稳定的 syscall-like 应用边界
Auxim.Cli        终端前端
Auxim.Gateway    未来 HTTP/WebSocket/平台前端
Auxim.Tools      内置能力适配器
Auxim.VAFS       虚拟文件系统和受控 agent shell
```

`Auxim.Cli` 和 `Auxim.Gateway` 应该是同级前端。它们都应该调用
`IAuximRuntime`，而不是互相依赖。

推荐依赖方向：

```text
Auxim.Cli ───────┐
                   ├──> Auxim.Core.Runtime.IAuximRuntime
Auxim.Gateway ───┘          │
                              ├──> Auxim.Core.Agent/State/Config/Approval
                              ├──> Auxim.Tools
                              └──> Auxim.VAFS
```

## 模块职责

### Auxim.Core

Core 负责共享运行时概念：

- `Agent/`：agent loop、消息、结果、模型 client 抽象、OpenAI-compatible client。
- `Runtime/`：`IAuximRuntime`、chat request/result、`AuximRuntimeService`。
- `Config/`：配置文件、`.env`、provider API key 命名、运行模式。
- `State/`：会话文档和当前会话指针。
- `Approval/`：高风险工具审批策略和持久化 allow-list。
- `Tools/`：`ToolDefinition` 和 `ToolRegistry` 抽象。
- `Plugins/`：运行时插件契约和 DLL 发现。
- `Logging/`：本地日志辅助。

Core 不应该依赖 CLI、Gateway 或具体 UI。它可以暴露 callback、interface 或
runtime contract 给前端实现。

### Auxim.VAFS

VAFS 负责 agent 可见的文件系统边界：

- `/workspace`、`/tmp`、`/volumes/<name>` 映射。
- 宿主机路径改写回虚拟路径。
- 防止路径逃逸。
- VAShell 命令解析、内置命令、外部命令计划。

任何会接触文件路径的功能都应该使用 VAFS。不要把真实宿主机路径暴露给模型。

### Auxim.Tools

Tools 负责 agent 可调用的内置能力：

- 文件读写、列表、patch
- 搜索
- git 只读操作
- web fetch
- shell adapter
- todo 状态
- time、echo 等基础工具

Tools 应该使用 Core 的 `ToolDefinition` 和 `ToolRegistry`。如果工具接触文件或命令，
应该依赖 VAFS。Tools 不应该依赖 CLI UI。

### Auxim.Cli

CLI 负责终端相关事情：

- `Program.cs` 参数解析。
- slash commands。
- 交互式 dashboard。
- 输入框和终端输入策略。
- 审批 UI。
- 终端 Markdown 渲染。

CLI 应该通过 `IAuximRuntime` 执行 chat 类工作，不应该复制 agent/session/tool
编排逻辑。像 `//` shell escape 这种纯终端功能可以留在 CLI。

### Auxim.Gateway

Gateway 是非终端平台适配入口。未来 Web 前端应该通过 HTTP 或 WebSocket 连接
Gateway。Gateway 负责把请求翻译成 `IAuximRuntime` 调用，并把 runtime 事件返回给
客户端。

Gateway 不应该依赖 `Auxim.Cli`。

## Runtime 边界

当前 runtime 边界从这个接口开始：

```csharp
public interface IAuximRuntime
{
    Task<AuximChatResult> ChatAsync(
        AuximChatRequest request,
        AuximRuntimeOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

`AuximRuntimeService` 负责公共编排：

1. 加载配置。
2. 创建模型 client。
3. 创建工具 registry。
4. 打开或创建当前 session。
5. 构建 `AgentOptions`。
6. 运行 `AuximAgent`。
7. 把本轮对话追加到 session。

前端可以通过 `AuximRuntimeOptions` 提供：

- content delta callback
- tool event callback
- approval callback

## 新增前端

如果要新增 Web UI，不要调用 CLI 代码。应该在 Gateway 里新增 adapter：

1. 接收 HTTP/WebSocket 请求。
2. 转换成 `AuximChatRequest`。
3. 调用 `IAuximRuntime.ChatAsync`。
4. 把 content delta、tool event、approval prompt 转成协议消息。
5. 把最终 `AuximChatResult` 返回给客户端。

推荐链路：

```text
Web UI -> Auxim.Gateway -> IAuximRuntime -> Core/Tools/VAFS
```

## 新增工具

内置工具应该放在 `Auxim.Tools`，插件工具除外。新增后从 `BuiltInTools` 注册。

建议：

- 所有文件路径都走 VAFS。
- 返回虚拟路径，不返回宿主机路径。
- 如果工具会写入、运行命令、改变状态或产生外部副作用，需要加入审批。
- 参数 schema 要明确且尽量窄。
- 给路径安全和错误行为加测试。

## 新增 Runtime 功能

如果一个功能未来需要同时被 CLI、Gateway、Web 前端使用，就不要直接写死在 CLI。
应该放到 Core runtime API 后面。

适合 runtime 化的功能包括：

- chat 执行
- streaming event surface
- session replay
- approval protocol
- model 状态查询
- tool listing 和 tool invocation API

CLI 可以保留人类友好的命令和终端渲染，但业务操作应尽量可复用。

## 当前设计备注

- `Auxim.Core` 目前仍同时包含核心 primitives 和 agent runtime。如果 agent loop
  继续扩大，未来可以拆出 `Auxim.Agent`。
- `Auxim.Gateway` 目前还是 placeholder。`IAuximRuntime` 是让 Gateway 和 Web
  前端更容易接入的第一步。
- provider API key 命名已经在 Core 共享；丰富的交互式 provider/model 菜单仍留在
  CLI，因为它属于终端 UX。
