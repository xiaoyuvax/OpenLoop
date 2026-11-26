# 设计：OpenUI —— agent 即时生成的 Web 界面（LoopApp）

> 状态：**方案（未实现）**。本文只写设计，不含已落地的改动。
> 目标读者：实现者；也用于判断改动是否越界。

## 1. 问题

agent 与用户的图形交互目前只有两条路：

1. 预先设计好的原生对话框（`AskUser`、审批卡、设置页），形态固定，复杂界面（表格编辑、看板、图表、3D）做不出来；
2. 生成一个网页让用户自己去系统浏览器打开，脱离了会话，改一次就要重新描述一次。

壳里已经有一个 WebView2（右侧栏「网页」页，可独立成窗口），但它现在被当作「看外部网页」的工具：只能整段注入 JS 或导航到远程 URL。缺的是把本地文件当成真正 web 应用加载的能力、一个页面能反向驱动会话的通道，以及一套让 agent 低成本写出好看界面的经验性资产。

**目标**：agent 在会话中按需生成一个单页应用（LoopApp），在壳的内嵌浏览器里即时渲染、即时迭代；界面能反向调用 OpenLoop（Puppet 接口），把用户操作送回会话。

**非目标**：

- 不做「JSON 描述界面」的 DSL。模型最擅长直接写 HTML/JS，DSL 只会多一层要学、且永远覆盖不全的东西。
- 不做跨会话共享的应用市场；暂不做 Web 壳（浏览器是用户的，没有内嵌 WebView）。
- 不替换现有对话框机制；OpenUI 是复杂交互的补充。

## 2. 已有基础（不重写）

| 能力 | 落点 |
|---|---|
| 按需创建/销毁的内嵌 WebView2（每会话一个） | `src/OpenLoop.App/BrowserView.cs` |
| 导航（`Normalize` 已放行 `file:` / `data:`） | `BrowserView.cs:91`、`:167` |
| 执行 JS 并取回 JSON 结果 | `BrowserView.cs:99` |
| 截图（PNG base64） | `BrowserView.cs:108` |
| 解锁后用户可直接操作页面 | `BrowserView.cs:116` |
| 独立窗口（同一个 WebView2 搬进顶层 `Form`） | `BrowserView.cs:130` |
| Puppet A/B 面 + 进程内 Hub | `App/PuppetHost.cs:56-70`、`Web/PuppetBridge.cs:16` |
| A/I 跨进程驱动浏览器 | `Web/PuppetTool.cs:143-173` + `MainForm.cs:417-495` |
| 技能机制（`SKILL.md` + `references/` + `scripts/`，渐进披露） | `Core/Skills/SkillCatalog.cs:33`、`ReadSkillTool.cs:63` |
| `.openloop/tmp` 在 Loop 侧可写 | `Service/Source/LoopScope.cs:15` |
| 仿用户消息入队（协作监测用的那套） | `Worker/WorkerSessionHost.Collab.cs:114`、`LoopView.cs:449` |

`Undock()` 已经把「agent 生成的桌面程序」这件事做完了：同一个浏览器实例搬进原生窗口，不需要另造宿主。

## 3. 缺口

| # | 缺口 | 后果 | 改法 | 量级 |
|---|---|---|---|---|
| 1 | 没有 `SetVirtualHostNameToFolderMapping` | 只能 `file://`（fetch / ES module 被 CORS 挡死）或 `data:`（Chromium 禁止顶层导航） | `EnsureCore` 里把 `https://openui.local` 映射到**该会话的** `.openloop/tmp/LoopApps` | ~10 行 |
| 2 | 没有 `WebMessageReceived`，也没有会话级 Puppet 实例 | 页面无法主动叫醒会话（只能被 `eval` 拉）；且壳只注册了跨会话的 `MainForm` 实例 | 每个 `BrowserView` 挂一个消息桥 + 一个会话级 Puppet 实例（§9） | ~120 行 |
| 3 | Worker 写边界默认封了 `.openloop` | Worker 写不了自己的 LoopApp | `WorkerSessionService.cs:132` 加 `allowed: [".openloop/tmp"]` | 1 行 |
| 4 | agent 没有浏览器工具 | 只能 `Puppet(invoke, EvalBrowserScript, [js])`：HTML 要塞进 JSON、结果 6000 字截断 | 新增 `LoopApp` 工具 | ~250 行 |

