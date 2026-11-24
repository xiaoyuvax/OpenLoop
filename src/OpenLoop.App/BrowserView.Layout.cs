using System.ComponentModel;
using Puppet.Core;
using Puppet.Core.AppAgent;
using Puppet.Core.Extentions;
using OpenLoop.Service.OpenUI;

namespace OpenLoop.App
{
    /// <summary>
    /// 「网页」页的呈现方式与收起：
    /// LoopApp 打开后按内容决定留在竖版右侧栏还是搬进独立窗口；任务做完可以收起（隐藏页面 → 关窗 → 收标签），
    /// 实例先留着，闲置超过账本 limit.browser_idle 分钟才销毁。
    /// 判定与阈值在 <see cref="LoopAppLayout"/>（纯函数，可单测），这里只负责量、搬、收。
    /// </summary>
    public partial class BrowserView
    {
        /// <summary>收起后的闲置检查节拍：一分钟看一次就够，别为回收策略拉高 CPU。</summary>
        private const int IdleCheckMs = 60_000;

        /// <summary>页面还在渲染、量不出内容时的补量间隔。</summary>
        private const int LayoutRetryMs = 500;

        private System.Windows.Forms.Timer _idleTimer;
        private System.Windows.Forms.Timer _layoutRetry;
        private DateTime _lastUsedUtc = DateTime.UtcNow;

        /// <summary>上次是「按内容判定」搬进独立窗口的：用户手动搬的不跟着内容回来。</summary>
        private bool _autoUndocked;

        /// <summary>用户自己关过独立窗口（或主动停靠）：这一页期间不再按内容自动浮出去。</summary>
        private bool _userDocked;

        /// <summary>最近一次被用到的时刻（导航、执行脚本、截图都记）。</summary>
        [PuppetIgnore] public DateTime LastUsedUtc => _lastUsedUtc;

        /// <summary>收起时问宿主能不能收标签（宿主实现：隐藏「网页」页）。</summary>
        [PuppetIgnore] public Action CollapseRequested { get; set; }

        /// <summary>闲置到点：宿主应该把这个实例整个销毁（与用户右键「关闭网页」同一条路）。</summary>
        [PuppetIgnore] public Action IdleExpired { get; set; }

        /// <summary>记一次使用：收起的实例按它判断闲置了多久。</summary>
        internal void Touch() => _lastUsedUtc = DateTime.UtcNow;

        /// <summary>页面被收起来了：开始盯闲置时长（只有看不见的实例需要回收）。</summary>
        internal void MarkCollapsed()
        {
            Touch();
            _idleTimer ??= CreateIdleTimer();
            _idleTimer.Start();
        }

        /// <summary>页面又露出来了：停止回收计时。</summary>
        internal void MarkShown()
        {
            _idleTimer?.Stop();
            Touch();
        }

