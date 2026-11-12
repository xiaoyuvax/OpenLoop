using System.Text;
using Puppet.Core.Hub;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 进程内 Puppet 入口登记处。
    /// A/I 的 BusHost 与 WinForms 壳的 PuppetHost 在启动时把自己的 Hub 挂上来，
    /// 内部 Agent（Worker / Loop）用 Puppet 工具 target=self 时直接走 ProcessWebRequest，不经端口。
    /// 对端与壳只能走 HTTP（Puppet 接口），壳是被动服务方。
    /// </summary>
    public static class PuppetBridge
    {
        /// <summary>本进程的 Puppet 能力入口；未挂上（如测试、发布辅助进程）时为 null。</summary>
        public static PuppetHub LocalHub { get; set; }

        /// <summary>本进程角色：a / i / shell。</summary>
        public static string SelfRole { get; set; }

        /// <summary>壳写入的 Puppet 监听地址（A/I 探测壳用）。</summary>
        public static string ShellEndpointFile => Path.Combine(AppPaths.DataRoot, "puppet.endpoint");

        public static string ShellEndpoint
        {
            get
            {
                try
                {
                    string path = ShellEndpointFile;
                    return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>共享 Puppet key；每次现读，壳换钥后无需重启 A/I。</summary>
        public static string Key
        {
            get
            {
                try { return File.ReadAllText(AppPaths.PuppetKeyPath).Trim(); }
                catch (Exception) { return null; }
            }
        }

        public static void SaveShellEndpoint(string endpoint)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ShellEndpointFile));
                if (string.IsNullOrWhiteSpace(endpoint)) File.Delete(ShellEndpointFile);
                else File.WriteAllText(ShellEndpointFile, endpoint, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 探测文件写失败只影响 A/I 主动探测壳，不影响壳自身服务
            }
        }
    }
}
