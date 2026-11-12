using OpenLoop.Service.Bus;
using OpenLoop.Service.Host;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Source;
using OpenLoop.Service.Web;
using OpenLoop.Service.Worker;

namespace OpenLoop.A
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (EvalRun.Wants(args)) return EvalRun.Execute(args);

            using var loop = new LoopApiClient();
            using var proxy = new LoopProxyHandler(KeyFile.ReadOrCreate(AppPaths.BusKeyPath));
            var sessions = new WorkerSessionHost(loop: loop);
            string key = KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            var ui = new WebUiHandler(key);
            using var puppet = new WebPuppet(key);
            puppet.Start();

            // 壳换完 A 会留下「更新 I」的交接单；I 不能换自己，由 A 接手
            _ = LoopUpdater.StartAsync();

            return HeadlessHost.Run(BusRoles.A, "127.0.0.1:19180", args, bus =>
            {
                bus.Busy = () => sessions.IsBusy;
                bus.Use(puppet.HandleApi)
                   .Use(proxy.Handle)
                   .Use(new WorkerApiHandler(sessions, key).Handle)
                   .Use(ui.Handle);
            });
        }
    }
}
