# OpenLoop 开发约定

> 通用规则（编码、PS/cmd 陷阱、提交纪律、dry-run、文档只写已验证等）见全局 `..\AGENTS.md`；本文件只记本项目特有约定。

## 定位

- OpenLoop 是一个自演进 agent 设施：**Worker（前台）** 面向用户干活，**Loop（后台）** 是 OpenLoop 自己的 Worker——开发与维护它的源码、编排二进制迭代，并观察 Worker 与用户判定来演进工作区 Harness 参数。
- Worker 与 Loop 各自是独立进程（A / I，同一个壳拉起与收摊），活动周期不同：Worker 跟任务走，Loop 由事件驱动（**演进**由 DrainSpool 攒够新证据后自动启动、跑到结束条件；**观察/开发轮**仍由人工发起——用户消息、/complain、转交或 Puppet，无人触发时 Loop 不自己开观察轮）。
- Loop 的工区 = 部署态（安装根：`src/` 克隆、`backends/`、`versions/`）+ 开发态（源码仓库与同级组件），两种布局等效、路径由 `SourceSync` 解析；源码有完全管理权，二进制只读（只经迭代/换版流程改）。
- **每次只迭代一个角色**：迭代 Worker 时 Loop 不变，反之亦然；两者二进制可以分别替换（`versions/<role>/`），但协议号必须对齐，替换只能走「登记 → 交接 → 重启 → 回滚」。
- **Worker 与 Loop 的 harness（agent 中性的控制逻辑）及壳 UI 的基本功能始终保持一致对齐**：只有角色/功能区分造成的合理差异可以例外；改一侧时同步检查另一侧。
- **Worker 与 Loop 的会话窗口按同一套界面操作体验实现**：输入框与排队队列（Enter/Ctrl+Enter/Shift+Enter 语义、「立即/等本轮」、编辑/删除、Stop 后暂停续发）、停止/打断、附件、模型选择、右侧预览等操作，两侧行为、提示文案与状态复位必须一致；允许的差异只有角色本身造成的部分——刻意的视觉识别设计，以及工具/能力范围（如 Loop 的迭代、仓库动作）不同。改一侧的交互语义时，另一侧同轮对齐（含 Web 壳同一功能的对应文案）。
  - 键位语义（两侧、含 Web 壳与提示文案同一套）：Enter=本步后发送（运行中软打断本轮，收尾后先发这一条）、Ctrl+Enter=本轮后发送、Shift+Enter=软换行。
  - 「立即/等本轮」必须可双向切换：切回「等本轮」、删除该条或进入编辑都要收回打断（A 侧发 `Interrupt(on:false)`，Loop 侧 `_interrupt` 跟随 `AnyImmediate`）；打断标记留在原地会让整批排队消息在步后提前发出。
  - 失败轮（`执行失败：…`、额度耗尽等错误中断）不续发队列：步后与轮后都不发，队列留在原地；等新的输入（用户发送，或自愈发的「继续」）激活会话后，才按原规则继续处理队列。
