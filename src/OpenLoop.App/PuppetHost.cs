using System.Net;
using System.Net.Sockets;
using Puppet.Core;
using Puppet.Core.Web;
using Puppet.Core.WinForms;

namespace OpenLoop.App
{
    /// <summary>
    /// Puppet 接入：只开 A 面（/agent/*，开发期 Agent 自调试）。
    /// 未设置 OPENLOOP_PUPPET_KEY 时不启动，避免默认开一个监听口。
    /// B 面（/appagent/*、/mcp）通过不调用 UseAppAgent* 保持关闭。
    /// </summary>
    internal static class PuppetHost
    {
        public const string KeyVariable = "OPENLOOP_PUPPET_KEY";
        public const string EndpointVariable = "OPENLOOP_PUPPET_ENDPOINT";
        public const string InstanceName = "MainForm";
        private static readonly int[] Ports = [9090, 9091, 9092, 9093, 9094, 9095, 9096, 9097, 9098, 9099];

        private static PuppetWebServer _server;

        public static bool Enabled => _server != null;
        public static string Endpoint { get; private set; }

        public static void Start(MainForm form)
        {
            string key = Environment.GetEnvironmentVariable(KeyVariable);
            if (string.IsNullOrWhiteSpace(key))
            {
                AppLog.Info($"Puppet 未启用：未设置 {KeyVariable}");
                return;
            }

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
                _server = new PuppetWebServer().UseFormControls();
                if (!_server.Start(endpoint))
                {
                    AppLog.Warn($"Puppet 启动失败：{endpoint} 不可用");
                    _server = null;
                    return;
                }

                Endpoint = endpoint;
                AppLog.Info($"Puppet A 面已启动：{endpoint}（实例 {InstanceName}）");
            }
            catch (Exception ex)
            {
                AppLog.Error("Puppet 启动异常", ex);
                _server = null;
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
