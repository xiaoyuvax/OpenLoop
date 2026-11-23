using System.ComponentModel;
using OpenLoop.Service.OpenUI;
using OpenLoop.Service.Pending;

namespace OpenLoop.App
{
    /// <summary>
    /// 「网页」页 ↔ LoopApp：建浏览器时把它接成**本会话专属**的界面通道。
    ///
    /// 接线点只有一处（<see cref="SessionView.ShowBrowser"/> 建视图时），因为 BrowserView 一旦
    /// 绑定 owner 就不能改：换会话要换视图，否则页面能操作到别的会话。
    /// </summary>
    public partial class SessionView
    {
        /// <summary>把新建的浏览器接成这个会话的 LoopApp 宿主（owner 由 Session 决定，页面无从指定）。</summary>
        private void AttachLoopApp(BrowserView browser)
        {
            if (browser == null || Session == null) return;
            browser.Configure(Session.Id, LoopAppPaths.Root(Session.Workspace));
            browser.StateProvider = () => new LoopAppSessionState(
                Busy, Model, Session.Title, _queue.Count);
            browser.StopHandler = Stop;
            // 任务做完收起（LoopApp 自己按内容决定停靠/独立窗口）；收起的实例闲置到点才销毁
            browser.CollapseRequested = CollapseBrowserPage;
            browser.IdleExpired = CloseBrowserPage;
            // 页面点按钮 = 用户点发送：默认等本轮，不打断 agent 正在做的事
            browser.SubmitHandler = text => SubmitFromPage(text, immediate: false);
        }

        /// <summary>
        /// 页面提交的落点：走用户点发送的同一条队列。
        /// 空闲就直接开跑；运行中就按「等本轮 / 立即」排队；失败轮不续发等规则全部沿用，不新开旁路。
        /// 与 <see cref="EnqueueText"/> 的区别只有一条——这里默认会真的发出去。
        /// </summary>
        /// <returns>一句给页面看的结果说明。</returns>
        internal string SubmitFromPage(string text, bool immediate = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return "内容为空，没有提交";

            var item = _queue.Add(text, "task", immediate ? PendingMode.NextStep : PendingMode.AfterTurn);
            if (item == null) return "队列拒绝接收";
            _queue.Paused = false;
            _pending.Sync();

            if (item.Immediate) OnImmediateRequested(item);
            else if (!_running) DrainQueue();

            return _running ? "已入队，本轮结束后发送" : "已发送";
        }

        /// <summary>页面能看到的队列长度（与队列控件同一口径）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int QueueLength => _queue.Count;
    }
}
