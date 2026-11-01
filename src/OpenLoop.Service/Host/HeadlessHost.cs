using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Host
{
    /// <summary>A/I 共用的无头宿主入口：起 bus、向对端握手、等退出信号。</summary>
    public static class HeadlessHost
    {
        public static int Run(string role, string defaultEndpoint, string[] args, Action<BusHost> configure = null)
        {
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
