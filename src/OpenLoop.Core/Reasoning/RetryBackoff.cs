namespace OpenLoop.Core.Reasoning
{
    /// <summary>「一直重试到好」的退避节奏：前三次 5 秒，之后 10s、20s…封顶 5 分钟。</summary>
    public static class RetryBackoff
    {
        public static readonly TimeSpan First = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

        /// <summary>第 attempt（0 起）次失败后等多久。</summary>
        public static TimeSpan Next(int attempt)
        {
            if (attempt < 3) return First;
            double ms = First.TotalMilliseconds * Math.Pow(2, attempt - 2);
            return ms >= Max.TotalMilliseconds ? Max : TimeSpan.FromMilliseconds(ms);
        }
    }
}