第 1 条是成败点：有映射后 `index.html` / `app.js` / `data.json` / `.wasm` / `.gltf` 全部按相对路径正常工作，页面是 secure context，无 CORS，无体积上限。映射根**按会话**设置（见 §9），不是全局一个目录。

## 4. 目录

```
src/openui/                        ← 产品组件（无程序集，不进 components.json）
  README.md                        ← 本设计摘要 + 验收方法
  skills/openui/
    SKILL.md                       ← 技能正文：决策表 + 30 行最小应用 + 陷阱
    references/
      runtime/openui.css           ← 设计令牌 + 工具类 + 组件（~18KB）
      runtime/openui.js            ← 运行时（~12KB / ~200 行）
      runtime/icons.svg            ← 图标 sprite（~4KB）
      runtime/vendor/three.module.js + GLTFLoader.js  ← 仅 3D 用，默认不引用
      quickstart.md  components.md  patterns.md  events.md  pitfalls.md
  examples/                        ← 4 个完整参考应用（kanban / form / dashboard / 3d-viewer）
```

运行时放在**技能目录里**是关键决定：装技能 = 装框架，`create` 从 `SkillInfo.Directory` 复制，不依赖安装布局的路径解析。

生成物：

```
<workspace>/.openloop/tmp/LoopApps/<slug>/
  app.json          {name,title,window:"dock"|"float",size:[w,h],owner:"<sessionId>"}
  index.html        ← 只写结构，引 ./runtime/openui.css、./app.js
  app.js            ← agent 唯一频繁改的文件
  data.json / assets/ / *.gltf
  runtime/          ← create 时复制进来，应用自包含
```

自包含而非跨域共享：多占 ~34KB 磁盘，换掉一整类 origin 问题，应用还能整目录搬走。

## 5. 工具 API

一个工具，两种宿主：

```
LoopApp(action, name?, js?, url?, dock?)
  create  name      → 建目录 + 复制 runtime + 返回路径与 URL
  open    name|path → 打开「网页」页并导航（页面不存在时自动创建）
  eval    js        → 执行并回 JSON
  shot              → PNG 产物
  drain             → 取走页面攒下的事件（页面没走推送时的兜底）
  close             → 隐藏页面 → 关独立窗口 → 收起「网页」标签（实例留着）
  undock / dock / interact(bool) / state / list
```

**不设 `write` 动作**：`WriteFile` / `Edit` / `Grep` 直接作用于 `app.js` 更好（可 diff、可局部改）。`create` 只做文件工具做不到的事——复制一份已知的资产树。

宿主抽象：

```csharp
public interface ILoopAppHost { Open; Eval; Shot; State; Close; Undock; Dock; Interact; }   // 全部 async + ct
```

实现只有一份 `ShellLoopAppHost`：它把动作发到壳的 `MainForm.LoopApp*For(sessionId, ...)`。
壳进程内（Loop 会话）走 `PuppetBridge.LocalHub` 直调，不经端口；A 进程里的 Worker 会话走 HTTP 到壳的 Puppet 端口（`ShellPuppet.InvokeAsync`）。两条路共用同一个实例名 `MainForm`。

为什么不是直接调会话级实例 `LoopApp-<id>`：那个实例只在「网页」页建出来之后才存在，而 agent 的第一次 `open` 恰恰发生在页面还不存在的时候——先有实例才能建页，鸡生蛋。所以壳上多一层 `LoopApp*For`：按 id 找到会话 → 建页（`SessionView.ShowBrowser` 顺手接线）→ 转给那个会话自己的 `BrowserView`。`sessionId` 由工具构造时注入，不是模型参数（§9.2）。

与 `CollabTools.For(host)` 同一模式，不新增架构概念。

## 5.1 停靠还是独立窗口（按内容判定）

