namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 主循环的可调参数。每个开关对应账本里的一个具名特征（M2 起从账本加载）。
    /// 这里刻意不做接口：护栏之间不可互换，只需独立开关。
    /// </summary>
    public sealed class AgentPolicy
    {
        public int MaxSteps { get; set; } = 100;
        public double CompressAt { get; set; } = 0.8;

        public bool RepeatCallGuard { get; set; } = true;
        public int MaxRepeatCalls { get; set; } = 3;

        public bool ExplorationGuard { get; set; } = true;
        public int MaxExplorationSteps { get; set; } = 30;

        public bool ClaimGuard { get; set; } = true;
        public bool PromiseGuard { get; set; } = true;

        /// <summary>「将要执行但未调用工具」的最大纠正次数（超过即按最终答案收尾）。</summary>
        public int MaxPromiseNudges { get; set; } = 2;
        public bool ArgRepair { get; set; } = true;
        public bool MalformedRetry { get; set; } = true;
        public int MaxMalformedRetries { get; set; } = 2;

        /// <summary>回填给模型的单条工具结果上限（字符）。</summary>
        public int ToolResultLimit { get; set; } = 6000;
    }
}
