using System.ComponentModel;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 会话的 LoopApp 接线：与 <c>SessionView.OpenUi</c> 同一套语义，
    /// 落点换成 Loop 自己的队列（<c>Send(text, immediate:false)</c>）。
    /// </summary>
    public partial class LoopView
    {
        /// <summary>把新建的浏览器接成这个 Loop 会话的 LoopApp 宿主。</summary>
        private void AttachLoopApp(BrowserView browser)
        {
            if (browser == null) return;
            browser.Configure(_sessionId, OpenLoop.Service.OpenUI.LoopAppPaths.Root(_workspace));
            browser.StateProvider = () => new LoopAppSessionState(Busy, _model, Title, _queue.Count);
            browser.StopHandler = () => { Stop(); return true; };
            // 任务做完收起（LoopApp 自己按内容决定停靠/独立窗口）；收起的实例闲置到点才销毁
            browser.CollapseRequested = CollapseBrowserPage;
            browser.IdleExpired = CloseBrowserPage;
            // 页面点按钮 = 用户点发送：默认等本轮
            browser.SubmitHandler = text => Send(text, immediate: false);
        }

        /// <summary>页面能看到的队列长度（与队列控件同一口径）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int QueueCount => _queue.Count;
    }
}
