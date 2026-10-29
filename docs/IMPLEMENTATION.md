# OpenLoop 实施方案

> 对应 `CHARTER.md` charter/3。本文是实现计划，不是规范。
> 范围：M1（可工作的通用 WinForms agent，含共用对话控件抽取）。M2/M3 只列形状。
> 未完成工作与跨机器交接见 `docs/PLAN-NEXT.md`。

---

## 1. 目标与范围

**M1 结束时应当有三样东西：**

1. `ChatFlow`：独立仓库的 WinForms 对话控件库，零业务依赖，VeniceMan 与 OpenLoop 共用
2. `OpenLoop.Core`：不依赖 UI 的 agent 库（循环、工具、trace、推理边界）
3. `OpenLoop.App`：WinForms 宿主，在真实目录里跑完一次任务，trace 完整落盘

**M1 当时不做**（后续里程碑已补，见 §7）：Loop 角色、账本、门禁、更替、任务集、流式输出。
**始终不做**：Puppet 接入、多 provider 抽象。

---

## 2. 仓库与项目布局

```
C:\Users\Xiaoyu\source\repos\
├── ChatFlow\                      新建：共用对话控件库
│   ├── ChatFlow\
│   │   ├── ChatFlow.csproj        net10.0-windows, UseWindowsForms
│   │   ├── ChatFlowControl.cs
│   │   ├── ChatMessageRow.cs
│   │   ├── ChatTextStyler.cs
│   │   ├── ChatModels.cs
│   │   ├── ChatTheme.cs
│   │   └── ChatMedia.cs
│   └── ChatFlow.Tests\
│       ├── ChatFlow.Tests.csproj
│       ├── ChatTextStylerTests.cs
│       ├── ChatThinkingRowTests.cs
│       └── ChatLinkClickTests.cs
│
└── OpenLoop\
    ├── CHARTER.md
    ├── docs\IMPLEMENTATION.md     本文
    ├── src\OpenLoop.Core\         net10.0，仅 BCL
    ├── src\OpenLoop.App\          net10.0-windows, UseWindowsForms
    ├── tests\OpenLoop.Core.Tests\ net10.0
    └── OpenLoop.slnx
```

**依赖方向**

```
OpenLoop.App ──▶ OpenLoop.Core
     └────────▶ ChatFlow ──▶ Vax-FCTB 2.19.0.223
```

- `ChatFlow` 不引用 `OpenLoop.Core`，也不引用 VeniceMan 任何东西。
- `OpenLoop.Core` 不引用 `ChatFlow`，不引用 WinForms。
- 两者之间的适配只有 App 里十几行。

引用方式：**ProjectReference**（源码引用），暂不出 NuGet。跨仓库相对路径：

```
src\OpenLoop.App\OpenLoop.App.csproj
  → ..\..\..\ChatFlow\ChatFlow\ChatFlow.csproj
```

---

## 3. 里程碑与顺序

| 里程碑 | 内容 | 验收 |
|---|---|---|
| **M1a** | 抽取 `ChatFlow` | 独立测试程序能渲染含 markdown / 工具行 / 思考折叠的对话；三个测试文件全绿 |
| **M1b** | 抽取 `OpenLoop.Core` | 库不认识 WinForms / VeniceMan；解析、压缩、重复检测、参数自愈、话术护栏有单测 |
| **M1c** | `OpenLoop.App` 接线 | 真实目录跑完一次任务，`.openloop/traces/` 四件齐全 |
| **M1.5** | VeniceMan 改引用 `ChatFlow` | OpenLoop 跑通之后再动 |

先做 M1a：它是唯一需要改动别处已有代码的部分，越早暴露问题越好。

---

## 4. M1a — 抽取 ChatFlow

### 4.1 搬运清单

| 源（VeniceMan） | 目标 | 处理 |
|---|---|---|
| `Controls\ChatFlowControl.cs`（37 KB） | `ChatFlowControl.cs` | 改命名空间、切主题、去资源 |
| `Controls\ChatMessageRow.cs`（19 KB） | `ChatMessageRow.cs` | 改命名空间、切主题 |
| `Controls\ChatTextStyler.cs`（34 KB） | `ChatTextStyler.cs` | 改命名空间、切主题、去 AttachmentHelper |
| `Models\ChatTurn.cs`（6 KB） | `ChatModels.cs` | 改类型名、并入 ChatMessage |
| （新） | `ChatTheme.cs` | 调色板 + 事件 |
| （新） | `ChatMedia.cs` | 图片扩展名判断 |

