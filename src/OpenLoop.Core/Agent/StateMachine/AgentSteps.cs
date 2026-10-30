using System.Diagnostics;
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

            if (AgentContext.EstimateTokens(state.Messages) < context.ContextLimit * context.Policy.CompressAt)
                return AgentStepResult.Pass;

            var compressed = await CompressAsync(state.Messages, context.Reasoner, ct);
            if (compressed == null)
                return AgentStepResult.Pass;

            state.Result.Guards.Compressions++;
            return AgentStepResult.Continue(new ReplaceMessagesEffect(compressed));
        }

        private static async Task<List<LlmMessage>> CompressAsync(
            IReadOnlyList<LlmMessage> messages, IReasoner reasoner, CancellationToken ct)
        {
            var (head, middle, tail) = AgentContext.SplitForCompression(messages, AgentContext.KeepRecentMessages);
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
        public string Name => "inference";

        public async Task<AgentStepResult> RunAsync(
            AgentState state, AgentRunContext context, CancellationToken ct)
        {
            if (!state.NeedsInference)
                return AgentStepResult.Pass;

            if (state.Step >= state.MaxSteps)
            {
                return AgentStepResult.Complete(
                    new SetBudgetExhaustedEffect(),
                    new SetFinalAnswerEffect(AgentLoop.BudgetText(state)));
            }

            state.Step++;

            var completion = context.OnDelta != null && context.Reasoner is IStreamingReasoner streaming
                ? await streaming.StreamAsync(state.Messages, context.OnDelta, ct, context.OnReasoning, context.OnRestart)
                : await context.Reasoner.CompleteAsync(state.Messages, ct);

            string content = completion?.Content;
            var calls = AgentToolCallParser.TryParseAll(content);

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

            bool parallel = calls.Count > 1
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
                    var (result, repairNote) = await ExecuteWithArgRepairAsync(state, context, call, ct);
                    executed.Add((call, result, repairNote));
                }
            }
            watch.Stop();

            string batchSignature = string.Join("\u0003",
                calls.Select(c => AgentLoop.SignatureOf(c.Tool, c.Args)));
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

            state.RepeatStreak = batchSignature == state.LastSignature ? state.RepeatStreak + 1 : 1;
            state.LastSignature = batchSignature;
            state.ExplorationStreak = calls.All(c => context.Tools.Find(c.Tool)?.IsReadOnly == true)
                ? state.ExplorationStreak + 1
                : 0;
            state.LastBatchTool = calls[0].Tool;
            state.LastBatchCount = calls.Count;
            state.LastActionWasTools = true;
            state.ProgressChecked = false;

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
            if (context.Policy.ArgRepair && !toolResult.Ok && state.Step < state.MaxSteps
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
                return Task.FromResult(AgentStepResult.Continue());
            }

            if (context.Policy.RepeatCallGuard
                && state.RepeatStreak >= Math.Max(2, context.Policy.MaxRepeatCalls))
            {
                state.ProgressChecked = true;
                state.Result.Guards.RepeatCallStops++;
                return Task.FromResult(AgentStepResult.Complete(
                    new SetFinalAnswerEffect(
                        $"检测到连续 {state.RepeatStreak} 次完全相同的工具调用（{state.LastBatchTool} 等 {state.LastBatchCount} 个），" +
                        "判定为无进展，已停止以免空转。请补充信息或调整目标后重试。"),
                    new SetGuardedStopEffect()));
            }

            if (context.Policy.ExplorationGuard
                && state.ExplorationStreak >= Math.Max(2, context.Policy.MaxExplorationSteps))
            {
                string guardText =
                    $"已连续 {state.ExplorationStreak} 步只读侦察、未产生任何写操作，可能是在原地打转。\n\n" +
                    "「是」= 允许再继续执行（护栏计数清零）；「否」= 立即停止。";

                if (state.Step >= state.MaxSteps || context.ConfirmContinue == null)
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

            if (state.Step >= state.MaxSteps)
            {
                return Task.FromResult(AgentStepResult.Complete(
                    new SetBudgetExhaustedEffect(),
                    new SetFinalAnswerEffect(AgentLoop.BudgetText(state))));
            }

            return Task.FromResult(AgentStepResult.Continue());
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
            string reasoning = state.PendingReasoning;
            string finishReason = state.PendingFinishReason;
            state.PendingContent = null;
            state.PendingReasoning = null;
            state.PendingFinishReason = null;

            bool contentEmpty = LlmTextSanitizer.StripSafetyLines(content ?? "").Trim().Length == 0;
            bool hasReasoning = !string.IsNullOrWhiteSpace(reasoning);

            if (AgentToolCallParser.LooksLikeToolCallAttempt(content))
            {
                if (context.Policy.MalformedRetry
                    && state.MalformedRetries < context.Policy.MaxMalformedRetries
                    && state.Step < state.MaxSteps)
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

            if (context.Policy.ClaimGuard && !state.ClaimNudged && !state.AnyToolSucceeded
                && state.Step < state.MaxSteps && AgentLoop.LooksLikeCompletionClaim(content))
            {
                state.ClaimNudged = true;
                state.Result.Guards.ClaimNudges++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    "（系统提示）你这条回复声称已经完成了某个操作，但本轮没有任何工具成功执行。不要谎报完成：需要执行就输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；确实不需要工具（纯回答/说明）就重写答案，明确说明「未执行任何操作」，且不要再出现「已写入/已修改/已完成」这类说法。")));
            }

            if (context.Policy.PromiseGuard
                && state.PromiseNudges < context.Policy.MaxPromiseNudges
                && state.Step < state.MaxSteps
                && AgentLoop.LooksLikeActionPromise(content))
            {
                state.PromiseNudges++;
                state.Result.Guards.PromiseNudges++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    "（系统提示）你上一条只是说明将要执行某事，但没有实际调用工具。若仍需执行操作，请立即输出一行 tool-call: {\"tool\":\"工具名\",\"args\":{...}}；若已全部完成，请直接给出最终结果，不要回复「请稍候」这类占位说明。")));
            }

            if (!state.EmptyRetried && state.Step < state.MaxSteps && contentEmpty && !hasReasoning)
            {
                state.EmptyRetried = true;
                state.Result.Guards.EmptyRetries++;
                return Task.FromResult(AgentStepResult.Continue(new AppendMessageEffect("user",
                    "（系统提示）你上一轮没有返回正文（只有安全判定或空内容）。请直接重写完整答案，不要输出安全判定。")));
            }

            return Task.FromResult(AgentStepResult.Complete(new SetFinalAnswerEffect(
                contentEmpty && hasReasoning ? reasoning.Trim() : content?.Trim(), finishReason)));
        }
    }
}