        private System.Windows.Forms.Timer CreateIdleTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = IdleCheckMs };
            timer.Tick += (s, e) =>
            {
                if (IsDisposed) { timer.Stop(); return; }
                // 露着的（含独立窗口）一直算在用：用户可能正盯着它
                if (Visible || IsUndocked) { Touch(); return; }

                double idleMinutes = (DateTime.UtcNow - _lastUsedUtc).TotalMinutes;
                int limit = LoopAppLayout.IdleMinutes(LoopAppLayout.WorkspaceOf(_loopAppsRoot));
                if (idleMinutes < limit) return;

                timer.Stop();
                AppLog.Info($"网页页闲置 {idleMinutes:0} 分钟（上限 {limit} 分钟），销毁 WebView2 实例");
                IdleExpired?.Invoke();
            };
            return timer;
        }

        /// <summary>导航完成：LoopApp 页面按内容决定停靠还是独立窗口。</summary>
        private void OnNavigationCompleted()
        {
            if (IsDisposed) return;
            // 事件在 WebView2 的回调里：量 DOM 要跑脚本，等事件返回再量
            RunLater(() => ApplyLayout(retry: true));
        }

        /// <summary>排到界面消息队列末尾（句柄还没建就直接跑，别把判定丢掉）。</summary>
        private void RunLater(Action action)
        {
            if (!IsHandleCreated) { action(); return; }
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { action(); }
        }

        /// <summary>
        /// 按 app.json 声明与 DOM 实测决定停靠还是独立窗口。
        /// 声明优先（agent 显式写 float/dock 就照办），没声明就看内容多寡；用户自己关过窗就不再自动浮出。
        /// </summary>
        /// <param name="retry">量不到内容（页面还在渲染）时补量一次：<c>ui.ready</c> 可能晚于导航完成。</param>
        private void ApplyLayout(bool retry = false)
        {
            if (IsDisposed || _userDocked) return;
            string slug = LoopAppSlug();
            if (slug.Length == 0) return;

            var hint = LoopAppLayout.ReadHint(Path.Combine(_loopAppsRoot, slug, "app.json"));
            var content = LoopAppLayout.ParseContent(
                Eval(LoopAppLayout.MeasureScript, out string value, out _) ? value : null);

            if (hint == null && content == null)
            {
                // 页面还没画出来：半秒后只再量一次，别让判定变成轮询
                if (retry) ScheduleLayoutRetry();
                return;
            }

            if (LoopAppLayout.PreferFloat(hint, content))
            {
                if (IsUndocked) return;
                ToFloat(hint?.Width ?? 0, hint?.Height ?? 0);
                _autoUndocked = true;
                return;
            }

            // 上次按内容搬出去、这次内容变窄了就搬回来；用户自己搬的不动
            if (!_autoUndocked) return;
            BringBack();
            _autoUndocked = false;
        }

        private void ScheduleLayoutRetry()
        {
            if (_layoutRetry != null) return;
            var timer = new System.Windows.Forms.Timer { Interval = LayoutRetryMs };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                _layoutRetry = null;
                ApplyLayout();
            };
            _layoutRetry = timer;
            timer.Start();
        }

        /// <summary>搬进独立窗口；声明了尺寸就按它开窗（太小照默认大小）。</summary>
        private void ToFloat(int width, int height)
        {
            OnUi(() =>
            {
                if (!IsUndocked) Undock();
                else _undocked.Activate();
                if (IsUndocked && width >= 480 && height >= 360)
                    _undocked.Size = new Size(Dip(width), Dip(height));
            });
        }

        /// <summary>当前 LoopApp 的应用目录名（URL 不是 LoopApp 时返回空串）。</summary>
        private string LoopAppSlug() => IsLoopAppPage ? SlugOf(Url) : "";

        /// <summary>
        /// 收起网页页：先隐藏页面内容，再把独立窗口搬回来关掉，最后收起标签。
        /// 实例留着不销毁——销毁交给闲置回收（账本 limit.browser_idle）。
        /// </summary>
        internal void HideAndCollapse()
        {
            OnUi(() =>
            {
                Navigate("about:blank", out _);
                if (IsUndocked) _undocked.Close();
                _autoUndocked = false;
                Touch();
                CollapseRequested?.Invoke();
            });
        }

        [PuppetAction(Desc = "收起本会话的 LoopApp：隐藏页面 → 关掉独立窗口 → 收起「网页」标签；实例留着，闲置到点再销毁")]
        public OperationResult LoopAppClose()
        {
            HideAndCollapse();
            return OperationResult.Success(Localization.T("已收起网页页"));
        }

        /// <summary>视图销毁时停掉回收计时（Designer 的 Dispose 调这里）。</summary>
        private void StopIdleTimer()
        {
            _idleTimer?.Stop();
            _idleTimer?.Dispose();
            _idleTimer = null;
            _layoutRetry?.Stop();
            _layoutRetry?.Dispose();
            _layoutRetry = null;
        }
    }
}