右侧栏是竖版窄栏，只适合小表单/单列列表；把一张宽表塞进去只会挤成一条缝。所以 `open` 之后由壳决定放哪：

- 导航完成（`NavigationCompleted`）时量一次 DOM：元素数、表格最大列数、宽块（`.grid.c3/.c4`、`table.wide`）、正文高度、横向溢出。
- 任一条超阈值 → 搬进独立窗口；都没超 → 留在右侧栏。
- `app.json` 的 `window: float|dock` 与 `size: [w,h]` 优先于实测：agent 明确要求就照办，关窗尺寸也用它。
- 用户自己关掉独立窗口 = 他要停靠：本次 LoopApp 期间不再自动浮出（再 `open` 时重置）。

阈值与判定收在 `OpenLoop.Service/OpenUI/LoopAppLayout.cs`（纯函数 + 单测 `LoopAppLayoutTests`），壳只负责量。改成"量不到内容就不弹窗"是有意的：`ui.ready` 可能晚于导航完成，宁可留在侧栏也不要莫名抢焦点。

## 5.2 收尾与闲置回收

任务做完（页面 `ui.submit` 过、这一轮收尾）不该继续占着侧栏：

- 壳在会话轮收尾时看到「这一页交过数据」，就走收起：导航到 `about:blank` → 关掉独立窗口 → 摘掉「网页」标签。实例留着（不销毁），所以再 `open` 只是把标签插回去。
- 收起的实例由 `BrowserView.Layout` 的计时器盯着：看不见（且不在独立窗口）超过限定分钟就 `IdleExpired` → 宿主走与右键「关闭网页」同一条路销毁。
- 限定分钟数来自本工作区账本的 `limit.browser_idle`（默认 30，可配可演化；读不到就用默认，回收策略不该把界面带崩）。
- `LoopApp(close)` 可以主动走同一条收起路径。

## 6. 迭代循环


```
首版   create → open                          ~300ms
迭代   改 app.js → eval("location.reload()")     ~50ms，确定性重载
微调   eval("ui.patch('#sel', html)")            不丢状态
```

不要每次把整页 HTML 注入进去：`ExecuteScriptAsync` 传大字符串要处理转义、有体积风险、还丢文档状态。落盘 + reload 又快又稳。

## 7. 运行时

只提供三样，不提供抽象层：

1. `openui.css`：令牌（浅色优先、深色跟随系统、单一强调色、4px 栅格、系统字体）+ ~20 个工具类 + 12 个组件类（card / btn / input / table / tabs / badge / modal / toast / grid / stack / sidebar / empty）。
2. `openui.js`：`mount / state / on / call / submit / $ / fmt / toast / modal / copy / persist` + `chart.spark` / `chart.bars`（内联 SVG，~60 行，替掉 200KB 的 Chart.js）。
3. `icons.svg`：sprite，`ui.icon("name")`。

零构建、零网络、首屏 <100ms。唯一可选外部依赖是 three.js（按需引用，不进默认模板）。

## 8. 后端：Puppet 就是 LoopApp 的接口层

LoopApp 的「后端」不是另起一套服务，就是 Puppet：A 面（`/agent/*`，反射调试）与 B 面（`/appagent/*`，具名 Action）支撑页面对 OpenLoop 的全部调用。分发走的是**同一个 Puppet Hub**——和 `/agent/*`、`/appagent/*` 端口请求完全同一条代码路径（`PuppetHub.ProcessWebRequest`，`BusHost.cs:87`、`PuppetHost.cs:68`）。差别只在传输这一段。

### 8.1 为什么不直接 `fetch` 端口

页面源是 `https://openui.local`，Puppet 在 `http://127.0.0.1:9090`（壳）或 `:19180/19181`（A/I）。Wima 确实提供了开关，且 `PuppetWebServer` 构造里已经默认打开（`PuppetWebServer.cs:28` 的 `UseCrossDomain()`），但实测这个开关只负责给响应加头，不管预检：

