# Auxim

Auxim 是一个基于 C#/.NET 的本地工作区 AI agent。它提供
OpenAI-compatible 模型调用、工具调用、本地会话、交互式终端界面、
运行时插件，以及一个面向 agent 的虚拟文件系统边界。

核心目标是：让模型通过明确的工具和虚拟路径工作，而不是直接暴露宿主机
文件系统路径。

## 快速开始

启动交互式终端：

```bash
auxim
```

执行一次聊天：

```bash
auxim chat "hello"
```

配置模型：

```bash
auxim model set
auxim auth set-api-key
auxim chat "Introduce yourself in one sentence."
```

直接设置 OpenAI-compatible provider：

```bash
auxim model set openai-api gpt-4o-mini https://api.openai.com/v1
```

非密钥配置默认存储在：

```text
~/.auxim/config.json
```

API key 默认存储在：

```text
~/.auxim/.env
```

## 常用命令

```bash
auxim
auxim chat <message>
auxim tools
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

## 交互式终端

运行 `auxim` 会打开交互式终端界面。它负责：

- 普通对话输入。
- Slash commands，例如 `/help`、`/status`、`/history`。
- 终端 Markdown 渲染。
- 工具调用事件展示。
- 高风险工具审批。
- 会话历史查看和滚动。

常用交互命令：

```text
/help                 查看命令帮助
/status               查看运行状态
/context              查看当前会话统计
/history [count]      查看对话历史
/resume               返回 dashboard
/model show           查看模型配置
/sandbox show         查看 VAFS 映射
/approval list        查看始终允许的工具
/exit                 退出
// <shell-command>    执行用户主动触发的真实 shell 命令
```

`//` 是用户主动的宿主机 shell escape，不是模型工具调用。模型能调用的是受控的
`shell.run` 工具。

## VAFS

VAFS 是 Virtual Agent File System，也就是 agent 可见的虚拟文件系统。

agent 看到的是：

```text
/workspace        当前工作区
/tmp              Auxim 可写临时目录
/volumes/<name>   显式挂载的额外目录
```

设置工作区：

```bash
auxim sandbox workspace /home/project/code1
```

挂载其他目录：

```bash
auxim sandbox mount code2 /home/project/code2
auxim sandbox mount docs /home/project/docs --read-only
```

VAFS 是工具级安全边界。它会拒绝未知绝对路径、路径逃逸，以及对只读挂载的写入。
它不是 Docker、VM 或操作系统沙箱的替代品。

## VAShell

`shell.run` 使用 `Auxim.VAFS` 中的 VAShell，而不是 `/bin/bash -lc`。

VAShell 会拒绝：

- 管道
- 重定向
- shell substitution
- 命令串联

路径参数必须通过 VAFS，例如 `/workspace`、`/tmp` 或 `/volumes/<name>`。

## 审批

高风险工具会触发审批，例如：

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

审批状态默认存储在：

```text
~/.auxim/approvals.json
```

## 项目模块

```text
src/Auxim.Core
  核心运行时、agent loop、模型 client、配置、会话、审批、插件、工具抽象

src/Auxim.VAFS
  虚拟文件系统和受控 VAShell

src/Auxim.Tools
  内置工具，例如 file、search、git、web、shell、todo

src/Auxim.Cli
  终端前端，包含命令行入口、交互式 UI、slash commands、审批界面

src/Auxim.Gateway
  未来 HTTP/WebSocket/平台适配入口，目前仍是 placeholder
```

更详细的开发者文档见：

[中文版开发架构文档](development-architecture.zh-CN.md)

## 架构类比

可以把当前项目理解为：

```text
Auxim.Core        类似内核基础层
IAuximRuntime    类似系统调用接口
Auxim.Cli        类似 terminal 前端
Auxim.Gateway    类似未来 GUI/API gateway 入口
Auxim.Tools      类似受控能力驱动
Auxim.VAFS       类似虚拟文件系统
```

CLI、Gateway、未来 Web 前端都应该通过 `IAuximRuntime` 调用核心能力，而不是各自
重新拼 agent、tools、session、config 和 VAFS。

## 开发

构建：

```bash
dotnet build Auxim.sln --nologo
```

测试：

```bash
dotnet test Auxim.sln --nologo
```

在受限环境中，VSTest 可能需要创建本地测试通信 socket 的权限。

## 状态

Auxim 仍处于早期阶段。CLI 路径已经可用，Gateway 仍是占位，长期记忆、
技能系统和多前端接入还在演进中。
