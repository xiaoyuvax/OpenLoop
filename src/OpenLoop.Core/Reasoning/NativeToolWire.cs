using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Reasoning
{
    /// <summary>
    /// 直连通道的原生工具调用适配层。内部历史仍是文本协议（assistant 的 tool-call 行 +
    /// user 的 tool-result 行），请求时翻成 OpenAI 的 tools / tool_calls / role:tool，
    /// 响应里的原生调用再翻回一行一条 tool-call。
    ///
    /// 这样状态机、压缩、trace、界面都不必认识两套协议；文本协议退化为「模型或网关
    /// 不支持原生通道」时的回落，而不是唯一通路。历史上它漏出原生模板标记，根因是请求里
    /// 从来没有 tools——模型只能用训练时的模板表达调用意图。
    /// </summary>
    internal static class NativeToolWire
    {
        /// <summary>一次原生工具调用（响应侧形态）。</summary>
        internal sealed record NativeCall(string Id, string Name, string Arguments);

        /// <summary>流式 tool_calls 按 index 增量拼装。</summary>
        internal sealed class NativeCallDraft
        {
            public string Id { get; set; }
            public StringBuilder Name { get; } = new();
            public StringBuilder Arguments { get; } = new();
        }

        /// <summary>工具声明：参数 schema 从各工具的散文式 ParamsSchema 还原成 JSON Schema。</summary>
        public static List<object> Declare(IReadOnlyList<ITool> tools)
        {
            if (tools == null || tools.Count == 0) return null;
            var declared = new List<object>(tools.Count);
            foreach (var tool in tools)
            {
                if (tool == null || string.IsNullOrWhiteSpace(tool.Name)) continue;
                declared.Add(new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = tool.Name,
                        ["description"] = string.IsNullOrWhiteSpace(tool.ParamsSchema)
                            ? tool.Description
                            : $"{tool.Description}\n参数: {tool.ParamsSchema}",
                        ["parameters"] = Parameters(tool.ParamsSchema)
                    }
                });
            }
            return declared.Count == 0 ? null : declared;
        }

        private static readonly Regex ParamNameRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>
        /// 散文 schema 形如 <c>path: string（说明）; offset: number 可选（说明）</c>。
        /// 只取参数名、类型与「可选」，说明原样保留；解析不出属性时退回宽松对象（不阻塞任何工具）。
        /// </summary>
        private static Dictionary<string, object> Parameters(string schema)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            var required = new List<string>();
            if (!string.IsNullOrWhiteSpace(schema))
            {
                foreach (string part in schema.Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon <= 0) continue;
                    string name = part[..colon].Trim();
                    if (!ParamNameRegex.IsMatch(name)) continue;
                    string rest = part[(colon + 1)..].Trim();
                    if (rest.Length == 0) continue;
                    properties[name] = PropertySchema(rest);
                    if (!rest.Contains("可选")) required.Add(name);
                }
            }

            var parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (properties.Count == 0) parameters["additionalProperties"] = true;
            else if (required.Count > 0) parameters["required"] = required;
            return parameters;
        }

        private static Dictionary<string, object> PropertySchema(string text)
        {
            var property = new Dictionary<string, object> { ["description"] = text };
            string type = text.Split(' ', '（', '(', '，', ',', '{')[0];
            switch (type)
            {
                case "string": property["type"] = "string"; break;
                case "number": property["type"] = "number"; break;
                case "integer": property["type"] = "integer"; break;
                case "boolean" or "bool": property["type"] = "boolean"; break;
                case "object": property["type"] = "object"; break;
                case "string[]":
                    property["type"] = "array";
                    property["items"] = new Dictionary<string, object> { ["type"] = "string" };
                    break;
                case "number[]":
                    property["type"] = "array";
                    property["items"] = new Dictionary<string, object> { ["type"] = "number" };
                    break;
            }
            return property;
        }

        /// <summary>
        /// 文本历史 → 线上消息。带 tool-call 行的 assistant 变成 tool_calls + 紧随其后的
        /// role:tool（id 在同一轮内自洽即可，线上只要求请求内配对）。
        /// </summary>
        public static List<object> Messages(IReadOnlyList<LlmMessage> messages)
        {
            var wire = new List<object>();
            if (messages == null) return wire;

            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null) continue;

                var calls = IsAssistant(message)
                    ? AgentToolCallParser.TryParseAll(message.Content)
                    : [];
                if (calls.Count == 0)
                {
                    wire.Add(Plain(message.Role, message.Content));
                    continue;
                }

                var results = i + 1 < messages.Count ? ReadResults(messages[i + 1]) : null;
                if (results == null)
                {
                    // 结果不在（压缩把配对拆散了）：只留正文，别在线上留下悬空的 tool_calls
                    wire.Add(Plain(message.Role, AgentToolCallParser.CutToolCallText(message.Content)));
                    continue;
                }

                var toolCalls = new List<object>(calls.Count);
                for (int c = 0; c < calls.Count; c++)
                {
                    toolCalls.Add(new Dictionary<string, object>
                    {
                        ["id"] = $"call_{i}_{c}",
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object>
                        {
                            ["name"] = calls[c].Tool,
                            ["arguments"] = JsonSerializer.Serialize(calls[c].Args, AgentLoop.ToolResultJson)
                        }
                    });
                }

                string prose = AgentToolCallParser.CutToolCallText(message.Content);
                wire.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = prose.Length == 0 ? null : prose,
                    ["tool_calls"] = toolCalls
                });

                for (int c = 0; c < calls.Count; c++)
                {
                    wire.Add(new Dictionary<string, object>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = $"call_{i}_{c}",
                        ["content"] = c < results.Value.Payloads.Count
                            ? results.Value.Payloads[c]
                            : "{\"ok\":false,\"error\":\"结果缺失（上下文被压缩）\"}"
                    });
                }

                var notes = new List<string>(results.Value.Notes);
                for (int c = calls.Count; c < results.Value.Payloads.Count; c++)
                    notes.Add($"tool-result: {results.Value.Payloads[c]}");
                if (notes.Count > 0) wire.Add(Plain("user", string.Join("\n", notes)));

                i++;
            }
            return wire;
        }

        private static Dictionary<string, object> Plain(string role, string content)
            => new() { ["role"] = role, ["content"] = content ?? "" };

        private static bool IsAssistant(LlmMessage message)
            => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase);

        /// <summary>tool-result 块：每行一条结果（payload 是单行 JSON），其余行是系统提示/修复说明。</summary>
        private static (List<string> Payloads, List<string> Notes)? ReadResults(LlmMessage message)
        {
            if (message == null
                || !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                || message.Content == null
                || !message.Content.StartsWith("tool-result:", StringComparison.Ordinal))
                return null;

            var payloads = new List<string>();
            var notes = new List<string>();
            foreach (string line in message.Content.Split('\n'))
            {
                string text = line.TrimEnd('\r');
                if (text.StartsWith("tool-result:", StringComparison.Ordinal))
                    payloads.Add(text["tool-result:".Length..].Trim());
                else if (!string.IsNullOrWhiteSpace(text))
                    notes.Add(text);
            }
            return payloads.Count == 0 ? null : (payloads, notes);
        }

        /// <summary>原生调用 → 文本协议（一行一条），交给既有解析器与执行链。</summary>
        public static string RenderCalls(IEnumerable<NativeCall> calls)
        {
            var sb = new StringBuilder();
            foreach (var call in calls ?? [])
            {
                if (string.IsNullOrWhiteSpace(call?.Name)) continue;
                string args = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments.Trim();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("tool-call: {\"tool\":")
                    .Append(JsonSerializer.Serialize(call.Name, AgentLoop.ToolResultJson))
                    .Append(",\"args\":").Append(args).Append('}');
            }
            return sb.ToString();
        }

        /// <summary>非流式 message.tool_calls。</summary>
        public static List<NativeCall> ReadCalls(JsonElement message)
        {
            var calls = new List<NativeCall>();
            if (message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("tool_calls", out var toolCalls)
                || toolCalls.ValueKind != JsonValueKind.Array)
                return calls;

            foreach (var item in toolCalls.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string name = null;
                string arguments = null;
                if (item.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                {
                    if (function.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        name = nameEl.GetString();
                    if (function.TryGetProperty("arguments", out var argsEl))
                        arguments = argsEl.ValueKind == JsonValueKind.String ? argsEl.GetString() : argsEl.GetRawText();
                }
                if (string.IsNullOrWhiteSpace(name)) continue;
                string id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                calls.Add(new NativeCall(id, name.Trim(), arguments));
            }
            return calls;
        }

        /// <summary>流式 delta.tool_calls 增量合并（按 index，缺 index 时按数组位置）。</summary>
        public static void MergeDelta(JsonElement delta, IDictionary<int, NativeCallDraft> drafts)
        {
            if (delta.ValueKind != JsonValueKind.Object
                || !delta.TryGetProperty("tool_calls", out var toolCalls)
                || toolCalls.ValueKind != JsonValueKind.Array)
                return;

            int position = 0;
            foreach (var item in toolCalls.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                int index = item.TryGetProperty("index", out var indexEl)
                            && indexEl.ValueKind == JsonValueKind.Number
                            && indexEl.TryGetInt32(out int parsed)
                    ? parsed
                    : position;
                position = index + 1;

                if (!drafts.TryGetValue(index, out var draft))
                    drafts[index] = draft = new NativeCallDraft();
                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    draft.Id = idEl.GetString();
                if (!item.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                    continue;
                if (function.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    draft.Name.Append(nameEl.GetString());
                if (function.TryGetProperty("arguments", out var argsEl))
                {
                    if (argsEl.ValueKind == JsonValueKind.String) draft.Arguments.Append(argsEl.GetString());
                    else if (argsEl.ValueKind == JsonValueKind.Object) draft.Arguments.Append(argsEl.GetRawText());
                }
            }
        }

        /// <summary>按 index 收拢流式草稿；没名字的（只有 id 或残片）丢弃。</summary>
        public static List<NativeCall> Complete(IDictionary<int, NativeCallDraft> drafts)
        {
            var calls = new List<NativeCall>(drafts?.Count ?? 0);
            if (drafts == null) return calls;
            foreach (int key in drafts.Keys.OrderBy(k => k))
            {
                var draft = drafts[key];
                string name = draft.Name.ToString().Trim();
                if (name.Length == 0) continue;
                calls.Add(new NativeCall(draft.Id, name, draft.Arguments.ToString()));
            }
            return calls;
        }
    }
}
