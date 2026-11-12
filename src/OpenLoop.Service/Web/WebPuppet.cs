using System.Text;
using System.Text.Json;
using Puppet.Core;
using Puppet.Core.AppAgent;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// Web 壳的 Puppet 挂钩（A/I 同一份）。
    /// /agent/*、/mcp 由 BusHost 里那只 PuppetWebServer 在同一个端口上提供，这里只补三件事：
    /// 全局 key、页面代理实例 WebUi、页面轮询用的 /api/puppet/*。
    /// 截图不是抓桌面：页面自己把 DOM 渲染成 PNG 回传，且需人工勾选开关（Puppet 不能自行开启）。
    /// </summary>
    public sealed class WebPuppet : IPuppet, IDisposable
    {
        public const string InstanceName = "WebUi";
        public const string KeyVariable = "OPENLOOP_PUPPET_KEY";
        private const int PageTimeoutMs = 15000;
        private const int PageAliveMs = 3000;

        private readonly string _busKey;
        private readonly object _gate = new();
        private long _seen;
        private long _active;
        private long _seq;
        private int _x, _y, _w, _h;
        private byte[] _png;
        private TaskCompletionSource _ready;
        private WebPageState _state = new();

        public WebPuppet(string busKey) => _busKey = busKey;

        /// <summary>页面最近一次上报的视图状态（浏览器 POST /api/puppet/state）。</summary>
        public WebPageState Page => Volatile.Read(ref _state);

        public void Start()
        {
            PuppetKeyVault.SetKey(ResolveKey());
            PuppetUiActions.CaptureResolver = instance =>
                ReferenceEquals(instance, this) ? new PageCapture(this) : null;
            PuppetRegistry.Register(this);
            AppLog.Info($"Web Puppet 已挂载：实例 {InstanceName}（与 Web/API 同端口，页面勾选后可截屏）");
        }

        public void Dispose()
        {
            PuppetUiActions.CaptureResolver = null;
            PuppetRegistry.Unregister(InstanceName);
        }

        string IPuppet.AgentAccessKey => PuppetKeyVault.GlobalKey;
        Common.Logging.ILog IPuppet.AgentLog => AppLog.Log;
        string IPuppet.AgentInstanceName => InstanceName;

        /// <summary>浏览器端轮询端点。鉴权跟 /api/* 一致（Cookie 或 X-OpenLoop-Key）。</summary>
        public bool HandleApi(WebRequest req)
        {
            string path = req?.Path ?? "";
            if (!path.StartsWith("/api/puppet", StringComparison.OrdinalIgnoreCase)) return false;
            if (!BusAuth.IsAuthorized(req, _busKey, allowCookie: true)) return NotFound(req);

            string method = (req.Method ?? "GET").ToUpperInvariant();

            if (method == "POST" && path.Equals("/api/puppet/state", StringComparison.OrdinalIgnoreCase))
            {
                var state = ReadBody<WebPageState>(req);
                if (state == null) return Json(req, new { ok = false }, 400);
                state.UpdatedAt = DateTimeOffset.Now.ToString("O");
                Volatile.Write(ref _state, state);
                return Json(req, new { ok = true });
            }

            if (method == "GET" && path.Equals("/api/puppet/pending", StringComparison.OrdinalIgnoreCase))
            {
                lock (_gate)
                {
                    Interlocked.Exchange(ref _seen, Environment.TickCount64);
                    if (_ready == null) req.Response.StatusCode = 204;
                    else return Json(req, new { id = _active, x = _x, y = _y, width = _w, height = _h });
                }
                return true;
            }

            if (method == "POST" && path.Equals("/api/puppet/shot", StringComparison.OrdinalIgnoreCase))
            {
                var body = ReadBody<ShotRequest>(req);
                if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                try
                {
                    Submit(body.Id, body.Png);
                    return Json(req, new { ok = true });
                }
                catch (ArgumentException ex)
                {
                    return Json(req, new { ok = false, error = ex.Message }, 400);
                }
            }

            return NotFound(req);
        }

        internal PuppetArtifact CaptureScreen(int x, int y, int width, int height)
        {
            TaskCompletionSource ready;
            lock (_gate)
            {
                if (Environment.TickCount64 - Interlocked.Read(ref _seen) > PageAliveMs)
                    throw new InvalidOperationException("页面没有在轮询截图请求：请打开 Web 页面并勾选「允许 Puppet 截图」。");
                if (_ready != null)
                    throw new InvalidOperationException("已有一次截图在等页面回传。");

                _active = ++_seq;
                _x = x;
                _y = y;
                _w = width;
                _h = height;
                _png = null;
                _ready = ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            bool delivered = ready.Task.Wait(PageTimeoutMs);
            byte[] png;
            lock (_gate)
            {
                png = _png;
                _png = null;
                _ready = null;
                _active = 0;
            }

            if (!delivered || png == null)
                throw new InvalidOperationException("页面没有在 15 秒内回传截图。");
            return new PuppetArtifact("webpage.png", "image/png", png);
        }

        private void Submit(long id, string png)
        {
            const string prefix = "data:image/png;base64,";
            if (png == null || !png.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException("需要 data:image/png;base64 图像。");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(png[prefix.Length..]); }
            catch (FormatException) { throw new ArgumentException("图像数据不是合法 base64。"); }
            if (bytes.Length is < 8 or > 1500000) throw new ArgumentException("图像大小超出范围。");
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                throw new ArgumentException("图像不是 PNG。");

            TaskCompletionSource ready;
            lock (_gate)
            {
                if (id != _active || _ready == null) throw new ArgumentException("截图请求未知或已完成。");
                _png = bytes;
                ready = _ready;
            }
            ready.TrySetResult();
        }

        /// <summary>环境变量优先；否则共享 key 文件（与 WinForms 壳同一份）；都没有就生成并落盘。</summary>
        private static string ResolveKey()
        {
            string env = Environment.GetEnvironmentVariable(KeyVariable)?.Trim();
            if (!string.IsNullOrEmpty(env)) return env;

            string path = AppPaths.PuppetKeyPath;
            try
            {
                if (File.Exists(path))
                {
                    string saved = File.ReadAllText(path).Trim();
                    if (saved.Length > 0) return saved;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Guid.NewGuid().ToString("N"), new UTF8Encoding(false));
                // 与壳并发首启时两边都可能写过：以落盘后的内容为准，保证读到同一把 key
                return File.ReadAllText(path).Trim();
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取 Puppet key 失败：" + ex.Message);
                return Guid.NewGuid().ToString("N");
            }
        }

        private static T ReadBody<T>(WebRequest req) where T : class
        {
            try
            {
                if (req.InputStream == null || !req.InputStream.CanRead) return null;
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8, false, 4096, leaveOpen: true);
                return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        private static bool Json(WebRequest req, object value, int status = 0)
        {
            req.Response.Headers.Set("Content-Type", "application/json; charset=utf-8");
            req.Response.Headers.Set("Cache-Control", "no-store");
            req.Response.Buffer = JsonSerializer.SerializeToUtf8Bytes(value, BusProtocol.Json);
            if (status != 0) req.Response.StatusCode = status;
            return true;
        }

        private static bool NotFound(WebRequest req)
        {
            req.Response.StatusCode = 404;
            req.Response.Buffer = Encoding.UTF8.GetBytes("not found");
            return true;
        }

        private sealed class PageCapture(WebPuppet owner) : IPuppetCapture
        {
            public PuppetArtifact Capture(int x, int y, int width, int height) => owner.CaptureScreen(x, y, width, height);
        }

        private sealed class ShotRequest
        {
            public long Id { get; set; }
            public string Png { get; set; }
        }
    }

    /// <summary>浏览器自己上报的页面视图（Puppet 用 /agent/state 读）。</summary>
    public sealed class WebPageState
    {
        public string Url { get; set; } = "";
        public string Tab { get; set; } = "";
        public string SessionId { get; set; } = "";
        public int SessionCount { get; set; }
        public string SessionState { get; set; } = "";
        public int Prompts { get; set; }
        public int Pending { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Dpr { get; set; }
        public bool CaptureEnabled { get; set; }
        public string UpdatedAt { get; set; } = "";
    }
}