- **Web 壳（`src/OpenLoop.Service/wwwroot`）与 WinForms 壳（`src/OpenLoop.App`）同样要保持对齐**：同一功能在两侧都要有，Web 的布局与交互尽量按 WinForms 的布局（左项目/会话、中对话、右详情/预览、顶部状态与模型）还原，不要各写一套信息架构。
- **Harness 是工作区级的五类对象**：**Prompt**（系统提示词模板 + `AGENTS.md`）、**Skill**、**Rule**、**Limit**、**Guard**；Limit/Guard 含自愈参数（连接重试、免费额度恢复、退化输出、参数修复）。激活集与二进制分开管理（`.openloop/` 存运行态），同一二进制在不同工作区可以是不同形态。
- **演进 = 非即时集中分析**：findings 与运行证据先攒着，到点集中分诊一次，判 `harness / source / unknown`；harness 类出激活集候选并过 Gate，source 类写 `loop/dispatch/<batch>.md` 派单，状态记 `loop/triage.jsonl`（追加式，按 id 取最后一条）。`/report` 只落账、不触发演进。
- **findings 只记「尚未处置、且能分派到某个对象」的观察**（当场修好的写进回复/提交）；用户入口是 `/report [harness|prompt|skill|rule|limit|guard|code] <观察>`，WinForms 与 Web 共用 `FindingTriage.Report`。
- **TargetCentricSession**：会话可绑定一个唯一编辑目标（系统提示词模板 `prompts/system.md` 与 `AGENTS.md`；以后可扩到 SVG/网页等）。实现是 `TargetPane` + `TargetSpec`（Kind/Title/Path/Actual），左栏呈现、文件 watcher 驱动刷新、可 undock 成独立窗体；用户用聊天让模型改、模型落盘，用户不直接改文本。加新目标类型 = 新 Kind + 呈现控件 + Bind，不改会话主流程。入口 `/sysprompt <要求>`、`/agentsmd <要求>`（`/agents` 同义），Worker 与 Loop 窗口同一套语义，**只改本会话工作区**的 `.openloop/prompts/system.md` 与 `AGENTS.md`（工作区 system 模板替换内置默认；工作区 AGENTS.md 追加在全局约定之后），不碰 OpenLoop 全局提示词/AGENTS.md。带要求时由壳新建或复用**本会话的旁路会话**执行：先把对应 TCF 编辑器打开，再把要求发给它；编辑只在内存，用户点保存才落盘，可多轮修订。同一源会话的修改请求始终复用同一条旁路会话（`SessionLineage.Find(sourceId, target-edit)`）。不带要求只在本会话打开编辑器。系统提示词模板按内容哈希登记为账本 `kind=prompt` 特征，未登记的内容不采用。
- **会话衍生关系**：`/complain`、`/fork`、`/new`、转交、协作、目标编辑旁路创建的会话都经 `SessionLineage`（`%APPDATA%\OpenLoop\chats\lineage.json`）登记「衍生会话 → 源会话」一条边（Id / Role / Workspace / SourceId / SourceRole / SourceWorkspace / Purpose / Target），按衍生会话 id upsert；`Get`/`Find`/`Derived` 供回溯与复用。新增衍生关系只加 `Purpose` 常量，不改读写路径。
- **TargetCentricFacility（TCF）**：左栏编辑器这类工具的统称——面向用户的文本多轮可视化编辑交互，对同一数据做多轮雕塑性修改。性质类似内建 Tool 但**不常设**：内建于壳、只有壳支持才启用、不进提示词与常设工具表——**未启用时 agent 不知道它存在**；agent 的认知来自宿主临时插入的消息（文件路径 + 属性/接口指南）。agent 经 Puppet A/B 面的专用属性（长度/行数/脏标志/保存路径等）与 Action（区段读写、替换、插入、删除、尾部追加、撤回、保存）操作编辑器本体；编辑只在壳内存、实时可见，用户确认前不落盘；关闭=隐藏（内容驻留内存），有未保存内容时通知 agent 询问用户保存或放弃。保存路径放 tooltip，保存状态用图标+色彩复用保存按钮，工具栏按钮一律图标化（省多语种文案）。一次性可视化交互建议走 openui 技能（LoopApp）；TCF 未启用时 LoopApp 也可承担多轮雕塑性任务，启用 TCF 后才由它接管。设计见 `docs/DESIGN-TCF.md`。
- **Harness 两种生效方式**（`HarnessMode`，`loop/harness.json` 存 mode，Harness 页与 Web 同一颗切换按钮）：`direct` = 用户直配，Prompt / `AGENTS.md` 改了下个会话直接生效；`eval` = A/B 评测，改动先落候选包 `.openloop/eval/candidates/pending/`（编辑面换成候选文件，左栏标题带「（候选）」），演进时叠到候选激活集上过考核题，严格更好才 `CommitPending` 覆盖正式文件。候选未采纳前正式文件与当前激活集都不变。

## 构建与验证

