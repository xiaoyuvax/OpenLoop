namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 主循环的可调参数。每个开关对应账本里的一个具名特征（M2 起从账本加载）。
    /// 这里刻意不做接口：护栏之间不可互换，只需独立开关。
    /// </summary>
    public sealed class AgentPolicy
    {
        /// <summary>一轮最多的工具调用数（超了整批拒绝）。提示词与拒收报错共用，避免各写一份字面量。</summary>
        public const int MaxParallelCalls = 4;

        /// <summary>单次任务步数上限；0 或负数表示不设上限。</summary>
        public int MaxSteps { get; set; } = 0;
        public double CompressAt { get; set; } = 0.8;

        public bool RepeatCallGuard { get; set; } = true;

        /// <summary>连续完全相同调用的停止阈值：2 = 相同请求允许重试 1 次；改参数后计数自动刷新。</summary>
        public int MaxRepeatCalls { get; set; } = 2;

        public bool ExplorationGuard { get; set; } = true;
        public int MaxExplorationSteps { get; set; } = 30;

        public bool ClaimGuard { get; set; } = true;
        public bool PromiseGuard { get; set; } = true;

        /// <summary>「将要执行但未调用工具」的最大纠正次数（超过即按最终答案收尾）。</summary>
        public int MaxPromiseNudges { get; set; } = 2;
        public bool ArgRepair { get; set; } = true;
        public bool MalformedRetry { get; set; } = true;
        public int MaxMalformedRetries { get; set; } = 3;

        /// <summary>单次轮询声明最多放行多少次（模型自报的 max 与此取小）。</summary>
        public int MaxPollRepeats { get; set; } = 10;

        /// <summary>全任务轮询放行总次数上限（所有签名合计）。</summary>
        public int MaxPollTotal { get; set; } = 20;

        /// <summary>步数上限到顶时每次自动追加的步数；&lt;=0 表示不追加。</summary>
        public int BudgetExtendSteps { get; set; } = 10;

        /// <summary>自动追加步数的最多次数；超出后按预算耗尽收尾。</summary>
        public int MaxBudgetExtensions { get; set; } = 3;

        /// <summary>回填给模型的单条工具结果上限（字符）。</summary>
        public int ToolResultLimit { get; set; } = 6000;
    }
}
