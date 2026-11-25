# 设计：TCF —— TargetCentricFacility（左栏文本编辑器与目标工具架构）

> 状态：**架构定稿（2026-11-24，用户给出设计要求）；实现完成（2026-11-25）**；运行时验收需迭代换版后按 §8.3 执行，现状见 §8.1。
> 目标读者：实现者；也用于判断改动是否越界。

## 1. 定性

**TCF（TargetCentricFacility）= 面向用户的文本多轮可视化编辑交互工具类型**：
从跟用户的视觉交互过程中获取数据，对**某一个数据对象**做多轮次的「雕塑性」修改
（每轮看效果、再改，直到用户满意）。

- 性质类似内建 Tool，但不走工具调用的「请求→结果」形态：它有持久可见的界面，
  数据在多轮之间驻留。
- **不是常设工具**：TCF 内建于壳、只有壳支持时才能启用，且只用于特定任务；
  不进默认提示词/工具表（为精简提示词）。启用方式见 §2。
- 现有实例：会话左栏的编辑目标面板（`TargetPane`，TargetCentricSession 的编辑面，
  prompt / agents 两种 Kind）。以后引入面向其他类型文件的编辑器
  （svg / gltf / 网页等）= 新 Kind + 新呈现控件 + Bind，不改会话主流程
  （沿用 `TargetSpec` 的扩展约定）。

### 1.1 与 LoopApp（OpenUI）的关系

TCF 与 LoopApp 不是能力互斥，而是**可见性与启用时机**不同：

| | TCF（目标编辑面） | LoopApp（OpenUI） |
|---|---|---|
| 定性 | 壳内建宿主控件，对同一数据对象**多轮雕塑性编辑** | agent 即时生成的可视化交互页面（**通常用于一次性任务**） |
| 数据 | 会话绑定的目标文件（`.openloop/prompts/system.md`、`AGENTS.md`；Loop 直配模式是源码根那份，以后可扩） | `.openloop/tmp/LoopApps/<slug>/` 下的自包含应用 |
| agent 操作通道 | Puppet：`target=shell`（Worker）/ `self`（Loop），`instance=MainForm`，按 `sessionId` 转发到面板 | 改文件 + `LoopApp(eval/reload)`，页面经 `ui.submit` 回消息 |
| agent 如何知道它 | **只有启用提示注入后才知道**；不进提示词、不进常设工具表 | openui 技能常设，agent 随时可用 |
| 生命周期 | 跟随会话目标模式，关闭=折叠、内容驻留内存直到保存或放弃 | 任务做完收起，闲置销毁 |
| 典型场景 | 「把这段提示词第 3 节改紧一点，我看着你改」 | 表单收集、看板、向导等一次性交互 |

要点：

- TCF 不随系统提示词注入，**未启用时 agent 不知道它存在**——这种情况下
  LoopApp 完全可以承担多轮雕塑性任务，没有限制。
- **一次性可视化交互建议 agent 走 openui 技能（LoopApp）实现**；
  LoopApp 通常用于一次性任务，但这只是建议，不是能力边界。
- 只有启用提示出现时（告知 `sessionId` 与操作接口），编辑该目标才交给 TCF，
  不另起 LoopApp 抢场景。openui 技能正文里已按这条写
  （`src/OpenUI/skills/openui/SKILL.md`）。

## 2. 启用方式与数据填充

### 2.1 术语：不叫「宿主消息」

代码里的「宿主消息」有确定含义：`User == null`、只在界面上可见、**不进模型上下文**
（`ChatStore` / `SessionView` 里的压缩进度就是这类）。TCF 需要的不是它。

**TCF 启用提示** = 面板打开期间、**拼在每条用户消息前面**的一块操作说明
（替换现在 `TargetDirective` 的那一句）：`target`、`sessionId`、路径、动作清单、保存语义。
不新增消息类型、不唤醒空闲会话、不产生队列项；面板一关就自然消失，不会额外堆积
（与 `TargetDirective` 现在是同一位置、同一生命周期）。6 个动作名、参数与行号基准都直给，
不要求 agent 先 `describe`；正文控制在一屏内（面板打开期间才付这份成本）。

