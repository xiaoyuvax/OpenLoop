using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧栏里的内嵌浏览器（WebView2）。
    /// 默认锁定：用户点不动网页（Agent 照常驱动）；「解锁」后用户可正常操作；「独立窗口」把网页移进顶层窗口。
    /// 所有方法都可以从任意线程调用，内部转回 UI 线程；WebView2 的异步初始化/脚本调用用消息泵等待。
    /// LoopApp 相关的能力（虚拟域映射、会话级 Puppet 实例、页面消息桥）在 BrowserView.OpenUi.cs。
    /// </summary>
    public partial class BrowserView : UserControl
    {
        private const int WaitTimeoutMs = 20000;

        private bool _ready;
        private bool _failed;
        private bool _interact;
        private Form _undocked;

        private readonly record struct CallResult(bool Ok, string Value, string Error);

        public BrowserView()
        {
            InitializeComponent();
            // 侧栏宽度会随分栏拖动变化：提示文字按当前宽度重排并居中，避免被裁掉
            _host.SizeChanged += (_, _) => CenterOverlay();
            _overlay.TextChanged += (_, _) => CenterOverlay();
            _overlay.VisibleChanged += (_, _) => { if (_overlay.Visible) CenterOverlay(); };
            // 侧栏窄时收起次要按钮，别把地址栏挤成一条缝
            _bar.SizeChanged += (_, _) => FitBar();
            CenterOverlay();
            FitBar();
        }

        /// <summary>工具栏放不下就按「系统浏览器 → 刷新 → 前进 → 独立窗口」的顺序收起，给地址栏留位置。</summary>
        private void FitBar()
        {
            int width = _bar.ClientSize.Width;
            if (width <= 0) return;
            int need = Dip(26 * 7 + 120);          // 七个按钮 + 最小地址栏
            int hidden = width >= need ? 0
                : width >= need - Dip(26) ? 1
                : width >= need - Dip(52) ? 2
                : width >= need - Dip(78) ? 3 : 4;
            SetBarButton(_btnExternal, 7, hidden < 1);
            SetBarButton(_btnReload, 2, hidden < 2);
            SetBarButton(_btnForward, 1, hidden < 3);
            SetBarButton(_btnUndock, 6, hidden < 4);
        }

        private void SetBarButton(Control button, int column, bool visible)
        {
            if (button.Visible == visible) return;
            button.Visible = visible;
            _bar.ColumnStyles[column].Width = visible ? 26F : 0F;
            // 改 ColumnStyles 不会自动重排，得显式触发一次
            _bar.PerformLayout();
        }

        /// <summary>锁定/错误提示按面板宽度换行并居中（先量高度再定尺寸，避免文字被裁）。</summary>
        private void CenterOverlay()
        {
            int pad = Dip(12);
            int max = Math.Max(Dip(80), _host.ClientSize.Width - pad * 2);
            Size need = TextRenderer.MeasureText(_overlay.Text, _overlay.Font,
                new Size(max, int.MaxValue), TextFormatFlags.WordBreak);
            _overlay.Size = new Size(Math.Min(max, need.Width + Dip(2)), need.Height + Dip(2));
            _overlay.Location = new Point(
                Math.Max(0, (_host.ClientSize.Width - _overlay.Width) / 2),
                Math.Max(0, (_host.ClientSize.Height - _overlay.Height) / 2));
        }

        /// <summary>解锁状态变化（界面按钮靠它刷新）。</summary>
        public event Action<bool> InteractChanged;

        public bool InteractEnabled => _interact;

        public string Url => _ready ? _web.CoreWebView2?.Source ?? "" : "";

        public bool IsUndocked => _undocked != null && !_undocked.IsDisposed;

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        /// <summary>提前把 WebView2 建起来（切到这个侧栏页时调用，免得第一次导航才初始化）。</summary>
        public void EnsureLoaded() => OnUiCall(() =>
        {
            EnsureCore(out _);
            return new CallResult(true, null, null);
        });

        public bool Navigate(string url, out string error)
        {
            var result = OnUiCall(() => NavigateCore(url));
            error = result.Error;
            return result.Ok;
        }

        /// <summary>在页面里执行 JS，返回 WebView2 的 JSON 结果串（字符串结果带引号）。</summary>
        public bool Eval(string script, out string value, out string error)
        {
            var result = OnUiCall(() => EvalCore(script));
            value = result.Value;
            error = result.Error;
            return result.Ok;
        }

        /// <summary>截当前页面（PNG 的 base64）。</summary>
        public bool Screenshot(out string base64Png, out string error)
        {
            var result = OnUiCall(ScreenshotCore);
            base64Png = result.Value;
            error = result.Error;
            return result.Ok;
        }

        public void SetInteract(bool enabled)
        {
            OnUi(() =>
            {
                _interact = enabled;
                _btnInteract.IconSvg = UiIcons.Of(enabled ? UiIcons.Lock : UiIcons.Unlock, UiStyle.Text);
                _tips.SetToolTip(_btnInteract, enabled ? "锁定：网页由 Agent 控制" : "解锁：允许你自己点按网页");
                // 锁定时禁用控件本身：Windows 不再把鼠标/键盘送给网页
                _web.Enabled = enabled;
                _overlay.Visible = !_ready;
                InteractChanged?.Invoke(enabled);
            });
        }

        public void Undock()
        {
            OnUi(() =>
            {
                if (IsUndocked) { _undocked.Activate(); return; }
                SetInteract(true);

                var form = new Form
                {
                    Text = "OpenLoop 浏览器",
                    Width = Dip(1100),
                    Height = Dip(800),
                    StartPosition = FormStartPosition.CenterScreen
                };
                _host.Controls.Remove(_web);
                _web.Dock = DockStyle.Fill;
                form.Controls.Add(_web);
                form.FormClosed += (s, e) =>
                {
                    _undocked = null;
                    if (IsDisposed) return;
                    _host.Controls.Add(_web);
                    _web.Dock = DockStyle.Fill;
                    // 用户自己关窗 = 他要停靠：本次 LoopApp 期间不再按内容自动浮出去
                    _userDocked = true;
                    _autoUndocked = false;
                };
                _undocked = form;
                form.Show();
            });
        }

        public void BringBack()
        {
            OnUi(() =>
            {
                if (IsUndocked) _undocked.Close();
            });
        }

        private CallResult NavigateCore(string url)
        {
            string target = Normalize(url);
            if (target == null) return new CallResult(false, null, "地址为空");
            if (!EnsureCore(out string initError)) return new CallResult(false, null, initError);
            try
            {
                // LoopApp 页面要先有映射，否则虚拟域会解析成 chrome-error
                if (target.StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase))
                    ApplyVirtualHost();
                _web.CoreWebView2.Navigate(target);
                _txtUrl.Text = target;
                Touch();
                return new CallResult(true, target, null);
            }
            catch (Exception ex)
            {
                return new CallResult(false, null, "导航失败：" + ex.Message);
            }
        }

        private CallResult EvalCore(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return new CallResult(false, null, "脚本为空");
            if (!EnsureCore(out string initError)) return new CallResult(false, null, initError);
            try
            {
                var task = _web.CoreWebView2.ExecuteScriptAsync(script);
                if (!Pump(task)) return new CallResult(false, null, "执行脚本超时");
                Touch();
                return new CallResult(true, task.Result, null);
            }
            catch (Exception ex)
            {
                return new CallResult(false, null, "执行脚本失败：" + ex.Message);
            }
        }

        private CallResult ScreenshotCore()
        {
            if (!EnsureCore(out string initError)) return new CallResult(false, null, initError);
            string file = Path.Combine(Path.GetTempPath(), "openloop-shot-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var stream = File.Create(file))
                {
                    var task = _web.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, stream);
                    if (!Pump(task)) return new CallResult(false, null, "截图超时");
                }
                Touch();
                return new CallResult(true, Convert.ToBase64String(File.ReadAllBytes(file)), null);
            }
            catch (Exception ex)
            {
                return new CallResult(false, null, "截图失败：" + ex.Message);
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        private bool EnsureCore(out string error)
        {
            error = null;
            if (_ready) return true;
            if (_failed) { error = _overlay.Text; return false; }

            try
            {
                var task = _web.EnsureCoreWebView2Async(null);
                if (!Pump(task))
                {
                    _failed = true;
                    _overlay.Text = "WebView2 初始化超时：检查是否装了 WebView2 Runtime";
                    error = _overlay.Text;
                    return false;
                }

                var core = _web.CoreWebView2;
                core.SourceChanged += (s, e) => _txtUrl.Text = core.Source;
                core.NavigationCompleted += (s, e) => OnNavigationCompleted();
                // 站内 target=_blank 一律在本面板打开，免得弹出没有归属的窗口
                core.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    core.Navigate(e.Uri);
                };

                // LoopApp：页面 → agent 的消息桥（来源在 OnWebMessage 里再校验一次）
                core.WebMessageReceived += (s, e) =>
                {
                    try { OnWebMessage(e.Source, e.WebMessageAsJson); }
                    catch (Exception ex) { AppLog.Warn("LoopApp 消息处理失败：" + ex.Message); }
                };
                InstallShim(core);

                _ready = true;
                _overlay.Visible = false;
                // 浏览器建好时应用可能已经在盘上了（agent 先 create 后 open）
                ApplyVirtualHost();
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                _overlay.Text = "WebView2 初始化失败：" + ex.Message;
                error = _overlay.Text;
                return false;
            }
        }

        // WebView2 的 await 要回到 UI 线程，而 Puppet 调用是同步等待的：这里抽一下消息，等任务自己跑完。
        // shortcut: 简单消息泵而不是真正的异步接口，等 Puppet 支持 Task 返回值时换成 async。
        private static bool Pump(Task task)
        {
            long start = Environment.TickCount64;
            while (!task.IsCompleted && Environment.TickCount64 - start < WaitTimeoutMs)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            return task.IsCompletedSuccessfully;
        }

        private static string Normalize(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return null;
            if (url.Contains("://", StringComparison.Ordinal)) return url;
            foreach (string scheme in new[] { "about:", "file:", "data:", "edge:", "view-source:" })
                if (url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || char.IsDigit(url[0]))
                return "http://" + url;
            return "https://" + url;
        }

        private CallResult OnUiCall(Func<CallResult> action)
        {
            if (IsDisposed) return new CallResult(false, null, "浏览器视图已关闭");
            if (!InvokeRequired) return action();
            try { return (CallResult)Invoke(action); }
            catch (Exception ex) { return new CallResult(false, null, ex.Message); }
        }

        private void OnUi(Action action)
        {
            if (IsDisposed) return;
            if (!InvokeRequired) { action(); return; }
            try { Invoke(action); }
            catch (Exception) { }
        }

        private void OnBack(object sender, EventArgs e) => OnWeb(w => { if (w.CanGoBack) w.GoBack(); });
        private void OnForward(object sender, EventArgs e) => OnWeb(w => { if (w.CanGoForward) w.GoForward(); });
        private void OnReload(object sender, EventArgs e) => Reload();
        private void OnGo(object sender, EventArgs e) => Navigate(_txtUrl.Text, out _);
        private void OnUndock(object sender, EventArgs e) => Undock();

        /// <summary>
        /// 刷新当前页。LoopApp 页面用带时间戳的重新导航：映射来的响应没有 Cache-Control，
        /// 直接 Reload 会命中 WebView2 的启发式缓存，改过的 app.js 不会生效（实测）。
        /// </summary>
        private void Reload()
        {
            string url = Url;
            if (url.StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(url.Contains('?') ? url : url + "?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds(), out _);
                return;
            }
            OnWeb(w => w.Reload());
        }

        /// <summary>用系统默认浏览器打开当前页；还没导航过就用地址栏里的地址。</summary>
        private void OpenExternal()
        {
            string url = Url;
            if (string.IsNullOrWhiteSpace(url)) url = Normalize(_txtUrl.Text);
            if (url == null) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _overlay.Text = "打开系统浏览器失败：" + ex.Message;
            }
        }

        private void OnExternal(object sender, EventArgs e) => OpenExternal();

        private void OnUrlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Navigate(_txtUrl.Text, out _);
        }

        private void OnToggleInteract(object sender, EventArgs e) => SetInteract(!_interact);

        private void OnWeb(Action<CoreWebView2> action)
        {
            if (!_ready || _web.CoreWebView2 == null) return;
            action(_web.CoreWebView2);
        }
    }
}