三个源文件均为 UTF-8 无 BOM。搬运用逐字节复制，再定点修改，不做编码往返。

### 4.2 四个接缝（已核对，全部在此）

| # | 接缝 | 出现处 | 处理 |
|---|---|---|---|
| 1 | `ThemeMan.CurrentPalette` | 控件 3 处、行 1 处、样式 1 处 | → `ChatTheme.Current` |
| 2 | `ThemeMan.ThemeChanged` | 控件订阅/退订各 1 处 | → `ChatTheme.Changed` |
| 3 | `Venice.Models` | 控件与行（`ChatTurn` 等）、`LlmMessage` | → 库内 `ChatMessage` |
| 4 | `Properties.Resources.arrow_up_color_16xLG` | `ChatFlowControl.cs:708` | **改为 GDI+ 画箭头**，不带资源 |
| 5 | `Venice.Agent.AttachmentHelper.IsImage` | `ChatTextStyler.cs:146` | → `ChatMedia.IsImage` |

### 4.3 `ChatPalette` 的字段

只收被实际用到的成员（已逐处统计）：

```csharp
public sealed class ChatPalette
{
    public Color Back, Fore, ForeDim, Accent, Border, Control, MenuHover;
    public Color InputBack, InputFore;
    public Color ChatFore, ChatThinkingFore;
}
```

语义约定：**`ChatTheme.Current == null` 表示浅色主题，控件回退到原有硬编码颜色。** 这条保留 VeniceMan 现有的双态行为，不新增主题框架。

```csharp
public static class ChatTheme
{
    public static ChatPalette Current { get; private set; }
    public static event Action Changed;
    public static void Set(ChatPalette p) { Current = p; Changed?.Invoke(); }
}
```

### 4.4 库内模型

```csharp
public sealed class ChatMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public Dictionary<string, byte[]> Attachments { get; set; }
}
```

`ChatTurn`、`ChatTurnBook`、`SystemMessageWrapper` 原样搬入（`LlmMessage` 换成 `ChatMessage`，`GetMessageText` 简化为 `.Text`）。
`ChatFlowItem`、`ChatFlowItemKind`、`ChatLinkClickedEventArgs` 保持留在 `ChatFlowControl.cs` 内，不移动，减少改动面。

**多模态取舍**：v0 的对话只显示文本；附件（`byte[]`）走 `Attachments`。VeniceMan 接入时把 `LlmMessage` 的文本项拼成 `Text` 即可，适配层不超过 20 行。

### 4.5 测试迁移

| 文件 | 需改 |
|---|---|
| `ChatTextStylerTests.cs` | `Venice.Controls` → `ChatFlow`（`venice.ai` 是测试文本，不动） |
| `ChatThinkingRowTests.cs` | 同上 + `ThemeMan` → `ChatTheme.Set(...)` + `LlmMessage` → `ChatMessage` |
| `ChatLinkClickTests.cs` | 同上 + `Venice.Models` → `ChatFlow` |

保留 `[Collection("ThemeState")]`——静态主题状态不能被并行测试争用。
测试项目需 `Xunit.StaFact`（STA 线程），与 VeniceMan 现有配置一致。

### 4.6 验收

- `dotnet build` 通过（net10.0-windows）
- `dotnet test` 通过（三个文件共 32 个用例）
- 一个临时 WinForms 冒烟程序：塞 5 条消息（含 markdown 代码块、工具行、折叠的思考区），肉眼确认渲染与滚动正常

---

## 5. M1b — 抽取 OpenLoop.Core

### 5.1 模块与文件

