using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Agent
{
    /// <summary>状态机运行所需的外部依赖与事件回调。</summary>
    public sealed class AgentRunContext
    {
        public ToolRegistry Tools { get; init; }
        public IReasoner Reasoner { get; init; }
        public AgentPolicy Policy { get; init; } = new();

        /// <summary>0 表示不压缩。</summary>
        public int ContextLimit { get; init; }

        public Func<bool> ShouldStop { get; init; }
        public Action<string> OnDelta { get; init; }
        public Action<string> OnReasoning { get; init; }
        public Action<AgentStepRecord> OnStep { get; init; }
        public Action<string, Dictionary<string, object>> OnToolStart { get; init; }

        /// <summary>需要宿主确认时调用；返回 false 表示停止。为 null 时直接停止。</summary>
        public Func<string, bool> ConfirmContinue { get; init; }
    }
}
