# OpenLoop

[English](README.md) | 简体中文

**一个可自我迭代的开放式 Agent。**

OpenLoop 把「干活」与「改进自己」拆成两个角色：**Worker（A，前台）** 面向用户完成真实任务；**Loop（I，后台）** 观察 Worker、用户反馈与运行证据，迭代出 Worker 与壳的下一个版本。两者是独立进程，由**壳**统一拉起、监督与换版。

名字里的「Open」指环不闭合：世界（和用户）始终是环的一部分——Loop 的判断来自真实使用，而不是一套封闭的自评。

## 现状

### 角色

| 角色 | 可执行文件 | 职责 |
|---|---|---|
| 壳 | `OpenLoop.exe`（WinForms）／`OpenLoop.Shell`（跨平台 CLI） | 唯一的进程编排者：拉起 A/I、展示界面、执行 stage / 重启 / 回滚 |
| Worker（A） | `OpenLoop.A` | 会话、工具调用、审批、流式输出、trace；同时提供 Web 界面与 `/api/*` |
| Loop（I） | `OpenLoop.I` | 按项目观察、读证据、记录发现、起草考核题、发起迭代（`/api/loop/*`） |

- 桌面壳与 Web 壳功能对齐：Web 界面由 A 在 bus 端口上直接提供（`http://<bus>/?key=...`），桌面壳右侧栏内嵌 WebView2。
- Loop 的写权限只限：配置目录（`%APPDATA%\OpenLoop`）、OpenLoop 源码工作树、二进制版本目录；读权限与 Worker 同宽。
- Loop 不改自己的源码；I 的二进制由 A 代换（I 不能重启自己，也不替换运行中的壳）。

### 能做什么

- **Worker**：多会话（关标签不停止）、流式正文与思考区、排队/立即插入、`/btw` 只读旁路、`/fork` 复制上下文、`/new` 新会话、模型与推理强度选择、上下文占比、成本与步数、完整 trace、调查卡（AskUser）。
- **工具**：`ReadFile`、`WriteFile`、`ListDir`、`Grep`、`Shell` 等内置工具；`ReadEvidence`、`RecordFinding`、`ProposeTask` 走证据通道；技能（内置 + 项目级）；外部 `OpenCodeTask` / `DshTask`；`WebFetch` 只读抓取；Puppet 探测对端进程与界面。
- **Loop**：每个项目一段 Loop 会话；读账本/trace/findings；把结论写成发现或考核题草稿；出迭代计划并执行。
- **模型与外部 agent**：
  - **opencode**：本机 OpenCode v2 服务通过 `~/.local/state/opencode/service.json` 自动发现，就绪后作为 OpenCode 提供商出现；模型 id 形如 `opencode/<模型>@<提供商>`，可直接当会话模型，也可用 `OpenCodeTask` 把整件事交给它的 agent（工具、会话、上下文都在对方那边，权限仍转给你审批）。免费模型有标记。
  - **dsh**：装了 dsh 就作为一个提供商出现，模型 id 形如 `dsh/<提供商>/<模型>`；`DshTask` 把任务交给 dsh 的 agent，用它是因为 dsh 的插件生态（如 `our-free-model` 的免费模型）或 dsh 特有工具。模型清单在后台探测，界面隔一拍重开下拉即可。
  - **其它**：任何 OpenAI 兼容端点（`baseUrl` + `model`，默认 OpenRouter）。
- **护栏**：重复调用检测、探索预算、参数自愈、畸形调用重试、上下文压缩、敏感操作审批。
- **Puppet**：A/I/壳各自带 Puppet 服务，agent 可探测状态、调用操作面（B 面具名 Action）；界面调试不靠 UI 自动化。

### 迭代与版本

- 两条线并行：**演进**改版本定义（提示词、技能、策略、模型参数、激活的模型与工具集）；**迭代**换可执行物（Worker `OpenLoop.A`、Loop `OpenLoop.I`、壳 `OpenLoop.App`）。
- 每轮迭代只换一个角色，两侧协议号必须对齐；版本定义与二进制分开存档。
- 版本图：`PuppetCore → Service → {A, I, 壳}`、`ChatFlow → 壳`、`Core → Service`；上游变了下游必须升，沿依赖边级联。
- 迭代入口：Loop 页「迭代」→ 先出计划（谁升版、现版本 → 目标版本、变更摘要）→ 用户确认 → 编译 → 换 A → A 更新 I → 最后发布新壳并由新壳自我重启（界面短暂退出）。
- 失败可回退：按整条链的快照回退；构建不通过就停在源码树、保留改动，不产生半成品版本。
- 换版前会检查是否还有在跑的会话（Worker / Loop / 旁路），有则先询问。
- 版本目录：`%APPDATA%\OpenLoop\versions\<role>\<ver>-<sha8>`；旧版本按角色保留有限份数（A/I 留当前 + 上一版，壳另留回退备份）。