```
OpenLoop.Core\
├── Messages\LlmMessage.cs            Role + Content + Attachments
├── Reasoning\
│   ├── IReasoner.cs
│   ├── CompletionResult.cs
│   └── OpenAiCompatibleReasoner.cs
├── Agent\
│   ├── AgentLoop.cs                  移植 + 改造
│   ├── AgentPolicy.cs                新：护栏开关与阈值
│   ├── AgentToolCallParser.cs        原样移植
│   ├── AgentTool.cs                  AgentTool + AgentToolResult
│   ├── AgentArgs.cs                  原样移植
│   ├── AgentStepRecord.cs
│   ├── AgentRunResult.cs
│   ├── StateMachine\                 状态机管线
│   │   ├── AgentState.cs
│   │   ├── AgentRunContext.cs
│   │   ├── AgentStepResult.cs
│   │   ├── AgentEffects.cs
│   │   ├── AgentStateMachine.cs
│   │   └── AgentSteps.cs
│   └── SystemPromptBuilder.cs        新
├── Tools\
│   ├── ITool.cs
│   ├── ToolArgs.cs
│   ├── ToolRegistry.cs
│   ├── WorkspaceScope.cs
│   └── Builtin\
│       ├── ReadFileTool.cs
│       ├── WriteFileTool.cs
│       ├── ListDirTool.cs
│       ├── GrepTool.cs
│       └── RunCommandTool.cs
└── Trace\
    ├── TraceWriter.cs
    └── TraceRecord.cs
```

### 5.2 关键类型

```csharp
public interface IReasoner
{
    Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct);
}

public sealed class CompletionResult
{
    public string Content { get; init; }
    public decimal? Cost { get; init; }
    public string FinishReason { get; init; }
}
```

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    string ParamsSchema { get; }
    bool IsReadOnly { get; }
    Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct);
}
```

```csharp
public sealed class AgentPolicy
{
    public int MaxSteps = 100;
    public double CompressAt = 0.8;

    public bool RepeatCallGuard = true;   public int MaxRepeatCalls = 3;
    public bool ExplorationGuard = true;  public int MaxExplorationSteps = 30;
    public bool ClaimGuard = true;
    public bool PromiseGuard = true;
    public bool ArgRepair = true;
    public bool MalformedRetry = true;    public int MaxMalformedRetries = 2;
}
```

**不上 `IGuardrail` 接口**：五个护栏不可互换，只需独立开关。`AgentPolicy` 本身就是账本词汇表的运行时形态（`guard.repeat_call` → `policy.RepeatCallGuard`）。

**只读由工具声明**（`ITool.IsReadOnly`），不再像 VAgent 那样在循环里硬编码工具名单。

### 5.3 从 VAgent 的映射

| VAgent | 处理 |
|---|---|
| `AgentToolCallParser.cs` | 原样（仅换命名空间） |
| `AgentTool.cs` / `AgentArgs.cs` | 原样 |
| `AgentContext.cs` | 去 `PlatformMan`；上下文上限由参数传入 |
| `AgentLoop.cs` | 拆护栏进 `AgentPolicy`；`LlmMessage` 换库内类型；`FormatDuration` 换库内实现；只读判断改走 `ITool` |
| `AgentOptions` | 丢弃（A/B 通道、确认旁路属 VeniceMan） |
| `AgentPrompts.BaseSystemPrompt` | **不搬**。基础规则改由 `SystemPromptBuilder` 生成通用版本，VeniceMan 的领域规则以后作为规则特征提供 |
| 所有 `*Skills` / `SkillRegistry` | 不搬 |

### 5.4 通用系统提示（`SystemPromptBuilder`）

三段，全部通用，不含领域词汇：

1. 定位：你是一个在文本工作目录里工作的 agent，只能通过工具访问环境
2. 工具清单：`ITool.RenderPrompt()` 逐条
3. 协议与安全：`tool-call:` 单行协议、一轮一工具、不谎报完成、参数按说明修正、步数上限

### 5.5 工具（v0 五个）

| 工具 | 只读 | 参数 |
|---|---|---|
| `read_file` | 是 | `path`, `offset?`, `limit?` |
| `write_file` | 否 | `path`, `content` |
| `list_dir` | 是 | `path?`, `recursive?` |
| `grep` | 是 | `pattern`, `path?`, `glob?` |
| `run_command` | 否 | `command`, `cwd?`, `timeoutSeconds?` |

约束：

- 所有路径过 `WorkspaceScope.Resolve()`，越界即失败（返回 `AgentToolResult.Failure`，不抛异常）
- `run_command` 用 `ProcessStartInfo` + 超时 + 捕获 stdout/stderr；不做 shell 解释器包装以外的沙箱（v0 明示无沙箱）
- 单次工具输出回填模型前截断 6000 字符（沿用 VAgent 的上限）

### 5.6 测试

从 `AgentLoopTests.cs`（111 个）**选择性移植**，保留与机制相关的：

- 工具调用解析（含跨行、尾随文字、标签式、OpenAI 风格、截断）
- 上下文压缩切分（不拆散 tool-call / tool-result 对）
- 重复调用签名（长参数不误撞）
- 参数自愈（去空参、类型纠正、非参数错误不重试）
- 承诺话术 / 谎报完成判定
- 步数预算与软停止

丢弃图像、模型册、Venice 领域相关的用例。

### 5.7 验收

- `dotnet build` + `dotnet test` 全绿
- `OpenLoop.Core.csproj` 的 `PackageReference` 为空
- 用假 `IReasoner` 能跑完一轮带工具的脚本化对话（单测内即可）

---

## 6. M1c — OpenLoop.App 接线

### 6.1 界面

```
┌───────────────────────────────────────────────────────────┐
│ workspace [路径....] [浏览]  baseUrl [..] model [..] key [**]│
├──────────────────────────────────┬────────────────────────┤
│ ChatFlowControl（对话 + 思考折叠） │ loop 面板（M1 只读占位） │
│                                  │ workspace / 本 run 成本 │
│                                  │ def: —（M2 才有）       │
├──────────────────────────────────┴────────────────────────┤
│ [输入框]                  [发送任务] [声明判据] [这是纠正]   │
└───────────────────────────────────────────────────────────┘
```

- 对话区用 `ChatFlowControl`，数据源 `ChatTurnBook`
- 步骤不另开列表：工具行直接进 `ChatTurn.ToolCallLines`，思考区显示 `ToolTraceText`（复用控件已有能力，不新造视图）
- 三个按钮把消息标签（`task` / `criterion` / `correction`）写进 trace；M1 不消费这些标签

### 6.2 配置与密钥

默认走 OpenRouter：`https://openrouter.ai/api/v1` + `openrouter/free`。

