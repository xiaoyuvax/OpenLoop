## 1. 直接说：「解析不出来就打回去重发」并不是 A 方案的核心

A 方案（Claude Code、Cursor、OpenAI Agents SDK）真正在做的是：根本不需要解析。服务端返回的参数已经是对象，客户端只做 schema 校验。重发只发生在一种情况下：参数合法 JSON、但不合 schema（缺必填、类型不对），此时把校验错误作为 tool_result 回喂，模型改参数再发。

如果只是「解析失败就打回去重发」，问题有三个：

- **模型不知道错在哪**。你回一句「格式无效，请按协议输出」，它看到的上下文里只有自己那段坏输出 + 一句笼统警告，大概率原样再来一次。我们这次的实测就是连撞 3 次同一个形状（`<｜｜DSML｜｜> invoke` + args 多一个 `}`），两次纯浪费。
- **每轮都是全上下文往返**。几十步的任务，一次格式错就多烧一次完整 prompt。
- **重发不解决根因**。`<｜｜DSML｜｜>` 是模型的聊天模板特殊 token，它出现在正文里说明**网关没按该模型的 tool 模板处理**（没下发 `tools`，或下发了但没解析回来）。这是请求层/网关层的配置问题，不是模型「不听话」，重发多少次都一样。

所以顺序应该是：**先给结构化通道 → 再在请求层剥掉模板 token / 设 stop → 最后才是本地修补 + 一次带具体错误的纠正**。重发是兜底，不是主力。

顺带一个容易漏的点：`finish_reason=length` 导致的坏 JSON 是**被截断**，不是写坏。这类应该续写或加预算，不能计入 `MaxMalformedRetries`——我们现在混在一起算，会更快撞停机线。

## 2. 「结构」具体长什么样

三家的线上格式（注意细节差别很大）：

**OpenAI Chat Completions** —— 注意 `arguments` 仍是 JSON **字符串**，但受服务端约束生成，不会写坏：
```json
{
  "role": "assistant",
  "content": null,
  "tool_calls": [{
    "id": "call_abc123",
    "type": "function",
    "function": { "name": "read_file", "arguments": "{\"path\":\"a.txt\"}" }
  }]
}
```
`finish_reason` 是 `"tool_calls"`。回结果时用 `{"role":"tool","tool_call_id":"call_abc123","content":"..."}`。

**Anthropic Messages** —— `input` 是真对象，不是字符串：
```json
{"role":"assistant","content":[
  {"type":"text","text":"我先看一下。"},
  {"type":"tool_use","id":"toolu_01","name":"read_file","input":{"path":"a.txt"}}
]}
```
回结果用 `{"type":"tool_result","tool_use_id":"toolu_01","content":"..."}`。

**OpenAI Responses API**：`{"type":"function_call","call_id":"...","name":"...","arguments":"..."}`。

请求侧还要先声明工具，schema 是 JSON Schema：
```json
{"tools":[{"type":"function","function":{
  "name":"read_file",
  "parameters":{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
}}]}
```

关键不是「JSON 长得不一样」，而是**工具调用是消息结构里的一等公民**：有 type、有 id、有独立的内容块，跟正文文本是两个字段。

## 3. 文本协议 vs 结构化：差在哪儿

| | 文本协议（OpenLoop 现在） | 结构化 tool_calls |
|---|---|---|
| 载体 | assistant 正文里的一行文本 | 独立字段 / content block |
| 边界 | 靠括号配对、正则猜哪儿结束 | 数组元素，天然有边界 |
| 解析责任 | 客户端（我们）写容错 | 服务端已解析，客户端只校验 schema |
| 生成约束 | 模型自由发挥 JSON 语法 | schema 约束 / 语法引导解码 |
| 调用↔结果配对 | `tool-result:` 塞回 user 消息，只能靠内容猜 | `call_id` / `tool_use_id` 精确对应 |
| 并行调用 | 多个 marker，容易串参（我们踩过 ReadFile 里挂上 Shell 的 argv） | 数组多个元素，互不干扰 |
| 输出预算 | 每个字符都要模型生成 | 服务端直接给结构 |
| 典型失败 | 语法错、模板 token 泄漏、被截断、混进正文 | 只剩 schema 校验错、截断 |
| 兼容性 | 任何模型都能用（含无 tool 能力的本地模型） | 需模型 + 网关支持 |

这次的现场就是表格里「模板 token 泄漏」那一行：模型本来就在生成它训练时的原生调用格式，只是被当成普通文本吐出来，没被模板层吃掉。`NormalizeTags` + `ParseInvokeBlocks` 是在客户端逆向还原一个本来不该由我们解析的东西——能修，但每遇到一个新模型就得追一次装饰前缀。

**对 OpenLoop 的落地顺序**：① 确认 reasoner 是否已下发 `tools` 并在返回里取 `tool_calls`（`CompletionResult.NativeToolSucceeded` 目前只覆盖推理通道内的原生工具，主通道是不是纯文本要核实）；② 主通道走原生，文本协议降级为不支持 tool 的模型/网关的回落；③ 请求里把 `<｜｜DSML｜｜>`、`<|DSML|>` 设为 stop 序列，从源头堵住泄漏；④ 坏 JSON 先看 `finish_reason`，截断走续写，写坏才计入纠正次数。