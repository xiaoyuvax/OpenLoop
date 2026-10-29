namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 有序、可重入的步骤管线：每次从第一个步骤开始问，第一个认领的步骤胜出；
    /// 应用它的效果后，从顶部重新开始，直到 Completed / Yielded / Cancelled。
    /// 宿主也可以自己调用 StepAsync + Apply，逐步驱动或把状态落盘后续跑。
    /// </summary>
    public sealed class AgentStateMachine
    {
        private readonly IReadOnlyList<IAgentStep> _steps;

        public AgentStateMachine(IEnumerable<IAgentStep> steps)
            => _steps = (steps ?? throw new ArgumentNullException(nameof(steps))).ToList();

        public IReadOnlyList<IAgentStep> Steps => _steps;

        public async Task<AgentStepResult> StepAsync(
            AgentState state, AgentRunContext context, CancellationToken ct = default)
        {
            foreach (var step in _steps)
            {
                var result = await step.RunAsync(state, context, ct);
                if (result.Applied) return result;
            }
            return AgentStepResult.Pass;
        }

        public void Apply(AgentState state, AgentRunContext context, AgentStepResult result)
        {
            if (result?.Applied != true) return;
            foreach (var effect in result.Effects)
                effect.Apply(state, context);
        }

        /// <summary>
        /// 便捷驱动：反复 Step/Apply，直到状态机停止。遇到 Yielded 且宿主提供了
        /// ConfirmContinue 时，由驱动方询问后继续；否则把控制权交回调用方。
        /// </summary>
        public async Task<AgentRunResult> RunAsync(
            AgentState state, AgentRunContext context, CancellationToken ct = default)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var result = await StepAsync(state, context, ct);
                if (!result.Applied)
                {
                    state.Result.FinalAnswer ??= "（状态机没有可执行的步骤）";
                    return state.Result;
                }

                Apply(state, context, result);

                switch (result.Status)
                {
                    case AgentStepStatus.Continuing:
                        continue;

                    case AgentStepStatus.Yielded:
                        if (!ResolveYield(state, context)) return state.Result;
                        continue;

                    case AgentStepStatus.Cancelled:
                        state.Result.StoppedByUser = true;
                        return state.Result;

                    default:
                        return state.Result;
                }
            }
        }

        private static bool ResolveYield(AgentState state, AgentRunContext context)
        {
            if (string.IsNullOrEmpty(state.PendingApproval) || context.ConfirmContinue == null)
                return false;

            state.ApprovalGranted = context.ConfirmContinue(state.PendingApproval);
            state.PendingApproval = null;
            return true;
        }
    }

    /// <summary>OpenLoop 的标准管线：工具 → 进度护栏 → 预算 → 回答护栏 → 软停止 → 压缩 → 推理。</summary>
    internal static class AgentPipeline
    {
        public static IReadOnlyList<IAgentStep> Create() =>
        [
            new ToolDispatchStep(),
            new ProgressGuardStep(),
            new ToolBudgetStep(),
            new ResponseGuardStep(),
            new StopStep(),
            new CompressionStep(),
            new InferenceStep(),
        ];
    }
}