- 构建：`dotnet build OpenLoop.slnx`
- Core 测试：`dotnet test tests\OpenLoop.Core.Tests`
- ChatFlow 测试：`dotnet test ..\ChatFlow\ChatFlow.Tests`（跨仓库引用）。同级组件检出在 `..\<组件>`（= `src\<组件>`），不是 `D:\PUB\<组件>`；改了组件源码（`ProjectReference` 走源码）要一起编消费方（`src\OpenLoop.App`/`src\OpenLoop.Service`），只编组件不算验证接口。
- ChatFlow 多目标框架（net7~net10）：构建不要带 `-f`，否则 `net7.0` 的 `#if` 分支漏编。
- `dotnet build`/`dotnet test` 的 stdout 在中文 Windows 是本地码页（工具里看是乱码）：判成败只看退出码。
- Web 壳前端改完跑 `node --check src\OpenLoop.Service\wwwroot\app.js`。
- 无独立 lint；Release 编译即类型检查。
- 不要用 `dotnet build -t:Compile` 做语法检查（跳过资源生成，之后的增量构建会复用坏产物）；若用过必须 `dotnet build --no-incremental` 重建。
- 运行前先结束旧进程（`OpenLoop.exe` 文件锁）：`Stop-Process -Name OpenLoop -Force`
- 启动：`Start-Process <bin>\OpenLoop.exe -WorkingDirectory <repo根>`（默认工作区 = CWD）。Puppet 默认在 loopback 上启用，key 自动落盘，见下节。
- 启动 OpenLoop 后确认 `D:\PUB\ConnMan\connman.exe` 已在跑（VPN 节点自动切换靠它，免费额度自愈也依赖它）：没有就 `Start-Process 'D:\PUB\ConnMan\connman.exe'`。

## 运行时验证（Puppet A/B 面）

