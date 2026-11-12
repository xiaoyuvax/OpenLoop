using System.Linq;
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
        public DateTime StartedAt { get; init; } = DateTime.Now;

        /// <summary>最后一次活动时间：会话列表按它倒序；开始时间只用于显示。</summary>
        public DateTime LastActiveAt { get; set; } = DateTime.Now;
        public int Steps { get; set; }
        public string Workspace { get; set; } = "";
        public AntdUI.TabPage Page { get; set; }
        public SessionView View { get; set; }

        public string Started => StartedAt.ToString("HH:mm:ss");

        public string StepsText => Steps == 0 ? "—" : Steps.ToString();

        /// <summary>会话列表里的复选列（批量删除用）。</summary>
        public bool Selected { get; set; }

        /// <summary>用户重命名或模型总结出来的内容简述；空值只显示工作目录名。</summary>
        public string CustomTitle { get; set; }

        /// <summary>[Loop] 页的行：标题按 Loop 规格带前缀。</summary>
        public bool IsLoop { get; init; }

        public string Title
        {
            get
            {
                string title = ComposeTitle(Workspace, CustomTitle, IsLoop);
                return title.Length == 0 ? "新会话" : title;
            }
        }

        /// <summary>标题规格：Worker = 工作区文件夹名 · 内容简述；Loop = Loop工作区 · 工作区文件夹名 · 内容简述。</summary>
        internal static string ComposeTitle(string workspace, string summary, bool loop = false)
        {
            string folder = "";
            try
            {
                string trimmed = (workspace ?? "").TrimEnd('\\', '/');
                if (trimmed.Length > 0) folder = Path.GetFileName(trimmed);
            }
            catch
            {
                // 非法路径按无标题处理
            }

            string body = string.Join(" · ", new[] { folder, summary?.Trim() }
                .Where(part => !string.IsNullOrEmpty(part)));
            return loop ? "Loop工作区" + (body.Length == 0 ? "" : " · " + body) : body;
        }
    }
}