```text
OPTIONS /agent/invoke            -> 404
OPTIONS /appagent/actions/*      -> 401
Access-Control-Allow-Origin      : *
Access-Control-Allow-Credentials : true
Access-Control-Allow-req.Methods : GET,POST,PUT,DELETE,OPTIONS   ← 头名拼错
```

三条都是硬伤：

| 问题 | 后果 |
|---|---|
| 预检不短路（OPTIONS 落到业务路由 → 404/401） | 浏览器要求预检返回 2xx，直接判 CORS 失败 |
| 方法头写成 `Access-Control-Allow-req.Methods` | 浏览器只认 `Access-Control-Allow-Methods`，等于没声明方法 |
| `Allow-Origin: *` 配 `Allow-Credentials: true` | 规范禁止；带 `credentials: 'include'` 的请求必被拒 |

要走 CORS，得改 Wima（第三方库）或给每个 handler 补 OPTIONS 分支，然后页面还必须持有 Bearer key。为一条本地通道付这个代价不划算——而且即使修好，下面两件事仍然成立：

1. **key 必须进页面**：`/agent/*` 只认 Bearer。key 一旦写进 `app.js`，agent 生成的任意页面代码、任意被 XSS 的输入渲染，都能拿到全量 A 面（含 `MainForm.StopSession(title)` 这类跨会话方法）。
2. **CORS 的粒度是「源」，不是「会话」**：它只能决定「这个源能不能调这个端口」，不能表达「只能动自己那个会话」。跨会话边界又退回服务端运行时判断，正是 §9 要避开的。

消息桥把这两件事同时解决：页面没有 key，通道按浏览器实例绑定 owner，且能调的 action 里根本没有会话参数。

PNA（Private Network Access）那一层未实测：`openui.local` 经 virtual host 映射落在 loopback，页面与目标同属 local 地址空间，按 Chromium 现行规则可能不触发额外预检。不依赖它——消息桥完全不经过 HTTP。

### 8.2 传输：WebView2 消息桥（默认）

`BrowserView` 在 `EnsureCore` 时挂两件事：

```csharp
core.AddScriptToExecuteOnDocumentCreatedAsync(OpenUiBridge.Shim);   // 注入 window.openui.call
core.WebMessageReceived += (s, e) => _bridge.OnMessage(e.WebMessageAsJson);
```

页面侧只有一种写法：

```js
const r = await ui.call("SessionSubmit", { text: JSON.stringify(form) });   // 走 postMessage
```

桥收到 `{id, action, args}` 后，在壳进程内调 `PuppetBridge.LocalHub` 上**本浏览器自己的那个 Puppet 实例**（§9；实例名由桥写死，页面无法指定），把结果通过 `PostWebMessageAsJson` 回给页面。没有 TCP、没有 CORS、没有 key 落地，延迟是一次消息往返（<1ms）。

桥只接受来源是 `https://openui.local/` 的消息（`CoreWebView2WebMessageReceivedEventArgs.Source`）；同一份 shim 会注入到该 WebView2 的每个新文档，若不校验来源，任何被导航到的远程站点都能拿到 `ui.call`。

选消息桥而不是 `AddHostObjectToScript`：前者只传 JSON，不把 .NET 对象暴露进页面 JS，且天然可审计（每条消息都能记一行日志）。

### 8.3 身份跟着浏览器走

桥是 `BrowserView` 的成员，而 `BrowserView` 属于某个 `SessionView`（`SessionView.cs:324`）或 `LoopView`（`LoopView.cs:127`）。所以：

- **页面不需要、也无法声明自己是哪个会话**——宿主在转发时注入 owner session id；
- 页面参数里出现 `sessionId` / `title` 一律丢弃，不参与寻址；
- 两个会话的 LoopApp 在两个独立 `BrowserView` 里，连 `openui.local` 的映射根都不同（§9）。

### 8.4 能力面：两层，各自没有会话参数

**第一层：页面 → 宿主**（`BrowserView` 上的会话级实例 `LoopApp-<sessionId>`）

页面能调的 action 就是这个实例上带 `[PuppetAction]` 的方法；其余一律 `unknown action`：