- **A 面（`/agent/*`）**：开发期全反射调试，key 见下；**B 面（`/appagent/*`）**：用户 Agent 的具名 Action 通道，凭证在框架写的发现档案 `%LOCALAPPDATA%\Puppet.AppAgents\OpenLoop.<pid>.json`（`probe`/`help` 免凭证，其余带 `Authorization: Bearer <key>`）。两面调的是同一批公开方法，结果同源；区别只在 A 用位置参数、B 用按名参数（未知/缺必填直接 400）。
- **壳的操作面按 Action 范式写**：`public` 实例方法返回 `OperationResult`/`bool`（= 成败可观察）即自动进 B 的 manifest；`string`/`string[]` 查询方法只属于 A/内部，不要为了 B 去改它们的返回形状。新增操作时同步考虑两面，别写两份。
- **壳重启只走 `RestartShell` 动作**（`Puppet` target=shell）：它先起交接 helper、确认起来才退出；helper 等旧壳退出再拉起新壳，进到界面才算成功，失败重试 3 次；helper 没起来就留在当前壳。默认重启同一份二进制；`from` 给一个含 `OpenLoop.exe` 的目录则换到那一份（覆盖前备份、起不来回滚）——壳调试就按「编译/发布到临时目录 → RestartShell(from=目录)」走。**禁止用 Shell 杀进程/关窗口来「重启」**——那样壳直接消失、没人接手（2026-11-24 事故）。**只换壳，不动 A/I**：旧壳退出前写 `runtime/shell-handoff.json`（`ShellHandoff.Begin`），`Supervisor.StopAll` 见到本壳的交接标记就不收 A/I；新壳启动先 `ShellHandoff.PublishShell` 发布自己的 pid，A/I 的 `ParentWatch` 改守护它、新壳 `Ensure` 接手，正在跑的 Worker 会话与标签页按落盘状态接回。只有壳自己进程里的 Loop 轮 / Worker 旁路会被中断，它们在跑时动作会拒绝（`BusyInShell`）。
- 常用 B 动作：`OpenSession` → `SetComposerText` → `Send`（空闲=发送 / 运行中=立即发送（软打断本轮，收尾后先发；`immediate:false` 等本轮返回）/ 运行中且输入框为空=停止）；`StopSession` 硬停当前轮、`InterruptSession` 软打断（下一 step 前收尾）；`EnqueuePending`/`SendPendingNow`/`RetractPending`/`RemovePending` 管排队；`OpenLoop`/`SendLoop`（同样收 `immediate`）/`StopLoop` 管 Loop 会话；`DeleteSessions`/`RemoveProject` 是不可恢复的 High 风险动作。
- **右侧栏「网页」页是内嵌 WebView2 浏览器**（`BrowserView`）：**按需创建**——侧栏默认没有这一页，只有 Agent 调浏览器动作时才插入并选中（用户点不出空 WebView，避免白占内存），右键页签可「关闭网页」销毁实例。默认锁定，只有 Agent 能操作；`OpenBrowser(url)`/`NavigateBrowser(url)`/`EvalBrowserScript(js)`/`BrowserScreenshot()` 驱动它（后三个在没有该页时也会自动打开），`SetBrowserInteract(true)` 解锁给用户点按，`UndockBrowser`/`DockBrowser` 在独立窗口与侧栏之间搬。状态看 `BrowserState`/`BrowserUrl`。`EvalBrowserScript` 返回 WebView2 的 JSON 结果（字符串结果带引号）。**`BrowserScreenshot` 需要「网页」页已激活**，否则 `CapturePreviewAsync` 失败且只报「截图超时」；`NavigateBrowser` 异步返回（`data` 是导航前的旧 URL），取页面内容前先轮询 `EvalBrowserScript("document.readyState")`，并检查 `location.href` 不是 `chrome-error://`（DNS/网络失败时 `NavigateBrowser` 仍回成功）；`BrowserScreenshot` 的 PNG 走 `asset` 链接（`/appagent/assets/<id>`）下载。侧栏最窄 360 DIP；再窄时工具栏按「系统浏览器 → 刷新 → 前进 → 独立窗口」收起按钮，保住地址栏。
- **LoopApp 的呈现方式与收尾**：右侧栏是竖版窄栏，只适合小表单/单列列表。`LoopApp(open)` 后壳在导航完成时量一次 DOM（元素数、表格列数、宽块、页高、横向溢出），内容多就自动搬进独立窗口，少就留在侧栏（判定与阈值在 `LoopAppLayout`，纯函数有单测）。`app.json` 的 `window: dock|float` 与 `size: [w,h]` 优先于实测；用户自己关掉独立窗口后本次不再自动浮出。任务做完（页面 `ui.submit` 过、本轮收尾）自动收起：隐藏页面 → 关独立窗口 → 摘掉「网页」标签，实例留着；收起且闲置超过账本 `limit.browser_idle` 分钟（默认 30，按工作区可配可演化）才销毁 WebView2。`LoopApp(close)` 可手动走同一条路。
- **B 面按实例强制串行**（并发 409 `instance-busy`），读类端点（manifest/state）不加锁；B 也认 `agentId`/`site` 协商参数。
- **窗口几何两套动作，单位不同**：`MoveAction`/`ResizeAction`/`SetBoundsAction` 收**物理像素**（WinForms `Bounds`）；DIP 版本由 Puppet 框架统一提供（`MoveDipAction`/`ResizeDipAction`/`SetBoundsDipAction`，x/y 相对目标屏工作区左上角，`screen` 选屏）。跨屏 DPI 不同时必须用 DIP 版本；各屏缩放比看 `Screens` 状态里的 `scale=`。窗体最小尺寸会钳制宽高（返回里会说明），核对用 `WindowBounds`（物理矩形 + 屏 + dpi）。
- **进程是 `SystemAware`**：窗口矩形能按目标屏换算正确，但内容仍按系统 DPI 渲染，跨到别的 DPI 屏会由 DWM 呈缩放画面（偏大/模糊都在这一层）。要彻底对齐得改 `ApplicationHighDpiMode=PerMonitorV2`，会动全局布局，需单独验证。
- **只用 Puppet 调试**：禁止 UIAutomation、`SendKeys`、`keybd_event`、模拟鼠标/拖动、系统级截屏——会抢焦点、误触菜单。要画面用 `CaptureAction`（应用内截图，不抢输入）；要状态用 `/agent/get`、`/agent/state`、`/appagent/state/*`、`/agent/logs`。Puppet 缺能力时在宿主加公开方法/属性，不要用 UI 自动化绕过。
- **key 落在共享配置文件里**：`%APPDATA%\OpenLoop\puppet.key`（用户目录，不进仓库）。启动时优先取环境变量 `OPENLOOP_PUPPET_KEY`（并同步写回该文件），否则读文件，都没有就生成一个；**其他 agent 直接读这个文件**拿 key，不必再向用户要、也不必注入环境变量。
- **启动后先验 Puppet 再干活**：9090 常被 VeniceMan 占用、会自动后移到 9091+，逐个探测 9090-9099 找监听端口，用 `%APPDATA%\OpenLoop\puppet.key` 里的 key 调 `zcurl registry`（GET /agent/registry），返回 200 才算可用；没有监听端口说明启动失败，看 `/agent/logs`（`?name=OpenLoop`）或进程日志，不要靠界面肉眼判断。
- 调用时用 `export ZCURL_KEY=$(cat "$APPDATA/OpenLoop/puppet.key")`（或 `--key`）传入。**不要**把 key 复制进工作区、文档或提交。
- **多 agent 并发共用同一个 Puppet（常态）**：Agent 侧直接用 `Puppet` 工具——宿主已代报身份、响应自动附 `peers`/`chat`，`say` 带 `to` 即定向投递，不要手写身份参数。只有裸 zcurl 脚本才按 `GET /agent/docs` 带 `agentId`/`agentPurpose`/`agentOp`/`agentWaitEst` 与 `agentDone`；写操作先 `POST /agent/lock/acquire` 再显式 release，读操作不锁（`get`/`state`/`logs`/`CaptureAction`）。换 key 安全（同伴能读到新 key），危险的是重启进程——先 `GET /agent/book` 确认没有同伴。
- **A/I 自己的 Puppet**：A（19180）/I（19181）在 bus 端口上同时挂 `/agent/*`；内部 Agent 用 `Puppet` 工具 `target=self` 直调进程内 Hub（`ProcessWebRequest`，不经端口/网络），对端用 `target=a|i` 走 Puppet 接口。壳的地址写在 `%APPDATA%\OpenLoop\puppet.endpoint`，`target=shell` 探测。Web 页只是半个 Puppet（实例 `WebUi`：页面状态 + 截图钩子，截图需页面勾选）。
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

