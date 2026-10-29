namespace OpenLoop.Core.Agent
{
    public enum AgentStepStatus
    {
        Continuing,
        Yielded,
        Cancelled,
        Completed
    }

    /// <summary>
    /// 一个步骤的结果：Pass 表示不认识当前状态，交给下一个步骤；
    /// Applied 表示本步骤认领了这一步，并带回要应用的效果与运行状态。
    /// </summary>
    public sealed class AgentStepResult
    {
        private AgentStepResult()
        {
        }

        public bool Applied { get; private init; }
        public AgentStepStatus Status { get; private init; }
        public IReadOnlyList<IAgentEffect> Effects { get; private init; } = [];

        public static AgentStepResult Pass { get; } = new();

        public static AgentStepResult Continue(params IAgentEffect[] effects)
            => New(AgentStepStatus.Continuing, effects);

        public static AgentStepResult Yield(params IAgentEffect[] effects)
            => New(AgentStepStatus.Yielded, effects);

        public static AgentStepResult Cancel(params IAgentEffect[] effects)
            => New(AgentStepStatus.Cancelled, effects);

        public static AgentStepResult Complete(params IAgentEffect[] effects)
            => New(AgentStepStatus.Completed, effects);

        private static AgentStepResult New(AgentStepStatus status, IAgentEffect[] effects)
            => new() { Applied = true, Status = status, Effects = effects ?? [] };
    }

    public interface IAgentStep
    {
        string Name { get; }

        Task<AgentStepResult> RunAsync(AgentState state, AgentRunContext context, CancellationToken ct);
    }
}
