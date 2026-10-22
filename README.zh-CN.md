# OpenLoop

[English](README.md) | 简体中文

OpenLoop 是一套自演进的 agent 设施：**Worker** 面向用户干活，**Loop** 观察、收集证据，并迭代出下一个版本的 Worker。两者同进程、同生命周期，但节奏不同——Worker 跟任务走，Loop 由事件驱动。

OpenLoop **不是 agent 本身**，而是 agent 运行的设施。名字里的「Open」指环不闭合：世界（和用户）始终是环的一部分。

## 特性

- 带工具的 agent 循环，采用单行 `tool-call:` 协议
- 内置工具：`read_file`、`write_file`、`list_dir`、`grep`、`run_command`
- 护栏：重复调用检测、探索预算、结果/承诺话术检查、参数自愈、畸形调用重试、上下文压缩
- append-only 账本：特征、激活记录、裁决、运行证据
- A/B 门禁：候选版本严格更好才更替，平局判旧版
- 任意历史版本可回滚，且不改写旧证据
- 流式输出、对话历史持久化，完整 trace 落在 `.openloop/`
- WinForms 桌面宿主，后端兼容 OpenAI 协议（默认 OpenRouter 免费模型）

## 仓库结构

```
OpenLoop.slnx
├── src/OpenLoop.Core/        agent 循环、工具、账本、门禁、推理（不依赖 UI）
├── src/OpenLoop.App/         WinForms 宿主
├── tests/OpenLoop.Core.Tests/
├── docs/IMPLEMENTATION.md    实施方案与进度
└── CHARTER.md                规范（唯一规范物）
```

依赖方向：

```
OpenLoop.App ──▶ OpenLoop.Core
      └───────▶ ChatFlow（共用对话控件）
      └───────▶ Puppet.Core / Puppet.Core.WinForms（界面自动化）
```

## 环境要求

- Windows 10/11
- .NET 10 SDK
- 将 `ChatFlow` 与 `PuppetCore` 检出为本仓库的同级目录（均以 `ProjectReference` 引用）：
  - `../ChatFlow/ChatFlow/ChatFlow.csproj`
  - `../PuppetCore/Puppet.Core/Puppet.Core.csproj`
  - `../PuppetCore/Puppet.Core.WinForms/Puppet.Core.WinForms.csproj`

## 构建与测试

```powershell
dotnet build OpenLoop.slnx
dotnet test tests\OpenLoop.Core.Tests
```

## 运行

```powershell
Stop-Process -Name OpenLoop -Force -ErrorAction SilentlyContinue
Start-Process bin\Debug\net10.0-windows\OpenLoop.exe -WorkingDirectory .
```

配置位于 `%APPDATA%\OpenLoop\settings.json`（`baseUrl`、`model`、加密后的密钥、工作目录）。密钥框留空时回退读取环境变量 `OPENROUTER_API_KEY` 或 `OPENAI_API_KEY`。运行态、trace 与账本写入工作区的 `.openloop/`。

默认：`https://openrouter.ai/api/v1` + `openrouter/free`。

## 文档

- [CHARTER.md](CHARTER.md) — 规范：不变量、账本契约、符合性
- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) — 架构与里程碑

## 许可证

[MIT](LICENSE)