## 协作会话（发起方 / 协作）

- 设置页「协作」打开后，Worker 与 Loop 会话都多三个工具：`CollabStart`（分解任务并起若干**同工作区、平级**的协作会话）、`CollabMonitor`（交出会话 id，开始后台轮询）、`ReadSession`（读协作会话的轮次与当前状态：进行中 / 已正常结束 / 异常结束及错误信息）。两侧语义一致：Worker 的协作会话是同工作区的其他 Worker 会话（A 侧落盘，A 重启后探活接管），Loop 的协作会话是同工作区的其他 Loop 会话（壳内视图，壳重启后由发起方重新 `CollabMonitor`）。
- 发起方启动协作后不再自己动手，只做分解、监测与协调；协作会话不是子 agent，没有父子调用栈，只在同一工作区各干一段。
- 监测汇报经 `WorkerSessionHost.EnqueueAuto` 作为仿用户消息插进发起方队列，`WorkerSessionSnapshot.PendingAuto` 标出哪几条是自动插入的；自动消息一律按「立即」入队（当前轮在下一个 step 前收尾后先发，不等整轮跑完），WinForms 队列栏与 Web 都用特殊底色 + 自动图标区分。
- 协作会话默认沿用发起方已完成的轮次（同模型才复用前缀缓存），也可由发起方用 `context` 另给上下文；轮询间隔见设置页「协作」（`CollabPollSeconds`）。全部会话结束或不可恢复地中止后监测自动停止，协作登记随 `.json` 落盘（A 重启后探活接管）。暂不支持跨工作区。
- **留言板私信（`say` 带 `to`）只对发送方与目标可见，并会主动投递**：`Puppet(target=.., action="say", text=.., to="A-xxxx")` 写板成功后，`PeerDelivery` 把这条以「会话私信」的协作提示正文推进目标队列（A 会话走 `WorkerApiClient.NoticeAsync(..., "mention")`，壳里的 Loop 会话说 `MainForm.DeliverNotice`；本进程能直投走 `PeerDelivery.Local`），目标不必来读板。只投同工作区的会话（`PeerDelivery.Resolve` 按 `AgentIdentity.Workspace` 比对留言板上的 Purpose，`to` 也可只写前缀）；别的会话不投，工具结果里会说明「只留在留言板上」。省略 `to` 才是广播——广播要对方主动读，没读到就错过。
- **私信可见性在会话侧过滤**：留言板宿主按 `to` 记账，但读接口不按观看者裁剪，所以 Agent 走 `Puppet` 工具 `chat`/`book`/`say` 时，`BoardPrivacy.Filter` 会摘掉不是我的私信（含 peer 的「最后一句」与最近一条 chat）。用户自己看的 `[Puppet]` 页不过滤。新增读板路径时同样要过这一层，别把别人的私信漏出去。
- **「同工作区新会话已启动」只播报正在跑一轮的会话**：开轮时 `SessionBoard.Opening` 把留言板上的 `EstimatedWaitSeconds` 标成非 0（`SessionBoard.RunningWaitSeconds`），轮次收尾（正常/停止/失败，含 Loop 的 `finally`）用 `SessionBoard.Finished` 归零；`AnnouncePeers` 只认 `PeerInfo.Active`。会话登记在板上要留 30 分钟，光看「还在不在」会把早就停手的会话当成新会话播报一条没用的协作提示。

