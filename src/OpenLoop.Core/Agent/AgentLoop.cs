using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// Agent 的便捷驱动：把任务交给状态机管线，直到模型给出最终答案或预算耗尽。
    /// 管线本身见 <see cref="AgentStateMachine"/> 与 <see cref="AgentPipeline"/>；
    /// 宿主也可以直接用 CreateMachine + StepAsync/Apply 逐步驱动。
    /// 不认识工作目录、模型、账本；这些都由构造方注入。
    /// </summary>
    public sealed class AgentLoop
    {
        private readonly ToolRegistry _tools;
        private readonly IReasoner _reasoner;
        private readonly AgentPolicy _policy;
        private readonly Action<AgentStepRecord> _onStep;
        private readonly Action<string, Dictionary<string, object>> _onToolStart;
        private readonly Func<string, bool> _confirmContinue;
        private readonly IReadOnlyList<SkillInfo> _skills;

        public AgentLoop(
            ToolRegistry tools,
            IReasoner reasoner,
            AgentPolicy policy = null,
            Action<AgentStepRecord> onStep = null,
            Action<string, Dictionary<string, object>> onToolStart = null,
            Func<string, bool> confirmContinue = null,
            IReadOnlyList<SkillInfo> skills = null)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _reasoner = reasoner ?? throw new ArgumentNullException(nameof(reasoner));
            _policy = policy ?? new AgentPolicy();
            _onStep = onStep;
            _onToolStart = onToolStart;
            _confirmContinue = confirmContinue;
            _skills = skills;
        }

        public string BuildSystemPrompt(string role = null, IReadOnlyList<string> rules = null, string instructions = null, string workspace = null)
            => SystemPromptBuilder.Build(
                _tools.Tools, Math.Clamp(_policy.MaxSteps, 1, 100), role, rules, instructions, _skills, workspace);

        public AgentStateMachine CreateMachine() => new(AgentPipeline.Create());

        public AgentState CreateState(IReadOnlyList<LlmMessage> history, string userMessage)
            => new(history, userMessage) { MaxSteps = Math.Clamp(_policy.MaxSteps, 1, 100) };

        public AgentRunContext CreateContext(
            int contextLimit = 0,
            Func<bool> shouldStop = null,
            Action<string> onDelta = null,
            Action<string> onReasoning = null,
            Action onRestart = null)
            => new()
            {
                Tools = _tools,
                Reasoner = _reasoner,
                Policy = _policy,
                ContextLimit = contextLimit,
                ShouldStop = shouldStop,
                OnDelta = onDelta,
                OnReasoning = onReasoning,
                OnRestart = onRestart,
                OnStep = _onStep,
                OnToolStart = _onToolStart,
                ConfirmContinue = _confirmContinue
            };

        /// <summary>
        /// 执行一次完整任务。history 为既有对话（不含本次用户消息），userMessage 为本次请求。
        /// contextLimit &gt; 0 时，每轮补全前估算占用，超阈值即压缩较早消息（system 不动）。
        /// shouldStop = 软停止：只在下一轮模型调用前检查，进行中的工具调用不被打断。
        /// </summary>
        /// <param name="onDelta">正文流式增量（null 时不走流式通道）。</param>
        /// <param name="onReasoning">推理通道流式增量，供宿主实时显示思考内容。</param>
        /// <param name="onRestart">流式断流整轮重发前调用，供宿主清掉半截输出。</param>
        public async Task<AgentRunResult> RunAsync(
            IReadOnlyList<LlmMessage> history, string userMessage,
            CancellationToken ct = default, int contextLimit = 0, Func<bool> shouldStop = null,
            Action<string> onDelta = null, Action<string> onReasoning = null, Action onRestart = null)
        {
            var state = CreateState(history, userMessage);
            var context = CreateContext(contextLimit, shouldStop, onDelta, onReasoning, onRestart);
            return await CreateMachine().RunAsync(state, context, ct);
        }

        internal static string BudgetText(AgentState state)
            => $"已达单次任务步数上限（{state.MaxSteps} 步）。已执行的步骤：\n"
               + string.Join("\n", state.Result.Steps.Select(s =>
                   $"{s.Index}. {s.Tool}({s.ArgsSummary}) → {(s.Ok ? "成功" : "失败")}"));

        private static readonly string[] ActionPromiseSignals =
        [
            "请稍候", "稍等", "马上", "正在", "即将", "稍后",
            "收到", "我将", "我会", "我来", "接下来", "下面我", "现在我",
            "开始处理", "继续编辑", "重改", "重出",
            "先查", "查一下", "查查",
            "我继续", "我接着", "然后我", "下一步我", "让我继续",
            "继续检查", "继续查看", "继续读取", "继续分析", "继续了解", "继续处理",
            "再看看", "再看一下", "进一步"
        ];

        /// <summary>
        /// 「要做但还没做」的意图：意图词 + 动作动词（允许中间夹「再/继续/进一步」等）。
        /// 固定词表容易漏（例如「让我继续检查其他文件」），所以补一条句式规则。
        /// </summary>
        private static readonly Regex ActionPromiseRegex = new(
            @"(?:(?:让我|我将|我会|我来|我继续|我接着|然后我|接下来我?|下一步我?|现在我|下面我)\s*(?:再|继续|进一步|去|先|马上)?\s*(?:检查|查看|读取|读|浏览|分析|阅读|搜索|查找|查|运行|执行|跑|修改|编辑|更新|改|生成|整理|确认|了解|处理)|(?:let me|i'?ll|i will|i'?m going to)\s+(?:continue|check|look|read|examine|review|inspect|analyze|search|run|execute|update|modify|edit))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool LooksLikeActionPromise(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            if (ActionPromiseRegex.IsMatch(content)) return true;
            foreach (string signal in ActionPromiseSignals)
                if (content.Contains(signal, StringComparison.OrdinalIgnoreCase)) return true;
            string tail = content.TrimEnd();
            foreach (char c in new[] { '，', ':', '：', ',', '、', '；' })
                if (tail.EndsWith(c)) return true;
            return false;
        }

        private static readonly string[] CompletionClaimSignals =
        [
            "已生成", "已保存", "已打开", "已创建", "已删除", "已移动", "已重命名", "已提交", "已写入", "已修改",
            "生成成功", "保存成功", "打开成功", "已重新生成", "已重新打开",
            "successfully generated", "successfully saved", "successfully created", "has been saved", "has been generated"
        ];

        internal static bool LooksLikeCompletionClaim(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            foreach (string signal in CompletionClaimSignals)
                if (content.Contains(signal, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string SummarizeArgs(Dictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            return string.Join(", ", args.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString() ?? "null", 40)}"));
        }

        internal static string SignatureOf(string tool, Dictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return tool;
            var sb = new StringBuilder(tool);
            sb.Append('\u0001');
            foreach (var kv in args.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value?.ToString() ?? "null").Append('\u0002');
            return sb.ToString();
        }

        internal static string FormatArgsDetailed(Dictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in args)
            {
                string v = kv.Value switch
                {
                    null => "null",
                    JsonElement je => je.ValueKind == JsonValueKind.Object
                        ? MapKeys(je.EnumerateObject().Select(p => p.Name), je.EnumerateObject().Count())
                        : Truncate(je.ToString(), 400),
                    System.Collections.IDictionary d => MapKeys(d.Keys.Cast<object>().Select(k => k?.ToString() ?? ""), d.Count),
                    string s => Truncate(s, 400),
                    _ => Truncate(kv.Value.ToString(), 400)
                };
                parts.Add($"{kv.Key}={v}");
            }
            return string.Join(", ", parts);
        }

        private static string MapKeys(IEnumerable<string> keys, int total)
        {
            var names = keys.Where(k => !string.IsNullOrEmpty(k)).Take(5).ToList();
            if (names.Count == 0) return $"{{共 {total} 项}}";
            string s = string.Join(", ", names);
            return total > names.Count ? $"{{{s}, …共 {total} 项}}" : $"{{{s}}}";
        }

        private static readonly string[] NonParameterErrors =
        [
            "401", "403", "unauthorized", "forbidden", "api key", "apikey", "密钥",
            "quota", "rate limit", "429", "insufficient", "余额", "额度",
            "timeout", "timed out", "超时", "network", "connection", "网络", "dns"
        ];

        internal static readonly JsonSerializerOptions ToolResultJson = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly string[] ParameterErrorSignals =
        [
            "400", "422", "bad request", "unprocessable", "invalid", "_errors",
            "expected ", "received null", "unknown parameter", "unexpected parameter",
            "missing", "required", "参数", "非法", "不合法", "字段"
        ];

        internal static bool IsParameterError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            string e = error.ToLowerInvariant();
            foreach (string noise in NonParameterErrors)
                if (e.Contains(noise, StringComparison.Ordinal)) return false;
            foreach (string signal in ParameterErrorSignals)
                if (e.Contains(signal, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool TryRepairArgs(
            Dictionary<string, object> args, string error,
            out Dictionary<string, object> repaired, out string note)
        {
            repaired = null;
            note = null;
            if (args == null || args.Count == 0) return false;

            var next = new Dictionary<string, object>(args);
            var fixes = new List<string>();

            foreach (string key in args.Where(kv => IsEmptyArg(kv.Value)).Select(kv => kv.Key).ToList())
                if (next.Remove(key)) fixes.Add($"移除空参数 {key}");

            string expected = ExpectedType(error);
            if (expected != null)
            {
                foreach (string key in args.Keys.Where(k => error?.Contains($"\"{k}\"", StringComparison.Ordinal) == true).ToList())
                {
                    if (!next.TryGetValue(key, out var value) || value == null || MatchesType(value, expected)) continue;
                    object coerced = CoerceTo(value, expected);
                    if (coerced == null) continue;
                    next[key] = coerced;
                    fixes.Add($"{key} 按 {expected} 修正");
                }
            }

            if (fixes.Count == 0) return false;
            repaired = next;
            note = string.Join("；", fixes);
            return true;
        }

        private static bool IsEmptyArg(object value) => value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            JsonElement je => je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                || (je.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(je.GetString())),
            _ => false
        };

        internal static string ExpectedType(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return null;
            var m = Regex.Match(error, @"Expected\s+(string|number|integer|boolean|bool|array|object)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string t = m.Groups[1].Value.ToLowerInvariant();
            return t == "bool" ? "boolean" : t;
        }

        private static bool MatchesType(object value, string expected) => expected switch
        {
            "string" => value is string || value is JsonElement { ValueKind: JsonValueKind.String },
            "number" or "integer" => value is long or int or double or float or decimal
                || value is JsonElement { ValueKind: JsonValueKind.Number },
            "boolean" => value is bool
                || value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False },
            _ => true
        };

        private static object CoerceTo(object value, string expected)
        {
            string raw = value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText(),
                _ => value?.ToString()
            };
            if (raw == null) return null;
            switch (expected)
            {
                case "string": return raw;
                case "number": return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
                case "integer": return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : null;
                case "boolean":
                    if (bool.TryParse(raw, out bool b2)) return b2;
                    if (raw == "1") return true;
                    if (raw == "0") return false;
                    return null;
                default: return null;
            }
        }

        internal static string ParameterHint(ToolRegistry tools, string toolName)
        {
            var tool = tools?.Find(toolName);
            return string.IsNullOrWhiteSpace(tool?.ParamsSchema)
                ? "\n（参数不被接受：请按工具说明修正参数后重试，不要原样重复调用。）"
                : $"\n（参数不被接受：请按该工具的参数说明修正后重试，不要原样重复调用——{Truncate(tool.ParamsSchema, 600)}）";
        }

        internal static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text[..max] + $"…（截断，共 {text.Length} 字符）";
        }
    }
}
