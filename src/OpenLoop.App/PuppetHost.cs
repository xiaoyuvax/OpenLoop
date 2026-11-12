using System.Net;
using System.Net.Sockets;
using System.Text;
using Puppet.Core;
using Puppet.Core.Web;
using Puppet.Core.WinForms;

namespace OpenLoop.App
{
    /// <summary>
    /// Puppet 接入：A 面（/agent/*，开发期 Agent 自调试）+ B 面（/appagent/*，用户 Agent 按具名 Action 操作）。
    /// key 取 OPENLOOP_PUPPET_KEY，其次取 %APPDATA%\OpenLoop\puppet.key；都没有就生成并落盘，
    /// 因此 Puppet 默认在 loopback 上启用，其他 agent 读同一个 key 文件即可协作。
    /// B 面的凭证是框架写入用户档案的发现文件（%LOCALAPPDATA%\Puppet.AppAgents\OpenLoop.json）。
    /// </summary>
    internal static class PuppetHost
    {
        public const string KeyVariable = "OPENLOOP_PUPPET_KEY";
        public const string EndpointVariable = "OPENLOOP_PUPPET_ENDPOINT";
        public const string InstanceName = "MainForm";
        public const string ProductName = "OpenLoop";
        private static readonly int[] Ports = [9090, 9091, 9092, 9093, 9094, 9095, 9096, 9097, 9098, 9099];

        private static PuppetWebServer _server;
        private static System.Threading.Timer _keySync;
        private static readonly object KeyFileLock = new();
        private static string _writtenKey;

        public static bool Enabled => _server != null;
        public static string Endpoint { get; private set; }

        /// <summary>共享 key 文件（用户配置目录，不进仓库）；其他 agent 读它接入同一个 Puppet。</summary>
        public static string KeyPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop", "puppet.key");

        public static void Start(MainForm form)
        {
            string key = ResolveKey();

            string endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                int port = Array.Find(Ports, IsFree);
                if (port == 0)
                {
                    AppLog.Warn("Puppet 未启动：9090-9099 均被占用");
                    return;
                }
                endpoint = $"127.0.0.1:{port}";
            }

            try
            {
                PuppetKeyVault.SetKey(key);
                PuppetRegistry.Register(form);
                _server = new PuppetWebServer()
                    .UseFormControls()
                    .UseAppAgentWithUiText(o => o.ProductName = ProductName);
                if (!_server.Start(endpoint))
                {
                    AppLog.Warn($"Puppet 启动失败：{endpoint} 不可用");
                    _server = null;
                    return;
                }

                Endpoint = endpoint;
                // 内部 Agent（Loop/旁路）直调本进程 Puppet 用同一 Hub；A/I 靠发现文件走 HTTP 探测壳
                OpenLoop.Service.Web.PuppetBridge.LocalHub = _server.Hub;
                OpenLoop.Service.Web.PuppetBridge.SelfRole = "shell";
                OpenLoop.Service.Web.PuppetBridge.SaveShellEndpoint(endpoint);
                // 同伴可能调用 /agent/key/refresh 换 key：把运行中的 key 持续同步到文件，保证别人读到的永远可用
                _keySync = new System.Threading.Timer(_ => SyncKeyFile(), null, 2000, 2000);
                AppLog.Info($"Puppet A/B 面已启动：{endpoint}（实例 {InstanceName}，key 见 {KeyPath}）");
            }
            catch (Exception ex)
            {
                AppLog.Error("Puppet 启动异常", ex);
                _server = null;
            }
        }

        /// <summary>环境变量优先（并同步落盘）；否则用已保存的 key；都没有就生成一个。</summary>
        private static string ResolveKey()
        {
            string key = Environment.GetEnvironmentVariable(KeyVariable)?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                SaveKey(key);
                return key;
            }

            try
            {
                lock (KeyFileLock)
                {
                    if (File.Exists(KeyPath))
                    {
                        string saved = File.ReadAllText(KeyPath).Trim();
                        if (saved.Length > 0)
                        {
                            _writtenKey = saved;
                            return saved;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取 Puppet key 失败：" + ex.Message);
            }

            string generated = Guid.NewGuid().ToString("N");
            SaveKey(generated);
            return generated;
        }

        private static void SaveKey(string key)
        {
            lock (KeyFileLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(KeyPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(KeyPath, key, new UTF8Encoding(false));
                    _writtenKey = key;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("保存 Puppet key 失败：" + ex.Message);
                }
            }
        }

        /// <summary>把运行中的全局 key（可能已被 /agent/key/refresh 换掉）同步到共享文件。</summary>
        private static void SyncKeyFile()
        {
            try
            {
                string key = PuppetKeyVault.GlobalKey;
                if (!string.IsNullOrEmpty(key) && key != _writtenKey) SaveKey(key);
            }
            catch (Exception ex)
            {
                AppLog.Warn("同步 Puppet key 失败：" + ex.Message);
            }
        }

        private static bool IsFree(int port)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public static void Stop()
        {
            try
            {
                _keySync?.Dispose();
                _keySync = null;
                OpenLoop.Service.Web.PuppetBridge.LocalHub = null;
                OpenLoop.Service.Web.PuppetBridge.SaveShellEndpoint(null);
                PuppetRegistry.Unregister(InstanceName);
                _server?.Stop();
                _server = null;
                Endpoint = null;
                AppLog.Info("Puppet 已停止");
            }
            catch (Exception ex)
            {
                AppLog.Error("Puppet 停止异常", ex);
            }
        }
    }
}