## 免费额度自愈（ConnMan）

- 免费模型额度耗尽（`provider.free-limit` / 429 / 402，判据 `ServerNotice.IsQuotaFailure`）后，A 的失败轮收尾会调 `FreeLimitRecovery.TryStart`（`src\OpenLoop.Service\Worker\FreeLimitRecovery.cs`）：换时速网络节点 + 验证可用性这件事交给独立仓库 `..\ConnMan`，OpenLoop 只负责触发和收尾。
- 契约（A 面，参数名是 `name` 不是 `instance`）：`POST /agent/invoke?name=ConnMan&method=StartRecoveryAction` body `[sessionId, model]` → 轮询 `GET /agent/get?name=ConnMan&path=RecoveryState`（`idle|running|ready|failed|exhausted`）→ `ready` 时给原会话 `Send(id, "task", "继续")`；`exhausted` = 本轮节点已挪满一圈，收工不重试。
- 端点与 key 取 `%APPDATA%\ConnMan\puppet.endpoint` / `puppet.key`（ConnMan 启动写、退出清空）；文件不在或连不上都退避重试（ConnMan 可能在更新/重启），这是可选自愈。
- 只对 `opencode/*` 模型生效；每个会话独立重试：前 3 次间隔 5 秒，之后指数退避封顶 5 分钟，直到用户动了该会话（`Send`/`Stop`/`SetModel` 等会加 `Session.UserEpoch`）。并发由 ConnMan 自己拒绝（`StartRecoveryAction` 回「已有恢复在跑」）。
- 环回 HTTP 必须 `new HttpClientHandler { UseProxy = false }`：VPN 开着时系统代理就是 mihomo 自己。

## 连接失败重试

