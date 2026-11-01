using System.Reflection;
using Puppet.Core.Web;
using Wima.Web;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Bus
{
    /// <summary>
    /// 一个进程的 HTTP 宿主：只承载 /bus/*（后续 M2 再加 /api/* 与静态资源）。
    /// 端口被占用时由 Puppet 引擎自动顺延，实际地址写进发现文件。
    /// </summary>
    public sealed class BusHost : IDisposable
    {
        private readonly string _role;
        private readonly string _listen;
        private readonly string _key;
        private readonly List<Func<WebRequest, bool>> _handlers = new();
        private PuppetWebServer _server;
        private bool _disposed;

        public BusHost(string role, string listen)
        {
            _role = role;
            _listen = listen;
            _key = KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        public string Version { get; }
        public RuntimeInfo Self { get; private set; }

        /// <summary>忙闲判定（A：有会话在跑；I：有演进在跑）。/bus/restart 忙时回 409。</summary>
        public Func<bool> Busy { get; set; }

        /// <summary>自己所在的版本目录，写进 runtime/&lt;role&gt;.json 供对端定位可执行文件。</summary>
        public string VersionDir { get; set; }

        /// <summary>收到 /bus/shutdown 时触发（由宿主决定怎么退出）。</summary>
        public event Action ShutdownRequested;

        /// <summary>挂接业务端点（/api/*）。必须在 <see cref="Start"/> 之前调用。</summary>
        public BusHost Use(Func<WebRequest, bool> handler)
        {
            if (handler != null) _handlers.Add(handler);
            return this;
        }

        public bool Start()
        {
            AppPaths.EnsureDirectory(AppPaths.RuntimeDir);

            var handler = new BusHandler(
                () => Self,
                () => Peer(),
                _key,
                () => ShutdownRequested?.Invoke(),
                _role,
                () => Busy?.Invoke() == true);

            _server = new PuppetWebServer().UseHandler(handler.Handle);
            foreach (var extra in _handlers) _server.UseHandler(extra);
            if (!_server.Start(_listen))
            {
                _server = null;
                return false;
            }

            Self = new RuntimeInfo
            {
                Role = _role,
                Pid = Environment.ProcessId,
                Version = Version,
                Protocol = BusProtocol.Version,
                Bus = _server.Endpoint,
                Web = "",
                Dir = VersionDir ?? AppContext.BaseDirectory,
                StartedAt = DateTimeOffset.Now.ToString("O")
            };
            Self.Save(AppPaths.RuntimeFile(_role));
            return true;
        }

        private RuntimeInfo Peer()
        {
            var peer = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.Other(_role)));
            return peer is { IsAlive: true } ? peer : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Self?.RemoveOwn(AppPaths.RuntimeFile(_role)); } catch { }
            try { _server?.Stop(); } catch { }
            _server = null;
        }
    }
}
