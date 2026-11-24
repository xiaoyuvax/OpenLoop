using System.Collections.Concurrent;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 每个模型「上游此刻真收得下」的 prompt 规模。模型自报的窗口（免费模型可报 100 万）
    /// 只是名义值：超了上游不报错，而是自己压缩上下文，然后很久没有任何输出。
    /// 所以只能从挂住的那一刻往回学：记住那次 prompt 的一半，之后同模型压缩得更早；
    /// 只降不升，进程重启后重新学。
    /// </summary>
    public static class OpenCodeStallBudget
    {
        private static readonly ConcurrentDictionary<string, int> Learned = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, int> Idle = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>撞过一次之后，同模型后续调用的空闲上限（秒）：不必再等满默认的十分钟。</summary>
        public const int StallIdleSeconds = 180;

        /// <summary>
        /// 记下「这么大的 prompt 挂住了」。小于 <see cref="AgentContext.DefaultContextLimit"/> 的 prompt
        /// 不算规模问题（任何模型都收得下），挂了多半是排队/额度，学了只会白白压掉上下文。
        /// </summary>
        public static void Learn(string model, int tokens)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            string key = model.Trim();
            // 空闲收紧与规模无关：额度排队、上游卡住都该更早收场。
            Idle[key] = StallIdleSeconds;
            if (tokens <= AgentContext.DefaultContextLimit) return;
            int limit = tokens / 2;
            Learned.AddOrUpdate(key, limit, (_, old) => Math.Min(old, limit));
        }

        /// <summary>学到的上限；没学过返回 0（调用方按模型自报的窗口走）。</summary>
        public static int Limit(string model)
            => !string.IsNullOrWhiteSpace(model) && Learned.TryGetValue(model.Trim(), out int limit) ? limit : 0;

        /// <summary>本模型后续调用判挂住要等多久；没挂过返回 0（调用方用默认值）。</summary>
        public static int IdleSeconds(string model)
            => !string.IsNullOrWhiteSpace(model) && Idle.TryGetValue(model.Trim(), out int seconds) ? seconds : 0;
    }
}
