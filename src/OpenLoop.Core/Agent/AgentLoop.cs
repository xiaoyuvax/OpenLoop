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
    /// Agent 主循环（文本协议工具调用）：构建消息 → 补全 → 解析工具块 → 执行 → 结果回填 → 继续，
    /// 直到模型给出最终答案或步数预算耗尽。
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

        /// <summary>
        /// 执行一次完整任务。history 为既有对话（不含本次用户消息），userMessage 为本次请求。
        /// contextLimit &gt; 0 时，每轮补全前估算占用，超阈值即压缩较早消息（system 不动）。
        /// shouldStop = 软停止：只在每轮开始前检查，进行中的调用绝不被打断。
        /// </summary>
        /// <param name="onDelta">正文流式增量（null 时不走流式通道）。</param>
        /// <param name="onReasoning">推理通道流式增量，供宿主实时显示思考内容。</param>
        public async Task<AgentRunResult> RunAsync(
            IReadOnlyList<LlmMessage> history, string userMessage,
            CancellationToken ct = default, int contextLimit = 0, Func<bool> shouldStop = null,
            Action<string> onDelta = null, Action<string> onReasoning = null)
        {
            int maxSteps = Math.Clamp(_policy.MaxSteps, 1, 100);
            var result = new AgentRunResult();
            var messages = new List<LlmMessage>(history ?? [])
            {
                new LlmMessage("user", userMessage)
            };

            string lastSignature = null;
            int repeatStreak = 0;
            int explorationStreak = 0;
            int malformedRetries = 0;
            int promiseNudges = 0;
            bool claimNudged = false;
            bool anyToolSucceeded = false;
            bool emptyRetried = false;

            for (int step = 1; step <= maxSteps; step++)
            {
                if (shouldStop?.Invoke() == true)
                {
                    result.StoppedByUser = true;
                    return result;
                }
                ct.ThrowIfCancellationRequested();

                if (contextLimit > 0
                    && AgentContext.EstimateTokens(messages) >= contextLimit * _policy.CompressAt)
                {
                    var compressed = await CompressMessagesAsync(messages, ct);
                    if (compressed != null)
                    {
                        messages = compressed;
                        result.Guards.Compressions++;
                    }
                }

                var completion = onDelta != null && _reasoner is IStreamingReasoner streaming
                    ? await streaming.StreamAsync(messages, onDelta, ct, onReasoning)
                    : await _reasoner.CompleteAsync(messages, ct);
                string content = completion?.Content;
                string reasoning = completion?.Reasoning;
                bool contentEmpty = LlmTextSanitizer.StripSafetyLines(content ?? "").Trim().Length == 0;
                bool hasReasoning = !string.IsNullOrWhiteSpace(reasoning);
                if (completion?.Cost is decimal cost) result.ChatCost = (result.ChatCost ?? 0m) + cost;
                if (completion?.TokensIn is int contextTokens) result.ContextTokens = contextTokens;

                // 并行调用（仅全部只读时并行执行，最多 4 个；含写操作或解析失败回退串行/单调用路径）
                var calls = AgentToolCallParser.TryParseAll(content);
                if (calls.Count == 0)
                {
                    // 无工具块 → 最终答案（ LooksLikeToolCallAttempt 的纠正路径保持不变）
                    if (AgentToolCallParser.LooksLikeToolCallAttempt(content))
                    {
                        if (_policy.MalformedRetry && malformedRetries < _policy.MaxMalformedRetries && step < maxSteps)
                        {
                            malformedRetries++;
                            result.Guards.MalformedRetries++;
                            messages.Add(new LlmMessage("assistant", content));
                            messages.Add(new LlmMessage("user",
                                "tool-result: {\"ok\":false,\"error\":\"工具调用格式无效，JSON 无法解析。请重新输出一行合法 JSON（可跨行），格式：tool-call: {\\\"tool\\\":\\\"工具名\\\",\\\"args\\\":{...}}。\"}"));
                            continue;
                        }
                        result.FinalAnswer =
                            "⚠ 模型多次输出无法解析的工具调用（JSON 格式错误），已停止本次执行。请重试或更换模型。";
                        return result;
                    }

                    // 谎报完成护栏
                    if (_policy.ClaimGuard && !claimNudged && !anyToolSucceeded && step < maxSteps
                        && LooksLikeCompletionClaim(content))
                    {
                        claimNudged = true;
                        result.Guards.ClaimNudges++;
                        messages.Add(new LlmMessage("assistant", content));
                        messages.Add(new LlmMessage("user",
                            "（系统提示）你这条回复声称已经完成了某个操作，但本轮没有任何工具成功执行。不要谎报完成：需要执行就输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；确实不需要工具（纯回答/说明）就重写答案，明确说明「未执行任何操作」，且不要再出现「已写入/已修改/已完成」这类说法。"));
                        continue;
                    }

                    // 占位话术护栏
                    if (_policy.PromiseGuard && promiseNudges < _policy.MaxPromiseNudges
                        && step < maxSteps && LooksLikeActionPromise(content))
                    {
                        promiseNudges++;
                        result.Guards.PromiseNudges++;
                        messages.Add(new LlmMessage("assistant", content));
                        messages.Add(new LlmMessage("user",
                            "（系统提示）你上一条只是说明将要执行某事，但没有实际调用工具。若仍需执行操作，请立即输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；若已全部完成，请直接给出最终结果，不要回复「请稍候」这类占位说明。"));
                        continue;
                    }

                    // 上游只给安全判定/空正文：自动重试一次，再空才收尾。
                    // 推理模型只给 reasoning、正文为空属于正常输出，用推理文本兑底，不再重试。
                    if (!emptyRetried && step < maxSteps && contentEmpty && !hasReasoning)
                    {
                        emptyRetried = true;
                        result.Guards.EmptyRetries++;
                        messages.Add(new LlmMessage("assistant", content ?? ""));
                        messages.Add(new LlmMessage("user",
                            "（系统提示）你上一轮没有返回正文（只有安全判定或空内容）。请直接重写完整答案，不要输出安全判定。"));
                        continue;
                    }

                    result.FinalAnswer = contentEmpty && hasReasoning ? reasoning.Trim() : content?.Trim();
                    result.FinishReason = completion?.FinishReason;
                    return result;
                }

                // 并行上限：只读批量侦察通常 3-5 个，4 已覆盖；超过的拒绝执行、要求分轮
                if (calls.Count > 4)
                {
                    messages.Add(new LlmMessage("assistant", content));
                    messages.Add(new LlmMessage("user",
                        $"tool-result: {{\"ok\":false,\"error\":\"一次最多并行 {4} 个工具调用（本轮 {calls.Count} 个）。请拆成 ≤4 个/轮，或改为多轮。\"}}"));
                    continue;
                }

                bool parallel = calls.Count > 1 && calls.All(c => _tools.Find(c.Tool)?.IsReadOnly == true);

                // 执行（并行=Task.WhenAll，串行=原路径；参数自愈只对单调用启用）
                var stepWatch = System.Diagnostics.Stopwatch.StartNew();
                var executed = new List<(AgentToolCall Call, AgentToolResult Result, string RepairNote)>();
                if (parallel)
                {
                    var tasks = calls.Select(async c => (c, await ExecuteToolSafeAsync(c, ct), (string)null));
                    var done = await Task.WhenAll(tasks);
                    executed.AddRange(done);
                }
                else
                {
                    foreach (var call in calls)
                    {
                        var (toolResult, repairNote, effectiveArgs) = await ExecuteWithArgRepairAsync(call, step, maxSteps, ct, result);
                        executed.Add((call, toolResult, repairNote));
                    }
                }
                stepWatch.Stop();

                // 记录 + 无进展护栏（签名按整轮调用集合算）
                string batchSignature = string.Join("\u0003", calls.Select(c => SignatureOf(c.Tool, c.Args)));
                string contentEcho = content;
                foreach (var (call, toolResult, repairNote) in executed)
                {
                    string dataOut = null;
                    string errorOut = null;
                    if (toolResult.Ok)
                    {
                        anyToolSucceeded = true;
                        dataOut = Truncate(toolResult.Data, _policy.ToolResultLimit);
                        if (repairNote != null) dataOut = repairNote + "\n" + dataOut;
                    }
                    else
                    {
                        errorOut = repairNote != null ? repairNote + "\n" + toolResult.Error : toolResult.Error;
                        if (IsParameterError(errorOut)) errorOut += ParameterHint(call.Tool);
                    }

                    string payload = toolResult.Ok
                        ? JsonSerializer.Serialize(new { ok = true, data = dataOut }, ToolResultJson)
                        : JsonSerializer.Serialize(new { ok = false, error = errorOut }, ToolResultJson);
                    string full = $"tool-result: {payload}";

                    var record = new AgentStepRecord
                    {
                        Index = step,
                        Tool = call.Tool,
                        ArgsSummary = SummarizeArgs(call.Args),
                        ArgsFull = FormatArgsDetailed(call.Args),
                        Ok = toolResult.Ok,
                        Summary = Truncate(toolResult.Ok ? dataOut : errorOut, 160),
                        FullResult = full,
                        ResultFull = Truncate(toolResult.Ok ? dataOut : errorOut, 20000),
                        ArgsRepaired = repairNote != null,
                        DurationMs = stepWatch.ElapsedMilliseconds
                    };
                    result.Steps.Add(record);
                    _onStep?.Invoke(record);
                }

                repeatStreak = batchSignature == lastSignature ? repeatStreak + 1 : 1;
                lastSignature = batchSignature;
                explorationStreak = calls.All(c => _tools.Find(c.Tool)?.IsReadOnly == true) ? explorationStreak + 1 : 0;

                if (_policy.RepeatCallGuard && repeatStreak >= Math.Max(2, _policy.MaxRepeatCalls))
                {
                    result.Guards.RepeatCallStops++;
                    result.FinalAnswer =
                        $"检测到连续 {repeatStreak} 次完全相同的工具调用（{calls[0].Tool} 等 {calls.Count} 个），判定为无进展，已停止以免空转。" +
                        "请补充信息或调整目标后重试。";
                    return result;
                }
                if (_policy.ExplorationGuard && explorationStreak >= Math.Max(2, _policy.MaxExplorationSteps))
                {
                    string guardText =
                        $"已连续 {explorationStreak} 步只读侦察、未产生任何写操作，可能是在原地打转。\n\n" +
                        "「是」= 允许再继续执行（护栏计数清零）；「否」= 立即停止。";
                    if (step >= maxSteps || _confirmContinue == null || !_confirmContinue(guardText))
                    {
                        result.Guards.ExplorationStops++;
                        result.FinalAnswer =
                            $"连续 {explorationStreak} 步仅执行只读侦察、未产生任何写操作，判定为在原地打转，已停止。" +
                            "请给出更明确的目标或参数（例如具体文件、目录或要执行的动作）。";
                        return result;
                    }
                    result.Guards.ExplorationContinues++;
                    explorationStreak = 0;
                }

                // 回填：一条 user 消息带全部结果（保持调用顺序）
                messages.Add(new LlmMessage("assistant", contentEcho));
                messages.Add(new LlmMessage("user", string.Join("\n",
                    executed.Select(e => $"tool-result: {JsonSerializer.Serialize(
                        e.Result.Ok
                            ? (object)new { ok = true, data = Truncate(e.Result.Data, _policy.ToolResultLimit) }
                            : new { ok = false, error = e.Result.Error }, ToolResultJson)}"))));

                if (step == maxSteps)
                {
                    result.BudgetExhausted = true;
                    result.FinalAnswer = $"已达单次任务步数上限（{maxSteps} 步）。已执行的步骤：\n"
                        + string.Join("\n", result.Steps.Select(s => $"{s.Index}. {s.Tool}({s.ArgsSummary}) → {(s.Ok ? "成功" : "失败")}"));
                    return result;
                }
            }

            return result;
        }

        private async Task<AgentToolResult> ExecuteToolSafeAsync(AgentToolCall call, CancellationToken ct)
        {
            try
            {
                return await _tools.ExecuteAsync(call.Tool, call.Args, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"工具执行异常：{ex.Message}");
            }
        }

        /// <summary>单调用路径（保留参数自愈）；返回 (结果, 修复说明, 生效参数)。</summary>
        private async Task<(AgentToolResult, string, Dictionary<string, object>)> ExecuteWithArgRepairAsync(
            AgentToolCall call, int step, int maxSteps, CancellationToken ct, AgentRunResult result)
        {
            _onToolStart?.Invoke(call.Tool, call.Args);
            var toolResult = await ExecuteToolSafeAsync(call, ct);

            var effectiveArgs = call.Args;
            string repairNote = null;
            if (_policy.ArgRepair && !toolResult.Ok && step < maxSteps && IsParameterError(toolResult.Error)
                && TryRepairArgs(call.Args, toolResult.Error, out var repairedArgs, out string fixedDesc))
            {
                result.Guards.ArgRepairAttempts++;
                string firstError = toolResult.Error;
                effectiveArgs = repairedArgs;
                _onToolStart?.Invoke(call.Tool, repairedArgs);
                try
                {
                    toolResult = await _tools.ExecuteAsync(call.Tool, repairedArgs, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    toolResult = AgentToolResult.Failure($"工具执行异常：{ex.Message}");
                }
                if (toolResult.Ok) result.Guards.ArgRepairSuccesses++;
                repairNote = toolResult.Ok
                    ? $"（参数自动修正后重试成功：{fixedDesc}）"
                    : $"（参数自动修正后重试仍失败：{fixedDesc}；原始错误：{Truncate(firstError, 300)}）";
            }
            return (toolResult, repairNote, effectiveArgs);
        }

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

        private async Task<List<LlmMessage>> CompressMessagesAsync(List<LlmMessage> messages, CancellationToken ct)
        {
            var (head, middle, tail) = AgentContext.SplitForCompression(messages, AgentContext.KeepRecentMessages);
            if (middle.Count == 0) return null;
            string summary;
            try
            {
                var completion = await _reasoner.CompleteAsync(
                    [new LlmMessage("user", AgentContext.SummaryInstruction + AgentContext.RenderForSummary(middle))], ct);
                summary = completion?.Content?.Trim();
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(summary)) return null;
            var rebuilt = new List<LlmMessage>(head)
            {
                new LlmMessage("user", AgentContext.SummaryPrefix + summary)
            };
            rebuilt.AddRange(tail);
            return rebuilt;
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

        private static readonly JsonSerializerOptions ToolResultJson = new()
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

        private string ParameterHint(string toolName)
        {
            var tool = _tools.Find(toolName);
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
