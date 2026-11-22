实测通过。agent2api 确实支持结构化通道，新链路跑通了。

## 1. 网关能力探测（单次原始请求）

`POST http://127.0.0.1:3065/v1/chat/completions`，模型 `deepseek-v4.1-flash`，带 `tools`：

```json
"finish_reason": "tool_calls",
"message": {"content":"I'll read the file for you.", "tool_calls":[
  {"id":"call_00_jdskmn43lbe6xaqku1xu5mdx","type":"function",
   "function":{"name":"ReadFile","arguments":"{\"path\": \"probe.txt\"}"}}]}
```

结论：agent2api 会透传 `tools` 并返回原生 `tool_calls`，不是「只能走文本协议」。

## 2. 端到端实测（真实 AgentLoop + 真实工具）

测试 `tests/OpenLoop.Core.Tests/NativeToolRealModelTests.cs`，系统提示词**刻意不提 `tool-call:` 文本协议**（只有 "Use the tools you are given"）。文件能产出，就只能是原生通道生效——模型没有任何文本协议可抄。

| 模型 | 步数 | 工具序列 | malformed | 耗时 |
|---|---|---|---|---|
| `deepseek-v4.1-flash` | 2 | `WriteFile(hello.txt, HELLO)` → `ReadFile(hello.txt)` | 0 | 69 s |
| `GLM-5.3-Flash` | 1 | `WriteFile(content=HELLO, path=hello.txt)` | 0 | 12 s |

deepseek 那次两轮工具调用说明**多轮配对也通了**：第 1 轮原生 `tool_calls` → 结果以 `role:"tool"` + `tool_call_id` 回填 → 第 2 轮再发原生调用 → 最终正文。文件内容断言 `HELLO` 通过。

对比同一模型的旧行为：会话 `e1a292d003233a94` 里它吐的是 `<｜｜DSML｜｜> invoke name="Shell">` + 坏 JSON，撞满 3 次停机。

## 3. 变更清单（工作树，未换二进制）

- `src/OpenLoop.Core/Reasoning/NativeToolWire.cs`（新）：散文 schema → JSON Schema、文本历史 → `tool_calls`/`role:"tool"`、流式增量拼装、原生调用 → 一行一条 `tool-call:`
- `src/OpenLoop.Core/Reasoning/OpenAiCompatibleReasoner.cs`：`RequestPayload` 加 `Tools`/结构化 `Messages`；`Parse` 补读非流式 `tool_calls`（原来只读 `content`，原生调用会被静默丢掉）；`PreferNativeCalls` 防同一次调用执行两遍
- `src/OpenLoop.Core/Agent/AgentLoop.cs:65`：`CreateContext` 里注入工具表（单一入口，四个宿主都走 `loop.RunAsync`）
- `src/OpenLoop.Core/Agent/StateMachine/AgentSteps.cs:576`、`AgentLoop.cs:157`：`finish_reason=length/max_tokens` 单独提示「被截断，请拆小调用」，不再算成「模型不会用协议」
- `src/OpenLoop.Core/Agent/SystemPromptBuilder.cs:134`：删掉点名 DSML / tool_calls 的负面指令（对低能力模型是诱导）
- `tests/OpenLoop.Core.Tests/NativeToolWireTests.cs`（新，7 例）+ `NativeToolRealModelTests.cs`（新，默认跳过）

回落仍保留：网关 400/422 且报错指向 `tools` 时置 `_toolsUnsupported`，此后该通道退回文本协议；`capabilities.tools == false` 的模型同理。

## 4. 验证

- `dotnet build src\OpenLoop.Core` / `src\OpenLoop.Service`：0 错误
- `dotnet test tests\OpenLoop.Core.Tests`：343 通过 / 1 跳过 / 0 失败（344 总计）
- 真实模型 2 次，均无格式纠正、无重试

未覆盖：Shell 这类需要审批的工具、并行多调用（>2 条 `tool_calls` 同批）、以及不支持 `tools` 的网关实际回落——回落路径只有单测。生效需下次迭代换 A/I（Core 两侧共用）。