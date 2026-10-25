using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenLoop.Core.Agent
{
    /// <summary>模型输出的工具调用块（文本协议）。</summary>
    public sealed class AgentToolCall
    {
        public string Tool { get; init; }
        public Dictionary<string, object> Args { get; init; } = [];
    }

    /// <summary>
    /// 解析模型输出的文本协议工具调用块。约定：单独一行 tool-call: &lt;json&gt;（JSON 含 tool 与 args）。
    /// 容错要点（实测模型常不守约）：
    /// - marker 后可以是跨行 JSON，也可以有尾随解释文字；
    /// - 用括号配对扫描提取对象，而非假设 JSON 恰好占满一行；
    /// - 模型省略 marker 时，兜底扫描正文中的 {…} 对象（要求含 tool 键）。
    /// </summary>
    public static class AgentToolCallParser
    {
        private const string Marker = "tool-call:";

        public static bool TryParse(string modelOutput, out AgentToolCall call)
        {
            call = null;
            if (string.IsNullOrWhiteSpace(modelOutput)) return false;

            string text = modelOutput.Replace("\r\n", "\n");

            // 1) 优先：每个 marker 之后的第一个 JSON 对象（容忍跨行与尾随文字）
            int searchFrom = 0;
            while (true)
            {
                int markerAt = text.IndexOf(Marker, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (markerAt < 0) break;
                int brace = text.IndexOf('{', markerAt + Marker.Length);
                if (brace >= 0)
                {
                    string json = ExtractJsonObject(text, brace);
                    if (json != null && TryParseJson(json, out call)) return true;
                }
                searchFrom = markerAt + Marker.Length;
            }

            // 2) 兜底：正文中任意位置的 {…} 对象（模型省略 marker 的情况）
            int scan = 0;
            while (true)
            {
                int brace = text.IndexOf('{', scan);
                if (brace < 0) break;
                string json = ExtractJsonObject(text, brace);
                if (json != null && TryParseJson(json, out call)) return true;
                scan = brace + 1;
            }

            // 3) 其它协议的等价形态：标签式（<tool_call>/<function=x>/<parameter=args>{…}</parameter>）
            //    与 OpenAI 风格 {"name":…,"arguments":{…}}——尽量还原成同一次调用，别白费一次往返
            if (TryParseToolCallBlock(text, out call)) return true;

            if (TryParseTaggedOrOpenAiStyle(text, out call)) return true;

            return false;
        }

        /// <summary>
        /// 解析全部工具调用（并行调用支持）：文本中按顺序出现的每个 tool-call marker 各还原一次调用。
        /// 只认显式 marker（多对象兜底扫描无法界定调用边界，容易把参数对象误当调用，故不参与）。
        /// 解析出 0 个时回退 TryParse 的全量容错路径（单调用兼容形态仍可用）。
        /// </summary>
        public static IReadOnlyList<AgentToolCall> TryParseAll(string modelOutput)
        {
            if (string.IsNullOrWhiteSpace(modelOutput)) return [];
            string text = modelOutput.Replace("\r\n", "\n");
            var calls = new List<AgentToolCall>();

            int searchFrom = 0;
            while (true)
            {
                int markerAt = text.IndexOf(Marker, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (markerAt < 0) break;
                int brace = text.IndexOf('{', markerAt + Marker.Length);
                if (brace >= 0)
                {
                    string json = ExtractJsonObject(text, brace);
                    if (json != null && TryParseJson(json, out var call))
                        calls.Add(call);
                }
                searchFrom = markerAt + Marker.Length;
            }

            if (calls.Count == 0)
                return TryParse(text, out var single) ? [single] : [];
            return calls;
        }

        /// <summary>参数 JSON 的开括号定位：&lt;parameter=args&gt;{…} 优先，其次 "arguments"/"args"/"parameters"/"input": {…}。</summary>
        private static readonly Regex TaggedArgsRegex = new(
            @"<\s*parameter\s*[=:]\s*[""']?args[""']?\s*>|""(?:arguments|args|parameters|input)""\s*:\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>工具名：标签式 <c>&lt;function=x&gt;</c> / <c>&lt;tool=x&gt;</c> / <c>&lt;tool_call=x&gt;</c>。</summary>
        private static readonly Regex TaggedToolRegex = new(
            @"<\s*(?:function|tool|tool_call)\s*[=:]\s*[""']?(?<name>[A-Za-z_][\w\-\.]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>工具名（JSON 形态）：<c>"name"</c> / <c>"tool"</c> / <c>"function"</c>。</summary>
        private static readonly Regex TaggedJsonNameRegex = new(
            @"""(?:name|tool|function)""\s*:\s*""(?<name>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 兼容两类「非本协议」但语义相同的调用形态：
        /// ① 标签式（<c>&lt;tool_call&gt;…&lt;parameter=args&gt;{json}&lt;/parameter&gt;</c>、<c>&lt;function=名字&gt;</c>）；
        /// ② OpenAI 函数调用 JSON（<c>{"name":"x","arguments":{…}}</c>）。
        /// 能凑出「工具名 + 参数对象」就当作一次调用（工具名找不到则视为格式损坏，交由上层纠正）。
        /// </summary>
        private static bool TryParseToolCallBlock(string text, out AgentToolCall call)
        {
            call = null;
            const string startTag = "<|tool_call_start|>";
            const string endTag = "<|tool_call_end|>";
            int start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            start += startTag.Length;
            int end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            string body = end < 0 ? text[start..] : text[start..end];
            body = body.Trim().TrimStart('[').TrimEnd(']');

            var m = Regex.Match(body, @"(?<name>[A-Za-z_][\w\.]*)\s*\((?<args>[^()]*)\)");
            if (!m.Success) return false;

            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            string argText = m.Groups["args"].Value.Trim();
            if (argText.Length > 0)
            {
                foreach (string part in SplitTopLevel(argText))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = part[..eq].Trim().Trim('"', '\'');
                    args[key] = ConvertValue(part[(eq + 1)..].Trim());
                }
            }

            call = new AgentToolCall { Tool = m.Groups["name"].Value, Args = args };
            return true;
        }

        private static IEnumerable<string> SplitTopLevel(string text)
        {
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == quote && (i == 0 || text[i - 1] != '\\')) inString = false;
                    continue;
                }
                if (c is '"' or '\'') { inString = true; quote = c; continue; }
                if (c is '[' or '{' or '(') depth++;
                else if (c is ']' or '}' or ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return text[start..i];
                    start = i + 1;
                }
            }
            if (start < text.Length) yield return text[start..];
        }

        private static object ConvertValue(string raw)
        {
            if (raw.Length >= 2 && ((raw[0] == '\'' && raw[^1] == '\'') || (raw[0] == '"' && raw[^1] == '"')))
                return raw[1..^1].Replace("\\'", "'").Replace("\\\"", "\"");
            if (bool.TryParse(raw, out bool b)) return b;
            if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            if (long.TryParse(raw, out long l)) return l;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
            if ((raw.StartsWith('[') && raw.EndsWith(']')) || (raw.StartsWith('{') && raw.EndsWith('}')))
            {
                try { using var doc = JsonDocument.Parse(raw.Replace('\'', '"')); return doc.RootElement.Clone(); }
                catch (JsonException) { }
            }
            return raw;
        }

        private static bool TryParseTaggedOrOpenAiStyle(string text, out AgentToolCall call)
        {
            call = null;

            string toolName = null;
            var tm = TaggedToolRegex.Match(text);
            if (tm.Success) toolName = tm.Groups["name"].Value;
            else
            {
                var jm = TaggedJsonNameRegex.Match(text);
                if (jm.Success) toolName = jm.Groups["name"].Value;
            }
            if (string.IsNullOrWhiteSpace(toolName)) return false;

            var args = new Dictionary<string, object>();
            var am = TaggedArgsRegex.Match(text);
            if (am.Success)
            {
                int brace = text.IndexOf('{', am.Index + am.Length);
                string argsJson = brace >= 0 ? ExtractJsonObject(text, brace) : null;
                if (argsJson == null) return false;          // 有名字没参数（或被截断）：交给上层纠正重试
                if (!TryFillArgs(argsJson, args)) return false;
            }
            call = new AgentToolCall { Tool = toolName.Trim(), Args = args };
            return true;
        }

        /// <summary>把参数对象的每个成员塞进 dict（类型映射与 <see cref="TryParseJson"/> 一致）。</summary>
        private static bool TryFillArgs(string argsJson, Dictionary<string, object> args)
        {
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    switch (p.Value.ValueKind)
                    {
                        case JsonValueKind.String: args[p.Name] = p.Value.GetString(); break;
                        case JsonValueKind.Number:
                            args[p.Name] = p.Value.TryGetInt64(out var l) ? (object)l : (object)p.Value.GetDouble();
                            break;
                        case JsonValueKind.True: args[p.Name] = true; break;
                        case JsonValueKind.False: args[p.Name] = false; break;
                        case JsonValueKind.Null: args[p.Name] = null; break;
                        default: args[p.Name] = p.Value.Clone(); break;
                    }
                }
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// 输出是否“看起来是想发起工具调用”（含协议 marker，或含 tool/args 的 JSON 片段）。
        /// 供主循环在解析失败时判定：这是格式损坏的工具调用，而不是最终答复——
        /// 绝不能把协议原文当作回答泄漏给用户。
        /// </summary>
        public static bool LooksLikeToolCallAttempt(string modelOutput)
        {
            if (string.IsNullOrWhiteSpace(modelOutput)) return false;
            if (modelOutput.Contains(Marker, StringComparison.OrdinalIgnoreCase)) return true;
            if (modelOutput.Contains("\"tool\"", StringComparison.Ordinal)
                && modelOutput.Contains("\"args\"", StringComparison.Ordinal)) return true;
            // 其它协议的等价形态（实测模型会自发用这些）：标签式 / OpenAI 函数调用 JSON
            if (modelOutput.Contains("<|tool_call_start|>", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<tool_call", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<parameter", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<function", StringComparison.OrdinalIgnoreCase)) return true;
            return modelOutput.Contains("\"name\"", StringComparison.Ordinal)
                && modelOutput.Contains("\"arguments\"", StringComparison.Ordinal);
        }

        /// <summary>
        /// 从 start（应为 '{'）起做括号配对扫描，返回完整的 JSON 对象文本；未闭合（被截断）返回 null。
        /// 扫描时跟踪字符串状态与转义，避免把字符串里的花括号误当结构括号。
        /// </summary>
        private static string ExtractJsonObject(string text, int start)
        {
            if (start < 0 || start >= text.Length || text[start] != '{') return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                }
            }
            return null;
        }

        private static bool TryParseJson(string json, out AgentToolCall call)
        {
            call = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (!root.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
                    return false;

                var args = new Dictionary<string, object>();
                if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in argsEl.EnumerateObject())
                    {
                        // 注意：三元表达式直接写 long/double 会被数值提升为 double，这里显式装箱保留整型类型
                        switch (p.Value.ValueKind)
                        {
                            case JsonValueKind.String: args[p.Name] = p.Value.GetString(); break;
                            case JsonValueKind.Number:
                                args[p.Name] = p.Value.TryGetInt64(out var l) ? (object)l : (object)p.Value.GetDouble();
                                break;
                            case JsonValueKind.True: args[p.Name] = true; break;
                            case JsonValueKind.False: args[p.Name] = false; break;
                            case JsonValueKind.Null: args[p.Name] = null; break;
                            // 对象/数组：Clone 保留结构类型（脱离即将释放的 JsonDocument）。
                            // 不能退化为原始字符串，否则 appagent_action 的嵌套 args、attachments 等传参失效。
                            default: args[p.Name] = p.Value.Clone(); break;
                        }
                    }
                }
                call = new AgentToolCall { Tool = toolEl.GetString(), Args = args };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