`%APPDATA%\OpenLoop\settings.json`：

```json
{ "baseUrl": "...", "model": "...", "keyProtected": "<base64>", "workspace": "..." }
```

- 密钥用 `ProtectedData.Protect(..., CurrentUser)`（.NET 10 自带，无需 PackageReference）
- 密钥框留空时回退读环境变量 `OPENROUTER_API_KEY` / `OPENAI_API_KEY`
- `免费模型` 按钮实时查 `/api/v1/models`，按 `pricing` 全零过滤——免费清单会变，不硬编码 id
- `测试连接` 发一次最小补全，直接暴露真实错误
- 对话历史按工作目录分文件存 `%APPDATA%\OpenLoop\chats\`（不进工作区）
- `"loopCommand": ["exe","arg",...]` 非空时，Loop 的构思走 `ProcessReasoner`（外部进程，stdin 收提示词）；留空则用同一个 OpenRouter 通道
- **密钥不进 workspace**，避免随仓库提交
- 界面上的 key 输入框只写不显

### 6.3 数据流

```
输入 + 标签
 → TraceWriter 开 .openloop/traces/T-<时间>-<随机>/
 → ToolRegistry(policy, workspace) 提供 5 个工具
 → SystemPromptBuilder 生成 system
 → AgentLoop.RunAsync(history, userMessage)
      OnStep        → ChatTurn.ToolCallLines / ToolTraceText
      OnToolStart   → ChatTurn.ToolStatusText
 → final → ChatTurn.AssistantText，Streaming=false
 → TraceWriter 落 run.json / messages.jsonl / steps.jsonl / final.md
