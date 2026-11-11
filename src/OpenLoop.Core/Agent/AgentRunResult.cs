namespace OpenLoop.Core.Agent
{
    /// <summary>Agent 循环执行结果。</summary>
    public sealed class AgentRunResult
    {
        public string FinalAnswer { get; set; }

        /// <summary>本轮结束时的完整消息列表（含工具调用与结果），宿主用它落盘做重放。</summary>
        public List<Messages.LlmMessage> Transcript { get; set; }

        /// <summary>上游结束原因（content_filter/refusal 等），用于区分『被拦截』与『空返回』。</summary>
        public string FinishReason { get; set; }
        public List<AgentStepRecord> Steps { get; } = [];
        public bool BudgetExhausted { get; set; }
        public decimal? ChatCost { get; set; }
        public bool Compressed => Guards.Compressions > 0;

        /// <summary>最近一次补全的输入 token 数（上下文占用），通道不报价时为 null。</summary>
        public int? ContextTokens { get; set; }

        /// <summary>用户请求停止后，在下一轮开始前终止。</summary>
        public bool StoppedByUser { get; set; }

        /// <summary>护栏判定无进展/格式持续错误而主动停机（不是用户停止，也不是预算耗尽）。</summary>
        public bool StoppedByGuard { get; set; }

        /// <summary>本轮护栏/恢复机制命中计数，用于评估参数调整（只观测，不参与决策）。</summary>
        public AgentGuardStats Guards { get; } = new();
    }

    public sealed class AgentGuardStats
    {
        public int RepeatCallStops { get; set; }
        public int RepeatNudges { get; set; }
        public int RepeatBans { get; set; }
        public int ExplorationStops { get; set; }
        public int ExplorationContinues { get; set; }
        public int ExplorationNudges { get; set; }
        public int ClaimNudges { get; set; }
        public int PromiseNudges { get; set; }
        public int EmptyRetries { get; set; }
        public int MalformedRetries { get; set; }

        /// <summary>坏 JSON 在本地修复成功、直接执行掉的次数。</summary>
        public int MalformedRepairs { get; set; }

        /// <summary>轮询声明放行的次数（被封顶削减的声明也计入）。</summary>
        public int PollExemptions { get; set; }

        /// <summary>步数上限自动追加的次数。</summary>
        public int BudgetExtensions { get; set; }
        public int ArgRepairAttempts { get; set; }
        public int ArgRepairSuccesses { get; set; }
        public int Compressions { get; set; }
    }
}
