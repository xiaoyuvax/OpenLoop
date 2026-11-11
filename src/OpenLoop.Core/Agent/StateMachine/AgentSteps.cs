using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Core.Agent
{
    internal sealed class StopStep : IAgentStep
    {
        public string Name => "stop";

        public Task<AgentStepResult> RunAsync(AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (context.ShouldStop?.Invoke() != true)
                return Task.FromResult(AgentStepResult.Pass);

            return Task.FromResult(AgentStepResult.Cancel(new SetStoppedByUserEffect()));
        }
    }

    internal sealed class CompressionStep : IAgentStep
    {
        public string Name => "compression";

        public async Task<AgentStepResult> RunAsync(
            AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (context.ContextLimit <= 0 || !state.NeedsInference)
                return AgentStepResult.Pass;

            int before = AgentContext.EstimateTokens(state.Messages);
            if (before < context.ContextLimit * context.Policy.CompressAt)
                return AgentStepResult.Pass;

            var compressed = await CompressAsync(state.Messages, context.Reasoner, ct,
                AgentContext.TailTokenBudget(context.ContextLimit));

            // 尾部（最近几条）自己就超过阈值时，压缩只会把中间换成「摘要的摘要」、长度不再下降；
            // 没有这道闸，RunAsync 会一直重入本步，每圈打一次模型，永不结束。
            if (compressed == null || AgentContext.EstimateTokens(compressed) >= before)
                return AgentStepResult.Pass;

            state.Result.Guards.Compressions++;
            return AgentStepResult.Continue(new ReplaceMessagesEffect(compressed));
        }

        internal static async Task<List<LlmMessage>> CompressAsync(
            IReadOnlyList<LlmMessage> messages, IReasoner reasoner, CancellationToken ct, int tailTokens = 0)
        {
            var (head, middle, tail) = AgentContext.SplitForCompression(
                messages, AgentContext.KeepRecentMessages,
                tailTokens > 0 ? tailTokens : AgentContext.KeepRecentTokens);
            if (middle.Count == 0) return null;

            string summary;
            try
            {
                var completion = await reasoner.CompleteAsync(
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
                new("user", AgentContext.SummaryPrefix + summary)
            };
            rebuilt.AddRange(tail);
            return rebuilt;
        }
    }

    internal sealed class InferenceStep : IAgentStep
    {
        private const int MaxContextOverflowRetries = 2;

        public string Name => "inference";

        public async Task<AgentStepResult> RunAsync(
            AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (!state.NeedsInference)
                return AgentStepResult.Pass;

            if (state.StepLimitReached)
            {
                if (ToolBudgetStep.TryExtendBudget(state, context, out string budgetNote))
                    return AgentStepResult.Continue(new AppendMessageEffect("user", budgetNote));
                return AgentStepResult.Complete(
                    new SetBudgetExhaustedEffect(),
                    new SetFinalAnswerEffect(AgentLoop.BudgetText(state)));
            }

            state.Step++;

            context.OnRequest?.Invoke(state.Messages, AgentContext.EstimateTokens(state.Messages));

            CompletionResult completion;
            try
            {
                completion = context.OnDelta != null && context.Reasoner is IStreamingReasoner streaming
                    ? await streaming.StreamAsync(state.Messages, context.OnDelta, ct, context.OnReasoning, context.OnRestart)
                    : await context.Reasoner.CompleteAsync(state.Messages, ct);
            }
            catch (Exception ex) when (state.ContextOverflows < MaxContextOverflowRetries
                                       && AgentContext.IsContextOverflow(ex.Message))
            {
                // 服务端真实上限可能低于配置值：按报错压缩一次再重试，不要直接判任务失败。
                state.ContextOverflows++;
                context.OnRestart?.Invoke();
                var compressed = await CompressionStep.CompressAsync(state.Messages, context.Reasoner, ct,
                    AgentContext.TailTokenBudget(context.ContextLimit));
                if (compressed == null) throw;
                state.Result.Guards.Compressions++;
                return AgentStepResult.Continue(new ReplaceMessagesEffect(compressed));
            }

            string content = completion?.Content;
            var calls = AgentToolCallParser.TryParseAll(content);
            if (calls.Count == 0 && string.IsNullOrWhiteSpace(content))
                calls = AgentToolCallParser.TryParseAll(completion?.Reasoning);

            // 解析不出调用但确实是在尝试调用：先本地修补（尾逗号/缺右括号/被吞掉开头），能还原就执行，
            // 不要把一次格式失误变成一次整轮浪费。
            if (calls.Count == 0 && AgentToolCallParser.LooksLikeToolCallAttempt(content))
            {
                var repaired = AgentToolCallParser.TryRepair(content);
                if (repaired.Count > 0)
                {
                    calls = repaired;
                    state.Result.Guards.MalformedRepairs++;
                    state.PendingRepairNote =
                        "（系统提示）你上一条工具调用 JSON 有格式错误，宿主已自动修复并执行。" +
                        "请严格按协议输出：tool-call: {\"tool\":\"工具名\",\"args\":{...}}（单行、双引号、不要代码围栏）。";
                }
            }

            state.PendingContent = content;
            state.PendingReasoning = completion?.Reasoning;
            state.PendingFinishReason = completion?.FinishReason;
            state.PendingCalls = calls.ToList();
            state.PendingFinal = calls.Count == 0;

            return AgentStepResult.Continue(
                new AppendMessageEffect("assistant", content),
                new RecordUsageEffect(completion?.Cost, completion?.TokensIn));
        }
    }

    internal sealed class ToolDispatchStep : IAgentStep
    {
        public string Name => "tools";

        public async Task<AgentStepResult> RunAsync(
            AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (state.PendingCalls.Count == 0)
                return AgentStepResult.Pass;

            var calls = state.PendingCalls;
            state.PendingCalls = [];
            state.PendingFinal = false;
            state.PendingContent = null;
            state.PendingReasoning = null;
            state.PendingFinishReason = null;

            if (calls.Count > 4)
            {
                state.LastActionWasTools = false;
                state.ProgressChecked = false;
                return AgentStepResult.Continue(new AppendMessageEffect("user",
                    $"tool-result: {{\"ok\":false,\"error\":\"一次最多并行 {4} 个工具调用（本轮 {calls.Count} 个）。请拆成 ≤4 个/轮，或改为多轮。\"}}"));
            }

            string batchSignature = string.Join("\u0003",
                calls.Select(c => AgentLoop.SignatureOf(c.Tool, c.Args)));

            var notes = new List<string>();
            if (state.PendingRepairNote != null)
            {
                notes.Add(state.PendingRepairNote);
                state.PendingRepairNote = null;
            }

            // 轮询声明只认单调用批次（轮询天然是一次一次发）；额度按调用签名计，换 id 不会多给额度。
            if (calls.Count == 1 && calls[0].Poll is { } poll)
            {
                int cap = Math.Max(1, context.Policy.MaxPollRepeats);
                int allowed = Math.Min(poll.Max, cap);
                if (!state.PollGrants.TryGetValue(batchSignature, out var grant))
                {
                    // 额度只在首次声明时确定；同签名再声明（换不换 id）都不追加，避免反复续期。
                    state.PollGrants[batchSignature] = grant = new AgentPollGrant
                    {
                        Id = poll.Id,
                        Allowed = allowed
                    };
                }
                if (poll.Max > cap)
                    notes.Add($"（系统提示）轮询 {poll.Id} 声明 {poll.Max} 次，已按上限 {cap} 次计。");
            }

            bool hadBanned = calls.Any(c => state.BannedSignatures.Contains(AgentLoop.SignatureOf(c.Tool, c.Args)));
            state.LastBatchBanned = hadBanned;
            bool parallel = calls.Count > 1 && !hadBanned
                && calls.All(c => context.Tools.Find(c.Tool)?.IsReadOnly == true);

            var watch = Stopwatch.StartNew();
            var executed = new List<(AgentToolCall Call, AgentToolResult Result, string RepairNote)>();
            if (parallel)
            {
                var tasks = calls.Select(async c =>
                    (c, await ExecuteToolSafeAsync(context.Tools, c, ct), (string)null));
                executed.AddRange(await Task.WhenAll(tasks));
            }
            else
            {
                foreach (var call in calls)
                {
                    if (state.BannedSignatures.Contains(AgentLoop.SignatureOf(call.Tool, call.Args)))
                    {
                        executed.Add((call, AgentToolResult.Failure(
                            $"该调用（{call.Tool} + 完全相同的参数）因重复被临时禁用一轮，本轮不执行。" +
                            "请换工具、换参数，或直接给出结论。"), null));
                        continue;
                    }
                    var (result, repairNote) = await ExecuteWithArgRepairAsync(state, context, call, ct);
                    executed.Add((call, result, repairNote));
                }
            }
            watch.Stop();
            state.BannedSignatures.Clear();
            var effects = new List<IAgentEffect>();
            var payloads = new List<string>();

            foreach (var (call, toolResult, repairNote) in executed)
            {
                string dataOut = null;
                string errorOut = null;
                if (toolResult.Ok)
                {
                    state.AnyToolSucceeded = true;
                    dataOut = AgentLoop.Truncate(toolResult.Data, context.Policy.ToolResultLimit);
                    if (repairNote != null) dataOut = repairNote + "\n" + dataOut;
                }
                else
                {
                    errorOut = repairNote != null ? repairNote + "\n" + toolResult.Error : toolResult.Error;
                    if (AgentLoop.IsParameterError(errorOut))
                        errorOut += AgentLoop.ParameterHint(context.Tools, call.Tool);
                }

                string payload = toolResult.Ok
                    ? JsonSerializer.Serialize(new { ok = true, data = dataOut }, AgentLoop.ToolResultJson)
                    : JsonSerializer.Serialize(new { ok = false, error = errorOut }, AgentLoop.ToolResultJson);
                payloads.Add($"tool-result: {payload}");

                effects.Add(new RecordStepEffect(new AgentStepRecord
                {
                    Index = state.Step,
                    Tool = call.Tool,
                    ArgsSummary = AgentLoop.SummarizeArgs(call.Args),
                    ArgsFull = AgentLoop.FormatArgsDetailed(call.Args),
                    Ok = toolResult.Ok,
                    Summary = AgentLoop.Truncate(toolResult.Ok ? dataOut : errorOut, 160),
                    FullResult = $"tool-result: {payload}",
                    ResultFull = AgentLoop.Truncate(toolResult.Ok ? dataOut : errorOut, 20000),
                    ArgsRepaired = repairNote != null,
                    DurationMs = watch.ElapsedMilliseconds
                }));
            }

            var resultSignature = new StringBuilder();
            foreach (var (_, result, _) in executed)
                resultSignature.Append(result.Ok ? "ok:" : "err:")
                    .Append(result.Ok ? result.Data : result.Error).Append('\u0003');

            bool isRepeat = batchSignature == state.LastSignature;
            bool sameResult = isRepeat && resultSignature.ToString() == state.LastResultSignature;

            bool exempt = false;
            if (isRepeat && (sameResult || hadBanned)
                && state.PollGrants.TryGetValue(batchSignature, out var active)
                && active.Used < active.Allowed
                && state.PollTotal < Math.Max(1, context.Policy.MaxPollTotal))
            {
                exempt = true;
                active.Used++;
                state.PollTotal++;
                state.Result.Guards.PollExemptions++;
                notes.Add($"（系统提示）轮询 {active.Id}：{active.Used}/{active.Allowed}，" +
                          $"剩余 {active.Allowed - active.Used} 次。");
            }

            state.RepeatStreak = !isRepeat ? 1 : exempt ? 0 : state.RepeatStreak + 1;
            state.LastSignature = batchSignature;
            state.LastResultSignature = resultSignature.ToString();
            state.ExplorationStreak = calls.All(c => context.Tools.Find(c.Tool)?.IsReadOnly == true)
                ? state.ExplorationStreak + 1
                : 0;
            state.LastBatchTool = calls[0].Tool;
            state.LastBatchCount = calls.Count;
            state.LastActionWasTools = true;
            state.ProgressChecked = false;

            if (notes.Count > 0) payloads.AddRange(notes);
            effects.Add(new AppendMessageEffect("user", string.Join("\n", payloads)));
            return AgentStepResult.Continue(effects.ToArray());
        }

        private static async Task<AgentToolResult> ExecuteToolSafeAsync(
            OpenLoop.Core.Tools.ToolRegistry tools, AgentToolCall call, CancellationToken ct)
        {
            try
            {
                return await tools.ExecuteAsync(call.Tool, call.Args, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"工具执行异常：{ex.Message}");
            }
        }

        private static async Task<(AgentToolResult Result, string RepairNote)> ExecuteWithArgRepairAsync(
            AgentState state, AgentRunContext context, AgentToolCall call, CancellationToken ct)
        {
            context.OnToolStart?.Invoke(call.Tool, call.Args);
            var toolResult = await ExecuteToolSafeAsync(context.Tools, call, ct);

            string repairNote = null;
            if (context.Policy.ArgRepair && !toolResult.Ok && !state.StepLimitReached
                && AgentLoop.IsParameterError(toolResult.Error)
                && AgentLoop.TryRepairArgs(call.Args, toolResult.Error, out var repairedArgs, out string fixedDesc))
            {
                state.Result.Guards.ArgRepairAttempts++;
                string firstError = toolResult.Error;
                context.OnToolStart?.Invoke(call.Tool, repairedArgs);
                try
                {
                    toolResult = await context.Tools.ExecuteAsync(call.Tool, repairedArgs, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    toolResult = AgentToolResult.Failure($"工具执行异常：{ex.Message}");
                }

                if (toolResult.Ok) state.Result.Guards.ArgRepairSuccesses++;
                repairNote = toolResult.Ok
                    ? $"（参数自动修正后重试成功：{fixedDesc}）"
                    : $"（参数自动修正后重试仍失败：{fixedDesc}；原始错误：{AgentLoop.Truncate(firstError, 300)}）";
            }

            return (toolResult, repairNote);
        }
    }

    internal sealed class ProgressGuardStep : IAgentStep
    {
        public string Name => "progress";

        public Task<AgentStepResult> RunAsync(AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (!state.LastActionWasTools || state.ProgressChecked)
                return Task.FromResult(AgentStepResult.Pass);

            if (!string.IsNullOrEmpty(state.PendingApproval) && !state.ApprovalGranted.HasValue)
                return Task.FromResult(AgentStepResult.Yield());

            if (state.ApprovalGranted.HasValue)
            {
                bool granted = state.ApprovalGranted.Value;
                state.ApprovalGranted = null;
                state.ProgressChecked = true;

                if (!granted)
                {
                    state.Result.Guards.ExplorationStops++;
                    return Task.FromResult(AgentStepResult.Complete(
                        new SetFinalAnswerEffect(
                            $"连续 {state.ExplorationStreak} 步仅执行只读侦察、未产生任何写操作，判定为在原地打转，已停止。" +
                            "请给出更明确的目标或参数（例如具体文件、目录或要执行的动作）。"),
                        new SetGuardedStopEffect()));
                }

                state.Result.Guards.ExplorationContinues++;
                state.ExplorationStreak = 0;
                state.ExplorationNudges = 0;
                return Task.FromResult(AgentStepResult.Continue());
            }

            if (context.Policy.RepeatCallGuard
                && state.RepeatStreak >= Math.Max(2, context.Policy.MaxRepeatCalls))
            {
                state.ProgressChecked = true;

                // 刚被"临时禁用"拒收过：让模型先看到拒绝结果再决定换做法，不直接判停。
                if (state.LastBatchBanned)
                    return Task.FromResult(AgentStepResult.Continue());

                if (state.RepeatNudges < 2)
                {
                    state.RepeatNudges++;
                    state.Result.Guards.RepeatNudges++;
                    bool ban = state.RepeatNudges >= 2 && state.LastBatchCount == 1
                        && !string.IsNullOrEmpty(state.LastSignature);
                    if (ban)
                    {
                        state.BannedSignatures.Add(state.LastSignature);
                        state.Result.Guards.RepeatBans++;
                    }
                    return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user", ban
                        ? $"（系统提示·强制）你已连续 {state.RepeatStreak} 次发出完全相同的调用（{state.LastBatchTool}），结果也没有变化。" +
                          "该调用已被临时禁用一轮：下一次相同调用会被直接拒绝。请换工具、换参数，或直接给出结论。"
                        : $"（系统提示·强制）你已经连续 {state.RepeatStreak} 次发出完全相同的调用（{state.LastBatchTool}），" +
                          "返回结果也没有变化。再重复一次会被临时禁用。请先想清楚缺哪条信息，换一个工具或参数，或直接给出结论。")));
                }

                state.Result.Guards.RepeatCallStops++;
                return Task.FromResult(AgentStepResult.Complete(
                    new SetFinalAnswerEffect(
                        $"检测到连续 {state.RepeatStreak} 次完全相同的工具调用（{state.LastBatchTool} 等 {state.LastBatchCount} 个），" +
                        "判定为无进展，已停止以免空转。若确实需要反复等待，请在调用里声明轮询：" +
                        "{\"tool\":\"…\",\"args\":{…},\"poll\":{\"id\":\"wait-1\",\"max\":5}}。"),
                    new SetGuardedStopEffect()));
            }

            if (context.Policy.ExplorationGuard
                && state.ExplorationStreak >= Math.Max(2, context.Policy.MaxExplorationSteps))
            {
                if (state.ExplorationNudges < 2)
                {
                    state.ProgressChecked = true;
                    state.ExplorationNudges++;
                    state.Result.Guards.ExplorationNudges++;
                    string nudge = state.ExplorationNudges == 1
                        ? $"（系统提示·强制）已连续 {state.ExplorationStreak} 步只读侦察、没有任何写操作。停止继续侦察。" +
                          "下一步二选一：① 直接输出具体的写操作 tool-call（确切的工具 + 参数）；② 直接给出结论，并说明还缺哪条信息。"
                        : "（系统提示·强制）这是最后一次纠正：再发只读侦察就会被判定在原地打转并停止。" +
                          "要么现在落笔执行，要么直接给结论。";
                    return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user", nudge)));
                }

                string guardText =
                    $"已连续 {state.ExplorationStreak} 步只读侦察、未产生任何写操作，可能是在原地打转。\n\n" +
                    "「是」= 允许再继续执行（护栏计数清零）；「否」= 立即停止。";

                if (state.StepLimitReached || context.ConfirmContinue == null)
                {
                    state.ProgressChecked = true;
                    state.Result.Guards.ExplorationStops++;
                    return Task.FromResult(AgentStepResult.Complete(
                        new SetFinalAnswerEffect(
                            $"连续 {state.ExplorationStreak} 步仅执行只读侦察、未产生任何写操作，判定为在原地打转，已停止。" +
                            "请给出更明确的目标或参数（例如具体文件、目录或要执行的动作）。"),
                        new SetGuardedStopEffect()));
                }

                state.PendingApproval = guardText;
                return Task.FromResult(AgentStepResult.Yield());
            }

            state.ProgressChecked = true;
            return Task.FromResult(AgentStepResult.Continue());
        }
    }

    internal sealed class ToolBudgetStep : IAgentStep
    {
        public string Name => "budget";

        public Task<AgentStepResult> RunAsync(AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (!state.LastActionWasTools || !state.ProgressChecked)
                return Task.FromResult(AgentStepResult.Pass);

            state.LastActionWasTools = false;
            state.ProgressChecked = false;

            if (state.StepLimitReached)
            {
                if (TryExtendBudget(state, context, out string note))
                    return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user", note)));
                return Task.FromResult(AgentStepResult.Complete(
                    new SetBudgetExhaustedEffect(),
                    new SetFinalAnswerEffect(AgentLoop.BudgetText(state))));
            }

            return Task.FromResult(AgentStepResult.Continue());
        }

        /// <summary>到上限时自动追加步数（次数有上限）；返回 false 表示追加额度已用完。</summary>
        internal static bool TryExtendBudget(AgentState state, AgentRunContext context, out string note)
        {
            note = null;
            int step = context.Policy.BudgetExtendSteps;
            if (step <= 0 || state.MaxSteps <= 0
                || state.BudgetExtensions >= Math.Max(1, context.Policy.MaxBudgetExtensions))
                return false;

            state.BudgetExtensions++;
            state.MaxSteps += step;
            state.Result.Guards.BudgetExtensions++;
            note = $"（系统提示）已到单次任务步数上限，宿主自动追加 {step} 步" +
                   $"（第 {state.BudgetExtensions} 次，当前上限 {state.MaxSteps} 步）。请优先收尾：给出结果，或只做必须的动作。";
            return true;
        }
    }

    internal sealed class ResponseGuardStep : IAgentStep
    {
        public string Name => "response";

        public Task<AgentStepResult> RunAsync(AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (!state.PendingFinal)
                return Task.FromResult(AgentStepResult.Pass);

            state.PendingFinal = false;
            string content = state.PendingContent;
            string finishReason = state.PendingFinishReason;
            state.PendingContent = null;
            state.PendingReasoning = null;
            state.PendingFinishReason = null;

            bool contentEmpty = LlmTextSanitizer.StripSafetyLines(content ?? "").Trim().Length == 0;

            if (AgentToolCallParser.LooksLikeToolCallAttempt(content))
            {
                if (context.Policy.MalformedRetry
                    && state.MalformedRetries < context.Policy.MaxMalformedRetries
                    && !state.StepLimitReached)
                {
                    state.MalformedRetries++;
                    state.Result.Guards.MalformedRetries++;
                    return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                        "tool-result: {\"ok\":false,\"error\":\"工具调用格式无效，JSON 无法解析。请重新输出一行合法 JSON（可跨行），格式：tool-call: {\\\"tool\\\":\\\"工具名\\\",\\\"args\\\":{...}}。\"}")));
                }

                return Task.FromResult(AgentStepResult.Complete(
                    new SetFinalAnswerEffect(
                        "⚠ 模型多次输出无法解析的工具调用（JSON 格式错误），已停止本次执行。请重试或更换模型。"),
                    new SetGuardedStopEffect()));
            }

            if (context.Policy.ClaimGuard && state.ClaimNudges < 2 && !state.AnyToolSucceeded
                && !state.StepLimitReached && AgentLoop.LooksLikeCompletionClaim(content))
            {
                state.ClaimNudges++;
                state.Result.Guards.ClaimNudges++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    state.ClaimNudges == 1
                        ? "（系统提示）你这条回复声称已经完成了某个操作，但本轮没有任何工具成功执行。不要谎报完成：需要执行就输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；确实不需要工具（纯回答/说明）就重写答案，明确说明「未执行任何操作」，且不要再出现「已写入/已修改/已完成」这类说法。"
                        : "（系统提示·强制）你又一次声称完成了操作，但没有任何工具成功执行。这是最后一次纠正：要么本条只输出 tool-call 去真正执行，要么把答案里所有「已完成/已写入/已修改/已生成」改写成「未执行」，并说明原因。")));
            }

            if (context.Policy.PromiseGuard
                && state.PromiseNudges < context.Policy.MaxPromiseNudges
                && !state.StepLimitReached
                && AgentLoop.LooksLikeActionPromise(content))
            {
                state.PromiseNudges++;
                state.Result.Guards.PromiseNudges++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    state.PromiseNudges == 1
                        ? "（系统提示）你上一条只是说明将要执行某事，但没有实际调用工具。若仍需执行操作，请立即输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；若已全部完成，请直接给出最终结果，不要回复「请稍候」这类占位说明。"
                        : "（系统提示·强制）最后一次：本条回复必须要么是 tool-call，要么是完整答案，禁止再出现「稍候/我将/让我看看」这类占位话术。")));
            }

            if (state.EmptyRetries < 3 && !state.StepLimitReached && contentEmpty)
            {
                state.EmptyRetries++;
                state.Result.Guards.EmptyRetries++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    state.EmptyRetries == 1
                        ? "（系统提示）你上一轮没有返回正文（只有安全判定或空内容）。请直接重写完整答案，不要输出安全判定。"
                        : "（系统提示·强制）又一次空正文。请直接回答用户的问题，或用 tool-call 继续任务；不要再输出安全判定/空白。")));
            }

            return Task.FromResult(AgentStepResult.Complete(new SetFinalAnswerEffect(
                content?.Trim(), finishReason)));
        }
    }
}