```

### 6.3.1 状态机管线

`AgentLoop.RunAsync` 现在只是便捷驱动：它创建 `AgentState` 与 `AgentRunContext`，交给 `AgentStateMachine` 反复 `StepAsync → Apply`，直到 `Completed/Yielded/Cancelled`。

- 状态：`AgentState`（消息 + 轮次/护栏计数 + 待处理工具/审批）。
- 一步：`IAgentStep.RunAsync` 返回 `Pass`（不认识）或 `Applied`；`Applied` 带 `AgentStepStatus` 与 `IAgentEffect`。
- 效果：追加/替换消息、记录 usage、记录工具步骤、设置最终答案/预算/停止；UI 流式回调仍由步骤直接触发。
- 管线顺序（`AgentPipeline`）：工具执行 → 进度护栏 → 步数预算 → 回答护栏 → 软停止 → 上下文压缩 → 推理。
- 停机语义：预算耗尽 → `BudgetExhausted`；用户停止 → `StoppedByUser`；护栏判定无进展/格式持续错误 → `StoppedByGuard`。宿主把三者映射为 `budget` / `stopped` / `ok`，不再把护栏停机记成 `ok`。
- 审批：探索护栏达到阈值时返回 `Yielded`；`ConfirmContinue` 存在时由驱动方询问后继续，否则把控制权交回调用方。
- 逐步驱动：`AgentLoop.CreateState/CreateContext/CreateMachine` + `StepAsync/Apply`；宿主可只走一步或把 `Messages`/`Pending*` 落盘后续跑。

行为与旧循环一致，回归由 `AgentLoopTests` 覆盖；状态机本身由 `AgentStateMachineTests` 覆盖（Yield 解析、逐步驱动、审批继续）。

### 6.3.2 运行中的请求队列

- 运行期间输入框保持可用；`Ctrl+Enter`/发送按钮提交的请求进入 `SessionView` 的待发送队列，不再打断当前轮。
- 队列以抽屉形式出现在聊天区下方、输入框上方（高度封顶 150 DIP），每行提供「撤回」（移回输入框）、「移除」、「立即发送」（取消当前轮并优先发送该条）。
- 发送按钮只有一个：输入框有文字 = 发送（排队），清空 = 停止。停止会暂停自动续跑，抽屉里的「全部发送」恢复。
- 当前轮结束后按队列顺序自动发送下一条；打断项优先于队列顺序。队列只在内存，关闭进程即丢弃。
- A 面驱动：`MainForm.RemovePending/RetractPending/SendPendingNow(index)`、`SendAllPending()`，`PendingCount/PendingSummary` 只读。

### 6.4 trace 格式

```
.openloop/traces/T-20261022-153012-7f3a/
  run.json
  {
    "id": "T-...", "startedAt": "...", "endedAt": "...",
    "workspace": "...", "model": "...", "tag": "task",
    "prompt": "...", "cost": 0.0, "outcome": "ok|budget|stopped|error"
  }
  messages.jsonl   每轮一行：{ role, content }
  steps.jsonl      每步一行：{ index, tool, args, ok, durationMs, resultPreview }
  final.md         最终回答
