using OpenLoop.Service.Bus;
using OpenLoop.Service.Host;
using OpenLoop.Service.Runtime;
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

            return HeadlessHost.Run(BusRoles.A, "127.0.0.1:19180", args, bus =>
            {
                bus.Busy = () => sessions.IsBusy;
                bus.Use(proxy.Handle)
                   .Use(new WorkerApiHandler(sessions, key).Handle)
                   .Use(ui.Handle);
            });
        }
    }
}