| action | 语义 |
|---|---|
| `SessionState` | 读 owner 会话的 `busy / model / title / queueLen` |
| `SessionSubmit` | 把页面数据作为一条用户消息入队 owner 会话（默认「等本轮」，不打断正在跑的轮次） |
| `SessionStop` | 硬停 owner 会话当前轮 |
| `LoopAppOpen` / `LoopAppEval` / `LoopAppShot` | 打开/执行 JS/截图（`Open` 也接受页面 URL，不只是目录名） |
| `LoopAppUndock` / `LoopAppDock` / `LoopAppInteract` | 独立窗口、锁定/解锁 |
| `LoopAppState` | 锁定/浮动/当前地址 |

Loop 侧是同一形状的另一个实例（`LoopView` 的 `BrowserView`），`SessionSubmit` 落到 `Send(text, immediate:false)`。

关键在于这些方法**签名里没有会话参数**（§9.2）：页面不是「被允许只能操作自己」，而是「没有语法能指向别人」。

**第二层：agent → 宿主**（`MainForm` 上的 `LoopApp*For`）

agent 侧工具要能在「网页」页还不存在时就把页面建出来，所以它调的是 `MainForm.LoopApp*For(sessionId, ...)`：
按 id 找到会话 → 建页（`SessionView.ShowBrowser` 顺手接线）→ 转给那个会话自己的 `BrowserView`。
`sessionId` 由 `LoopAppTool` 构造时注入（本会话 id），不是模型给的参数；找不到会话就拒绝，绝不新建。

### 8.5 事件方向

- **页面 → agent（主）**：`ui.submit(payload)` = `SessionSubmit`，直接进会话队列，跑完一轮的 agent 下一轮就看到用户填的表。这是 LoopApp 从「预览」变成「应用」的那一步。
- **页面 → agent（兜底）**：`ui.emit(type, payload)` 只入队 `window.__openui.events`，agent 调 `LoopApp(drain)` 取走。用于 agent 不方便被唤醒的场合（例如它正在长工具里）。
- **agent → 页面**：`eval`，或改文件 + `reload`。

### 8.6 什么时候仍走真实端口

| 场景 | 做法 |
|---|---|
| 页面在系统浏览器打开 | 只能 HTTP：`http://127.0.0.1:<port>/appagent/...`，key 由用户自己给（`?k=` 或页面里粘），明确标注「调试用」 |
| Web 壳（`wwwroot`） | 走**独立 origin + 路径令牌**，不是同源 `fetch`（Cookie 是全权的，会破 §9）；见 §11 |
| 页面需要流式/长轮询 | P2 再考虑，届时优先补桥的双向推送，不是开 CORS |

## 9. 会话边界：用结构堵死，不靠校验

LoopApp 与 OpenLoop 同机同源、代码全部由 agent 生成，调内部状态本身没有信任问题。唯一要硬守的是：**一个会话的 LoopApp 不能借 Puppet 干涉另一个会话**。

### 9.1 为什么「桥里做白名单」不算硬限制

白名单若写在桥里，就是运行时字符串比较：最终仍要落到 `MainForm.StopSession(title)` 这类**带会话参数**的方法上，于是 `if (args.title != owner) reject` 成了唯一防线——改个参数就能试，漏一个分支就破。要硬，就得让「指向别的会话」这件事**无法被表达**。

### 9.2 机制：每个浏览器一个会话级 Puppet 实例

`PuppetRegistry` 本来就支持多实例（`WebUi` 已是第二个）。给每个 `BrowserView` 注册一个实例，**构造时绑定 owner，方法签名里没有任何会话参数**：

```csharp
// BrowserView.OpenUi.cs：一个浏览器一份，Configure(owner, root) 时绑定，之后无法更换
public partial class BrowserView : IPuppet
{
    private string _owner = "";                                   // 构造时绑定
    string IPuppet.AgentInstanceName => "LoopApp-" + _owner;

    [PuppetAction] public string SessionState() => ...;           // busy/model/title/queueLen
    [PuppetAction] public OperationResult SessionSubmit(string text, string tag = "task") => ...;
    [PuppetAction] public OperationResult SessionStop() => ...;
    [PuppetAction] public OperationResult LoopAppOpen(string target) => ...;
    [PuppetAction] public OperationResult LoopAppEval(string js) => ...;
}
```

