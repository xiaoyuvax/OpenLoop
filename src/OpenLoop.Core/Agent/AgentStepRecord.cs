using System.Globalization;
using System.Text.Json;

namespace OpenLoop.Core.Agent
{
    /// <summary>一次工具执行记录（界面步骤流 + trace 用）。</summary>
    public sealed class AgentStepRecord
    {
        public int Index { get; init; }
        public string Tool { get; init; }
        public string ArgsSummary { get; init; }
        public string ArgsFull { get; init; }
        public bool Ok { get; init; }
        public string Summary { get; init; }
        public string FullResult { get; init; }
        public string ResultFull { get; init; }
        public bool ArgsRepaired { get; init; }
        public long DurationMs { get; init; }

        public string DurationText => DurationMs < 1000
            ? $"{DurationMs}ms"
            : (DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";

        /// <summary>步骤流里的一行：` &gt;&gt; 工具[调用内容] · 耗时`。
        /// 方括号与 `&gt;&gt;` 前缀是有意的：否则正则会把正文里的普通函数调用也当成工具行。
        /// 原生工具（native: 前缀）的入参是 JSON 原文，这里压成可读摘要；trace 仍保存原文。</summary>
        public string DisplayLine
        {
            get
            {
                string tool = Tool ?? "";
                string name;
                string args;
                bool native = tool.StartsWith("native:", StringComparison.Ordinal);
                if (native)
                {
                    name = tool["native:".Length..];
                    args = NativeArgs(name, ArgsFull);
                }
                else
                {
                    name = tool;
                    args = ArgsSummary ?? "";
                }
                if (name.Length > 1 && char.IsLower(name[0]))
                    name = char.ToUpperInvariant(name[0]) + name[1..];
                // 原生调用（OpenCode 侧执行）与 OpenLoop 自己的工具同形同名，前面加一个 > 区分
                string line = " >> " + (native ? "> " : "") + name + "[" + args + "]";
                if (DurationMs > 0) line += " · " + DurationText;
                return Ok ? line : line + " · 失败";
            }
        }

        private static readonly string[] GenericKeys =
            ["path", "file", "filePath", "command", "pattern", "query", "url", "prompt", "description", "code", "args"];

        private static string[] KeysFor(string tool)
        {
            switch (tool.ToLowerInvariant())
            {
                case "shell":
                case "bash":
                case "powershell":
                    return ["command", "cmd", "workdir"];
                case "read":
                case "write":
                case "edit":
                    return ["path", "filePath", "file"];
                case "grep":
                case "glob":
                case "list":
                case "listdir":
                    return ["pattern", "path", "include", "query"];
                case "webfetch":
                case "websearch":
                    return ["url", "query", "prompt"];
                case "webtask":
                case "subagent":
                case "task":
                    return ["description", "prompt"];
                case "execute":
                    return ["code"];
                default:
                    return GenericKeys;
            }
        }

        private static string NativeArgs(string tool, string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "\"\"" || json == "{}") return "";
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return OneLine(NativeValue(root));

                var parts = new List<string>();
                foreach (string key in KeysFor(tool))
                {
                    if (!root.TryGetProperty(key, out var value)) continue;
                    string text = NativeValue(value);
                    if (text.Length == 0) continue;
                    parts.Add(key + "=" + text);
                    if (parts.Count == 2) break;
                }
                if (parts.Count == 0)
                {
                    foreach (var property in root.EnumerateObject())
                    {
                        string text = NativeValue(property.Value);
                        if (text.Length == 0) continue;
                        parts.Add(property.Name + "=" + text);
                        break;
                    }
                }
                return OneLine(string.Join(", ", parts));
            }
            catch (JsonException)
            {
                return OneLine(json);
            }
        }

        private static string NativeValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String) return ShortPath((value.GetString() ?? "").Trim());
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
            return value.GetRawText();
        }

        /// <summary>绝对路径只留最后三段，否则一行全被盘符路径占满。</summary>
        private static string ShortPath(string text)
        {
            if (text.Length < 40 || text.IndexOfAny([' ', '\t', '\r', '\n']) >= 0) return text;
            string[] segments = text.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 3 ? "…\\" + string.Join("\\", segments[^3..]) : text;
        }

        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string joined = string.Join(" ", text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return joined.Length <= 120 ? joined : joined[..120] + "…";
        }
    }
}
