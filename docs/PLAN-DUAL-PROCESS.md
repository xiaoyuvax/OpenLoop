# 阴阳双进程架构 · 实施方案

> 状态：**M0–M5 已实施；M0–M3 在本机 Windows 验证，M4 已在 WSL2 Ubuntu-22.04 实机验收；M5 的 dsh 桥未做**。
> 目标：把「同进程的 Worker + Loop」拆成两个独立二进制，使两者可以各自被对方替换、重启后立即生效。
> 逐里程碑的实测记录见 §14；与本节计划的偏差见 §3 末尾「实施偏差」。
> 现状基线见 `docs/IMPLEMENTATION.md`（M1–M3 已完成，Puppet 已接入）。

---

## 1. 目标与不变的部分

**要解决的只有一个问题：二进制不能在运行时更新。** 现在 Worker 与 Loop 同进程，只能整体替换；
拆成两个进程后，谁演进谁，就由对方提交新版本、由壳拉起新进程。

对用户不变：

- WinForms 界面布局、会话/标签/队列的操作逻辑不变；
- `.openloop/`（账本、defs、eval、traces）路径与格式不变；旧工作区可直接打开；
- `%APPDATA%\OpenLoop\settings.json`、`chats\`、`AGENTS.md`、技能目录位置不变；
- Puppet 调试通道不变（壳保留；A/I 按需开启）。

变的只有：多两个后台进程、两套 Web 界面、一个更新/重启通道。

---

## 2. 三个可执行文件

| 文件 | 角色 | 形态 | 拥有 |
|---|---|---|---|
| **`OpenLoop.exe`**（Windows）<br>**`OpenLoop`**（Linux） | **壳 + 监督者** | Windows：WinForms 客户端；Linux：控制台启动器（无界面，打印地址） | 进程监管（拉起/监控/重启 A、I）、界面、更新回滚 |
| **`OpenLoop.A.exe`** | **阳 A**：Worker 主导 | 无界面 server（跨平台） | 交互式 Worker 会话、worker 对话历史、worker trace、审批与调查的待办 |
| **`OpenLoop.I.exe`** | **阴 I**：Loop 主导 | 无界面 server（跨平台） | Loop 会话、账本、门禁与考核、候选/回滚、loop 对话历史 |

要点：

- **壳不跑 agent。** 它只做三件事：拉起 A 与 I、显示界面（WinForms 或浏览器）、执行 A/I 请求的重启。
  Windows 壳与 Linux 壳共用同一套监督逻辑（`Service/Shell`），各自 exe 只有几十行入口。
- A/I 都是无头进程，**同一份代码在 Windows 与 Linux 上一样**；WinForms 是壳的专属程序集，
  A/I 不引用 WinForms、AntdUI、ChatFlow。
- 界面不再直连 agent：`SessionView` / `LoopView` 改为总线客户端（§8），因此
  **换 A 时界面不重启**，换 I 时 loop 面板短暂失联后自动重连。
- 进程生命周期：A/I 由壳以 `--parent <pid>` 拉起；壳退出（或崩溃）后它们 2 秒内自退，
  Windows 另挂 Job Object（KILL_ON_JOB_CLOSE）。壳是唯一 spawner，避免两边互相拉起形成环。
- 开发/排障可 `OpenLoop.A.exe --standalone` 直接起（无监管、无热更新）。

**端口（默认）**

| | bus + web | Puppet |
|---|---|---|
| 壳 | 无 HTTP（纯客户端） | `9090+`（现有 `MainForm`，保留） |
| A | `127.0.0.1:19180` | 默认关，`--puppet` 开启 |
| I | `127.0.0.1:19181` | 默认关，`--puppet` 开启 |

---

## 3. 程序集布局

```
src/
  OpenLoop.Core/        已有：agent 循环、工具、store、trace、安全（仅 BCL，不动）
  OpenLoop.Service/     新：跨平台共享层
      Config/           AppSettings、路径解析（XDG / %APPDATA%）、密钥保护（DPAPI→0600 回退）
      Models/           ModelCatalog、OpenCodeHub、OpenRouterCatalog、Provider*、Reasoners
      Worker/           WorkerSessionService（原 SessionView 的运行管线）
      Loop/             LoopService（原 LoopView + OnEvolve/OnRollback）
      Bus/              BusServer、BusClient、DTO、协议版本
      Web/              WebHost（静态资源 + /api/*）、口令文件鉴权
      Shell/            Supervisor（spawn/监控/重启/回滚）、UpdateRequest 读写
      Plugins/          插件清单、stdio 宿主（§9）
      wwwroot/          唯一的静态界面（A 与 I 共用这一套，不复制）
  OpenLoop.A/           新：net10.0 控制台 exe → OpenLoop.A[.exe]
  OpenLoop.I/           新：net10.0 控制台 exe → OpenLoop.I[.exe]
  OpenLoop.Shell/       新：net10.0 控制台 exe → OpenLoop（Linux 壳）
  OpenLoop.Win/         新：net10.0-windows WinExe → OpenLoop.exe（Windows 壳，原 OpenLoop.App 改造）
```

依赖方向：`A/I/Shell/Win → Service → Core`；`Win` 是唯一引用 WinForms/AntdUI/ChatFlow 的项目。
`wwwroot` 挂在 `Service` 上（`PreserveNewest` 复制到引用方的输出/发布目录），A 与 I 的版本目录里各带一份
**内容相同**的副本，改界面只改一处。

**实施偏差（2026-11-01，以代码为准）**

- 界面程序集沿用 `src/OpenLoop.App`（未新建 `OpenLoop.Win`），它就是 Windows 壳；
- `Plugins/` 未实现（M5）；
- A/I 的 `--puppet` 未实现，Puppet 仍只在壳（`MainForm`）；
- 没有 `--standalone` 开关：不给 `--parent` 即为独立启动；绑定参数是 `--bind`，不是 `--web`；
- 子进程守护是 1 秒轮询（`ParentWatch`），未挂 Job Object；
- 对外绑定与 `web-password.txt`（Basic 口令）未实现，只绑 `127.0.0.1`、用 `?key=` 换 Cookie。

---

## 4. 进程间通道

**选择：HTTP + JSON（loopback），不引入命名管道/消息总线。** 理由：房屋内已有 Kestrel 封装
（`Puppet.Core.Web.PuppetWebServer`，ChanTool 已用它承载产品 Web 界面），零新依赖；
可被浏览器、`zcurl`、测试脚本直接调试；跨平台一致。

鉴权：`%APPDATA%\OpenLoop\bus.key`（或 `~/.config/openloop/bus.key`），首次以 `FileMode.CreateNew`
原子生成，读到即用；请求头 `X-OpenLoop-Key`。浏览器走 `?key=` 换 HttpOnly Cookie；
默认只绑 `127.0.0.1`，对外需显式 `--web 0.0.0.0:19180` 且放口令文件（沿用 ChanTool 的
`web-password.txt` + Basic 口令模式）。

发现：`runtime/a.json` / `runtime/i.json`，内容 `{role,pid,bus,web,version,protocol,startedAt}`，
启动成功后原子写、退出时删除。壳靠它判断在跑与否，不靠端口探测。

| 端点 | 方向 | 说明 |
|---|---|---|
| `GET /bus/health` | 双方 | 角色、pid、版本、协议号、忙闲 |
| `POST /bus/hello` | 双方 | 启动/重连时登记对端 |
| `POST /bus/stage` | 更新方→被更新方 | `{version, dir, sha256}`：只校验并登记，不落地 |
| `POST /bus/restart` | 更新方→被更新方 | `{version, reason, force}` → 202 / 409(busy)；被更新方登记退出 |
| `POST /bus/shutdown` | 壳→A/I | 正常退出（不带新版本） |

业务 API 形态两边一致，`since=revision` 增量拉取，前端 500ms 轮询（不做 SSE，与 ChanTool 同套路；
若对话观感不可接受再升级，属局部改动）：

**为什么不上 SignalR**：现有引擎（Wima.Web）只有 `Buffer` 响应，没有 WebSocket/SSE
（已核对 dll，无相关符号），要上就得先把产品 Web 换成 ASP.NET Core；而界面本来每 250ms 才把流式增量
合并成一次渲染（ChatFlow `UpdateStreamingMessage`），250–500ms 轮询在回环上没有可感知差异，
事件量也只有单用户、每工具步一条。**若日后真要推**：先换 ASP.NET Core + SSE（单向、浏览器原生、
壳用 `HttpClient` 读流，零客户端依赖），仍不够再考虑 SignalR（要加
`Microsoft.AspNetCore.SignalR.Client` 包和双向协议）。

```
GET  /api/capabilities                  { worker: true|false, loop: true|false }
GET  /api/sessions                      [{id,title,workspace,state,revision}]
POST /api/sessions                      {workspace, model}
GET  /api/sessions/{id}?since=N         {turns[], tools[], pending[], approvals[], state, revision}
POST /api/sessions/{id}/messages        {text, tag}
POST /api/sessions/{id}/stop
POST /api/sessions/{id}/approval        {id, allow}        # 审批回传（§8）
# loop 侧另加（只在 I 实现，A 反代）
POST /api/loop/sessions/{id}/evolve | /rollback | /answer
GET  /api/loop/ledger?workspace=...
```

---

## 5. 状态归属与单写者

| 状态 | 唯一写者 | 说明 |
|---|---|---|
| `.openloop/ledger.jsonl`、`defs/`、`eval/` | I | A 不再 `Ledger.Open(...).AppendRun` |
| `.openloop/traces/<T-id>/` | 各自 | trace id 唯一，互不覆盖 |
| `.openloop/spool/*.json` | A 写、I 删 | Worker 运行证据的投递箱；A 跑完 `POST /api/loop/spool/drain`，I 每次演进开始前也 drain 一次，按 run id 幂等 |
| `chats/`（worker） | A | 路径不变 |
| `chats/`（loop） | I | 路径不变，kind 仍为 `loop` |
| `settings.json` | 壳（设置界面）；I 仅无头模式写 loop 级字段 | 原子写、最后写入者胜；使用时读盘 |
| `bus.key`、`puppet.key`、`web-password.txt` | 首次创建者 | 原子创建，读到即用 |
| `versions/`、`current.json`、`runtime/update*` | **壳** | A/I 只读自己的 `--version-dir` 与提交请求 |
| git 快照 `refs/openloop/<ts>` | 各自 | 引用只增不改，天然无冲突 |

读取激活集：A 每次运行前 `GET /api/loop/activation?workspace=` 拿当前 `AgentPolicy` 与规则文本；
拿不到（I 未起）按现有语义回退（全部关闭 + 提示），不阻塞 Worker。

---

## 6. Web 界面

**一套 `wwwroot`，两个宿主。** 界面构成基本相同，差异只在能力位，不做两份前端：

- `wwwroot/index.html` 单页，左栏会话列表 / 主区对话 / 右侧 loop 面板，与 WinForms 布局同构；
- `GET /api/capabilities` 决定显示哪些面板：A 返回 `{worker:true, loop:true}`，I 返回 `{worker:false, loop:true}`；
- 同一份 `app.js` 按能力位隐藏不可用面板与入口；**不按角色分叉资源**；
- A 的 `/api/loop/*` 反代给 I（30 行），浏览器只需认一个端口、一套鉴权；
- I 不实现 worker 路径（404，不是隐藏面板那么简单）；
- 原生 JavaScript，无打包器：`wwwroot/` 直接是发布产物（`PreserveNewest` 复制），改完刷新即生效。

---

## 7. 更新与互相重启

### 7.1 目录

```
%APPDATA%\OpenLoop\versions\
  a\<version+sha8>\        OpenLoop.A.exe + 依赖 + wwwroot/
  i\<version+sha8>\        OpenLoop.I.exe + 依赖 + wwwroot/
  shell\<version+sha8>\    OpenLoop.exe / OpenLoop（只手工更新）
  current.json             { a: {...}, i: {...}, shell: {...} } 原子写
runtime\update-log.jsonl   每次更新结果（append-only）
```

运行中的 exe 永远在自己的版本目录；新版本先解到**另一目录**，不碰被锁文件。

### 7.2 流程（壳是唯一 spawner）

1. 更新方（如 I，Loop 演进完 Worker）把新产物放进 `versions/a/<ver>/`，算 sha256；
2. `POST /bus/stage` 让 A 校验（协议号、清单、可执行）→ 只登记；
3. `POST /bus/restart`：A 若在跑任务返回 409，界面提示用户；`force` 才停；
4. A 写 `runtime/update-a.json`（版本、目录、请求方、时间）后退出；
5. 壳发现 A 退出，读该文件，以 `--version-dir <新目录> --parent <壳pid>` 拉起新 A，等 `hello`（10 秒）；
6. 成功 → `current.json` 落新版本、删请求文件、删旧目录（保留最近 2 个）；
   失败 → 拉回旧版本、记 `rollback`、两边界面显示原因。

对称：A 更新 I 走同一流程（`runtime/update-i.json`）。

三条防呆：

- 协议号不等 → 拒绝 stage，提示「两边一起更新」；
- 同一角色 10 分钟内重启超过 2 次 → 停止自动更新并记账（崩溃循环保护）；
- 同时只允许一个更新在飞：壳持有 `updating` 标记，第二个请求排队。

### 7.3 考核要考的是**被演进的那个二进制**

现在 `Gate` 在 I 内用 `AgentTaskRunner` 跑 Worker。一旦 A 与 I 版本分叉，这就考错了对象。
因此 A 加一个一次性入口：

```
OpenLoop.A.exe --eval-run <spec.json> --out <dir>    # 跑一题、出 final.md / run.json，退出码表结果
```

`AgentTaskRunner` 增加「进程内 / 子进程」两种执行器，默认进程内；`settings.EvalInWorkerProcess=false`
或检测到 A 版本 ≠ I 版本时走子进程。工作目录、种子、判据都不变。

### 7.4 壳自身

壳不做热更新（它是薄客户端 + 监督者，改动频率低）。需要换壳时先退出壳，A/I 随父进程退出，
手工替换后重开。这条写进文档，不假装支持。

---

## 8. 从现有代码剥离什么

| 现在 | 去处 | 规模 |
|---|---|---|
| `AppSettings`、`ModelCatalog`、`OpenCodeHub`、`OpenRouterCatalog`、`Provider*`、`Reasoners` | `Service/Config`、`Service/Models`（去 `AntdUI`/`WinForms` 依赖） | 中；DPAPI 加 Unix 回退 |
| `SessionView.SendTask`（约 961–1280 行）+ 命令/队列/流式回调 | `Service/Worker/WorkerSessionService` | **最大单块** |
| `SessionView` 的运行期状态（`ChatTurnBook`、状态栏、按钮使能） | 改由 A 的 API 提供；界面只做渲染与订阅 | 大：界面从直连改为客户端 |
| `SessionView.OnEvolve/OnRollback`（约 1548–1690） | `Service/Loop/LoopService`（在 I） | 中 |
| `LoopView` 的对话/流式/反馈队列 | `Service/Loop/LoopService` + 总线会话 | 中 |
| 审批 `ApprovalGate.Ask`（`SessionView:1987`、`LoopView:566`） | 运行中挂起为 `pendingApproval`，界面回传 `allow/deny`；无界面在 60s 后按拒绝处理（等同现有「无审批通道即失败」） | 中，跨进程语义新增 |
| 调查/问答（`SurveyStore`） | 保持文件态（`.openloop`），API 附带查询接口 | 小 |
| `PuppetHost` | 壳保留（`MainForm`）；A/I 改为 `--puppet` 可选，实例名 `A` / `I` | 小 |
| `ChatStore` | 不动，两个进程各读各的 kind | 小 |
| `Program.cs` | 拆成四个入口（A/I/Shell/Win） | 小 |

`ChatFlowControl` 的渲染与样式保持不变：A 的会话事件（delta / tool / status / final）映射回
`ChatTurnBook`，不改控件。验收时以「界面像素与今天一致」为准（Puppet 截图对照）。

---

## 9. 插件模型（一切皆插件）

**不移植 Cordis。** 目标是让「加能力」不用改宿主，而不是让循环本身可替换。

- 插件形态：目录 + `plugin.json`（id、version、`entry {command,args}`、
  `contributions {tools[], providers[], commands[]}`、`permissions {network,shell,workspace}`）；
- 宿主：stdio JSON-RPC 子进程（复用 `ProcessReasoner` 的约定：stdin 收请求、stdout 出结果）；
- 隔离：插件崩溃只摘掉它的贡献，最多自动重启一次；无插件时全部内置能力照常；
- 内置能力仍编译在 Core 里，只把**注册表**改成贡献式——不把现有工具重写成插件（纯搬运，无收益）。

**与 dsh 的兼容，分三层说清楚**：

1. **可直接复用**：dsh 插件若提供 OpenAI 兼容端点（如本机 `dsh-our-free-model` 的独立服务），
   OpenLoop 按「provider 插件」接入，零代码；
2. **可桥接**：`openloop-dsh-bridge`（可选、需 Node ≥22）用 `@deepseek-ai/cordis` 加载插件，
   把它注册的 `ctx.llm` / `ctx.tools` / `ctx.commands` 通过同一 stdio 协议转给 OpenLoop；
   只桥这几类，先用一个真实插件验证再决定是否扩大；
3. **不支持**：session 事件表、agent loop 拦截、浏览器客户端插件（React）。这些要求实现
   Cordis 的时空组合语义，成本无限；写进文档，不假装支持。

---

## 10. 里程碑与验收

| # | 内容 | 验收 |
|---|---|---|
| **M0** | 骨架：`Service`（bus、发现、密钥、监督）；A/I/Shell 三个入口；握手、父进程守护。**配置/模型抽取移到 M1** | 壳拉起 A+I；杀壳后 A/I 自退；A/I 互认；现有构建与测试全绿 |
| **M1** | 状态通道：`WorkerSessionService`、`LoopService`；界面改为总线客户端（含审批回传）；账本单写者 + spool | Puppet 回归：WinForms 全流程行为与今天一致；`.openloop` 产物逐字段一致；A 重启后界面可重连 |
| **M2** | Web：静态 SPA、A 全量 + 反代、I 仅 loop、口令文件 | 浏览器在 A 端口跑完一次 worker 任务并看到 loop；在 I 端口只能 loop；进程重启后页面自动恢复 |
| **M3** | 更新：版本目录、stage/restart、壳回滚、防抖、忙时拒绝、`--eval-run` | 四个脚本：换 A 成功 / 换 A 失败自动回滚 / 换 I 成功 / 更新中账本与会话不丢 |
| **M4** | 跨平台：`linux-x64` 发布、XDG 路径、密钥回退、无界面壳 | Linux 上 `OpenLoop` 拉起 A+I，浏览器完成一次任务与一轮 loop（模型调用前先征得用户同意） |
| **M5** | 插件：清单 + stdio 宿主 + 注册表贡献化；dsh 桥 spike | 示例插件提供 1 个 tool 与 1 个 provider；插件崩溃宿主不倒；spike 结论写进文档 |

顺序不可换：M0 之前不改任何行为，M1 之后才有资格谈「两个进程」。

---

## 11. 明确不做

- 不实现 Cordis、不追求 dsh 插件 100% 兼容；
- 不引入 SignalR（先量再决定；要升级先走 SSE）；
- 不做 WebSocket/SSE 实时流（轮询够了）；
- 不做跨机器部署/多用户（只保留 loopback + 口令文件的单机远程访问）；
- 不在本轮做 Loop 的「取源码 → 构建 → 发布」（只留 stage/restart）；
- 不做壳的热更新；
- 不新增 NuGet 依赖（HTTP 用现有 `Puppet.Core.Web`）。

---

## 12. 风险

| 风险 | 处理 |
|---|---|
| `SessionView` 从直连改为客户端，行为回归面大 | 先做 M1 的接口与事件，再逐按钮切换；每步用 Puppet 走真实路径回归，不写测试专用钩子；渲染不动 |
| 轮询导致流式观感变差 | 500ms 起步；先量再决定是否给会话加长轮询，不预先上 SSE |
| 审批跨进程后语义变化（超时/失联） | 60s 无回传按拒绝处理，与现在「无审批通道即失败」一致；`pendingApproval` 留在会话状态里可重放 |
| 换 A 时有任务在跑 | `409 busy` + 界面确认；`force` 终止任务并记账 |
| 壳崩溃带走 A/I | 这是设计：孤儿进程比丢任务更难排查；`--standalone` 供排障 |
| 更新失败后版本目录残留 | 只保留最近 2 个；占用中的目录下轮再删 |
| 双通道同时操作同一会话 | 会话级串行：同一 session 同时只允许一个运行，第二个请求排队 |
| 密钥跨平台 | DPAPI 仅 Windows；Unix 落 0600 文件并提示 |
| 账本丢失 | A 只投 spool，I 幂等 drain；I 被换前先 drain 一次 |
| Linux 上 `Puppet.Core.Web` 未实测 | M4 前先 `dotnet publish -r linux-x64` 冒烟；不行就换 `HttpListener`（同进程内的薄封装，接口不变） |

---

## 13. 已拍板（2026-11-01）

1. **名称**：`OpenLoop.A.exe` / `OpenLoop.I.exe` / `OpenLoop.exe`（Windows）/ `OpenLoop`（Linux 壳）。
2. **壳是界面客户端 + 监督者**，不跑 agent；WinForms 从 A 进程移出，A/I 变成纯无头、跨平台。
3. **dsh 兼容只做到「provider/tool 插件 + 一次桥接 spike」**；不实现 Cordis。
4. **Web 用原生 JS、无构建**，A 与 I 共用 `src/OpenLoop.Service/wwwroot/` 这一套静态界面。
5. **批准改写 `CHARTER.md` §12**：把「Worker 与 Loop 同进程时，二进制只能整体替换」改为
   「双进程各自可替换、协议号对齐」；charter 版本 +1，四条不变量与原 §12 的其余规则不动。

实施从 M0 开始；M0 不改任何现有行为。

---

## 14. 进度（已验证）

**2026-11-01 · M0 骨架（Windows / Debug）**

已落地：

- `src/OpenLoop.Service`：`BusHost` / `BusClient` / `BusHandler`（health、status、hello、shutdown）、
  发现文件 `runtime/{a,i}.json`、`bus.key` 原子创建、`ParentWatch`、`RestartGuard`、`Supervisor`、`HeadlessHost`；
- `src/OpenLoop.A`（19180）、`src/OpenLoop.I`（19181）、`src/OpenLoop.Shell`（`OpenLoop.exe`）；
- `tests/OpenLoop.Service.Tests`；`OpenLoop.slnx` 已收录新项目。

验证结果：

- `dotnet build OpenLoop.slnx`：0 警告 0 错误；
- `dotnet test tests\OpenLoop.Service.Tests`：10/10 通过；
- 壳拉起 A+I，`/bus/health` 分别返回 `a` / `i`，A 的 `/bus/status` 看到 `peer=i`；
- 错误 key 访问 `/bus/health` 返回 404；
- `Stop-Process -Force` 杀壳后 3 秒内 A/I 全部退出，`runtime/a.json`、`runtime/i.json` 被清理。

未做（按计划）：

- `OpenLoop.Win`（WinForms 壳）与 `OpenLoop.App` 的迁移 → M1；
- `AppSettings` / 模型 / `Reasoners` 抽取 → M1；
- `wwwroot` 与 `/api/*` → M2；
- `stage` / `restart`、版本目录、回滚 → M3；
- Job Object（当前只有 1s 轮询守护）；
- A/I 的 Puppet 接入（`--puppet`）；
- `CHARTER.md` §12 改写尚未落笔。

现有 `OpenLoop.App` 未改动，仍是今天的行为；M0 只是把两个后台进程与壳立起来。

**2026-11-01 · M1 第一步（共享层抽取，Windows / Debug）**

已落地：

- `AppSettings` / `ProviderConfig` / `ProviderPresets` → `OpenLoop.Service.Config`；
- `ModelInfo` / `ModelCatalog` / `ProviderCatalog` / `OpenRouterCatalog` / `OpenCodeHub` / `Reasoners`
  / `OpenCodeTools` → `OpenLoop.Service.Models`；
- `AppLog` → `OpenLoop.Service.Logging`；`OpenLoop.App` 通过全局 using 继续用原名引用；
- Unix 上 DPAPI 不可用时改存 `plain:` 前缀并按 0600 收紧设置文件
  （`shortcut: 等接入系统 keyring 再换`）。

验证：`dotnet build OpenLoop.slnx` 0 错误；`OpenLoop.Core.Tests` 181 通过 / 1 跳过；
`OpenLoop.Service.Tests` 10 通过；启动 `OpenLoop.exe` 后 Puppet `registry` 在 9090 返回 200（界面可启动）。

真实任务回归（`openrouter/free`，Puppet 驱动）：会话 `b47a671a` 从 `Running` 到 `Done`，
工作区 `%TEMP%\opencode\m1-smoke` 生成 `hello.txt` 内容 `ok`；trace
`T-20261101-095847-7c1472` 四件齐全（`outcome=ok`、`steps=3`、`cost=0`、`malformedRetries=2` 已由护栏恢复），
账本追加 `R-4338106d…` 的 run 证据。

下一步：`WorkerSessionService`（把 `SessionView.SendTask` 的运行管线抽到 Service，界面改为订阅事件），
之后才是 A 的 `/api/sessions` 与 WinForms 客户端化。

**2026-11-01 · M1 第二步（运行管线 + 会话宿主 + A 的 API，Windows / Debug）**

已落地：

- `WorkerSessionService`：账本/策略 → 工具与审批 → trace → `AgentLoop` → 账本证据；
  界面相关的格式化、流式显示、通知仍留在调用方（通过 `WorkerRunRequest` 的回调）。
  `SessionView.SendTask` 改为调用它，成功后由 `Started` 回调建立 turn，行为与之前一致。
- `WorkerSessionHost`：会话、队列、运行状态、`Paused/Resume`、历史拼接；
  可注入 runner 以便测试（4 个用例：完成、排队、停止/恢复、历史）。
- `WorkerApiHandler`（A）：`/api/capabilities`、`/api/sessions`（增/查/发/停/恢复/改模型），
  与 `/bus/*` 共用 `bus.key`。
- `BusHost.Use(...)` 支持挂接业务端点。

验证：

- `dotnet build OpenLoop.slnx` 0 错误；
- `OpenLoop.Service.Tests` 14 通过；
- WinForms 真实任务（`openrouter/free`）：`e6f1210f` 从 `Running` 到 `Done`，`hello.txt=ok`，
  trace `T-20261101-110313-0fcda3` 四件齐全；
- **A 进程单独**（无界面）用 zcurl 走 HTTP：`e05ee761` `running → done`，`hello.txt=ok`，
  trace `T-20261101-110618-e6a1fd` 四件齐全，`/bus/shutdown` 正常退出。

未做：WinForms 改为通过 HTTP 访问 A（仍在进程内直连）；Loop 服务与 I 的 API；账本单写者 + spool。

**2026-11-01 · M1 第三步（WinForms 变成 A 的客户端，Windows / Debug）**

已落地：

- `WorkerApiClient`：`/api/sessions` 的 HTTP 客户端（共用 `bus.key`，地址优先取 `runtime/a.json`，其次 19180）；
  `POST /api/sessions` 接受界面给的会话 id，A 端会话与 `chats/` 文件名保持一致；
- `SessionView.SendTask` 改为：确保 A 上有会话（没有就带 id 建，A 从 `chats/` 读回历史）→ 发消息 → 每 250ms
  拉一次快照（正文/思考/工具行/步数/trace/def）并按轮次映射到本地对话；停止按钮改成 `POST .../stop`，
  轮询上限由 `_cts` 控制；
- 审批跨进程：快照里带 `prompts`（`kind` + `category`），界面弹原有 `ApprovalGate`，答案回 `POST .../approval`
  （0 拒绝 / 1 一次 / 2 同类后续都允许，选 2 时界面把 `AllowOutsideWorkspace` / `AllowSensitiveActions` 写回设置）；
- 每轮把 `Checkpoint`（git 快照 id）随快照带回，界面「撤回对话」仍能恢复文件（`FileUndoLog` 在 A 进程内存里，
  跨进程后只剩快照恢复）；
- `OpenLoop.App` 的入口改用 `Supervisor` 拉起 A（`bin/worker/OpenLoop.A.exe`，A 带自己那份依赖，
  不与界面程序集互相覆盖），退出时 `StopAll`；`--parent` 守护仍生效。

验证：

- `dotnet build OpenLoop.slnx` 0 错误；
- `OpenLoop.Service.Tests` 20 通过（新增 API 形状/审批回传用例）；`OpenLoop.Core.Tests` 181 通过 / 1 跳过；
  `ChatFlow.Tests` 65 通过；`FastColoredTextBox.Tests` 87 通过；
- 真实任务回归（`openrouter/free`，Puppet 驱动界面）：`a690e20c` → `Done`，`hello.txt=ok`，
  trace `T-20261101-122324-f4fc99` 四件齐全；同时用 `GET /api/sessions/{id}` 直连 A 确认
  `state=done turns=1 trace=…`（`2d994e4d` / `T-20261101-122612-97ad06`），即状态确实在 A；
- 停止路径：长任务跑到 4KB 正文时点停止，界面 `idle`、会话 `Interrupted`，A 侧
  `state=interrupted outcome=stopped`（会话 `6ee51cbd`）；
- 多轮上下文：同一会话连发两条，A 侧 `state=done turns=2`，第二条直接答 `ok`
  （读过第一轮写的 `hello.txt`），会话 `555e67b7`、标题自动生成；
- 重负载：`openrouter/free` 流式到 6.8KB 正文期间，Puppet 每次 `get` 44–66ms、进程内存稳定 206MB、
  `Responding=True`，停止后回到 `idle`（会话 `6ee51cbd` 之后的 `m1-load` 轮）。

未做 / 已知：

- 审批弹框的端到端点击未自动化（要真人点；Puppet 不点模态框），只有宿主层与 API 层用例；
- `WorkerApiClient` 在构造时解析一次地址，A 若换端口需重建客户端；
- 界面与 A 都会写同一份 `chats/<hash>.json`（内容一致，最后写入者生效），等账本单写者落地后一并收口；
- `SessionView` 的 `/btw` 侧任务仍走进程内服务；
- `MainForm` 的会话列表/新建仍只用本地 `chats/`，A 端会话在首次发送时按 id 懒创建。

**2026-11-01 · M1 第四步（Loop 的写操作搬到 I，Windows / Debug）**

已落地：

- `LoopService`（I）：演进（账本 → `LoopAgent` 提案 → 三层 `Gate` → 采纳/保留）、回滚、登记规则；
  演进按工作区单实例，后台跑、状态可查、可停；
- `LoopApiHandler`（I）：`POST /api/loop/evolve`（202 + 状态）、`GET /api/loop/evolve?workspace=…`、
  `POST /api/loop/evolve/stop`、`POST /api/loop/rollback`、`POST /api/loop/rule`、`GET /api/capabilities`
  （`{worker:false, loop:true}`）；
- `LoopApiClient`（界面）：地址优先取 `runtime/i.json`，其次 19181；
- 界面 `OnEvolve` / `OnRollback` / `OnAddRule` 改为调 I，进度按 `phase`（准备/构思/评审）本地化后显示；
  按钮逻辑不再自己碰账本；
- 界面入口用 `Supervisor` 同时拉起 A 与 I（`bin/worker/` 下各自带依赖），退出时一起收摊；
  `MainForm.StartEvolve()` 作为 A 面驱动入口（与既有 `Send`/`SetModel` 同一套约定）。

验证：

- `dotnet build OpenLoop.slnx` 0 错误；`OpenLoop.Service.Tests` 26 通过（新增 LoopService/LoopApi 用例）；
- 启动界面后 `runtime/{a,i}.json` 都在，子进程为 `OpenLoop.A` + `OpenLoop.I`；杀界面后 3 秒内全部退出（0 残留）；
- I 直连：`/api/capabilities` → `{"worker":false,"loop":true}`；`POST /api/loop/evolve`（空工作区）→
  `running/preparing` → `error`，错误为「没有考核题：先放进 …/eval/tasks」（未调用模型）；
  `POST /api/loop/rollback`（只有 v1）→ 400「没有更早的版本」；
- 界面路径：`MainForm.StartEvolve()` → `LoopApiClient` → I，状态栏走完「准备中 → 演进失败」并弹提示，
  即客户端 + 轮询 + 相位映射都通；
- 演进成功路径（免费模型 `openrouter/free`，工作区 `%TEMP%\opencode\m1-evolve` 预置 1 道考核题）：
  I → `LoopAgent` 提案 → 三层 `Gate` → `state=done adopted=false`，报告
  `.openloop/eval/reports/r-20261101-124522-d635/report.md`：结论「保留 v1」、
  理由「符合性未通过：可回指（还没有任何运行证据）」，即提案与门禁在 I 里跑通；
- 回归：真实任务（`openrouter/free`）`7e124b1d` → `Done`、`hello.txt=ok`，
  trace `T-20261101-124325-8b1f02` 四件齐全。

未做 / 已知：

- A/B 逐题评估（`AgentTaskRunner` 真跑题）没跑到：现场账本没有运行证据，门禁在「符合性」就保留 v1；
  补一条证据后重试时 OpenRouter 免费额度当日用尽（429 `free-models-per-day`），留待额度恢复后补跑；
- `LoopView`、`SettingsForm`、`SideWorkView` 仍在自己进程里 `Ledger.Open(...).EnsureInitialized()`
  （幂等初始化 + 只读查询），尚未全部走 I；`/btw` 侧任务也会写 `AppendRun`，单写者还没收口；
- 界面回滚按钮的确认框是模态，未自动化点击（I 的接口已用 zcurl 验证）。

**2026-11-01 · M1 第五步（账本单写者）**

已落地：

- 账本只有 I 写：A 每轮运行前 `GET /api/loop/activation?workspace=` 拿激活集 + 特征表 + 启用技能
  （`LoopService.LoadActivation`，内部做 `EnsureInitialized` 与技能发现即启用），拿不到就按「全部关闭」跑，
  不再 `Ledger.Open(...).AppendRun` / `SkillCatalog.Sync`；
- 运行证据走投递箱：A 把 `RunRecord` 原子写到 `.openloop/spool/<run id>.json`（`RunSpool`），
  跑完 `POST /api/loop/spool/drain` 让 I 入账；I 侧按 run id 幂等，并在每次演进开始前再 drain 一次
  （补上 I 停机期间积压的）；
- 技能启用/停用、技能激活集读取改走 I：`POST /api/loop/skills`、`GET /api/loop/activation`；
  界面侧 `SessionView` / `LoopView` / `SettingsForm` 不再写账本（只读查询保留在本地）；
- `LoopApiClient` 失败时按 `runtime/i.json` 重新发现一次地址（I 重启换端口不用重建客户端）。

验证：

- `dotnet build OpenLoop.slnx` 0 错误；`OpenLoop.Service.Tests` 35 通过
  （新增 activation / spool 幂等 / Web UI 用例）；
- A + I 独立进程（`bin/worker/`）：发一条消息（模型 429 失败）后，A 侧 `activationVersion=1 / featureCount=12`，
  spool 文件被 I 抽走，账本多出一条 `kind:"run"`（`R-f8289185…`，`activationVersion=1`）；
  再投一次同 id 不重复记账；
- 上一轮中断遗留的 `activationVersion=0` 记录（I 未就绪时写的）也在下一次运行后自动补进账本。

未做 / 已知：

- `/btw` 侧任务仍在壳进程内跑 agent，且尚未改成走 spool（当前它不写 `AppendRun`，所以没有冲突）；
- 审批弹框、回滚确认框仍是模态，未自动化点击。

**2026-11-01 · M2（一套 wwwroot + 浏览器界面 + A 反代 I）**

已落地：

- `src/OpenLoop.Service/wwwroot/`：`index.html` + `app.js` + `style.css`，原生 JS 无构建；
  左会话列表 / 中对话 / 右 loop 面板，与 WinForms 同构；
- `WebUiHandler`：静态文件 + `?key=` 换 HttpOnly Cookie（`SameSite=Strict`），
  路径逃逸与缺失文件 404；`/api/*`、`/bus/*` 不归它管；
- 鉴权：`BusAuth.IsAuthorized(..., allowCookie)`，`/api/*` 认 Cookie（浏览器没法加请求头），
  `/bus/*` 只认 `X-OpenLoop-Key`；
- `LoopProxyHandler`（只在 A）：`/api/loop/*` 原样转给 I（方法、query、body、状态码、Content-Type），
  I 没起回 503「Loop 未就绪」；浏览器只认一个端口、一套鉴权；
- 能力位：A `{worker:true, loop:true}`（A 反代 loop），I `{worker:false, loop:true}`；
  I 不实现 `/api/sessions`（404，不是藏面板）；前端按能力位隐藏会话/对话面板，I 上是单列 loop 面板；
- `wwwroot` 挂在 `Service` 上（`PreserveNewest`），随 A/I 复制到各自输出目录（`bin/worker/wwwroot/`），
  改界面只改一处。

验证：

- `dotnet build OpenLoop.slnx` 0 错误；`OpenLoop.Service.Tests` 35 通过；
- zcurl：`GET /?key=…` → index.html；无 key → 401 文案；`GET /app.js`（仅 Cookie）→ JS；
  A `/api/capabilities` → `{"worker":true,"loop":true}`；A `/api/loop/activation`（反代）→ I 的账本投影；
  无 key 访问反代 → 404；I `/api/sessions` → 404；
- 浏览器（应用内 Chromium，`http://127.0.0.1:19180/?key=…`）：无 console 错误，
  新建会话 → 发消息 → 轮次渲染（`say hi` 收到模型回复，`run=done · ok · 0 步 · v1`），
  会话列表状态随快照更新；填工作目录后账本行显示 `v1 · 12 特征 · 3 规则`；
- 浏览器打开 I（`http://127.0.0.1:19181/?key=…`）：角色位 `I · 阴（Loop）`，会话/对话面板隐藏，
  单列 loop 面板，无 console 错误；
- 进程重启后页面恢复：杀掉 A 再起新的 A，页面下一次 404 轮询按同一个 id 重建会话
  （A 从 `chats/` 读回历史），状态栏显示「已重连 c83ef3ab」、历史 1 轮完整回来。

未做 / 已知：

- 本机自动化窗口不可见，`browser.click` / `screenshot` 报 `UnknownVizError`，
  点击与截图没走到（改用 DOM 级驱动同一批事件处理器；元素 `elementFromPoint` 命中正常）；
- 对外绑定（`--web 0.0.0.0`）与 `web-password.txt` + Basic 口令未实现：现在只绑 `127.0.0.1`，
  浏览器用 `?key=` 换 Cookie；
- 浏览器界面还没有模型下拉（手填模型名）、没有设置页、没有 trace/report 入口，属后续工作；
- 审批弹窗在浏览器里用页面内提示条（不是模态框），与 WinForms 的 `ApprovalGate` 视觉不同。

**2026-11-01 · M3（版本目录 / stage-restart / 壳回滚 / `--eval-run`）**

已落地：

- 版本目录（`VersionStore`）：`versions/<role>/<version>-<sha8>/` 各带一份完整产物，
  `current.json` 记每个角色当前跑哪份（只有壳写），剪枝保留最近 2 份，
  `runtime/update-log.jsonl` 一行一次结果（ok / rollback / skipped）；
- 被更新方（`BusHandler`）：`POST /bus/stage` 只校验并登记（协议号、目录、可执行文件、sha256），
  `POST /bus/restart` 忙回 409（`force` 才停），不忙就写 `runtime/update-<role>.json` 交接单再退出；
  忙闲来源：A = 有会话在跑，I = 有演进在跑；
- 壳（`Supervisor`）：子进程退出时先看有没有交接单——有就拉起新目录、10 秒内报到才算成功
  （记 `current.json`、清交接单、剪旧目录），起不来就退回原来的可执行文件并记 rollback；
  同一角色 10 分钟内更新超过 2 次直接跳过（记账 `skipped`）；更新期间屏蔽该角色的 Exited 重入；
- A/I 启动参数加 `--version-dir`，写进 `runtime/<role>.json` 的 `dir`，对端据此定位可执行文件；
- 子进程 stdout/stderr 落 `logs/<role>.log`，壳自己的监督日志落 `logs/shell.log`（GUI 壳没有控制台）；
- 坏包不交给操作系统执行：`Spawn` 先验 PE/ELF/shebang 头，不是可执行文件直接判失败走回滚
  （踩过：`Process.Start` 打开非 PE 文件在本机会卡住不返回）；
- `OpenLoop.A.exe --eval-run <spec.json> --out <dir>`：spec 带 workspace/model/激活集/题目，
  A 自己跑题、写 `final.md` + `run.json`，退出码 0=通过 / 1=没通过 / 2=出错；
  `LoopService` 在 A 与 I 版本不一致时改走 `ProcessTaskRunner`（子进程跑），一致时仍在进程内。

验证（四个脚本在 `%TEMP%\opencode\m3-*.ps1`，逐条 PASS）：

- 换 A 成功：stage 202 → restart 202 → A 换到 `versions/a/9.9.1-…`（pid 变、`dir` 变）、交接单清掉、
  `current.json.a` 指向新目录、`/bus/health` 正常、日志 `result":"ok"`；
- 换 A 失败自动回滚：把包里的 `OpenLoop.A.exe` 换成非可执行内容 → 壳 1 秒内判死、退回原目录、
  记 `result":"rollback"`、`current.json` 不指向坏包、A 恢复健康；
- 换 I 成功：同 A，`current.json.i` 指向 `versions/i/9.9.3-…`，`/api/capabilities` 仍 `{worker:false,loop:true}`；
- 更新不丢状态：先造一条会话（`keep0001`，1 轮，模型 429 但已落 `chats/`），换 A 后
  新 A 的 `/api/sessions` 是空的、带同一 id 重建后 `turns=1`，`ledger.jsonl` 与 `chats/*.json` 的 sha256 分毫未变；
- 构建 0 错误；`OpenLoop.Service.Tests` 45 通过（新增 `BusUpdateTests`、`VersionStoreTests`）；
  `OpenLoop.Core.Tests` 181 通过 / 1 跳过。

未做 / 已知：

- `--eval-run` 只验证到「解析 spec → 起 agent → 模型失败退出码 2」，通过路径（0/1）要等免费额度恢复；
- A/I 版本相等时 Gate 仍走进程内 `AgentTaskRunner`，`ProcessTaskRunner` 只在版本分叉时启用，
  这条分支没有免费模型可完整跑一轮；
- 更新只做了「手工 stage + restart」，Loop 演进完自动把新产物 stage 给对方的闭环还没接；
- 崩溃循环保护（重启防抖）沿用 M0 的 `RestartGuard`（3 次/1 分钟），没在这次脚本里打出来。

**2026-11-01 · M4（linux-x64 产物 / XDG / 密钥回退 / 无界面壳）**

已落地：

- `scripts/publish-linux.ps1`：一条命令产出 `artifacts/linux-x64/`，布局与 Windows 一致——
  `OpenLoop`（壳）+ `worker/OpenLoop.A` + `worker/OpenLoop.I` + 各自 `wwwroot/`，全部 self-contained；
- 无界面壳（`OpenLoop.Shell`）：默认先找 `<壳目录>/worker/OpenLoop.A`、`worker/OpenLoop.I`，再找同目录；
  启动后打印浏览器入口 `http://127.0.0.1:19180/?key=<bus.key>`（`?key=` 换 HttpOnly Cookie）；
  Ctrl+C 之外再注册 `SIGTERM`（systemd / 容器里能正常收摊）；
- 密钥回退（Unix 无 DPAPI）：`AppSettings.Protect/Unprotect` 先判平台，非 Windows 直接落 `plain:` 前缀，
  `Save` 时把 `settings.json` 收成 0600；`bus.key` 也以 0600 创建；
- XDG：`AppPaths` / `AppSettings` / `ChatHistory` 统一用 `SpecialFolder.ApplicationData`，
  Linux 上即 `$XDG_CONFIG_HOME/OpenLoop`（缺省 `~/.config/OpenLoop`），与 Windows 的 `%APPDATA%\OpenLoop` 同构。

验证：

- `dotnet publish ... -r linux-x64 --self-contained true` 对 A / I / 壳三者都成功，产物是 ELF（`7F 45 4C 46`），
  `worker/wwwroot/index.html` 存在；修掉平台判空后发布不再报 `CA1416`；
- 用 Windows 版壳做同布局回归（`%TEMP%\opencode\m4-shell-test.ps1`）：默认 `worker/` 命中、
  A+I 各就绪、打印带 key 的 URL、`/api/capabilities` = `{"worker":true,"loop":true}`、页面含 `<title>`、
  杀壳后 4 秒内 A/I 与壳全部退出（残留 0）；
- `OpenLoop.Service.Tests` 47 通过（新增 `SettingsKeyTests`：`plain:` 前缀在任意平台都能解、垃圾值解成空）；
  `OpenLoop.Core.Tests` 181 通过 / 1 跳过；`dotnet build OpenLoop.slnx` 0 错误、无 `CA1416`。

未做 / 已知：

- **Linux 实机没跑起来**：本机 WSL2 Ubuntu-22.04 无法启动（`HCS_E_HYPERV_NOT_INSTALLED`，虚拟化未启用），
  也没有 Docker，所以验收里的「Linux 上完成一次任务与一轮 loop」尚未执行；产物与壳路径只做了 Windows 等价回归；
  （2026-11-03 已补跑，见下面「M4 实机验收」）
- 从 Windows 拷到 Linux 后要给三个 apphost 补执行位：`chmod +x OpenLoop worker/OpenLoop.A worker/OpenLoop.I`；
- 设置文件不能跨平台直接搬：Windows 写的 `KeyProtected` 是 DPAPI 密文，Linux 解不开（解成空），
  要在 Linux 上重填密钥，或改用 `OPENROUTER_API_KEY` 等环境变量；反向（Linux `plain:` → Windows）可以读；
- 只出 `linux-x64`，没有 arm64；A/I 目前依赖集相同，所以共用一个 `worker/`；
  将来若分叉，应改成 `worker/a/` 与 `worker/i/` 两套目录；
- `Puppet.Core.Web` 在 Linux 上只用 Kestrel 的托管实现，运行期尚未实测（发布阶段已带全 Kestrel 依赖）。

**2026-11-03 · M4 实机验收（WSL2 Ubuntu-22.04）**

环境：WSL2 Ubuntu-22.04（Linux 6.18 x64），把 `artifacts/linux-x64/` 整个拷到 `/root/openloop-test`，
对三个 apphost `chmod +x`，密钥经 `OPENROUTER_API_KEY` 环境变量注入。

实测：

- `./OpenLoop` 起壳，`~/.config/OpenLoop/runtime/{a,i}.json` 数秒内就绪（a=19180、i=19181），`bus.key` 为 0600；
- `/api/capabilities`：A = `{"worker":true,"loop":true}`，I = `{"worker":false,"loop":true}`；两边共用一份 `wwwroot`，页面 `<title>OpenLoop</title>`；
- Windows 浏览器经 WSL localhost 转发打开 `http://127.0.0.1:19180/?key=…`：新建会话（`/root/uiwork`）→ 发「创建 ui-note.txt」
  → `done · ok · 1 步 · v1`，`WriteFile` 工具行正常渲染，文件内容 `hello-from-linux-ui`（19 字节）；
- Loop 面板选 `/root/olwork`（含 `calc-file` 考核题）→ 演进：`proposing → evaluating → done`，基线 1/1、候选 1/1、平局保留旧版；
  `.openloop/eval/runs/` 两轮都落 `calc.txt=42`，报告 `binary = 1.0.0+0a9fb14…`，即 Gate 起的是 `worker/OpenLoop.A --eval-run`，不是同进程；
- SIGTERM 杀壳后 A/I 都正常退出（残留 0）。

顺带修：`LoopAgent.BuildPacket` 的输出样例只给了 `{"enabled":true}`，免费模型于是给 `limit.steps`/`limit.tool_result`/`compress.at`
也塞 `enabled`，被 `CandidateValidator` 判非法、整轮作废；样例后补一句参数表（guard 用 `enabled`/`max`，limit 只用 `value`，规则不带 params）。

本次未做：只验证 NAT + localhost 转发，未测 mirrored 网络模式。

**2026-11-03 · 采纳分支验收（同一 WSL2 环境）**

目的：`proposing → evaluating → done` 只证明「拒绝」这条；「候选优于基线 → 采纳 v2 → Worker 下一轮用 v2」这一半没跑过。

搭法：新工作区 `/root/adoptwork`，`defs/v1.json` 用完整默认集但把 `limit.steps` 压到 1；
考核题 `token-copy` 要求「先 ReadFile 读 `secret.txt` 的随机令牌，再 WriteFile 写到 `token.txt`」——
1 步跑不完，8 步可以。

实测（两轮演进，第二轮采纳）：

- 第一轮：基线 0/1、候选 0/1（免费模型那一跑没落笔）→ 平局保留 v1；
- 第二轮：基线 0/1、候选 1/1 → `adopted=true`，`.openloop/defs/v2.json` 出现（`version=2, by=loop`），
  账本落 `verdict: adopted, base=v1, result=v2`；
- 采纳当次证据 `run.json`：`version=2, passed=true, steps=2, outcome=ok, binary=1.0.0+5ee2d49…`；
- 同工作区再开一个 Worker 会话：`activationVersion=v2`，两笔工具调用 `ReadFile → WriteFile`，
  `token.txt` 与 `secret.txt` 逐字节相同——说明采纳后的激活集真的被 A 读到了。

说明：免费模型自己不会把 `limit.steps` 抬上去（多轮都提「加规则/加护栏」），所以这一轮用
`settings.json → loopCommand`（本项目既有的「构思端换外部进程」开关）指向一个固定输出候选的脚本，
让 Gate 的采纳逻辑能在真实模型、真实二进制下被验证；验证完已删掉该设置与脚本。
不这么做就得等一个会自己调阈值的模型，属于模型运气，不是代码路径。

同轮修掉两个真实缺陷：

- `LoopAgent.Normalize`：模型给 `limit.*` 塞 `enabled` 时，`true` 视为冗余去掉、`false` 当停用，
  不再让整轮候选被校验打回（三次实测里每次都栽在这个字段上）；
- `AgentTaskRunner`：单题模型调用抛异常（空 body / 429 / 截断 JSON）不再带走整轮演进，
  记成「调用失败」的未通过题目，原因写进该题 `run.json`；取消仍然照常向上抛。

**2026-11-03 · 更新通道验收（同上环境）**

`/bus/stage` + `/bus/restart` + 壳的换版/回滚/防抖此前只有代码，没走过一次。用现有产物复制成
`/root/upd/a-v2`（同版本、不同目录）当「新版本」，坏包用 `#!/bin/sh exit 1` 冒充。

实测：

- 正常换版：`stage {dir:/root/upd/a-v2,version:v2}` → `restart` 返回 202 → A 退出、壳拉起新目录，
  `runtime/a.json` 的 pid 从 367 变 405、`dir=/root/upd/a-v2`，`versions/current.json` 记 v2，
  `runtime/update-log.jsonl` 落 `result: "ok"`，`/api/capabilities` 照常；
- 回滚：stage 坏包 → restart → 壳 10 秒内等不到报到，`update-log.jsonl` 落 `result: "rollback"`,
  退回 v2（新 pid 130、目录仍是 `/root/upd/a-v2`），`current.json` 不动；
- 防抖：10 分钟内第三次更新被拦，`result: "skipped"`、detail `too many updates`，并照旧重启上一版；
- 参数校验：没 stage 就 restart → 409 `nothing staged`；sha256 不匹配 / 目录里没有 `OpenLoop.A` /
  协议号不一致 → 409；错 key 打 `/bus/stage` → 404。

顺带修：**Linux 壳启动时不认 `versions/current.json`**（Windows 壳先查 `store.Exe(role)`，Linux 壳只找
`worker/`），所以更新过一次、壳重启就退回随包版本。现在两条壳都按「命令行/环境变量 → current.json → worker/」
的顺序取 exe；实测换到 `a-v2` 后整壳重启仍从 `a-v2` 起来，删掉 `current.json` 又回到 `worker/`。

本次未做：「新版本」是同版本复制体，没有真正不同版本的构建产物；Windows 侧同链路未复测；
`VersionStore.Prune`（每个角色只留两份）没在真实目录结构下验证过。

**2026-11-03 · 事件驱动自动演进（同上环境）**

此前 Loop 只有点「演进」或 `POST /api/loop/evolve` 才会跑，「事件驱动、可自主启动」一直没落地。

已落地：

- `AppSettings`：`autoEvolve`（默认**关**）、`autoEvolveRuns`（新证据条数，默认 5）、
  `autoEvolveCooldownMinutes`（默认 60）、`autoEvolvePerDay`（每工作区每天上限，默认 3）；
- 触发点：A 每轮跑完把记录投进 `.openloop/spool/`，I 的 `/api/loop/spool/drain` 收到新记录后调
  `LoopService.TryAutoEvolve`（不是定时器，靠真实证据触发）；
- 判据是纯函数 `AutoEvolve.ShouldStart`（开关 / 新证据 / 冷却 / 每日上限），冷却与配额记在
  `<工作区>/.openloop/loop/auto.json`（`day`、`count`、`at`、`runs`）；
- 护栏：同一工作区演进在跑就不启动、没有默认模型不启动、没有考核题不启动、并发 drain 用集合去重。

实测：

- `settings.json` 开 `autoEvolve`、`runs=1`、`cooldown=60`：跑一轮 Worker（auto1）后**没有发任何 POST**，
  `/api/loop/evolve` 自己变成 `running / proposing`，`auto.json` 落 `{count:1, runs:6}`；
  这一轮最后 `error: 模型接口返回 429`（免费额度到顶）——触发链是对的，模型调用没成功；
- 再跑一轮（auto2）：I 日志 `自动演进暂缓：冷却中，11:06 后再试`，`auto.json` 不变；
- 换到没有考核题的工作区（`/root/uiwork`）：日志 `自动演进跳过：没有考核题`，Loop 仍是 `idle`；
- `AutoEvolveTests` 5 条覆盖开关、证据不足、冷却、跨日重置、上限为 0。

未做 / 已知：

- 没跑出一次**成功**的自动轮（撞 429），成功轮的证据仍来自手动触发那几次；
- 自动轮用 `settings.model` 的默认模型，没有「只许免费档」的硬限制；WinForms 也还没有开关，
  要开就编辑 `settings.json`（默认关就是怕不打招呼地花钱）。

**2026-11-02 · M5（插件清单 / stdio 宿主 / 贡献注册 / dsh spike）**

已落地：

- 清单：`plugin.json` 带 `id`、`entry {command,args,timeoutMs}`、
  `contributions {tools[], providers[]}`、`permissions`（声明式，不强制）；
- 发现：`<工作区>/.openloop/plugins/<id>/` 与 `%APPDATA%\OpenLoop\plugins\<id>\`
  （Linux 同目录规则），工作区同名优先，清单无效的目录跳过并记日志；
- 宿主（`PluginHost`）：一个插件一个子进程，stdin/stdout 一行 JSON，
  `method=tool.call`，应答 `result {ok,data,error}` 或 JSON-RPC `error`；
  stderr 转日志；超时（`entry.timeoutMs`，默认 60s）杀进程；
- 崩溃隔离：失败后**不重放本次调用**（工具可能有副作用），只丢弃进程，
  下一次调用重新拉起；整个宿主生命周期最多重启一次，再失败就标死，
  后续调用直接返回失败，代理循环与其他插件照常；
- 工具注入：Worker 每次运行前加载工作区 + 全局插件，把贡献的工具接到
  `ToolRegistry.Workspace` 的 `extra`；运行结束 Dispose 收摊；
- 提供商注入：`Reasoners.Create` 先查插件里声明的模型（`models[]`），
  命中就用该 `baseUrl` + `keyEnv` 环境变量，走现成的 OpenAI 兼容通道，零代码；
- 示例：`samples/plugins/echo/`（Node，1 个 `Echo` 工具 + 1 个 OpenAI 兼容 provider 占位，
  含安装说明与协议）；`__crash__` / `__sleep__` 两个特殊输入用于演练崩溃与超时。

验证（`OpenLoop.Service.Tests` 62 通过，新增 14 条）：

- 清单：空目录、工作区/全局发现、同名工作区优先、无效清单跳过、提供商大小写不敏感；
- 宿主：正常回包、`ok=false`、非 JSON 回包、崩溃后下一次换新进程、两次失败标死、
  用户取消不被吞；重启**不重放**同一次调用；
- 真进程：`samples/plugins/echo` 经 Node 跑通 `hi → hi`；`__crash__` 失败后下一次
  `alive → alive`（进程已换）；`timeoutMs=300` 时 `__sleep__` 约 0.3s 返回失败；
- 提供商真链路：本机回环假 OpenAI 端点 + 清单 provider → `Reasoners.Create`
  → `CompleteAsync` 得到 `pong`，请求体带 `stub-model`、`Authorization: Bearer sk-test`；
- `dotnet build OpenLoop.slnx` 0 错误；`Core.Tests` 181 通过 / 1 跳过。

dsh spike 结论（实测）：

- `dsh-our-free-model` standalone 可无界面启动：
  `node packages/standalone/cli.mjs --port 18999 --data-dir <临时目录>`，
  启动日志打印 `API: http://127.0.0.1:18999/v1`，并把 `forwardKey` 写进
  `<data-dir>/settings.json`；
- `/v1/models` 不带 Bearer 返回 401，带 `Authorization: Bearer <forwardKey>`
  返回 OpenAI 形状的模型列表（含 `context_window`），说明「provider 贡献指向本机
  OpenAI 兼容网关」这条路成立，零代码；
- `/v1/chat/completions`（`big-pickle`，短提示）返回 `RATE_LIMIT`
  「Free usage limit reached」，与 OpenRouter 免费额度是同一堵墙：真实补全未验到；
- Cordis 的 `ctx.llm` / `ctx.tools` 动态注册**没做桥接**：当前清单是静态贡献，
  只有 `tool.call` 一个方法；要桥接需要新增握手（如 `tools.list`）和 Node 桥进程，
  收益不足以在本轮做。结论：优先「OpenAI 端点即 provider」，tool 桥留到有真实插件需求时。

未做 / 已知：

- 插件工具只进 Worker 运行；Loop 构思与 Gate 考核仍只用内置工具（避免新插件影响判分）；
- 插件模型的 id 不进模型下拉，只在按 id 解析时生效；要出现在下拉里得再接到 `ModelCatalog`；
- `permissions` 只是声明，插件是本机进程，没有沙箱/资源限额；只应安装可信插件；
- 插件进程的生命周期是「一次 Worker 运行」，跨轮次不保温；需要常驻时再改成按工作区缓存；
- 现在的超时是整次调用 60s 上限，长任务插件需要自己把工作拆成多次调用。