桥调用时把实例名写死：`LocalHub.PostAsync("/appagent/actions/SessionSubmit?name=LoopApp-abc12345")`。走进程内 Hub 不经过 HTTP 鉴权，`name` 不来自页面，页面侧也根本没有 key 可拿（只有 §8.6 的回退路径才用 `PuppetBridge.Key`）。

于是跨会话干涉在这个形状下**不存在可写的表达**：

| 页面想做的事 | 为什么做不到 |
|---|---|
| 指定操作别的会话 | 方法签名里没有会话参数，`SessionSubmit` 只认 `text`/`tag` |
| 改实例名去调 `MainForm.StopSession` | `name` 由桥拼，不读页面输入 |
| 伪造 `{action:"invoke", args:[...]}` | `invoke` 不是该实例的 action，`unknown action` |
| 直连端口 `POST /appagent/actions/StopSession` | 无 Bearer key → 401；`probe` / `help` 免凭证但读不到会话状态 |

**agent 侧的第二层**（§8.4）：`LoopAppTool` 调的是 `MainForm.LoopApp*For(sessionId, ...)`，`sessionId` 在工具构造时注入，同样不是模型参数。它按 id 找会话、建页、转给那个会话自己的实例——找不到会话就拒绝，不会新建。两条链上都没有「模型指定别的会话」的位置。

### 9.3 其余口子逐条封

| 口子 | 处理 |
|---|---|
| shim 注入到每个新文档，远程站点也能拿到 `ui.call` | 桥校验消息来源必须是 `https://openui.local/`，shim 自身也判断 `location.origin` |
| `https://openui.local/../../<别的目录>` | WebView2 的文件夹映射不解析越界路径；映射根就是该会话的 LoopApps |
| 页面导航到别处后 shim 仍存在 | 来源校验拦住；导航本身不拦（它就是个浏览器） |
| 页面读工作区其他文件 | 映射根之外的路径取不到；工作区文件本来也只能经 agent 工具读 |
| 页面加载任意远程资源 | 与既有 WebView 行为一致，不新增权限，不收紧 |

### 9.4 工作区范围

- 生成物只落在 **owner 会话的工作区** `.openloop/tmp/LoopApps/<slug>/`；跨工作区天然不同根，互不可见。
- 同一工作区的两个会话共享这个目录（它们本来就共享工作区文件）。`app.json.owner` 只记来源、供 `list` 标注，**不做访问控制**——读到另一个应用的静态文件不构成「干涉会话状态」。
- 需要外部资料时与 session 一样走 shell / WebFetch，受同一套审批约束。

### 9.5 明确不防的

- agent 自己用 `Puppet` 工具 `target=a|i|shell` 本来就能跨会话操作——既有能力，与本方案无关，不因 LoopApp 收紧。
- 用户解锁后手点页面，等于能触发上述 action；这与用户自己点界面按钮同权，符合预期。
- 页面可以把自己渲染崩、可以占满 CPU——它是浏览器，不是沙箱里的插件。

### 9.6 为什么不用别的机制

- **不用 origin / 独立 profile 隔离**：跨会话的问题不在源，而在「有没有可寻址的会话参数」。
- **不给 Puppet 加 ACL / scope 字段**：那要维护「调用方是谁」的映射表，正是想避开的运行时判断。
- **不用 `AddHostObjectToScript` 暴露 .NET 对象**：那才是真把能力开给页面；消息桥只过 JSON。
- **不做 Puppet 全量透传**：透传要把任意参数里的会话标识清洗干净，漏一个就破；实例化后连清洗都不需要。

生成物仍在 `.openloop/tmp` 下，不进账本；`ReadEvidence` 之外的工具照旧读不到 `.openloop`（Worker 侧新增的放行只到 `.openloop/tmp`）。

## 10. 两个 agent 的差异

