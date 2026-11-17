namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 当前运行会话在宿主留言板上的身份。工具执行与提示词构建都在同一个异步上下文里，
    /// 用 AsyncLocal 传递，避免把会话 id 一路塞进每个工具构造函数。
    /// </summary>
    public static class AgentIdentity
    {
        private sealed class Frame
        {
            public string Id;
            public string BoardTarget;
        }

        private static readonly AsyncLocal<Frame> Current = new();

        /// <summary>本会话的留言板 id（A-会话id / I-会话id）；未登记时为 null。</summary>
        public static string Id => Current.Value?.Id;

        /// <summary>留言板所在的 Puppet 目标：A 进程内为 self，壳里的 Loop 会话为 a。</summary>
        public static string BoardTarget => Current.Value?.BoardTarget ?? "self";

        public static string IdFor(string role, string sessionId) => role + "-" + sessionId;

        public static IDisposable Begin(string id, string boardTarget)
        {
            var previous = Current.Value;
            Current.Value = new Frame { Id = id, BoardTarget = boardTarget };
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Frame _previous;
            public Scope(Frame previous) => _previous = previous;
            public void Dispose() => Current.Value = _previous;
        }
    }
}
