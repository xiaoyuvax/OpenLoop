using ChatFlow;

namespace OpenLoop.App
{
    internal enum SessionState
    {
        Idle,
        Running,
        Done,
        Interrupted
    }

    /// <summary>
    /// 一个并行会话：拥有自己的对话、工作目录和 Loop 状态。
    /// 关闭标签页只是移除页面，会话与视图继续存活（任务不会被停止）。
    /// </summary>
    internal sealed class Session
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
        public ChatTurnBook Book { get; } = new();
        public SessionState State { get; set; } = SessionState.Idle;
        public DateTime StartedAt { get; } = DateTime.Now;
        public int Steps { get; set; }
        public string Workspace { get; set; } = "";
        public AntdUI.TabPage Page { get; set; }
        public SessionView View { get; set; }

        public string Started => StartedAt.ToString("HH:mm:ss");

        public string StepsText => Steps == 0 ? "—" : Steps.ToString();

        /// <summary>用户重命名后覆盖默认标题；空值回退到工作目录名。</summary>
        public string CustomTitle { get; set; }

        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomTitle)) return CustomTitle.Trim();
                string name = "";
                try
                {
                    string trimmed = Workspace.TrimEnd('\\', '/');
                    if (trimmed.Length > 0) name = Path.GetFileName(trimmed);
                }
                catch
                {
                    // 非法路径按无标题处理
                }
                return string.IsNullOrEmpty(name) ? "新会话" : name;
            }
        }
    }
}
