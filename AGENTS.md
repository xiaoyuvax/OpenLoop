# OpenLoop 开发约定

> 通用规则（编码、PS/cmd 陷阱、提交纪律、dry-run、文档只写已验证等）见全局 `..\AGENTS.md`；本文件只记本项目特有约定。

## 定位

- OpenLoop 是一个自演进 agent 设施：**Worker（前台）** 面向用户干活，**Loop（后台观察者）** 观察、收集、迭代版本。
- Worker 与 Loop 各自是独立进程（A / I，同一个壳拉起与收摊），活动周期不同：Worker 跟任务走，Loop 由事件驱动（可自主启动、跑到结束条件）。
- **每次只迭代一个角色**：迭代 Worker 时 Loop 不变，反之亦然；两者二进制可以分别替换（`versions/<role>/`），但协议号必须对齐，替换只能走「登记 → 交接 → 重启 → 回滚」。
- **Worker 与 Loop 的 harness（agent 中性的控制逻辑）及壳 UI 的基本功能始终保持一致对齐**：只有角色/功能区分造成的合理差异可以例外；改一侧时同步检查另一侧。
- **Web 壳（`src/OpenLoop.Service/wwwroot`）与 WinForms 壳（`src/OpenLoop.App`）同样要保持对齐**：同一功能在两侧都要有，Web 的布局与交互尽量按 WinForms 的布局（左项目/会话、中对话、右详情/预览、顶部状态与模型）还原，不要各写一套信息架构。
- 版本定义（提示词/技能/策略/模型参数）与二进制分开管理；`.openloop/` 存运行态。

## 构建与验证

- 构建：`dotnet build OpenLoop.slnx`
- Core 测试：`dotnet test tests\OpenLoop.Core.Tests`
- ChatFlow 测试：`dotnet test ..\..\..\ChatFlow\ChatFlow.Tests`（跨仓库引用）
- 无独立 lint；Release 编译即类型检查。
- 不要用 `dotnet build -t:Compile` 做语法检查（跳过资源生成，之后的增量构建会复用坏产物）；若用过必须 `dotnet build --no-incremental` 重建。
- 运行前先结束旧进程（`OpenLoop.exe` 文件锁）：`Stop-Process -Name OpenLoop -Force`
- 启动：`Start-Process <bin>\OpenLoop.exe -WorkingDirectory <repo根>`（默认工作区 = CWD）。Puppet 默认在 loopback 上启用，key 自动落盘，见下节。
- 启动 OpenLoop 后确认 `D:\PUB\ConnMan\connman.exe` 已在跑（VPN 节点自动切换靠它，免费额度自愈也依赖它）：没有就 `Start-Process 'D:\PUB\ConnMan\connman.exe'`。

## 运行时验证（Puppet A/B 面）