关闭时的未保存由壳的对话框就地处理（§4.3），**不发通知**——
agent 从下一次动作返回的 `state.visible`（或直接失败）就能知道面板已经关了。

### 2.2 数据填充只有一条路径

**壳预填充**：

- 面板打开时，壳把目标文件读进编辑器（缓冲就是编辑器文本，见 §4.1）。
- 启用提示直接给：`target`、`instance=MainForm`、`sessionId`、绝对路径、
  候选/正式标记、行数与字符数、动作清单与保存语义。
- agent 想重建内容（文件被外部改、怀疑不同步）就调 `TargetReload`。
  原设计的「agent 自行填充」不再单列——它就是 Reload 动作。

## 3. 操作通道：Puppet + 寻址

TCF 位于壳内，agent 操作一律走 **Puppet**（与「只用 Puppet 调试」同一条路，
不走 UI 自动化）。`Puppet` 工具本身常驻（`PuppetTools.For()` 总是注入），
TCF 不新增工具。

### 3.1 两面同源的两个硬约束

- **B 面只收 `bool` / `OperationResult`**（`ActionizePolicy`）：读取类动作若返回 `string`，
  就只有 A 面可见，agent 走 B 面读不到。所以所有动作统一返回 `OperationResult`，
  正文放进 `data`。
- **B 面 state 只收简单类型属性**：复杂对象属性 B 看不到。状态统一序列化成 JSON 字符串；
  `TargetState` 属性只能反映**当前标签页**（A 的 `get` 没有参数），
  所以**每个动作的返回里都带自己的 `state`**，后台会话以动作返回为准。

### 3.2 寻址（必须写进实现）

- Worker 会话跑在 A 进程：`target=self` 是 A 的 Hub，那里没有壳实例。
  TCF 一律 `target=shell`、`instance=MainForm`。
- Loop 会话在壳进程内：`target=self`。
- 一个壳里有多个会话标签，Loop 会话也各有自己的面板（`LoopView`）。
  **所有动作第一个参数 `sessionId`**，从启用提示里拿；缺省 = 当前标签，只用于手工调试
  （后台会话缺省会改到用户正看的那个，绝不能靠缺省）。
- 动作声明在 `MainForm`（partial），按会话转发给对应 `TargetPane`：
  照 `MainForm.OpenUi.cs` 的 `LoopApp*For` + `FindBrowserById` 模式加 `TargetPaneFor(sessionId)`。
  `[PuppetAction]` 只扫 DeclaredOnly，别指望在 `TargetPane` 上声明的动作自动出现在
  `MainForm` 的 manifest 里。
- A→壳的传输不用另写：agent 的 `Puppet` 工具自带 `target=shell`；宿主侧已有
  `ShellPuppet.InvokeAsync("MainForm", ...)`（壳内直调 LocalHub、A 走 HTTP）与
  `ShellLoopAppHost` 这层先例，`TargetPaneFor` 与它同一形状。
- 动作统一在 UI 线程执行（Puppet 对 `ISynchronizeInvoke` 目标自动 marshal），
  面板内部不要再自己 `Invoke`。

### 3.3 状态（JSON）

字段：`kind`、`title`、`path`、`session`、`candidate`、`actual`、`visible`、`dirty`、
`pendingSave`、`lines`、`chars`、`lastError`。

形态：动作返回里的 `state` 对象；`MainForm.TargetState` 属性是当前标签页的同一份 JSON
（给 `get` / `state` 清单与手工排查用，后台会话不要依赖它）。

- `visible` 让 agent 知道用户是不是正看着面板（收起时改得再对也看不到）。
- `lastError` 必须有：agent 不能悬停 tooltip，保存失败/冲突的原因要能从状态里读到。
- `actual=true`（「实际」页，只读 trace）时编辑动作一律拒绝。
- 没有绑定面板时返回最小状态（`session` 为空、`visible=false`），不抛异常。

