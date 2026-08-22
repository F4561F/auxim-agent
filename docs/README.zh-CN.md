# Auxim

[English README](../README.md)

Auxim 是一个面向 AI Agent 的可信运行时。它负责把文件、网络、进程、工具、凭据和
会话等真实资源，以受控、可授权、可审计的方式暴露给 Agent。

换句话说，Auxim 是 AI Agent 的可信执行与资源控制层。Agent 不应该直接接触原始
宿主机路径、不受限 shell 或未追踪的凭据。Auxim 会把这些真实资源收敛成更窄的
资源界面，并在这个界面上施加策略、审批和日志。

Auxim 仍处于早期阶段，但 CLI、虚拟文件系统、审批流、Gateway、SDK 和 Telegram
connector 现在已经可用。

## Auxim 控制什么

- 通过 Virtual Agent File System 暴露文件，而不是直接暴露宿主机路径
- 通过受限 agent shell 暴露进程执行，而不是直接拼接 shell 字符串
- 通过统一 registry、schema、策略和审批流暴露工具
- 通过本地 config 和 `.env` 管理凭据，避免把密钥放进 prompt
- 通过持久化本地会话管理 Agent 上下文
- 通过显式工具和未来 Gateway 策略控制网络能力
- 通过 Gateway、SDK 和 connector adapter 接入外部 app 与 bot

## 核心原则

- 最小权限：Agent 只能看到已挂载资源和已注册工具。
- 可授权：高风险动作需要审批或预先加入 allow-list。
- 可审计：工具调用、审批和运行时事件会被记录。
- 资源抽象：Agent 面向稳定的路径和工具，宿主机细节留在 Auxim 内部。
- 边界分离：CLI、Gateway、SDK 和 connectors 都是同一个运行时边界外的入口或适配器。

## 安装

从源码构建并安装用户级 `auxim` 命令：

```bash
./build-install-auxim.sh
```

从最新 GitHub Release 安装：

```bash
./install-online-auxim.sh
```

覆盖安装目录：

```bash
AUXIM_INSTALL_DIR=/some/bin ./build-install-auxim.sh
```

移除已安装命令和本地 Auxim 状态：

```bash
./remove-auxim.sh
```

## 快速开始

启动交互式终端：

```bash
auxim
```

执行一次 Agent 回合：

```bash
auxim chat "hello"
```

配置模型 provider：

```bash
auxim model set
auxim auth set-api-key
auxim chat "Introduce yourself in one sentence."
```

直接设置 OpenAI-compatible provider：

```bash
auxim model set openai-api gpt-4o-mini https://api.openai.com/v1
```

非密钥配置存储在 `~/.auxim/config.json`。API key 存储在 `~/.auxim/.env`。

## 常用命令

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

交互式终端里的常用 slash commands：

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

`//` 会在真实本地 shell 中执行用户主动输入的命令。模型工具调用使用受控的
`shell.run` 工具。

## 资源边界

### VAFS

VAFS 是 Auxim 的 Virtual Agent File System。Agent 看到的是虚拟路径，而不是原始
宿主机路径：

```text
/workspace        配置的工作区目录
/tmp              Auxim 可写临时目录
/volumes/<name>   显式挂载的额外目录
```

配置工作区和挂载：

```bash
auxim sandbox workspace /home/project/code1
auxim sandbox mount code2 /home/project/code2
auxim sandbox mount docs /home/project/docs --read-only
```

VAFS 会拒绝未知绝对路径、词法路径逃逸、符号链接逃逸，以及对只读挂载的写入。它是
工具级安全边界，不是 Docker、VM 或操作系统沙箱的替代品，不能用来运行不可信代码。

### VAShell

`shell.run` 使用 `Auxim.VAFS` 中的 VAShell，而不是把命令交给 `/bin/bash -lc`。
VAShell 会拒绝管道、重定向、substitution 和命令串联。路径参数必须使用 VAFS
路径，例如 `/workspace`、`/tmp` 或 `/volumes/<name>`。

### 审批

以下内置资源访问行为仍然需要审批：

```text
shell.run
file.write
file.patch
todo.done
```

审批选项：

```text
Allow once
Always allow
Deny and give feedback
```

`Always allow` 会保存精确的 `ResourceAction + ResourceUri` grant。授权记录存储在
`~/.auxim/approvals.json`，设置 `AUXIM_HOME` 时则存储在
`$AUXIM_HOME/approvals.json`。

## Gateway