## 仓库布局

```
OpenLoop.slnx
├── src/OpenLoop.Core/        agent 循环、工具、护栏、账本、技能、trace（不依赖 UI）
├── src/OpenLoop.Service/     进程编排之外的全部服务：Worker 宿主、Loop、Web/API、版本、存储
├── src/OpenLoop.App/         Windows 壳（WinForms）
├── src/OpenLoop.Shell/       跨平台壳（CLI）
├── src/OpenLoop.A/           Worker 可执行文件
├── src/OpenLoop.I/           Loop 可执行文件
├── tests/OpenLoop.Core.Tests/
├── tests/OpenLoop.Service.Tests/
├── docs/                     实施方案、双进程、版本管理、考核题来源等
└── CHARTER.md                规范（不变量、权限边界、换版顺序）
```

编译期依赖：

```
PuppetCore ──▶ Service ──▶ { A, I, App/Shell }
Core       ──▶ Service
ChatFlow   ──▶ App（桌面壳的对话控件）
```

## 环境要求

- Windows 10/11（桌面壳）；跨平台壳可用于其它平台
- .NET 10 SDK
- 开发布局需要同级检出（均以 `ProjectReference` 引用）：
  - `../ChatFlow/ChatFlow/ChatFlow.csproj`
  - `../PuppetCore/Puppet.Core/Puppet.Core.csproj`
  - `../PuppetCore/Puppet.Core.WinForms/Puppet.Core.WinForms.csproj`
- 安装布局没有同级检出时，源码由启动时的同步流程克隆（见下节）

### 源码同步

- 开发布局直接编译同级检出，不往安装目录复制源码；组件仓库（`PuppetCore`、`ChatFlow`）已检出时只报提交号，不再联网。
- 安装布局会把 `OpenLoop`、`PuppetCore`、`ChatFlow`（只有 Windows 版本编译它）浅克隆到 `<安装根>/src/`（`src\openloop`、`src\PuppetCore`、`src\ChatFlow`）。
- 已存在的克隆只 `fetch --depth 1`，绝不 `reset` / `checkout` 覆盖工作树——Loop 改的就是那份源码。
- 同步后比较源码版本与运行版本：源码更新 →「可以迭代」；上游（origin）更新 → 提示先更新源码，但不阻止继续用本地代码迭代。

## 构建与测试

```powershell
dotnet build OpenLoop.slnx
dotnet test tests\OpenLoop.Core.Tests
dotnet test tests\OpenLoop.Service.Tests
```

对话控件 `ChatFlow` 的测试在同级仓库：`dotnet test ..\ChatFlow\ChatFlow.Tests`。

Debug 构建会把 A/I 的产物拷进壳输出目录的 `backends\`；Release/发布产物只含壳，`backends\` 由换版链路填充。

## 运行

```powershell
Stop-Process -Name OpenLoop -Force -ErrorAction SilentlyContinue
Start-Process src\OpenLoop.App\bin\Debug\net10.0-windows\OpenLoop.exe -WorkingDirectory <仓库根>
```

配置位于 `%APPDATA%\OpenLoop\settings.json`（`baseUrl`、`model`、加密密钥、工作目录、项目列表）。密钥留空时回退读取 `OPENROUTER_API_KEY` 或 `OPENAI_API_KEY`。运行态、trace、账本写入工作区的 `.openloop/`；二进制版本与 Puppet 地址/密钥在 `%APPDATA%\OpenLoop\`。

默认：`https://openrouter.ai/api/v1` + `openrouter/free`。

## 文档

- [CHARTER.md](CHARTER.md) — 规范：角色、权限边界、版本与换版顺序
- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) — 架构与里程碑
- [docs/PLAN-DUAL-PROCESS.md](docs/PLAN-DUAL-PROCESS.md) — 双进程现状与逐条验收
- [docs/PLAN-VERSION-MANAGEMENT.md](docs/PLAN-VERSION-MANAGEMENT.md) — 版本图、指纹级联、迭代流程
- [docs/DESIGN-TASK-SOURCING.md](docs/DESIGN-TASK-SOURCING.md) — 考核题从哪里来
- [docs/CONFORMANCE.md](docs/CONFORMANCE.md) — 语言中立的契约与测试向量

## 许可证

[MIT](LICENSE)
