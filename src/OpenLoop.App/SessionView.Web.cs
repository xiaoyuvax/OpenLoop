using OpenLoop.Service.OpenUI;

namespace OpenLoop.App
{
    /// <summary>
    /// 会话页的「网页」页生命周期：收起（只摘标签、不销毁）与闲置回收（销毁实例）。
    /// 呈现方式（停靠/独立窗口）由 <see cref="BrowserView.Layout"/> 按内容判定，这里只管页的进退。
    /// </summary>
    public partial class SessionView
    {
        /// <summary>「网页」标签被收起了，但 WebView2 实例还留着（再打开时重新插回标签）。</summary>
        private bool _browserTabHidden;

        /// <summary>
        /// 收起「网页」页：摘掉标签、隐藏页面；实例留着，闲置到点由 <c>IdleExpired</c> 销毁。
        /// 与右键「关闭网页」的区别就在这里——那个是立刻销毁。
        /// </summary>
        private void CollapseBrowserPage()
        {
            var browser = _browser;
            if (browser == null || _browserTabHidden) return;

            int selected = _sideTabs.SelectIndex;
            if (_sideTabs.Items.Count > BrowserPage) _sideTabs.Items.RemoveAt(BrowserPage);
            if (_filePage > BrowserPage) _filePage--;
            _browserTabHidden = true;
            browser.Visible = false;
            browser.MarkCollapsed();
            ShowSidePage(selected == BrowserPage ? 0 : Math.Max(0, selected > BrowserPage ? selected - 1 : selected));
        }

        /// <summary>
        /// 本轮结束时调用：页面已经把数据交回来了（<c>ui.submit</c>），任务就算做完，
        /// 不必再占着侧栏——隐藏页面、关独立窗口、收起标签，一套做完。
        /// </summary>
        private void CollapseBrowserIfDone()
        {
            var browser = _browser;
            if (browser == null || IsDisposed || !browser.Submitted) return;
            browser.HideAndCollapse();
        }
    }
}