`Auxim.Gateway` 通过 HTTP/SSE 暴露可信运行时，让 app、服务、connector 和未来前端
都能访问同一套受控资源界面，而不需要嵌入 CLI 代码。

Gateway 是单一项目。HTTP host、typed SDK 源码和内置 connectors 都位于同一个
`src/Auxim.Gateway/Auxim.Gateway.csproj` 下：

```text
src/Auxim.Gateway/
  Program.cs
  SDK/
  Connectors/Telegram/
  Auxim.Gateway.csproj
```

Gateway 和 CLI 只通过 `Auxim.Core.Runtime.IAuximRuntime` 执行应用操作。配置、
凭据、审批、沙箱状态、工具、session、chat、输入历史、宿主命令和外部 conversation
映射都由 runtime 管理；前端只保留终端、HTTP/SSE 和平台传输职责。

```bash
dotnet run --project src/Auxim.Gateway/Auxim.Gateway.csproj --urls http://127.0.0.1:5055
```

面向 app 的可选配置：

```bash
AUXIM_GATEWAY_TOKEN=local-secret
AUXIM_GATEWAY_CORS_ORIGINS=http://localhost:5173,http://127.0.0.1:5173
```

设置 `AUXIM_GATEWAY_TOKEN` 后，除 `/health` 外所有接口都需要
`Authorization: Bearer <token>`。`AUXIM_GATEWAY_CORS_ORIGINS` 用于允许指定
浏览器来源访问 Gateway。

当前接口：

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

Gateway 使用异步的非交互审批 handler。需要审批的资源访问必须匹配已有资源 grant，
否则会直接拒绝，不会尝试调用终端 UI。

## SDK

`Auxim.Gateway` 内的 `SDK/` 模块通过 `Auxim.SDK` 命名空间提供 typed .NET
client。它与 Gateway 编译到同一个程序集，并封装 bearer auth、JSON 请求、session
API、connector messages 和 Server-Sent Events 解析。

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

Connectors 会把外部 app 事件转换成 Auxim 的受控运行时协议。`POST /v1/messages`
是面向聊天 app 和 bot 的通用 connector 边界。Slack、Telegram、Discord、飞书等
adapter 可以把各自平台事件转成 Auxim 的统一消息 envelope。

内置的 `Connectors/Telegram/` 模块使用 Telegram Bot API long polling。配置后，
它会作为 Gateway 后台服务运行，并与 `/v1/messages` 一样直接调用
`IAuximRuntime.SendExternalMessageAsync` 处理消息。

```bash
AUXIM_TELEGRAM_BOT_TOKEN=<bot-token> \
AUXIM_TELEGRAM_BOT_USERNAME=<bot-username> \
AUXIM_TELEGRAM_ALLOWED_USERS=<telegram-user-id-or-username> \
dotnet run --project src/Auxim.Gateway/Auxim.Gateway.csproj --urls http://127.0.0.1:5055
```

可选配置：

```text
AUXIM_TELEGRAM_ALLOWED_CHATS      逗号分隔的 chat id
AUXIM_TELEGRAM_REQUIRE_MENTION    true/false
AUXIM_TELEGRAM_SCOPE              participant 或 conversation
AUXIM_TELEGRAM_POLL_TIMEOUT       秒，默认 30
```

## 插件

Auxim 会从以下目录发现插件 DLL：

```text
./plugins
~/.auxim/plugins
```

插件实现 `IAuximPlugin`，并通过共享的 `ToolRegistry` 注册额外工具。

Native DLL 插件是 **trusted in-process extension**。它与 Auxim 进程拥有相同的
操作系统权限，不会自动受到 VAFS、VAShell、资源声明或审批策略约束。只能安装可信
代码；资源声明用于提高审批与审计可见性，不构成 sandbox。

## 文档

- [架构文档](architecture.md)
- [开发架构文档](development-architecture.md)
- [中文开发架构文档](development-architecture.zh-CN.md)

## 开发

构建：

```bash
dotnet build Auxim.sln --nologo
```

测试：

```bash
dotnet test Auxim.sln --nologo
```

在受限环境中，VSTest/MSBuild 可能需要创建本地测试通信 socket 或 pipe 的权限。

## 状态

Auxim 仍处于早期阶段。可信执行边界正在成型；Gateway、connectors、长期记忆和
打包后的 skills 仍在演进。

## 许可证

Apache-2.0。见 [LICENSE](../LICENSE)。