### 3.4 动作（6 个）

全部返回 `OperationResult`；行号 **1-based 闭区间**，`toLine=0` 表示到末尾，
越界返回失败（不静默钳制）。每次编辑是一个 undo 组（`BeginUpdate/EndUpdate`），保留光标位置。

| 动作 | 参数 | 语义 |
|---|---|---|
| `TargetRead` | `sessionId, fromLine=1, toLine=0` | `data = { text, fromLine, toLine, totalLines, chars, dirty, nextLine }`；`nextLine=0` = 已读完；不传区间 = 从第 1 行起读一页 |
| `TargetReplace` | `sessionId, fromLine, toLine, text` | 替换 `[fromLine, toLine]`；`fromLine=toLine+1` 即插入，`text=""` 即删除 |
| `TargetAppend` | `sessionId, text` | 尾部追加（等于 `fromLine=totalLines+1`；按原样追加，不自动补换行） |
| `TargetReload` | `sessionId` | 丢弃缓冲、重读磁盘（原「agent 自行填充」模式） |
| `TargetRequestSave` | `sessionId` | 只把状态推到 `pendingSave`（按钮变色/闪），**不写盘** |
| `TargetDiscard` | `sessionId` | 丢弃缓冲、回到磁盘内容（需用户已确认） |

- **每个动作的 `data` 里都带 `state`**（字段同 §3.3）：改完立刻知道 `dirty`/`pendingSave`/
  `lastError`，后台会话不必再查一次。
- **读取要翻页**：工具结果回填模型的上限是 2000 行 / 51200 字节（对齐 OpenCode 的 `tool_output`，
  见 `ToolText.Clip`），30KB 的 AGENTS.md 一次就占掉大半上下文；Puppet 响应另有账本
  `limit.puppet_result`（默认同值，可单独收紧）。`TargetRead` 按页返回并用
  `nextLine` 给下一页起点（单页按 ≤4000 字符切）；提示里说明「要全文就按 `nextLine` 读到 0」，
  按行号编辑时只读目标区段。
- **不暴露 Undo**：FCTB 的撤销栈和用户 Ctrl+Z 共用，agent 调 undo 可能撤掉用户/模型的东西
  或撤不掉。回滚用「读回 + 反向 `TargetReplace`」。
- `TargetDiscard` 标 `Risk=High`（丢掉未保存的编辑）；`TargetRequestSave` 不标，编辑类动作也不标。

### 3.5 启用提示

- 内容从 B 面 manifest 生成（`PuppetActions.LoadAsync` 已有这套），不硬编码动作清单，
  否则接口一改提示就漂。
- 必须含：`target`、`sessionId`、绝对路径、候选标记、行号基准、
  「只改这个文件、不要动其它文件」、「改的是内存、落盘等用户确认」、
  「**不要用 WriteFile 改这个文件**」。
- 明确说「不需要先探测 manifest、不需要读文档」：照提示直接调。

## 4. 编辑与保存语义

### 4.1 缓冲

- **缓冲 = 编辑器里的文本**，不另存副本（另存一份就多一份要同步的状态）。
- 所有 agent 编辑只改内存，实时反映在面板上。
- **关闭 = 折叠面板**，不 `Bind(null)`：内容与脏标志留在控件里。
- 折叠期间可以停掉 watcher；重新打开时先按 §4.2 判定（干净就重读，脏就保留并标记「磁盘已变」）。
- 只有目标（Kind / 工作区 / 直配↔评测）变化才重新 `Bind`；此时有脏内容先按 §4.3 处理。
- 切 Kind（prompt ↔ agents）会换目标：有脏内容就三选一（保存 / 放弃 / 取消），
  **不引入多缓冲**。
- 应用退出不保留脏内容（不做草稿持久化，见 §9）。

### 4.2 与磁盘写入的冲突

现有 watcher 是无条件覆盖（`Reload`），模型 `WriteFile` 一落盘就会静默冲掉未确认的编辑。
规则：

