using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenLoop.Core.Agent
{
    /// <summary>模型输出的工具调用块（文本协议）。</summary>
    public sealed class AgentToolCall
    {
        public string Tool { get; init; }
        public Dictionary<string, object> Args { get; init; } = [];

        /// <summary>轮询声明：模型说明这是有意重复的等待/轮询，不是空转。次数由宿主封顶。</summary>
        public AgentPoll Poll { get; init; }
    }

    /// <summary>轮询声明（&lt;c&gt;"poll":{"id":"build-1","max":8}&lt;/c&gt;）。</summary>
    public sealed class AgentPoll
    {
        public string Id { get; init; }
        public int Max { get; init; }
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

            // 4) 模型自创的 XML 式调用（实测 DeepSeek 系会写 <|DSML|invoke name="Shell"> … <|DSML|parameter name="command">…）
            if (TryParseInvoke(text, out call)) return true;

            // 5) 特殊标记被翻成中文的调用（实测 <｜工具调用： Shell>{"argv":[…]}<｜工具调用结束｜>）
            if (TryParseChineseMarker(text, out call)) return true;

            if (TryParseTaggedOrOpenAiStyle(text, out call)) return true;

            return false;
        }

        /// <summary>
        /// 模型自创的 XML 式调用：<c>&lt;[装饰]invoke name="Shell"&gt; &lt;[装饰]parameter name="path"&gt;v&lt;/[装饰]parameter&gt; &lt;/[装饰]invoke&gt;</c>。
        /// 装饰字符（如 <c>|DSML|</c>、全角竖线）不固定，只要求标签名前面有一段短前缀，别把普通 XML 正文误判成调用。
        /// </summary>
        private static bool TryParseInvoke(string text, out AgentToolCall call)
        {
            call = null;
            var head = InvokeToolRegex.Match(text);
            if (!head.Success) return false;

            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (Match p in InvokeParamRegex.Matches(text))
            {
                string key = p.Groups["name"].Value.Trim();
                if (key.Length == 0) continue;
                args[key] = ConvertValue(p.Groups["value"].Value.Trim());
            }

            if (!NormalizeWrappedArgs(args)) return false;
            call = new AgentToolCall { Tool = head.Groups["name"].Value, Args = args };
            return true;
        }

        /// <summary>模型抄原生工具格式，把整段参数包在单个 args/arguments/parameters/input 参数里。</summary>
        private static readonly string[] ArgsAliases = ["args", "arguments", "parameters", "input"];

        /// <summary>
        /// 单个 args 参数摊平成真正的参数表；摊不平说明模型只写了参数名（如 {path, offset, limit}），
        /// 当作格式损坏返回 false，让上层给出格式纠正，别拿空参数去执行一次。
        /// </summary>
        private static bool NormalizeWrappedArgs(Dictionary<string, object> args)
        {
            if (args.Count != 1) return true;
            string key = args.Keys.First();
            if (!ArgsAliases.Contains(key, StringComparer.OrdinalIgnoreCase)) return true;
            string payload = args[key] is JsonElement el ? el.GetRawText() : args[key] as string;
            if (payload == null) return false;

            var unwrapped = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!TryFillArgs(payload, unwrapped)) return false;
            args.Clear();
            foreach (var pair in unwrapped) args[pair.Key] = pair.Value;
            return true;
        }

        /// <summary>包装型工具名：模型把真实调用塞进 args 里、再用 tool_call/function_call 当工具名。</summary>
        private static bool IsWrapperToolName(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return false;
            string normalized = tool.Replace("_", "").Replace("-", "").Trim();
            return normalized.Equals("toolcall", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("functioncall", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("call", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>拆包装：内层要有工具名（tool/name/function）与参数（args/arguments/…）；拆不出按格式损坏处理。</summary>
        private static bool TryUnwrapNestedCall(Dictionary<string, object> args, out string tool)
        {
            tool = null;
            foreach (string key in new[] { "tool", "name", "function" })
                if (args.TryGetValue(key, out var value) && value is string name && name.Trim().Length > 0)
                {
                    tool = name.Trim();
                    break;
                }
            if (tool == null) return false;

            var inner = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string key in ArgsAliases)
            {
                if (!args.TryGetValue(key, out var value)) continue;
                string payload = value switch
                {
                    JsonElement el when el.ValueKind == JsonValueKind.Object => el.GetRawText(),
                    JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
                    string text => text,
                    _ => null
                };
                if (payload == null || !TryFillArgs(payload, inner)) return false;
                break;
            }
            args.Clear();
            foreach (var pair in inner) args[pair.Key] = pair.Value;
            return true;
        }

        private static readonly Regex InvokeToolRegex = new(
            @"<[^<>]{0,16}invoke\b[^<>]*?\bname\s*=\s*[""'](?<name>[A-Za-z_][\w\-\.]*)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex InvokeParamRegex = new(
            @"<[^<>]{0,16}parameter\b[^<>]*?\bname\s*=\s*[""'](?<name>[A-Za-z_][\w\-\.]*)[""'][^<>]*>(?<value>.*?)</[^<>]{0,16}parameter\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// 本地模型的特殊标记会被词表翻成中文：<c>&lt;｜工具调用： Shell&gt;{json}&lt;｜工具调用结束｜&gt;</c>。
        /// 名称在标签里，参数是紧随其后的 JSON 对象本身（不是 {"tool","args"} 包装）——整条协议换个壳，语义不变。
        /// </summary>
        private static bool TryParseChineseMarker(string text, out AgentToolCall call)
        {
            call = null;
            var head = ChineseMarkerToolRegex.Match(text);
            if (!head.Success) return false;

            int brace = text.IndexOf('{', head.Index + head.Length);
            string json = brace >= 0 ? ExtractJsonObject(text, brace) : null;
            if (json == null) return false;                    // 被截断：交给上层纠正重试

            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!TryFillArgs(json, args)) return false;
            call = new AgentToolCall { Tool = head.Groups["name"].Value, Args = args };
            return true;
        }

        private static readonly Regex ChineseMarkerToolRegex = new(
            @"<\s*[|｜]\s*(?:工具调用|函数调用|tool[▁_\s]?call)\s*[:：]\s*(?<name>[A-Za-z_][\w\-\.]*)\s*[|｜]?\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>只认标记头（不带名称），供「这是不是在发起调用」判定用，含截断/只有开始标签的情形。</summary>
        private static readonly Regex ChineseMarkerHeadRegex = new(
            @"<\s*[|｜]\s*(?:工具调用|函数调用|tool[▁_\s]?call)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                if (!NormalizeWrappedArgs(args)) return false;
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

            // 模型会把协议分隔符写成全角（实测 deepseek：<｜tool":"ReadEvidence","args":{…}），
            // 归一化后再判定，否则这段坏调用会被当成最终答复原样显示。
            string normalized = NormalizeDelimiters(modelOutput);
            if (normalized.Contains("\"tool\":", StringComparison.Ordinal)) return true;
            if (normalized.Contains("\"tool\"", StringComparison.Ordinal)
                && normalized.Contains("\"args\"", StringComparison.Ordinal)) return true;
            if (normalized.Contains("\"args\":", StringComparison.Ordinal)
                && normalized.Contains("\"tool\"", StringComparison.Ordinal)) return true;
            // 其它协议的等价形态（实测模型会自发用这些）：标签式 / OpenAI 函数调用 JSON
            if (modelOutput.Contains("<|tool_call_start|>", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<tool_call", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<parameter", StringComparison.OrdinalIgnoreCase)
                || modelOutput.Contains("<function", StringComparison.OrdinalIgnoreCase)) return true;
            // 模型自创的 XML 式调用（<|DSML|invoke …>）：认出来就走「格式错误 → 纠正重试」，别当最终答复
            if (InvokeToolRegex.IsMatch(modelOutput)) return true;
            // 特殊标记被翻成中文（<｜工具调用： Shell> / <｜工具调用结束｜>）：哪怕名称/JSON 缺失也算尝试
            if (ChineseMarkerHeadRegex.IsMatch(modelOutput)) return true;
            if (modelOutput.Contains("invoke", StringComparison.OrdinalIgnoreCase)
                && modelOutput.Contains("parameter", StringComparison.OrdinalIgnoreCase)) return true;
            return modelOutput.Contains("\"name\"", StringComparison.Ordinal)
                && modelOutput.Contains("\"arguments\"", StringComparison.Ordinal);
        }

        /// <summary>
        /// 正文是否就是工具调用协议（以 marker 开头）：这种内容只能当调用显示，不能当回答显示。
        /// Worker、Loop、旁路三处界面的流式过滤共用同一判据。
        /// </summary>
        public static bool LooksLikeToolCallText(string modelOutput)
            => !string.IsNullOrWhiteSpace(modelOutput)
               && modelOutput.TrimStart().StartsWith("tool-call", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 流式显示用：截掉从第一个工具协议片段起的尾巴。模型在正文后面接着吐调用原文时，
        /// 只在整段以 marker 开头才屏蔽会让协议原文先显示、收尾再被清掉（用户看到闪现）。
        /// </summary>
        public static string CutToolCallText(string modelOutput)
        {
            if (string.IsNullOrWhiteSpace(modelOutput)) return "";
            var match = ProtocolFragmentRegex.Match(modelOutput);
            return match.Success ? modelOutput[..match.Index].TrimEnd() : modelOutput;
        }

        /// <summary>协议片段起始：marker，或带装饰的标签头（<c>&lt;|DSML| invoke</c>、<c>&lt;tool_call&gt;</c>、<c>&lt;｜工具调用</c> 等）。</summary>
        private static readonly Regex ProtocolFragmentRegex = new(
            @"<\s*/?\s*[|｜]{0,3}\s*(?:DSML|tool_call|invoke|parameter|function|工具调用|函数调用)\b|\btool-call\s*[:：]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>全角引号/竖线归一化成 ASCII，供协议判定使用（不改动原文本）。</summary>
        private static string NormalizeDelimiters(string text) => text
            .Replace('｜', '"').Replace('＂', '"')
            .Replace('“', '"').Replace('”', '"')
            .Replace('‘', '\'').Replace('’', '\'');

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

        /// <summary>
        /// 轻量修复后重新解析：尾逗号、缺失的收尾括号、被吞掉开头的「"tool":…,"args":{…}」。
        /// 只做能确定含义的修补；修不出来就返回空，交给上层纠正重试。
        /// </summary>
        public static IReadOnlyList<AgentToolCall> TryRepair(string modelOutput)
        {
            if (string.IsNullOrWhiteSpace(modelOutput)) return [];
            string text = NormalizeDelimiters(modelOutput.Replace("\r\n", "\n"));

            var calls = new List<AgentToolCall>();
            for (int i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
            {
                string raw = ExtractJsonObject(text, i) ?? text[i..];
                foreach (string candidate in RepairedVariants(raw))
                    if (TryParseJson(candidate, out var call)) { calls.Add(call); break; }
            }
            if (calls.Count > 0) return calls;

            // 开头被吞掉：<"tool":"X","args":{…> 这种没有外层花括号的碎片
            var tool = ToolFieldRegex.Match(text);
            if (!tool.Success) return [];
            string argsJson = "{}";
            var argsAt = ArgsFieldRegex.Match(text, tool.Index + tool.Length);
            if (argsAt.Success)
            {
                int brace = text.IndexOf('{', argsAt.Index + argsAt.Length);
                if (brace >= 0 && ExtractJsonObject(text, brace) is { } extracted) argsJson = extracted;
            }
            string shell = $"{{\"tool\":\"{tool.Groups["name"].Value}\",\"args\":{argsJson}}}";
            foreach (string candidate in RepairedVariants(shell))
                if (TryParseJson(candidate, out var repaired)) return [repaired];
            return [];
        }

        private static readonly Regex ToolFieldRegex = new(
            @"""tool""\s*:\s*""(?<name>[^""\s]+)""", RegexOptions.Compiled);
        private static readonly Regex ArgsFieldRegex = new(
            @"""args""\s*:", RegexOptions.Compiled);
        private static readonly Regex TrailingCommaRegex = new(
            @",\s*(?=[}\]])", RegexOptions.Compiled);

        /// <summary>在原文基础上给出几种可解析的变体（原文优先，避免把合法内容改坏）。</summary>
        private static IEnumerable<string> RepairedVariants(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) yield break;
            yield return json;

            string noTrailing = TrailingCommaRegex.Replace(json, "");
            if (noTrailing != json) yield return noTrailing;

            foreach (string source in new[] { json, noTrailing })
            {
                string closed = CloseOpenBrackets(source);
                if (closed != source) yield return closed;
            }
        }

        /// <summary>补齐被截断的结尾括号（字符串内的括号不算）。</summary>
        private static string CloseOpenBrackets(string text)
        {
            var stack = new Stack<char>();
            bool inString = false, escaped = false;
            foreach (char c in text)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c is '{' or '[') stack.Push(c);
                else if (c is '}' or ']') { if (stack.Count > 0) stack.Pop(); }
            }
            if (stack.Count == 0) return text;
            var sb = new System.Text.StringBuilder(text);
            foreach (char open in stack)
                sb.Append(open == '{' ? '}' : ']');
            return sb.ToString();
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
                if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                {
                    // 模型把整段参数再 JSON 编码成字符串（args:"{\"path\":…}"）：解不开就按格式损坏处理，
                    // 交给上层纠正，别拿空参数去执行一次
                    string payload = argsEl.GetString();
                    if (!string.IsNullOrWhiteSpace(payload) && !TryFillArgs(payload, args)) return false;
                }
                else if (root.TryGetProperty("args", out argsEl) && argsEl.ValueKind == JsonValueKind.Object)
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
                string tool = toolEl.GetString();
                if (IsWrapperToolName(tool))
                {
                    // 整条调用被再包一层（{"tool":"Tool_call","args":{"tool":"ReadFile","args":{…}}}）：拆出内层
                    if (!TryUnwrapNestedCall(args, out tool)) return false;
                }
                call = new AgentToolCall { Tool = tool, Args = args, Poll = ReadPoll(root) };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>轮询声明：id 非空、max 为正整数才算数；写错就当没声明，走普通判重。</summary>
        private static AgentPoll ReadPoll(JsonElement root)
        {
            if (!root.TryGetProperty("poll", out var poll) || poll.ValueKind != JsonValueKind.Object)
                return null;
            if (!poll.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return null;
            string id = idEl.GetString()?.Trim();
            if (string.IsNullOrEmpty(id) || id.Length > 32) return null;
            if (!poll.TryGetProperty("max", out var maxEl) || maxEl.ValueKind != JsonValueKind.Number
                || !maxEl.TryGetInt32(out int max) || max <= 0)
                return null;
            return new AgentPoll { Id = id, Max = max };
        }
    }
}
