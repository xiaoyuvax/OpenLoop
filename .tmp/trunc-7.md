重新判断后，结论是：**直连通道从头到尾没有声明过 `tools`**，所以模型没有结构化出口，只能用它训练时的原生模板表达；而聚合网关又把这段模板当普通文本原样吐出。这不是模型“不听话”。

## 一、证据：直连请求里没有工具通道

`src/OpenLoop.Core/Reasoning/OpenAiCompatibleReasoner.cs:554` 的 `RequestPayload` 只有 `model / stream / messages / usage / stream_options / reasoning / include_reasoning`：

- **没有 `tools`**，也没有 `tool_choice`、`parallel_tool_calls`
- **没有 `stop` / `stop_sequences`**
- `MessagePayload`（`:568`）只有 `role` + `content`，没有 `tool_calls`、`tool_call_id`、`role:"tool"`

也就是说：直连 teamorouter / agent2api 时，模型收到的是一段纯文本对话，外加一句“请用 `tool-call: {...}` 格式”。它**没有任何结构化工具定义可用**。

对比 OpenCode 通道：`OpenCodeClient.cs:129-133` 的会话会带工具表，模型的原生调用由 OpenCode 解析成结构化事件，我们再转成 `native:` 步骤（`OpenCodeReasoner.cs:168`）。所以那条路上不会泄漏，因为结构化出口是存在的。

## 二、泄漏的五个机制（按可能性排序）

**1. 模型有原生工具训练，但请求里没有 tools → 用训练模板表达**

`<｜｜DSML｜｜>` 这类特殊 token 是 DeepSeek / GLM / Qwen 系列训练时的聊天模板 token，在词表里。当模型判断“这一步该调工具”时，最高概率的续写就是它训练过几千遍的那套标记。请求里没有 `tools` 数组，网关就不会启动工具解析器，这些 token 被当普通文本解码进 `content`。

注意泄漏文本里用的是**全角竖线** `<｜｜DSML｜｜>`（U+FF5C）——这是 tokenizer 把特殊 token ID 解码成可见文本的结果。说明网关既没按工具模板解析，也没把这些 token 当特殊 token 处理，直接透传了。

**2. 聚合网关的工具解析只在带 `tools` 时启用，且流式路径最容易漏**

teamorouter / agent2api 这类中转做的是格式翻译（各家 → OpenAI 兼容）。多数实现里工具解析器挂在“请求带 tools”的分支上；不带就直通原始文本。更常见的是**流式路径没实现**——非流式能解析，流式只把 raw text 塞进 `delta.content`。我们一直用 `stream: true`（`:117`）。

**3. 我们发给模型的历史是“伪造的工具对话”**

历史里工具调用是 assistant 正文 `tool-call: {...}`，结果塞在 user 正文 `tool-result: {...}`，没有 `role:"tool"`、没有 `tool_call_id`。对原生训练过的模型，这是它没见过的形态；它会倾向“纠正”成自己认识的那套。这能解释为什么前十几步守规矩、后面突然崩——历史越长，原生格式的诱导越强。

**4. 系统提示词自己点名了这些标记**

`SystemPromptBuilder.cs:134`：「禁止输出原生工具标记变体（**DSML**、全角竖线包裹的标签、**tool_calls** 标签）」。

对小模型或低能力模型（这次是 `deepseek-flash-1017`），负面指令点名 token 反而提高它的激活概率——这是提示词工程里的老问题。这行是可以直接删掉或改成纯正面描述的。

**5. 没有 stop 序列兜底**

一旦模型开始吐 `<｜`，请求里没有任何 stop 能让它停下，只能等它写完（或写完一半被截断），然后我们才发现解析不了。

## 三、次要因素

- `include_reasoning: true` 是无条件发给所有 provider 的（`:122`）。OpenRouter 认这个字段，其他网关多半忽略，但有些聚合会误当成“要返回思维链”而改变输出形态。属于低概率但便宜可查。
- `finish_reason` 直连路径**有读**（`:423`、`:474`），但 `AgentSteps.cs:632` 只是把它塞进 `SetFinalAnswerEffect`，没有用来区分“被截断”和“写坏”。所以 `length` 截断的坏 JSON 也会计入 `MaxMalformedRetries`，加速撞停机线。
- 直连通道没有能力探测：模型目录里没有类似 OpenCode 的 `capabilities.tools` 标记，所以所有直连模型一律按“纯文本”处理，无论它其实支持结构化。

## 四、所以真正的分叉点

| | OpenCode 通道 | 直连通道 |
|---|---|---|
| 工具定义 | 有（会话工具表） | **无** |
| 模型怎么表达 | 原生结构化 | 提示词要求写文本 |
| 网关怎么处理 | 按模板解析成结构 | 原样透传文本 |
| 泄漏可能性 | 极低 | 高（原生模型尤其） |

不是“直连的模型不会结构化”，是**我们没向它们要结构**。DeepSeek 官方 API、智谱 GLM、Qwen、Moonshot 的 chat-completions 都支持 `tools` + `tool_calls`；teamorouter / agent2api 作为聚合层，多数也会透传。真正不支持的是少数本地小模型。

要根治，方向是给直连路径也声明 `tools`（把 `ToolRegistry` 转成 JSON Schema）并读回 `tool_calls`，文本协议降级成回落分支；但在那之前，**第 4 条（删掉提示词里的负面点名）和第 5 条（加 stop）是两行改动能立刻降频的**。