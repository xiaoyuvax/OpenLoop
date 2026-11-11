using System.Text.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;

namespace OpenLoop.Service.Host
{
    /// <summary>A/I 共用的无头宿主入口：起 bus、向对端握手、等退出信号。</summary>
    public static class HeadlessHost
    {
        public static int Run(string role, string defaultEndpoint, string[] args, Action<BusHost> configure = null)
        {
            if (TryManage(args, role, out int exitCode)) return exitCode;

            int parent = IntArg(args, "--parent");
            string bind = StringArg(args, "--bind") ?? defaultEndpoint;

            using var host = new BusHost(role, bind);
            host.VersionDir = StringArg(args, "--version-dir") ?? AppContext.BaseDirectory;
            configure?.Invoke(host);
            if (!host.Start())
            {
                Console.Error.WriteLine($"[{role}] bus 启动失败（{bind}）");
                return 1;
            }

            Console.WriteLine($"[{role}] bus {host.Self.Bus} · 协议 {host.Self.Protocol} · 版本 {host.Self.Version}");
            HelloPeer(role, host.Self);

            var stop = new ManualResetEventSlim();
            host.ShutdownRequested += stop.Set;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

            using var watch = parent > 0
                ? ParentWatch.Start(parent, () =>
                {
                    Console.WriteLine($"[{role}] 父进程 {parent} 已退出，随之退出");
                    host.Dispose();
                    Environment.Exit(0);
                })
                : null;
            if (parent > 0) Console.WriteLine($"[{role}] 守护父进程 {parent}");

            stop.Wait();
            return 0;
        }

        /// <summary>
        /// 发布辅助（不起 bus）：
        /// <c>--info</c> 打印本二进制的 role/version/protocol；
        /// <c>--write-manifest &lt;dir&gt; [--manifest-version X]</c> 把清单写进版本目录，顺带校验二进制版本与发布版本一致。
        /// </summary>
        private static bool TryManage(string[] args, string role, out int exitCode)
        {
            exitCode = 0;
            bool info = args != null && args.Any(a => string.Equals(a, "--info", StringComparison.OrdinalIgnoreCase));
            string manifestDir = StringArg(args, "--write-manifest");
            if (!info && string.IsNullOrWhiteSpace(manifestDir)) return false;

            string version = SelfVersion();
            if (!string.IsNullOrWhiteSpace(manifestDir))
            {
                string expected = StringArg(args, "--manifest-version");
                if (!string.IsNullOrWhiteSpace(expected) && !SourceSync.SameVersion(version, expected))
                {
                    Console.Error.WriteLine(
                        $"[{role}] 二进制版本 {version} 与发布版本 {expected} 不一致：先把 csproj <Version> 改成 {expected} 再发布");
                    exitCode = 2;
                    return true;
                }

                string dir = Path.GetFullPath(manifestDir.Trim());
                if (!File.Exists(VersionStore.ExePath(dir, role)))
                {
                    Console.Error.WriteLine($"[{role}] 版本目录里没有可执行文件：{dir}");
                    exitCode = 3;
                    return true;
                }

                var manifest = VersionManifest.Write(dir, role, version, BusProtocol.Version);
                Console.WriteLine(JsonSerializer.Serialize(manifest, BusProtocol.Json));
                return true;
            }

            Console.WriteLine(JsonSerializer.Serialize(
                new { role, version, protocol = BusProtocol.Version }, BusProtocol.Json));
            return true;
        }

        /// <summary>入口程序集自己的版本（A/I 各自 csproj 的 &lt;Version&gt;），去掉 +sha。</summary>
        private static string SelfVersion()
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(HeadlessHost).Assembly;
            string raw = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                              .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                              .FirstOrDefault()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString(3)
                         ?? "unknown";
            string clean = SourceSync.ParseVersion(raw);
            return clean.Length > 0 ? clean : raw;
        }

        private static void HelloPeer(string role, RuntimeInfo self)
        {
            var peer = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.Other(role)));
            if (peer is not { IsAlive: true }) return;

            try
            {
                using var client = new BusClient(peer.Bus);
                client.HelloAsync(self).Wait(3000);
                Console.WriteLine($"[{role}] 已与 {peer.Role} 握手（{peer.Bus}）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{role}] 握手失败（{peer.Bus}）：{ex.Message}");
            }
        }

        private static string StringArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static int IntArg(string[] args, string name)
            => int.TryParse(StringArg(args, name), out int value) && value > 0 ? value : 0;
    }
}
