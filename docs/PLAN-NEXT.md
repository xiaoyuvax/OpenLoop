# 后续计划（跨机器继续）

> 对应 `CHARTER.md` charter/2。本文只列**未完成**的工作与已验证的现状；
> 已完成项以代码与 `bump` 后的测试为准，不重复描述设计动机。

---

## 0. 现状快照

已完成并验证：

- Worker 会话（AntdUI 外壳、多 Tab、项目→会话两层、新建会话 Tab、只读工作区路径）。
- Loop 侧：右侧面板分页（Loop / 详情）、独立 Loop Tab（聊天 + 工具 + 独立历史 `ChatStore kind="loop"`）、`打开 Loop 会话`。
- 主题：浅色 / 深色 / 灰蓝（`UiStyle.Apply`，☰ 菜单切换，重启生效）。
- `AGENTS.md` 注入系统提示词（`AgentsInstructions.Load`，上限 32KB）。
- 会话隔离：新会话不继承同路径历史；按会话 id 保存。
- 模型输出清洗：剥离 `User Safety: / Response Safety:` 行。
- 测试：Core 50 通过；ChatFlow 33 通过。

---

## 1. 环境准备（新机器）

1. .NET SDK 10.0.401+。
2. 同级目录需存在：`ChatFlow`、`PuppetCore`（`OpenLoop.slnx` 用相对路径引用）。
3. 构建/测试：
   ```powershell
   dotnet build OpenLoop.slnx
   dotnet test tests\OpenLoop.Core.Tests
   dotnet test ..\..\..\ChatFlow\ChatFlow.Tests
   ```
4. 运行时验证需要 `OPENLOOP_PUPPET_KEY`；端口 9090 可能被其他程序占用，会自动后退。
5. 本机 DPI 250%：运行时像素必须按 `DeviceDpi/96` 缩放（见根目录 `AGENTS.md`）。

---

## 2. 待做 A：本地化（卫星程序集）

**目标**：中文/英文切换，语言随设置保存，重启生效。

步骤：

1. 新增 `src/OpenLoop.App/Resources/Strings.resx`（中性资源=中文）与 `Strings.en.resx`（英文）；SDK 会自动编成卫星程序集（`en\OpenLoop.App.resources.dll`）。
2. 新增 `Localization.cs`：`ResourceManager("OpenLoop.App.Resources.Strings", asm)` + `T(string key)`；缺 key 时回退 key 本身。
3. `AppSettings.Language`（已存在，默认 `"zh"`）；`Program.Main` 在创建窗体前设置 `CultureInfo.CurrentUICulture`。
4. 设置界面加语言下拉（当前 `SettingsForm` 只有接口地址/密钥/模型）；保存后提示重启。
5. 替换文案（按文件分批，先外壳后细节）：
   - `MainForm.Designer.cs`：菜单项、标题/副标题、Tab 文本
   - `SessionOverview.Designer.cs`：项目/搜索/新建会话/状态列/空文本
   - `SessionView.Designer.cs`：模式、停止、只看免费、发送、占位符、Loop 页标签
   - `LoopView.Designer.cs`：标题、副标题、占位符、发送/停止
   - `NewSessionView.Designer.cs`：副标题、占位符、开始、项目
6. 验收：`language=en` 重启后上述区域均为英文；切回 `zh` 恢复；缺失 key 不崩溃。

注意：含中文的 `.resx` 用编辑工具写，禁止 PowerShell 文本往返（中文会坏）。

---

## 3. 待做 B：用户判定兜底（verdict）

**目标**：用户忘记/错标 `任务・判据・纠正` 时，由 Loop 从小模型从对话中抽取判定，写账本。

1. 新文件 `.openloop/verdicts.jsonl`（追加式）：
   ```json
   {"id":"V...","at":"...","role":"user","kind":"reject|accept|correction|complaint",
    "confidence":0.0,"quote":"原文片段","turn":12}
   ```
2. 触发点：`SessionView.SendTask` 结束后异步执行（不阻塞 UI、失败静默记账）。
3. 抽取方式：单次小模型调用（复用 `IReasoner`，提示词只用用户消息 + 上一轮 assistant 摘要）；结果与显式标签冲突时以显式标签为准，抽取结果标 `inferred=true`。
4. `inferred=true` 的判定**不得**用于门禁裁决（charter §3.2），只作为 Loop 的构思输入。
5. 验收：构造一轮“用户说不对/不完全对”的对话，`verdicts.jsonl` 出现 `reject`/`correction` 且带原文引用；显式 `判据` 模式下不产生冲突记录。

---

## 4. 待做 C：源码自举 + 基因组

见 `CHARTER.md` §12。实现顺序：

1. `src/OpenLoop.Core/Self/EvolveManifest.cs`：读 `<workspace>/evolve.json`
   ```json
   {"repos":[{"name":"openloop","url":"...","ref":"main","path":"src/openloop"}],
    "nuget":["Puppet.Core@1.x"]}
   ```
2. `SourceBootstrap.Ensure(manifest, log)`：
   - 目录存在且 `.git` 完整 → 跳过
   - 否则 `git clone --depth 1`（需要 `git` 在 PATH）
   - 失败 → 返回 `Scratch`，记录原因，**不抛异常**
3. 演进前调用；结果写入 trace（`bootstrap=clone|scratch|skip`）。
4. 基因组完整性检查（并入门禁第 2 层）：`charter` 摘要、manifest、双提示词、门禁规则、AGENTS 模板、账本 schema、回滚能力；缺项拒绝激活。
5. 源码模式构建：在 `git worktree` 中 `dotnet build` + 测试；二进制替换由外部重启完成（进程不能替换自身）。
6. 验收：删掉 `src/` 后启动仍可运行（scratch 记账）；断网时演进不中断；基因组缺项时门禁拒绝并留证据。

---

## 5. 待做 D：Loop 侧完善（低优先）

- 面板新增“轨迹”页（最近 N 条 trace 列表，点击打开目录）。
- Loop Tab 显示 `def` 版本与当前门禁状态；支持从 Loop Tab 直接触发“演进一轮”。
- `LoopView` 与 `SessionView` 的发送逻辑重复度较高，可抽 `AgentChatController`（仅当第三处出现时再做）。

---

## 6. 已知限制与坑

- AntdUI：`Menu` 竖向条目的 `SubText` 会被裁切 → 用单行文本；`Label.PrefixSvg` 需显式 `IconGap`。
- `/agent/control` 只能操作原生 WinForms 控件；AntdUI 按钮/菜单点不了 → 给 `MainForm` 暴露公开方法（如 `OpenLoop`、`ShowSidePage`、`SetNewSessionDraft`）。
- `Column.Width` 是 DIP；`SplitContainer.SplitterDistance`、`TabControl.ItemSize` 不随 DPI 缩放。
- ChatFlow 行内度量已按 DPI 缩放（本次修复）；若再改布局，保持 `Dip()` 一致，否则 250% 下文字会裁切。
- 设置文件用 PascalCase 序列化；读取已改为大小写不敏感（旧 camelCase 文件兼容）。
- 会话仅内存态；Worker 历史按会话 id 存于 `%APPDATA%\OpenLoop\chats`，重启后不自动恢复。

---

## 7. 完成判定

- `dotnet build OpenLoop.slnx` 无错误；Core 与 ChatFlow 测试全绿。
- 三类界面截图（浅色/深色/灰蓝）均无裁切、无错位。
- A/B/C 各自的验收条目在 `docs/` 留一条验证记录（命令 + 结果），不写计划当成已完成。
