**根治 = 给直连通道接上原生工具通道。** 工作树里已有一版 `NativeToolWire` 半成品（编译不过、没接线），我把它补完并接进主链路。

## 改动

**1. 请求侧真正声明 `tools`**（`src/OpenLoop.Core/Reasoning/OpenAiCompatibleReasoner.cs`）
- `RequestPayload` 增加 `Tools`，`Messages` 改为结构化对象（`tool_calls` / `role:"tool"`）
- `BuildPayload` 在有工具表时带上 `NativeToolWire.Declare(Tools)`；压缩摘要这类单条 user 请求不带
- 历史里的 `tool-call:` / `tool-result:` 行由 `NativeToolWire.Messages` 翻成标准 `tool_calls` + `role:"tool"`，配对 id 在同一请求内自洽；结果缺失（压缩拆散）时丢弃悬空调用，避免网关 400

**2. 响应侧读回原生调用**
- 流式：`ParseStreamAsync` 已有 `MergeDelta` / `Complete`，翻成一行一条 `tool-call:`
- 非流式：`Parse` 新增 `NativeToolWire.ReadCalls`（原来只读 `content`，原生调用会被静默丢掉）
- 原生调用与文本协议同时出现时，切掉文本协议，避免同一次调用执行两遍（`PreferNativeCalls`）

**3. 注入点**（`src/OpenLoop.Core/Agent/AgentLoop.cs:65`）
- 在 `CreateContext` 里把 `_tools.Tools` 交给直连 reasoner，而不是构造函数——同一个 reasoner 可能被不同 scope 的 loop 复用

**4. 网关不认 tools 时自动回落**
- 已有逻辑：400/422 且报错指向 tools → 置 `_toolsUnsupported`，此后不带 tools，退回文本协议，不判整轮失败

**5. 截断与写坏分开**（`AgentSteps.cs:576`、`AgentLoop.cs:157`）
- `finish_reason=length/max_tokens` 的坏调用提示「被截断，请拆小调用」，停机文案也改为「拆小任务/换输出上限更高的模型」，不再误报「模型不会用协议」

**6. 提示词去掉负面点名**（`SystemPromptBuilder.cs:134`）
- 原句直接点了 DSML / tool_calls 这些 token 名，对低能力模型是诱导；改成不点名的「不要输出模型原生的工具标记格式」

## 阶梯容错怎么处理

**全部保留，但角色从「唯一通路」降为「回落通路」**，触发条件收窄：

| 层 | 位置 | 处置 |
|---|---|---|
| 协议形态识别（DSML/中文标记/无头块等） | `AgentToolCallParser` | 保留。只在网关拒绝 tools、或模型无视 tools 仍吐原生标记时用到 |
| 值级修补（括号配对、尾逗号、多残渣） | `ConvertValue` / `TryRepair` | 保留。上一轮修的那条就是它，回落路径仍需要 |
| 参数修补（去空参数、按报错类型强转） | `ExecuteWithArgRepairAsync` | 保留。与协议无关，原生通道下参数仍可能不合 schema |
| 格式纠正重试（上限 3 次） | `ResponseGuardStep` | 保留，但触发率应大幅下降；截断不再计入「格式错误」 |
| 显示层截断协议片段 | `CutToolCallText` | 保留。`RenderCalls` 生成的是合法文本，显示层截它是对的 |

不删的理由：`capabilities.tools == false` 的模型、不认 tools 的网关、OpenCode 免费模型之外的一些本地模型，都还得靠文本协议。删了就是把回落路径一起砍掉。

## 验证

- `dotnet build src\OpenLoop.Core` / `src\OpenLoop.Service`：0 错误
- `dotnet test tests\OpenLoop.Core.Tests`：342 通过 / 1 跳过
- 新增 `tests/OpenLoop.Core.Tests/NativeToolWireTests.cs` 7 个用例：schema 生成、历史翻转配对、悬空调用丢弃、非流式与流式响应读回、增量拼装、`RenderCalls` 闭环解析

没跑真实模型实测（会产生费用，需你同意）；没改 MCP 工具的 JSON Schema 解析（散文解析器不认 JSON，仍退成宽松对象），也没动 `finish_reason` 之外的重试策略。