- **A 面（`/agent/*`）**：开发期全反射调试，key 见下；**B 面（`/appagent/*`）**：用户 Agent 的具名 Action 通道，凭证在框架写的发现档案 `%LOCALAPPDATA%\Puppet.AppAgents\OpenLoop.<pid>.json`（`probe`/`help` 免凭证，其余带 `Authorization: Bearer <key>`）。两面调的是同一批公开方法，结果同源；区别只在 A 用位置参数、B 用按名参数（未知/缺必填直接 400）。
- **壳的操作面按 Action 范式写**：`public` 实例方法返回 `OperationResult`/`bool`（= 成败可观察）即自动进 B 的 manifest；`string`/`string[]` 查询方法只属于 A/内部，不要为了 B 去改它们的返回形状。新增操作时同步考虑两面，别写两份。
- 常用 B 动作：`OpenSession` → `SetComposerText` → `Send`（空闲=发送 / 运行中=排队 / 运行中且输入框为空=停止）；`StopSession` 硬停当前轮、`InterruptSession` 软打断（下一 step 前收尾）；`EnqueuePending`/`SendPendingNow`/`RetractPending`/`RemovePending` 管排队；`OpenLoop`/`SendLoop`/`StopLoop` 管后台观察者；`DeleteSessions`/`RemoveProject` 是不可恢复的 High 风险动作。
- **B 面按实例强制串行**（并发 409 `instance-busy`），读类端点（manifest/state）不加锁；B 也认 `agentId`/`site` 协商参数。
- **窗口几何两套动作，单位不同**：`MoveAction`/`ResizeAction`/`SetBoundsAction` 收**物理像素**（WinForms `Bounds`）；DIP 版本由 Puppet 框架统一提供（`MoveDipAction`/`ResizeDipAction`/`SetBoundsDipAction`，x/y 相对目标屏工作区左上角，`screen` 选屏）。跨屏 DPI 不同时必须用 DIP 版本；各屏缩放比看 `Screens` 状态里的 `scale=`。窗体最小尺寸会钳制宽高（返回里会说明），核对用 `WindowBounds`（物理矩形 + 屏 + dpi）。
- **进程是 `SystemAware`**：窗口矩形能按目标屏换算正确，但内容仍按系统 DPI 渲染，跨到别的 DPI 屏会由 DWM 呈缩放画面（偏大/模糊都在这一层）。要彻底对齐得改 `ApplicationHighDpiMode=PerMonitorV2`，会动全局布局，需单独验证。
- **只用 Puppet 调试**：禁止 UIAutomation、`SendKeys`、`keybd_event`、模拟鼠标/拖动、系统级截屏——会抢焦点、误触菜单。要画面用 `CaptureAction`（应用内截图，不抢输入）；要状态用 `/agent/get`、`/agent/state`、`/appagent/state/*`、`/agent/logs`。Puppet 缺能力时在宿主加公开方法/属性，不要用 UI 自动化绕过。
- **key 落在共享配置文件里**：`%APPDATA%\OpenLoop\puppet.key`（用户目录，不进仓库）。启动时优先取环境变量 `OPENLOOP_PUPPET_KEY`（并同步写回该文件），否则读文件，都没有就生成一个；**其他 agent 直接读这个文件**拿 key，不必再向用户要、也不必注入环境变量。
- 端口 9090 常被 VeniceMan 占用，会自动落到 9091+；端口用 `zcurl registry` 逐个探测确认。
- 调用时用 `export ZCURL_KEY=$(cat "$APPDATA/OpenLoop/puppet.key")`（或 `--key`）传入。**不要**把 key 复制进工作区、文档或提交。
- **多 agent 并发共用同一个 Puppet（常态）——用 Puppet 自带协调机制协商，契约以 `GET /agent/docs` 为准**：
  1. **报身份**：每个请求带 `agentId=<名字>&agentPurpose=<目的>&agentOp=<在干什么>&agentWaitEst=<预计秒>`（query 参数）；最后一次请求带 `agentDone=true`，或 `POST /agent/book/release` 退场。心跳 5 分钟超时只清会话，**不会自动放锁**。
  2. **看同伴**：响应自动附 `peers`/`peersHint`；详情 `GET /agent/book`（会话 + `helpLog`）、`GET /agent/lock/status?site=*`。有同伴在跑就不动它的状态（`SetModel`/`Send`/`OpenSession`/`SubmitNewSession`），不拉窗口、不抢焦点。
  3. **写前先锁**：`POST /agent/lock/acquire {site, mode:"Read|Write", timeoutMs?, agentId?}`，用完显式 `/agent/lock/release`；读操作不锁（如 `get`/`state`/`logs`/`CaptureAction`）。
  4. **求助**：任意请求加 `agentHelp=<需求>`（定向 `agentHelpTo=<agentId>`）。
  5. **换 key 是安全的**：`POST /agent/key/refresh` 刷新全局密钥，同伴能读到新 key；**真正危险的是重启进程**（会砸掉同伴正在跑的任务）——重启前先 `GET /agent/book` 确认没有同伴，或先用 `agentHelp` 打招呼。
