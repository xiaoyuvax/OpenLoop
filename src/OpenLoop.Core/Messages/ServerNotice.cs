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
    }
}
