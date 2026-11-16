using Microsoft.Web.WebView2.Core;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧栏里的内嵌浏览器（WebView2）。
    /// 默认锁定：用户点不动网页（Agent 照常驱动）；「解锁」后用户可正常操作；「独立窗口」把网页移进顶层窗口。
    /// 所有方法都可以从任意线程调用，内部转回 UI 线程；WebView2 的异步初始化/脚本调用用消息泵等待。
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
                _btnInteract.Text = enabled ? "锁定" : "解锁";
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
                _web.CoreWebView2.Navigate(target);
                _txtUrl.Text = target;
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
                // 站内 target=_blank 一律在本面板打开，免得弹出没有归属的窗口
                core.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    core.Navigate(e.Uri);
                };

                _ready = true;
                _overlay.Visible = false;
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
        private void OnReload(object sender, EventArgs e) => OnWeb(w => w.Reload());
        private void OnGo(object sender, EventArgs e) => Navigate(_txtUrl.Text, out _);
        private void OnUndock(object sender, EventArgs e) => Undock();

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
