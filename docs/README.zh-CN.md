# Auxim

[English README](../README.md)

Auxim 是一个基于 C#/.NET 的可移植本地 AI agent 运行时，面向真实工作区。它结合了
OpenAI-compatible 模型、工具调用、本地会话、审批控制、插件、终端界面，以及
Virtual Agent File System（VAFS），让模型看到虚拟路径，而不是直接接触宿主机
文件系统路径。

Auxim 仍处于早期阶段，但 CLI 现在已可用。

## 特性

- 带 slash commands 和会话历史的交互式终端界面
- 支持 OpenAI-compatible chat completions 和 tool calling
- 支持无 API key 的本地 echo smoke test 模式
- 会话存储在 `~/.auxim/sessions`
- 高风险工具审批，例如 shell 和文件写入
- VAFS 路径：`/workspace`、`/tmp`、`/volumes/<name>`
- 通过受控 VAShell 执行 `shell.run`
- 内置文件、搜索、git、web fetch、shell、time、echo、todo 工具
- 从 `./plugins` 和 `~/.auxim/plugins` 发现运行时插件

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

执行一次聊天：

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

## VAFS

VAFS 是 Auxim 的 Virtual Agent File System。agent 看到的是虚拟路径，而不是原始
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

VAFS 是工具级安全边界。它会拒绝未知绝对路径、路径逃逸，以及对只读挂载的写入。
它不是 Docker、VM 或操作系统沙箱的替代品，不能用来运行不可信代码。

## VAShell

`shell.run` 使用 `Auxim.VAFS` 中的 VAShell，而不是把命令交给 `/bin/bash -lc`。
VAShell 会拒绝管道、重定向、substitution 和命令串联。路径参数必须使用 VAFS
路径，例如 `/workspace`、`/tmp` 或 `/volumes/<name>`。

## 审批

高风险工具需要审批：

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

审批状态存储在 `~/.auxim/approvals.json`，设置 `AUXIM_HOME` 时则存储在
`$AUXIM_HOME/approvals.json`。

## 插件

Auxim 会从以下目录发现插件 DLL：

```text
./plugins
~/.auxim/plugins
```

插件实现 `IAuximPlugin`，并通过共享的 `ToolRegistry` 注册额外工具。

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

Auxim 仍处于早期阶段。CLI 已经可用，Gateway、长期记忆和打包后的 skills 仍在演进。

## 许可证

Apache-2.0。见 [LICENSE](../LICENSE)。
