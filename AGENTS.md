# OpenLoop 开发约定

> 通用规则（编码、PS/cmd 陷阱、提交纪律、dry-run、文档只写已验证等）见全局 `..\AGENTS.md`；本文件只记本项目特有约定。

## 定位

- OpenLoop 是一个自演进 agent 设施：**Worker（前台）** 面向用户干活，**Loop（后台观察者）** 观察、收集、迭代版本。
- Worker 与 Loop 同进程、同生命周期，但活动周期不同：Worker 跟任务走，Loop 由事件驱动（可自主启动、跑到结束条件）。
- **每次只迭代一个角色**：迭代 Worker 时 Loop 不变，反之亦然；但二进制必须整体替换（同进程，不能分开换）。
- 版本定义（提示词/技能/策略/模型参数）与二进制分开管理；`.openloop/` 存运行态。

## 构建与验证

- 构建：`dotnet build OpenLoop.slnx`
- Core 测试：`dotnet test tests\OpenLoop.Core.Tests`
- ChatFlow 测试：`dotnet test ..\..\..\ChatFlow\ChatFlow.Tests`（跨仓库引用）
- 无独立 lint；Release 编译即类型检查。
- 不要用 `dotnet build -t:Compile` 做语法检查（跳过资源生成，之后的增量构建会复用坏产物）；若用过必须 `dotnet build --no-incremental` 重建。
- 运行前先结束旧进程（`OpenLoop.exe` 文件锁）：`Stop-Process -Name OpenLoop -Force`
- 启动：`Start-Process <bin>\OpenLoop.exe -WorkingDirectory <repo根>`（默认工作区 = CWD）

## 运行时验证（Puppet A 面）

- **只用 Puppet 调试**：禁止 UIAutomation、`SendKeys`、`keybd_event`、模拟鼠标/拖动、系统级截屏——会抢焦点、误触菜单。要画面用 `CaptureAction`（应用内截图，不抢输入）；要状态用 `/agent/get`、`/agent/state`、`/agent/logs`。Puppet 缺能力时在宿主加公开方法/属性，不要用 UI 自动化绕过。
- 必须带 `OPENLOOP_PUPPET_KEY`；端口 9090 常被 VeniceMan 占用，会自动落到 9091+。
- `POST /agent/invoke?instance=MainForm&method=<名>`，body 为参数数组；路径参数用正斜杠（反斜杠会被 JSON 转义吞掉）。
- `GET /agent/get?instance=MainForm&path=<属性>`；`POST /agent/set` body 为 `{"path":..,"value":..}`。
- 复杂对象判空看 `type` 不看 `value`：`value:null` + 真实 `type` = 有值；`type:null` 才是空。
- `POST /agent/invoke ... method=CaptureAction` body `[x,y,w,h]`（相对元素），返回 base64 PNG（用于看真实渲染）。
- `/agent/control` 只支持原生 WinForms 控件；AntdUI 的按钮/菜单点不了 → 给 MainForm/SessionView 暴露公开方法供 A 面驱动。
- 截图前若窗口被最小化，先 `ShowWindow(hwnd, 9)`。
- **远程 API 测试须先经用户同意**：会调用真实模型/网关（产生费用）的操作，未经同意不得通过 UI、Puppet 或单测触发；纯本地测试（纯函数、控件状态、文件解析）不受限。

## UI 规则

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
