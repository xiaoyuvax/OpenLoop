# OpenLoop 开发约定

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
- 运行前先结束旧进程（`OpenLoop.exe` 文件锁）：`Stop-Process -Name OpenLoop -Force`
- 启动：`Start-Process <bin>\OpenLoop.exe -WorkingDirectory <repo根>`（默认工作区 = CWD）

## 运行时验证（Puppet A 面）

- 必须带 `OPENLOOP_PUPPET_KEY`；端口 9090 常被 VeniceMan 占用，会自动落到 9091+。
- `POST /agent/invoke?instance=MainForm&method=<名>`，body 为参数数组。
- `GET /agent/get?instance=MainForm&path=<属性>`；`POST /agent/set` body 为 `{"path":..,"value":..}`。
- `POST /agent/invoke ... method=CaptureAction` body `[x,y,w,h]`，返回 base64 PNG（用于看真实渲染）。
- `/agent/control` 只支持原生 WinForms 控件；AntdUI 的按钮/菜单点不了 → 给 MainForm/SessionView 暴露公开方法供 A 面驱动。
- 截图前若窗口被最小化，先 `ShowWindow(hwnd, 9)`。

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

## 设计器兼容（强制）

- 每个 Form/UserControl 必须有 `.cs` + `.Designer.cs`；字段声明、创建、属性设置、`Controls.Add` 全在 `InitializeComponent` 内。
- 构造函数只调用 `InitializeComponent()`；运行时初始化走 `Initialize(...)`。

## 文本与编码

- 源码/文档统一 UTF-8；含中文的文件**禁止**用 PowerShell 做文本往返（会变乱码），用编辑工具改。
- 不用 shell 重定向写源码/配置。

## AGENTS.md 与提示词

- `<workspace>/AGENTS.md` 会在每次任务开始时作为系统提示词的“项目约定”段落注入（`AgentsInstructions.Load`，上限 32KB）。
- 本文件即 OpenLoop 自身的项目约定；改动会影响 agent 行为，保持短、可执行。

## 纪律

- 未明确要求不提交；提交前看 `git status` / `git diff`。
- 破坏性操作先 dry-run。
- 文档只写已验证的事实；计划与完成分开。
