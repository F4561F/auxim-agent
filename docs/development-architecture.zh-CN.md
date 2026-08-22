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
Auxim.Gateway    包含 SDK 和 connector 源码模块的 HTTP/SSE host
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
- `Approval/`：异步前端审批契约和持久化资源 grant。
- `Resources/`：稳定的 `ResourceAction`、`ResourceUri` 和资源访问声明。
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
应该依赖 VAFS。所有访问资源的 Tool 都应该提供参数级 `ResourceAccessResolver`。
资源声明用于审批和审计，不是 sandbox。Tools 不应该依赖 CLI UI。

### Auxim.Cli

CLI 负责终端相关事情：

- `Program.cs` 参数解析。
- slash commands。
- 交互式 dashboard。
- 输入框和终端输入策略。
- 审批 UI。
- 终端 Markdown 渲染。

CLI 的全部应用操作都必须通过 `IAuximRuntime`，包括配置、凭据、审批、沙箱状态、
工具、session、chat、输入历史和宿主命令。参数解析、交互选择和渲染保留在 CLI；
`//` 是终端输入语法，但命令执行属于 runtime。

### Auxim.Gateway

Gateway 是非终端平台适配入口和 HTTP/SSE runtime 边界。未来 Web 前端应该通过
HTTP 或 Server-Sent Events 连接 Gateway。Gateway 负责把请求翻译成
`IAuximRuntime` 调用，并把 runtime 事件返回给客户端。

Gateway 暴露 status、工具发现、session 管理、阻塞 chat 和 SSE streaming chat
接口。面向浏览器的 app 可以通过 `AUXIM_GATEWAY_TOKEN` 开启 bearer-token 鉴权，
并通过 `AUXIM_GATEWAY_CORS_ORIGINS` 显式允许跨源访问。

Gateway route handler 不得自行实例化或访问配置存储、凭据存储、审批存储、VAFS、
`ToolRegistry`、`SessionStore` 或其他应用基础设施。外部 conversation mapping 及其
持久化也属于 `IAuximRuntime`。

消息平台集成应该优先使用通用 `/v1/messages` connector 边界。外部 adapter 可以调用
该 HTTP API；内置 connector 则放在 `Auxim.Gateway/Connectors` 下，并直接调用
`IAuximRuntime.SendExternalMessageAsync`。每个 connector 自己负责平台凭据、
allow-list、polling 或 webhook 机制和回复格式。runtime 负责把外部 conversation
稳定映射到 Auxim session。

Gateway 不应该依赖 `Auxim.Cli`。

### Gateway SDK 模块

`Auxim.Gateway/SDK` 源码模块应该保持纯客户端职责，继续描述 Gateway 的公开协议，
而不是 Core runtime service。目前它与 Gateway 共用项目和程序集，但通过
`Auxim.SDK` 命名空间保持 client API 与 host 内部实现的边界。

## Runtime 边界

当前 runtime 边界从这个接口开始：

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

`AuximRuntimeService` 负责共享操作：

1. 读取和更新配置、凭据、审批与沙箱状态。
2. 管理 CLI 输入历史，并执行终端语法请求的宿主命令。
3. 通过配置的 registry factory 发现和调用工具。
4. 列出、搜索、创建、选择和清除 session。
5. 持久化外部 conversation mapping 并派发外部消息。
6. 创建模型 client 和 `AgentOptions`。
7. 运行 `AuximAgent` 并把本轮对话追加到 session。

`AuximRuntimeOptions` 只提供一个 `IRuntimeEventSink` 和一个异步
`IApprovalHandler`。content delta、Tool 生命周期、审批生命周期和 Run 生命周期统一
进入结构化事件流；Runtime 日志也消费这套事件，不再走 Agent 私有回调。

`AuximRunId` 标识一次执行，与 conversation Session ID 分离。RuntimeEvent 是临时状态，
不会追加到 Session 文档；这为未来 Run 模型保留边界，但本次不实现 Run Engine。

## 新增前端

如果要新增 Web UI，不要调用 CLI 代码。应该连接 Gateway，或者在 Gateway 里新增
adapter：

1. 接收 HTTP/SSE 请求。
2. 转换成 `AuximChatRequest`。
3. 调用 `IAuximRuntime.ChatAsync`。
4. 把结构化 `RuntimeEvent` 转成协议消息；支持交互的前端实现异步审批 handler。
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
- 参数确定后解析真实的 `ResourceAction + ResourceUri`。
- 在需要保留或增加当前策略时，把对应资源声明标记为需要审批。
- 参数 schema 要明确且尽量窄。
- 给路径安全和错误行为加测试。

Native DLL 插件是 trusted in-process extension。其 handler 拥有 Auxim 宿主权限，
除非插件代码主动使用 VAFS，否则可以绕过 VAFS。不得把 DLL 插件描述为 sandboxed；
资源声明也不提供进程隔离。

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
- `Auxim.Gateway` 通过 HTTP/SSE 暴露 runtime，并在一个项目中包含 SDK 和内置
  connector 源码模块。
- provider API key 命名已经在 Core 共享；丰富的交互式 provider/model 菜单仍留在
  CLI，因为它属于终端 UX。