- **A/I 自己的 Puppet**：A（19180）/I（19181）在 bus 端口上同时挂 `/agent/*`；内部 Agent 用 `Puppet` 工具 `target=self` 直调进程内 Hub（`ProcessWebRequest`，不经端口/网络），对端用 `target=a|i` 走 Puppet 接口。壳的地址写在 `%APPDATA%\OpenLoop\puppet.endpoint`，`target=shell` 探测。Web 页只是半个 Puppet（实例 `WebUi`：页面状态 + 截图钩子，截图需页面勾选）。
- **启动后先验 Puppet 再干活**：探测 9090-9099 找监听端口，用 `%APPDATA%\OpenLoop\puppet.key` 里的 key 调 `zcurl registry`（GET /agent/registry），返回 200 才算可用；没有监听端口说明启动失败，看 `/agent/logs`（`?name=OpenLoop`）或进程日志，不要靠界面肉眼判断。
- `POST /agent/invoke?instance=MainForm&method=<名>`，body 为参数数组；路径参数用正斜杠（反斜杠会被 JSON 转义吞掉）。
- `GET /agent/get?instance=MainForm&path=<属性>`；`POST /agent/set` body 为 `{"path":..,"value":..}`。
- 复杂对象判空看 `type` 不看 `value`：`value:null` + 真实 `type` = 有值；`type:null` 才是空。
- `POST /agent/invoke ... method=CaptureAction` body `[x,y,w,h]`（相对元素），返回 base64 PNG（用于看真实渲染）。
- AntdUI 等自绘控件点不到 → 统一走 `POST /agent/invoke ... method=Test`，body `["动词", 控件, 参数2]`（实现 `TestSurface`，`[PuppetTest]` 标记）：`tree` 列控件（路径|类型|名字|文本|坐标|可见可用）；`click`（按钮 / 下拉框 / Dropdown / 勾选框 / 订阅了 Click 的标签）；`settext`；`select`；`menu`；`key`（只发 Enter/Escape/Tab/方向键这类命名键）；`wheel`；`clicknowait`；`close`。控件先按 `Name`、再按可见文本找。
- 开模态窗口的按钮必须用 `clicknowait`（排队后立刻返回），轮询 `tree` 等到窗口出现，操作完用 `close`；直接 `click` 会一直阻塞到窗口关闭。
- 下拉列表 / 右键菜单等弹层不在控件树里：只能验证「展开了 / 选中了什么」，别指望 `tree` / `CaptureAction` 看到弹层内容。
- 同类不兼容控件的新动作加到 `TestSurface`，不要再往 MainForm 堆测试方法；整体移除 = 删 `PuppetTestSurface.cs` + MainForm 里 `Test` 字段与方法。
- 不要把测试面注册成第二个 Puppet 实例：那会让所有 `/agent/*` 调用必须带 `?name=`，已有脚本全失效。
- 截图前若窗口被最小化，先 `ShowWindow(hwnd, 9)`。
- **远程 API 测试须先经用户同意**：会调用真实模型/网关（产生费用）的操作，未经同意不得通过 UI、Puppet 或单测触发；纯本地测试（纯函数、控件状态、文件解析）不受限。
- **测试会话要保留**：用 Puppet 造的会话、项目和临时工作区一律不删，留给用户自己查看；需要清理时先问。

## 免费额度自愈（ConnMan）

- 免费模型额度耗尽（`provider.free-limit` / 429 / 402，判据 `ServerNotice.IsQuotaFailure`）后，A 的失败轮收尾会调 `FreeLimitRecovery.TryStart`（`src\OpenLoop.Service\Worker\FreeLimitRecovery.cs`）：换时速网络节点 + 验证可用性这件事交给独立仓库 `..\ConnMan`，OpenLoop 只负责触发和收尾。
- 契约（A 面，参数名是 `name` 不是 `instance`）：`POST /agent/invoke?name=ConnMan&method=StartRecoveryAction` body `[sessionId, model]` → 轮询 `GET /agent/get?name=ConnMan&path=RecoveryState`（`idle|running|ready|failed|exhausted`）→ `ready` 时给原会话 `Send(id, "task", "继续")`；`exhausted` = 本轮节点已挪满一圈，收工不重试。
- 端点与 key 取 `%APPDATA%\ConnMan\puppet.endpoint` / `puppet.key`（ConnMan 启动写、退出清空）；文件不在或连不上都退避重试（ConnMan 可能在更新/重启），这是可选自愈。
- 只对 `opencode/*` 模型生效；每个会话独立重试：前 3 次间隔 5 秒，之后指数退避封顶 5 分钟，直到用户动了该会话（`Send`/`Stop`/`SetModel` 等会加 `Session.UserEpoch`）。并发由 ConnMan 自己拒绝（`StartRecoveryAction` 回「已有恢复在跑」）。
- 环回 HTTP 必须 `new HttpClientHandler { UseProxy = false }`：VPN 开着时系统代理就是 mihomo 自己。
- 壳在运行时整解构建会被文件锁卡住（MSB3021/3027），验证改动只需 `dotnet build src\OpenLoop.Service` + `src\OpenLoop.A`；要换 A 的二进制得先关壳（会中断用户会话，先问）。