| | Worker (A) | Loop（壳内） |
|---|---|---|
| 浏览器归属 | 该会话自己的 `SessionView.Browser` | `LoopView.Browser` |
| 目标定位 | 必须按 session id 取，不能靠「当前可见标签页」 | 直接引用 |
| 工作区 | 用户工作区 | 源码工作区 |
| 桥的能力面 | 该 Worker 会话的 `LoopAppPuppet` 实例 | 该 Loop 会话的 `LoopAppPuppet` 实例 |
| 实例名 | `LoopApp-<worker sessionId[..8]>` | `LoopApp-<loop sessionId[..8]>` |

`MainForm.Browser`（`:393`）现在是「可见标签页的浏览器」，Worker 后台调工具时会打错窗口。必须给 `LoopApp` 显式 owner 参数，用已有的 `FindByTitle` / `ResolveSession` 定位会话。

## 11. Web 壳（只有浏览器时）

先立不变式，两个壳都只是在实现它：**LoopApp 永远跑在自己的 origin 上，永远不持有 API 凭证。**

| | 桌面壳 | Web 壳 |
|---|---|---|
| 页面 origin | `https://openui.local`（虚拟域 → 该会话的 LoopApps 目录） | `http://127.0.0.1:<loopapp 端口>`（独立监听，不是 bus 端口） |
| 页面 → 后端 | `postMessage` → `WebMessageReceived` → `LocalHub` | `POST /t/<token>/api/<Action>` |
| 身份从哪来 | 桥绑定的那个 `BrowserView` | 路径里的 `<token>` |
| 凭证 | 无（进程内直调） | 无（token 只对这一个应用有效） |
| agent → 页面 | `Puppet(target=shell, invoke=EvalBrowserScript/…)` | 改文件 + 页面轮询版本戳自动刷新 |

### 11.1 为什么不能直接用同源 iframe

Web 壳页面是 `http://127.0.0.1:<bus>/`，鉴权是 HttpOnly Cookie（`WebUiHandler.cs:48`，`Path=/`）。同源 iframe 里的 LoopApp 会自动带上这枚 Cookie，于是它能直接 `fetch('/api/sessions/<别的 id>/stop')`。这是两个壳最大的结构差异：

- 桌面壳：页面没有 key；
- Web 壳：页面**天生带 key**，而且这枚 key 是全权的。

靠 `Sec-Fetch-Site` / `Referer` 判断是运行时校验，正是 §9 要避开的东西。必须换 origin。

### 11.2 机制：独立端口 + 路径令牌

A/I 启动时多监听一个 loopback 端口（写进 `AppPaths` 供壳页面与 agent 查），只做两件事：

```
GET  /t/<token>/...            静态文件：owner 会话工作区的 .openloop/tmp/LoopApps/<slug>/
POST /t/<token>/api/<Action>   action 白名单（与桌面 LoopAppPuppet 同一份实现）
```

- `<token>` 在 `LoopApp(create|open)` 时铸出（32 字节随机），内存里记 `token → (sessionId, slug)`；会话结束或页面关闭即失效。
- 页面 URL 里只有 token，没有 sessionId；不同 origin 也拿不到 Cookie。
- `<Action>` 与桌面完全同一份：`SessionState / SessionSubmit / SessionStop / AppClose`，签名里没有会话参数。
- 页面够不到 bus 端口的 `/api/*`：跨源、无凭证、无 CORS。

token 与 Cookie 的区别正是要害：Cookie 回答「这个人是谁」（全权），token 回答「这个页面被允许做什么」（一个会话）。

### 11.3 壳页面这一侧

- 右侧 Segmented 现在只有 Loop / 详情 / 旁路（`wwwroot/app.js:516`），加一项「应用」：`iframe` 指向 `http://127.0.0.1:<loopapp>/t/<token>/`，占满面板。
- 打开时机：`LoopApp(open)` 把 `activeLoopApp { slug, token, title }` 写进会话快照，壳页面已有的 800ms 轮询（`app.js:1464`）取到就自动切到「应用」页——对应「默认弹出」。
- 工具栏：「关闭」清掉 `activeLoopApp`；「在新窗口打开」用 `window.open`（需要用户手势，agent 触发会被弹窗拦截，这一步只能由用户点）。

