using System.ComponentModel;
using System.Text.Json;
using Puppet.Core.AppAgent;

namespace OpenLoop.App
{
    /// <summary>
    /// TCF（目标面板）的壳内 Puppet 入口：6 个动作按 sessionId 转发到对应会话的 <see cref="TargetPane"/>。
    ///
    /// 为什么动作声明在 MainForm 而不是 TargetPane：<c>[PuppetAction]</c> 只扫 DeclaredOnly，
    /// 声明在面板上不会出现在 MainForm 的 manifest 里；而 Worker 会话（A 进程）只能打到壳的 MainForm。
    /// 缺省 sessionId = 当前标签页，只给手工调试用——后台会话必须显式给 id，否则会改到用户正看的面板。
    /// </summary>
    public partial class MainForm
    {
        /// <summary>读取目标正文的一页；toLine=0 表示到末尾，nextLine=0 表示已读完。</summary>
        [PuppetAction(Desc = "读目标面板正文：从 fromLine 起读一页（≤4000 字符），按返回的 nextLine 翻到 0；行号 1-based，toLine=0=到末尾")]
        public OperationResult TargetRead(string sessionId, int fromLine = 1, int toLine = 0)
            => OnTarget(sessionId, pane => pane.Read(fromLine, toLine));

        /// <summary>替换 [fromLine, toLine]；fromLine=toLine+1 即插入，text="" 即删除。</summary>
        [PuppetAction(Desc = "替换目标面板的 [fromLine,toLine]；fromLine=toLine+1=插入，text=\"\"=删除；只改内存，写盘等用户确认")]
        public OperationResult TargetReplace(string sessionId, int fromLine, int toLine, string text)
            => OnTarget(sessionId, pane => pane.Replace(fromLine, toLine, text));

        /// <summary>尾部追加（按原样，不自动补换行）。</summary>
        [PuppetAction(Desc = "在目标面板末尾按原样追加 text（不自动补换行）；只改内存")]
        public OperationResult TargetAppend(string sessionId, string text)
            => OnTarget(sessionId, pane => pane.Append(text));

        /// <summary>丢弃缓冲、重读磁盘。</summary>
        [PuppetAction(Desc = "丢弃目标面板缓冲、重读磁盘（文件被外部改过时用它重新同步）")]
        public OperationResult TargetReload(string sessionId)
            => OnTarget(sessionId, pane => pane.ReloadFromDisk());

        /// <summary>只把保存状态推到待确认，不写盘。</summary>
        [PuppetAction(Desc = "把目标面板推到「待保存」状态（按钮变色），不写盘：写盘只由用户点保存触发")]
        public OperationResult TargetRequestSave(string sessionId)
            => OnTarget(sessionId, pane => pane.RequestSave());

        /// <summary>丢弃未保存的编辑（需用户已确认）。</summary>
        [PuppetAction(Risk = "High", Desc = "丢弃目标面板未保存的编辑、回到磁盘内容（会丢改动，先问用户）")]
        public OperationResult TargetDiscard(string sessionId)
            => OnTarget(sessionId, pane => pane.DiscardEdits());

        /// <summary>当前标签页目标面板的状态（后台会话以各动作返回的 data.state 为准）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string TargetState
            => JsonSerializer.Serialize(TargetPaneFor(null)?.StateObject() ?? EmptyTargetState());

        private OperationResult OnTarget(string sessionId, Func<TargetPane, OperationResult> action)
        {
            var pane = TargetPaneFor(sessionId);
            if (pane != null) return action(pane);
            return OperationResult.Failure(
                string.IsNullOrWhiteSpace(sessionId) ? "没有打开的会话" : $"找不到会话 {sessionId} 的目标面板",
                new Dictionary<string, object> { ["state"] = EmptyTargetState() });
        }

        /// <summary>按 id 找那个会话的目标面板（先 Worker 再 Loop）；空 id = 当前标签页。</summary>
        private TargetPane TargetPaneFor(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return Current?.View?.TargetPane;

            var session = _sessions.FirstOrDefault(s =>
                string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
            if (session != null) return EnsureView(session)?.TargetPane;

            var loop = _loops.Values.FirstOrDefault(e =>
                e.View != null && string.Equals(e.View.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            return loop.View?.TargetPane;
        }

        private static Dictionary<string, object> EmptyTargetState() => new()
        {
            ["kind"] = "",
            ["title"] = "",
            ["path"] = "",
            ["session"] = "",
            ["candidate"] = false,
            ["actual"] = false,
            ["visible"] = false,
            ["dirty"] = false,
            ["pendingSave"] = false,
            ["lines"] = 0,
            ["chars"] = 0,
            ["lastError"] = "",
            ["status"] = "clean",
            ["diskChanged"] = false
        };
    }
}