## 连接失败重试

- 轮内连不上本地 opencode/代理（拒绝连接、DNS 失败，判据 `OpenCodeReasoner.IsConnectFailure`）不判这一轮失败：按 `RetryBackoff.Next`（前三次 5 秒，之后指数退避封顶 5 分钟）重试到连上为止，界面状态显示「连接失败，N 秒后重试」；`Stop`/软打断才收尾，HTTP 状态错误与上游报错照旧直接抛。

## UI 规则

- Worker（`WorkerSessionHost`）与 Loop、旁路的会话展示保持一致：流式正文先过滤工具协议原文（统一用 `AgentToolCallParser.LooksLikeToolCallText`），不要各写一份判据。
- 仓库采用 AntdUI（2.4.15）；`AntdUI.xml` 是 API 事实来源，不猜属性。
- 本机 250% DPI：
  - 设计器里的尺寸会自动缩放；**运行时算出来的像素必须 `DeviceDpi/96` 缩放**。
  - `AntdUI.Column.Width`（字符串数字）是 **DIP**，由控件自己再乘 DPI；不要再手动缩放。
  - `SplitContainer.SplitterDistance`、`TabControl.ItemSize` 不自动缩放。
- 字体以“点”为单位，像素盒必须一起缩放，否则 250% 下文字被裁切（ChatFlow 即因此修复）。
- `AntdUI.Menu` 竖向模式的 `SubText` 会被裁切 → 项目列表用单行 `Text`，路径另放标签。
- `AntdUI.Label` 带 `PrefixSvg` 时默认 `IconGap=0`，会与文字重叠，显式设 `IconGap`。
- 大量模型（399 个）时 `Select` 只放过滤后的列表；免费模型用绿点 + “免费”副标题标识。
- 新增 `ChatFlowControl` 必须在 `InitializeComponent()` 后、`Bind` 前显式设 `ChatFont`——它不是 `Control.Font`，字体遍历/`Font` 赋值不会自动覆盖。
- 新控件的颜色统一从 `UiStyle` 取，不写死 `Color.X`；自绘控件跟随主题。

## 工程规则

- **读写分离**：只读方法不得写盘/建目录（判据：让它返回空，程序还能不能跑——能跑就不该“确保存在”）；需要“确保存在”的由显式写入口（界面/落盘）承担。
- 请求体构建抽成 `internal` 纯函数并单测，不在 async 方法里内联拼字典；可空字段条件写入（`JsonIgnoreCondition.WhenWritingNull` 不作用于 `Dictionary` 的值）。
- WinForms 菜单不要在 `Closed` 里同步 `Dispose`：`ModalMenuFilter` 关闭流程还会访问句柄，会抛 `ObjectDisposedException` 崩进程；用 `BeginInvoke` 延后销毁。

## 设计器兼容（强制）

- 每个 Form/UserControl 必须有 `.cs` + `.Designer.cs`；字段声明、创建、属性设置、`Controls.Add` 全在 `InitializeComponent` 内。
- 构造函数只调用 `InitializeComponent()`；运行时初始化走 `Initialize(...)`。

## AGENTS.md 与提示词

- `<workspace>/AGENTS.md` 会在每次任务开始时作为系统提示词的“项目约定”段落注入（`AgentsInstructions.Load`，上限 32KB）。
- 本文件即 OpenLoop 自身的项目约定；改动会影响 agent 行为，保持短、可执行。