- 轮内连接层失败（拒绝连接、DNS 失败、连接被重置；状态码 408/502/503（`OpenCodeHttpException`，503 即 opencode 自己重启的 `service_starting`）；OpenCode 报的 `provider.transport`/`ECONNRESET` 文本也算，判据 `OpenCodeReasoner.IsConnectFailure`）不判这一轮失败：按 `RetryBackoff.Next`（前三次 5 秒，之后指数退避封顶 5 分钟）重试到连上为止，界面状态显示「连接失败，N 秒后重试」；`Stop`/软打断才收尾。500/504（语义模糊，可能这一轮本身不行）只重试 2 次就报错（`IsLimitedRetry`）；其余 HTTP 状态错误、额度与上游报错照旧直接抛。

## UI 规则

- Worker（`WorkerSessionHost`）与 Loop、旁路的会话展示保持一致：流式正文一律用 `AgentToolCallParser.CutToolCallText` 截掉协议片段（不只判「整段以 marker 开头」，否则协议原文会先闪现再被收尾清掉），不要各写一份判据。
- 工具闪现区（`ChatFlow`）：没有正文、也没有正在执行的工具行时，按节拍把思维链**尾部半行**（`ChatFlowControl.TailHalfLine`，按真实排版宽度截到半行宽）刷进正文 FCTB，让用户看到模型还在动；正文出来或工具有行就立刻让位，回合结束清空。节拍取账本 `limit.flash_interval`（毫秒，300–5000，默认 500，`SessionView.FlashIntervalMs` 读、`ChatFlowControl.FlashInterval` 注入），Worker / Loop / 旁路三处绑定时都要设。
- 仓库采用 AntdUI（2.4.15）；`AntdUI.xml` 是 API 事实来源，不猜属性。
- `AntdUI.Tabs` 的 `DragOrder` 有库缺陷（2.4.15/2.4.16 同）：`MouseDown` 不重置私有拖动锚点 `oldXY`，第一次移动就必然越过 `TouchThreshold`，点页签/关闭按钮会被当成重排（关闭还会被吞）。`MainForm.OnTabTiledDragDown` 用反射把 `oldXY` 放回按下点；升级 AntdUI 后确认该字段仍在（取不到会静默退回原行为）。
- 本机 250% DPI：
  - 设计器里的尺寸会自动缩放；**运行时算出来的像素必须 `DeviceDpi/96` 缩放**。
  - `AntdUI.Column.Width`（字符串数字）是 **DIP**，由控件自己再乘 DPI；不要再手动缩放。
  - `SplitContainer.SplitterDistance`、`TabControl.ItemSize` 不自动缩放。
- 字体以“点”为单位，像素盒必须一起缩放，否则 250% 下文字被裁切（ChatFlow 即因此修复）。
- `AntdUI.Menu` 竖向模式的 `SubText` 会被裁切 → 项目列表用单行 `Text`，路径另放标签。
- `AntdUI.Button` 放进 `TableLayoutPanel` 时必须显式给 `Height`（或 `Dock=Fill`）：不给高度会算成 0，控件树里可见、画面上完全没有（Puppet 页的发送按钮就这样丢过）。
- `AntdUI.Label` 带 `PrefixSvg` 时默认 `IconGap=0`，会与文字重叠，显式设 `IconGap`。
- 模型很多（数百个）时 `Select` 只放过滤后的列表；免费模型用绿点 + “免费”副标题标识。
- 新增 `ChatFlowControl` 必须在 `InitializeComponent()` 后、`Bind` 前显式设 `ChatFont`——它不是 `Control.Font`，字体遍历/`Font` 赋值不会自动覆盖。
- 给 `FastColoredTextBox` 赋比例字体（雅黑/Segoe 等非等宽）前必须先设 `AllowProportionalFont = true`：FCTB 2.19.2 起构造期 `SetFont` 直接抛异常，整个视图构造失败（会话/Loop tab 打不开）；等宽代码场景（Consolas）不受影响。TargetPane 即因此炸掉两类会话页（2026-11-24）。
- 新控件的颜色统一从 `UiStyle` 取，不写死 `Color.X`；自绘控件跟随主题。
- 聊天区消息对齐（ChatFlow，Worker 与 Loop、WinForms 与 Web 同一套）：用户消息靠右、正文块宽度封顶行宽一半（文字在块内左对齐），助手消息贴左占满整行；行内不显示「用户 / AI」角色标注，靠左右位置区分。
- 聊天区链接分流（`ChatPathActions` + 各视图 `OnChatLinkClicked`，Worker 与 Loop 一致）：网址 → 右侧「网页」内置浏览器；目录 → 直接用资源管理器打开；文本 → 右侧「文件」页（同一个页换内容，不叠 tab）；其余文件（二进制/过大/解码失败）→ 问一句再用系统默认程序打开。
- **附件与生图**：Worker/Loop 输入框都可挂附件（WinForms + Web 同一契约），落 `.openloop/tmp/attachments/<会话>/`，路径以「附件」段拼进用户消息；上限按模型类别给通用值（文本 8 / 生图 4，`MediaModels.AttachmentLimit`）。存在生图模型时工具栏显示「生图模型」，会话工具表注入 `image_generate`/`image_edit`（同系列按 id 前缀互指，有编辑变体就用它），结果存 `.openloop/tmp/images` 并写 EXIF（提示词 + 模型）。

