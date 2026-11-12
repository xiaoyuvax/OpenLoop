namespace OpenLoop.Core.Messages
{
    /// <summary>
    /// 服务端/网络状态文案。轮次正文里写它是给用户看的，不是模型说过的话，
    /// 拼下一轮上下文时必须剔掉，否则「free-limit」「连接超时」会被当成 AI 回复喂回去。
    /// </summary>
    public static class ServerNotice
    {
        public const string FailedPrefix = "执行失败：";
        public const string Stopped = "（已停止）";

        public static bool IsNotice(string text)
            => !string.IsNullOrEmpty(text)
               && (text.StartsWith(FailedPrefix, StringComparison.Ordinal)
                   || text.StartsWith(Stopped, StringComparison.Ordinal));

        /// <summary>额度/限流类失败的常见信号：各家没有统一错误码，只能靠错误文本启发式。</summary>
        private static readonly string[] QuotaSignals =
        [
            "provider.free-limit", "429", "402", "rate limit", "too many requests",
            "quota", "usage limit", "insufficient", "credits", "payment required",
            "额度", "余额"
        ];

        /// <summary>这一轮失败是否属于额度/限流（429、402、余额不足、免费额度等）。</summary>
        public static bool IsQuotaFailure(string text)
            => !string.IsNullOrEmpty(text)
               && text.StartsWith(FailedPrefix, StringComparison.Ordinal)
               && QuotaSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}