```

全部 append-only；`run.json` 在结束时一次写入。

### 6.5 验收

- 在真实目录里完成一次"读文件 → 改内容 → 跑命令确认"的任务
- `.openloop/traces/` 四件齐全，`steps.jsonl` 与界面显示一致
- 符合性三问的第一问可当场演示（"把上次运行调出来"）
- 应用退出后重新打开，能加载历史对话——**已做**：`App\ChatStore` 按工作目录存 `%APPDATA%\OpenLoop\chats\`，每次运行结束与关窗时保存

---

## 7. M2 / M3 已实现

**M2**：

- `Store\Records`：`FeatureRecord` / `FeatureRef` / `ActivationRecord` / `VerdictRecord` / `RunRecord`
- `Store\Ledger`：append-only `.openloop/ledger.jsonl` + 一次写定的 `.openloop/defs/vN.json` + `eval\` 目录
- `Store\FeatureCatalog`：12 条种子特征（9 护栏/阈值 + 3 规则），激活记录 → `AgentPolicy` / 规则文本
- `AgentPolicy` 从当前激活加载；**未出现在激活里的护栏默认关闭**——这就是"收窄"
- 规则按激活顺序进 `SystemPromptBuilder` 的「经验规则」段
- 每次运行写一条 `run` 证据（版本、结果、步数、成本、trace 指针）
- `Rollback(version)` 写新版本 + `rolled-back` 裁决，不覆盖任何旧文件
- 验收：`LedgerTests` 覆盖种子、id 复用拒绝、收窄、回滚不改旧文件、规则顺序、证据成本

**M3**：

- `Loop\CandidateValidator`：只能引用账本已有特征；参数有范围；规则不接受参数；布尔/数字类型校验
- `Loop\Conformance`：可回指 / 可还原 / 可裁决 / 外部裁决 / 可回滚，五项都是结构检查
- `Loop\Gate`：构建检查（可选注入）→ 符合性 → 任务集 A/B；**严格更好才更替，平局判旧版**；无论结果都写裁决
- `Loop\AgentTaskRunner`：每题在 `.openloop/eval/runs/` 下开隔离目录，播种 → 真 Worker 执行 → 自动判定，留 `final.md` / `run.json`
- `Loop\LoopAgent`：读账本生成构思包（**不含考核题**，O5 隔离），只收 JSON 候选，写完过校验
- 失败候选存 `.openloop/eval/candidates/`（休眠，留证据，不影响当前版本）
- UI：侧栏 `演进一轮` / `回滚上一版`
- 验收：`GateTests` 覆盖采纳、平局判旧版、构建失败短路、符合性失败短路、校验器、指纹、判定器、构思解析、隔离执行

**M3 收尾（已完成）**：

- `Reasoning\ProcessReasoner`：提示词走 stdin、回答取 stdout；Loop 可用 `settings.json` 的 `loopCommand` 切换成外部进程
- OpenRouter 请求带 `usage.include`，`usage.cost` 解析进 `CompletionResult.Cost`，账本里的成本不再是空值
- 流式输出：`IStreamingReasoner` + SSE 解析；`AgentLoop.RunAsync` 加 `onDelta`，界面走 ChatFlow 的 UpdateStreamingMessage（250ms 合并重建）；工具调用片段不回显
- 对话历史持久化：`App\ChatStore`
- 验收：`ReasonerTests` 覆盖成本解析、SSE 解析、外部进程往返与退出码

**未实现**：Puppet 接入、VeniceMan 改造（M1.5）。

**考核题格式**（`.openloop/eval/tasks/*.json`，由 User 提供）：

```json
{
  "id": "write-hello",
  "problem": "基础写入",
  "prompt": "在当前目录创建 hello.txt，内容写 ok。",
  "seed": { "notes/a.txt": "起始内容" },
  "checks": [
    { "type": "file_exists", "path": "hello.txt" },
    { "type": "file_contains", "path": "hello.txt", "text": "ok" },
    { "type": "file_equals", "path": "hello.txt", "text": "ok" },
    { "type": "file_missing", "path": "old.txt" },
    { "type": "command", "args": ["cmd", "/c", "exit", "0"], "expectExit": 0 }
  ]
}
```

**护栏观测**：`AgentRunResult.Guards` 记录 repeat/exploration/claim/promise/empty/malformed/argRepair/compress 的命中次数，写入 trace 的 `run.json` 和考核运行的 `run.json`（`guards` 字段），只用于评估，不参与决策。`samples/eval/tasks/` 提供四个探针任务（步数、谎报完成、占位话术、只读侦察误停），复制到 `.openloop/eval/tasks/` 后即可参与 A/B。

**提示词归因**：`run.json` 的 `systemPrompt.sha256/chars` 记录实际注入的系统提示指纹（不落原文），`binary` 记录 `RuntimeInfo.Version`，用于区分“提示词变了”还是“二进制变了”。

**规则登记**：`Ledger.AddRule(name, text)` 把用户规则写成休眠特征，id = `rule.<slug>_<sha8(正文)>`，同文重复登记幂等，改字即新 id。Worker 侧栏“新增规则”调用它；启用仍由 Loop 提案 + 门禁 A/B 决定。`LoopAgent` 的构思包含规则正文（截断 400 字），否则它无法判断该启用哪条。

**规则优化**：`RuleAssistant` 用独立系统提示（只润色规则、不执行任务）把用户草稿压成单行祈使句；结果只回填输入框，由用户检查后登记，不自动写入账本或激活集。

**设置（多栏）**：`SettingsForm` 分「普通」「模型」两栏。普通栏管理语言、主题、完全许可（Full Access）与 AGENTS.md 开关；模型栏管理多个 provider（名称、地址、密钥、模型列表），内置 provider 可拉取 `/models`，自定义 provider 手动添加。`AppSettings.EndpointFor(model)` 按模型找到 provider 的地址与密钥；会话模型下拉合并 provider 模型与 OpenRouter 目录。

**完全许可**：关闭路径越界检查、`.openloop` 保护、风险命令审批与子进程环境隔离（格式化/关机等 Denied 命令仍拒绝），因此可直连网络；仅作用于交互 Worker，Loop 与门禁运行保持受限。开启需二次确认。

**AGENTS.md 开关**：关闭后 Worker、Loop、门禁运行都不再注入全局与项目约定。

除新增 `onDelta` 回调外，`AgentLoop` 在 M2/M3 **没有改动**——只换它拿到的 `AgentPolicy`、工具集和提示词。这是 M1b 抽取是否成功的判据。后续已重构为状态机管线（见 6.3.1），公开的 `RunAsync` 签名不变。

---

## 8. 风险与已知缺口

| 风险 | 处理 |
|---|---|
| 抽取 ChatFlow 触碰 VeniceMan 现有行为 | 只复制不修改原地；三个测试文件随库走；VeniceMan 接入放到 M1.5 |
| 静态主题状态被并行测试污染 | 保留 `[Collection("ThemeState")]` |
| `arrow_up_color_16xLG` 换成自绘后观感变化 | 自绘保持 16×16 与同色，冒烟时肉眼核对 |
| 长任务界面像卡住 | **已解决**：流式 SSE + ChatFlow 的 UpdateStreamingMessage；工具调用片段不回显 |
| 单次 A/B 噪声 | 与本阶段无关（M3 才用），届时靠"判据确定 + 平局判旧" |
| `run_command` 无沙箱 | **P0 已落地**（见 §10）：argv-only、路径 realpath、环境白名单、危险命令拒绝/风险命令审批、每轮 git 快照、超时与输出上限 |
| Core 里混入 UI 依赖 | 验收项写死：`OpenLoop.Core.csproj` 无 `UseWindowsForms`、无 `PackageReference` |

---

## 9. 明确不做

- 多 provider 抽象（只做 OpenAI 兼容）
- Puppet 接入（`/agent/*`、`/appagent/*`、ActionHub）
- 主题系统、自定义绘制控件、第三方 UI 包
- VeniceMan 改造（M1.5）

---

## 10. 安全边界（P0，已验证）

跨平台、全部在 Core，不依赖 WinForms：

- **不经 shell**：`run_command` 只接受 `argv: string[]`；旧的 `command` 字符串直接报错。可执行文件与参数分开传递（`ProcessStartInfo.ArgumentList`）。
- **路径牢笼**：`WorkspaceScope` 先解析 symlink/junction（`ResolveLinkTarget`，最深已存在祖先 + 拼回尾部），再按平台比较大小写（Windows/macOS 忽略，Linux 敏感）。
- **环境白名单**：`ChildEnvironment` 只放行 PATH/TEMP/HOME/LANG 等，API Key 等敏感变量不进子进程。
- **命令策略**：`CommandPolicy` 分 Safe/Risky/Denied。Denied（`rm -rf /`、`format`、`shutdown`、`dd of=/dev/*`）直接拒绝；Risky（shell 解释器、`git reset --hard`、`git clean -f`、`git push`、包管理器安装、`rm/del`、`curl/wget`、`taskkill/reg/sc/net`）经 `Func<CommandRequest,bool>` 审批，无审批通道即失败。
- **快照**：任务开始前 `git stash create` + `refs/openloop/<时间戳>`，不触碰工作区；`Snapshots.Restore` 为显式回滚入口。
- **资源**：超时（1–600s，杀进程树）、输出截断（6KB）。

测试：`tests/OpenLoop.Core.Tests/SafetyTests.cs`（拒绝/风险/安全分类、`..` 与 symlink 逃逸、环境无密钥、argv 执行、快照）。

未做（P1）：Linux 的 bubblewrap/Landlock、Windows 的低完整性令牌/AppContainer、出网限制、内存上限。