- 缓冲脏时，watcher **不覆盖正文**，只置「磁盘已变」标志并在面板上提示。
- 保存前比对基线 hash（读入时记录）；不一致 → 拒绝保存，返回失败并说明
  「磁盘已被改动，先 Reload 再改」。
- 解决路径只有 `TargetReload`（丢弃缓冲重读）；**不提供强制覆盖**，
  避免无声吞掉别人的改动。
- 多会话绑同一文件（Worker 与 Loop 同时开 AGENTS.md）走同一条规则，后保存者被挡住。

### 4.3 保存必须经用户确认

状态：`clean` / `dirty` / `pendingSave` / `failed`；保存成功回到 `clean`。

- agent 的 `TargetRequestSave` 只把状态推到 `pendingSave`（保存按钮变色/闪）；
  **写盘只由用户点保存按钮触发**。agent 拿到的是 `pendingSave`，不能谎报「已保存」。
- 写盘成功 → `clean`，并在此处调 `_changed` 登记账本（只在保存成功后调，不在每次 Reload 调）；
  失败 → `failed` + `lastError`。
- **关闭时有脏内容：壳自己弹三选一（保存 / 放弃 / 取消）**，不绕 agent。
  理由：用户点的关闭，壳直接问最快；不受会话 paused／失败轮「队列不续发」影响；
  省一次模型调用。就地落盘或丢弃后再关。agent 不需要被通知：`visible` 立刻为 false，
  下一次动作会明确失败。
- 保存目标目录不存在时，由保存动作创建（写入口 ensure，只读侧不建目录）。

### 4.4 写回保真

- 保留原文件的 **BOM 有无**与**换行风格**（读原始字节判断；本仓库的 `AGENTS.md`
  与 `.openloop/prompts/system.md` 都是 CRLF）。
- 原子写：临时文件 + 替换，避免 watcher 读到半截。
- 写后立即更新基线 hash；随后 watcher 触发的 Reload 若内容相同则不动作
  （不抖光标、不扰动撤销栈）。

## 5. 界面要求

- **保存目标路径**：不必持续展现；放 tooltip，悬停可见。
- **保存状态**：图标 + 色彩 + tooltip（四态：无改动 / 未保存 / 待确认 / 失败），
  **复用保存按钮**，不另加状态栏。颜色不能是唯一信号（用图标形状区分）。
- **工具栏按钮图标化**（带 tooltip），减少多语种维护。
- **undock 时工具栏一起搬**：保存按钮与状态不能留在被折叠的面板里（现状只搬正文与提示条）。
- **面板保持只读**（`ReadOnly = true`）：本设计说的「可编辑」= agent 可编辑，
  用户仍不直接打字（与 TCS 的约定一致）。
- 「实际」页只读，且禁用保存与所有编辑动作。
- 颜色从 `UiStyle` 取，不写死 `Color.X`。

## 6. 与 TargetCentricSession 的关系

- **TargetCentricSession（TCS）是会话模式**：绑定一个唯一编辑目标、聊天驱动模型落盘。
- **TCF 是这类工具的统称与扩展架构**：TCS 的 TargetPane 是 TCF 的第一个实例；
  「agent 可通过 Puppet 操作编辑器本体」是 TCF 对 TCS 现状的增强方向
  （现状面板只读，改动只能由模型经文件工具落盘、watcher 刷新）。
- 两条数据流并存：模型经 WriteFile 落盘 → watcher 刷新（现状）；
  agent 经 Puppet 编辑内存 → 用户确认落盘（本设计）。冲突裁决见 §4.2。
- Loop 直配模式的目标可能是源码根（绝对路径、不在会话工作区）：提示里必须带绝对路径
  与候选标记，不能只写文件名。

## 7. 提示词成本

TCF 不进系统提示词、不进常设工具表——未启用时 agent 不知道它存在。
面板打开期间，agent 的认知来自**同一块文本**（§2.1 的操作块，就是改写后的
`TargetDirective`，每条用户消息前都带）：给路径、`sessionId`、动作用法、保存语义。
这块必须一并改掉现在那句「要改就只改这个文件」——否则模型会继续直接写文件
（还会触发 watcher 冲突）。

