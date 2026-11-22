using System.ComponentModel;
using Puppet.Core;
using Puppet.Core.AppAgent;
using OpenLoop.Service.OpenUI;

namespace OpenLoop.App
{
    /// <summary>
    /// agent 侧 LoopApp 的壳内入口：把「按会话 id 找浏览器」做成 Puppet 实例 <c>MainForm</c> 上的动作。
    ///
    /// 为什么不直接调 <c>LoopApp-&lt;会话id&gt;</c>：那个实例活在某个会话的「网页」页里，
    /// 而 agent 的第一次 open 恰恰发生在「网页」页还不存在的时候——先有实例才能建页，鸡生蛋。
    /// 这里只做一件事：按 owner 找到（或建出）那个会话的浏览器，再转给它自己的实例。
    /// 传进来的 sessionId 来自工具构造时注入的本会话 id，不是模型给的参数；
    /// 而且它只会找到那一个会话的浏览器——跨会话在形状上仍然不可表达。
    /// </summary>
    public partial class MainForm
    {
        /// <summary>按会话 id 打开它自己的 LoopApp（应用不存在时不会凭空建）。</summary>
        [PuppetAction(Desc = "打开某个会话自己的 LoopApp；sessionId 为空=当前会话")]
        public OperationResult LoopAppOpenFor(string sessionId, string name)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppOpen(name));

        /// <summary>在某个会话自己的 LoopApp 里执行 JS。</summary>
        [PuppetAction(Desc = "在某个会话自己的 LoopApp 里执行 JS；sessionId 为空=当前会话")]
        public OperationResult LoopAppEvalFor(string sessionId, string js)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppEval(js));

        /// <summary>重新加载某个会话自己的 LoopApp（改完文件用这个）。</summary>
        [PuppetAction(Desc = "重新加载某个会话自己的 LoopApp；sessionId 为空=当前会话")]
        public OperationResult LoopAppReloadFor(string sessionId)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppReload());

        /// <summary>截取某个会话自己的 LoopApp。</summary>
        [PuppetAction(Desc = "截取某个会话自己的 LoopApp；sessionId 为空=当前会话")]
        public OperationResult LoopAppShotFor(string sessionId)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppShot());

        /// <summary>把某个会话自己的 LoopApp 搬进独立窗口。</summary>
        [PuppetAction(Desc = "把某个会话自己的 LoopApp 搬进独立窗口；sessionId 为空=当前会话")]
        public OperationResult LoopAppUndockFor(string sessionId)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppUndock());

        /// <summary>把某个会话自己的 LoopApp 搬回右侧栏。</summary>
        [PuppetAction(Desc = "把某个会话自己的 LoopApp 搬回右侧栏；sessionId 为空=当前会话")]
        public OperationResult LoopAppDockFor(string sessionId)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppDock());

        /// <summary>锁定/解锁某个会话自己的 LoopApp。</summary>
        [PuppetAction(Desc = "锁定/解锁某个会话自己的 LoopApp；sessionId 为空=当前会话")]
        public OperationResult LoopAppInteractFor(string sessionId, bool enabled)
            => OnLoopAppBrowser(sessionId, browser => browser.LoopAppInteract(enabled));

        /// <summary>某个会话的 LoopApp 状态：锁定/浮动/地址。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string LoopAppStateFor(string sessionId)
        {
            var session = ResolveSessionById(sessionId);
            return session?.View?.Browser?.LoopAppState ?? "没有网页页";
        }

        /// <summary>
        /// 找到会话的浏览器并把动作转给它。会话必须已经存在（有 <see cref="Session"/> 记录）：
        /// 找不到就拒绝，不会顺手新建一个会话。
        /// </summary>
        private OperationResult OnLoopAppBrowser(string sessionId, Func<BrowserView, OperationResult> action)
        {
            var session = ResolveSessionById(sessionId);
            if (session == null)
                return OperationResult.Failure(string.IsNullOrWhiteSpace(sessionId)
                    ? "没有打开的会话"
                    : $"找不到会话 {sessionId}");

            var browser = EnsureBrowserFor(session);
            if (browser == null) return OperationResult.Failure("打不开这个会话的网页页");
            return action(browser);
        }

        /// <summary>按 id 找会话；空 id 取当前会话。</summary>
        private Session ResolveSessionById(string sessionId)
            => string.IsNullOrWhiteSpace(sessionId)
                ? Current
                : _sessions.FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 取该会话的浏览器，没有就建（<see cref="SessionView.ShowBrowser"/> 会顺手接线）。
        /// 不切标签页：后台会话调工具不该把用户正看的东西换掉。
        /// </summary>
        private BrowserView EnsureBrowserFor(Session session)
        {
            var view = EnsureView(session);
            if (view == null) return null;
            if (view.Browser == null) view.ShowBrowser();
            return view.Browser;
        }
    }
}