## 工程规则

- **宿主接线层共用 `ReasonerWiring`**（Service/Models）：OnNotice / OnNativeTool / OnProgress / ShouldStop 接线与原生工具步骤记录只在这一份；Worker、Loop、旁路、演进 runner 都经 `ReasonerWiring.Attach`。新增通知通道只改这里。
- **读写分离**：只读方法不得写盘/建目录（判据：让它返回空，程序还能不能跑——能跑就不该「确保存在」）；「确保存在」由显式写入口承担。
- 请求体构建抽成 `internal` 纯函数并单测，不在 async 方法里内联拼字典；可空字段条件写入（`JsonIgnoreCondition.WhenWritingNull` 不作用于 `Dictionary` 的值）。
- WinForms 菜单不要在 `Closed` 里同步 `Dispose`（`ModalMenuFilter` 还会访问句柄，抛 `ObjectDisposedException` 崩进程），用 `BeginInvoke` 延后销毁。
- A/I 二进制一律从安装目录 `backends\` 启动；`%APPDATA%\OpenLoop\versions\<role>\<ver>-<sha>\` 只是存档与回退源，每角色只留当前 + 上一版（壳启动与换版后各清一次）。换版 = 存档 → 覆盖 `backends\`（旧文件先备份）→ 重启；起不来用备份还原。
- 参与迭代的外部组件在仓库根 `components.json`（url / 检出目录 / 程序集 / 版本号文件 / `consumers` / `packageId`+`packageVersion`）；清单缺失或漏项按内置官方组件兜底（临时组件不进兜底）。换 fork 只改该组件的 `url`，由 Loop 代改；`consumers` 没写对，组件会判成「走包」而不参与迭代。源码拉不到时按 `packageId` 走包（消费方 csproj 按 `-p:OpenLoopPkg_<包id去标点>` 注入条件引用），本轮不参与迭代；`packageVersion` 只是下限，实际取 nuget.org 上 >= 它的最高稳定版，最新版编不过就退回它。没有 `packageId` 是硬失败，留给用户处理。
- 一次性产物（临时补丁脚本、探针日志、参考源码副本）放仓库根 `.tmp/`（已 gitignore）；要留档的内容写进 docs/ 或代码注释。

## 设计器兼容（强制）

- 每个 Form/UserControl 必须有 `.cs` + `.Designer.cs`；字段声明、创建、属性设置、`Controls.Add` 全在 `InitializeComponent` 内。构造函数只调 `InitializeComponent()`，运行时初始化走 `Initialize(...)`。

## AGENTS.md 与提示词

- `<workspace>/AGENTS.md` 在每次任务开始时作为系统提示词的「项目约定」注入（`AgentsInstructions.Load`，上限 32KB）。本文件即 OpenLoop 自身的项目约定，保持短、可执行。