`AGENTS.md` 只保留一句定性（见该文件 TargetCentricSession 条目）。

## 8. 现状、实现顺序、验收

### 8.1 现状（已存在）

- `src/OpenLoop.App/TargetPane.cs` / `.Designer.cs`：**TCF 已实现**——缓冲就是编辑器文本；
  保存状态机（clean/dirty/pendingSave/failed）+ 基线 hash 冲突检测 + 原子写回（保 BOM/CRLF）；
  关闭=折叠（脏内容三选一：保存/放弃/取消）；工具栏图标化并随 undock 一起搬；保存按钮承载四态。
- `src/OpenLoop.App/MainForm.Target.cs`：6 个 Puppet 动作（`sessionId` 转发到对应会话面板）
  + `TargetState` 状态属性；动作返回统一 `OperationResult`，`data.state` 带完整状态。
- `src/OpenLoop.Core/Agent/TargetText.cs`：行号编辑纯函数（翻页读取、区间替换），有单测
  （`tests/OpenLoop.Core.Tests/TargetTextTests.cs`）。
- 启用提示：`TargetPane.Prompt(target)` 从 B 面 manifest 生成动作清单；Worker 用 `target=shell`、
  Loop 用 `target=self`，替换了原来那句 `TargetDirective`。
- `src/OpenUI/skills/openui/SKILL.md`：有 LoopApp 决策表与 TCF 区分说明（已补）。
- **已修（2026-11-25）**：`PuppetActions.NoticeText` 收了 `target` 参数却没用，执行说明里写死
  `target=self`；Worker 会话调用时传的是 `shell`，按 §3.2 会打到 A 的 Hub（A 没有壳实例）。
  现在两处（invoke 与 describe）都用传入的 `target`，TCF 复用同一套寻址。

### 8.2 实现顺序

1. 缓冲所有权 + 冲突规则 + 保存状态机 + 三选一关闭 + 写回保真（§4）。
   先做这步：后做必然先丢数据。
2. 界面（图标化、保存状态、路径 tooltip、undock 携带工具栏、只读）。
3. Puppet 状态 + 6 个动作（`MainForm` 转发 + `sessionId`），A/B 两面同源（§8.1 的 `target` 缺陷已修）。
4. 启用提示模板（从 manifest 生成）+ 改写 `TargetDirective`。

### 8.3 验收（可测）

| 用例 | 判定 |
|---|---|
| 正常替换 | agent 读 → 改 → 面板实时变；`TargetRequestSave` 返回 `pendingSave`；用户点保存后动作返回的 `state.dirty=false`、账本已登记 |
| 保存冲突 | 外部先改文件 → 保存被拒、`lastError` 说明；`TargetReload` 后重做成功 |
| 关闭未保存 | 点关闭 → 三选一；选「放弃」后面板回磁盘内容，选「保存」后文件已更新 |
| 写回保真 | BOM 有无、CRLF 与尾随换行与原文件一致 |
| 寻址 | 后台（非当前标签）会话调用只改它自己的面板 |
| 提示自足 | agent 收到提示后不探测 manifest、不读文档，直接完成一次替换 |
| 无 TCF 时 | 系统提示词无 TCF 内容；会话无目标绑定时面板不出现 |

## 9. 不做 / 待定

- **Web 壳不做**：本期只做 WinForms 壳；Web 壳没有左栏编辑面，这里显式豁免
  「两侧功能对齐」，以后要做得先有文本编辑面。
- **不做草稿持久化**：应用退出即丢脏内容。
- **不做强制覆盖**：冲突只能 Reload 后重做。
- **不做 Undo 动作**：回滚用反向替换。
- **不做多缓冲**：切 Kind 有脏内容时三选一。
- 非文本 Kind（svg / gltf / 网页）：沿用 `TargetSpec` 扩展约定，动作集到时候另定。