### 11.4 能力降级（诚实版）

| 动作 | 桌面 | Web | 说明 |
|---|---|---|---|
| `create` / `open` | ✓ | ✓ | |
| 改文件 + 刷新 | ✓ | ✓ | Web 靠版本戳轮询，慢 0.8–3s |
| `eval` | ✓ | ✗ | 浏览器里没有远程执行入口；工具明确回「当前壳不支持 eval，改文件后页面会自动刷新」 |
| `shot` | ✓ | ✗ | 只能走 WebPuppet 的 DOM 截图钩子（需页面勾选），不纳入默认路径 |
| `undock` | ✓ | 用户点按钮 | `window.open` 需要手势 |
| 页面 → agent | ✓ | ✓ | 同一份 action |

### 11.5 迭代循环的差别

- 桌面：`改 app.js → eval("location.reload()")`，~50ms。
- Web：`改 app.js → 页面下次轮询发现 mtime 变了 → location.reload`，≤800ms。

确定性相同，慢一档。`ui.patch` 这类不重载的微调仍可用（页面自己调），但 agent 侧没有 `eval`，迭代退化为纯落盘。

### 11.6 两侧共用什么

- action 清单与实现：`ILoopAppHost` 只负责「把 action 送到 owner 会话」；桌面实现走 Hub，Web 实现走进程内 `WorkerSessionHost`——两者都在同一个进程，差别只在入口。
- 生成物布局、`openui.*` 运行时、技能正文：完全相同，不按壳分叉。

## 12. 成本与风险

- 单应用 token：简单表单 ~1.5–3k，富交互 ~8–15k。迭代一轮只改 `app.js` 的 diff。
- **agent 看不见自己画的东西**：`shot()` 只给用户看，模型侧只能靠 DOM 断言（`scrollHeight`、`getBoundingClientRect`、计算样式）自检。这是最大的体验缺口，`openui.js` 里要配一组 `ui.assert*`。
- 桥是新的攻击面：页面消息进 Hub 前只做三件事——校验来源 `https://openui.local/`、只接受 JSON 基本类型、action 名必须是本实例的 `[PuppetAction]`；不做参数清洗（实例方法签名已无会话参数，§9.2）。

## 13. 分期与验收

| 阶段 | 内容 | 验收 |
|---|---|---|
| **P0** | 按会话映射 + `LoopApp` 工具 + `src/openui` 骨架 + skill + 消息桥 + 会话级 Puppet 实例（§9） | agent 从零生成带表格与表单的页面；用户点「提交」，数据作为一条消息进入该会话并触发下一轮；**两个会话各开一个 LoopApp，A 的页面无论调什么 action，B 的 `busy/queueLen` 前后不变** |
| **P1** | `app.json` 控制浮动/尺寸；`drain` 兜底通道；Loop 侧对齐 | Loop 会话里同样的应用可用；独立窗口尺寸按声明生效 |
| **P2** | examples 精选、`list`；Loop 把好设计提炼进 `runtime/components.md`；**Web 壳**（独立端口 + token + 右侧「应用」页，§11） | 复用率可观察；Web 壳里同一个 LoopApp 可开、可提交，且页面拿不到 `/api/*` |

## 14. 待定

1. **默认停靠还是默认浮动**：需求原文是「默认弹出内嵌 webview（用户可选收起 dock 到右侧栏）」。当前按「自动打开并选中网页页、默认停靠右栏、用户可切独立窗口」设计；若要默认浮动，`open` 的默认值反过来即可，但会抢焦点。
2. **3D**：是否预置 three.js（~700KB 磁盘、不进上下文）。倾向预置但默认不引用。
3. **`SessionSubmit` 的默认排队语义**：默认「等本轮」（不打断）还是「立即」（软打断本轮）——前者更像用户点按钮，后者响应更快。
4. **Web 壳的优先级**：现在排在 P2。若你日常主用 Web 壳（而非 WinForms 壳），它就该提到 P0——两者共用同一份 action 与生成物，但 Web 侧要多一个监听端口和一页 UI。
