namespace OpenLoop.Core.Agent
{
    /// <summary>Agent 循环执行结果。</summary>
    public sealed class AgentRunResult
    {
        public string FinalAnswer { get; set; }
        public List<AgentStepRecord> Steps { get; } = [];
        public bool BudgetExhausted { get; set; }
        public decimal? ChatCost { get; set; }
        public bool Compressed { get; set; }

        /// <summary>最近一次补全的输入 token 数（上下文占用），通道不报价时为 null。</summary>
        public int? ContextTokens { get; set; }

        /// <summary>用户请求停止后，在下一轮开始前终止。</summary>
        public bool StoppedByUser { get; set; }
    }
}
