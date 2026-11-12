using OpenLoop.Service.Bus;
using OpenLoop.Service.Host;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Web;

namespace OpenLoop.I
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var loop = new LoopService();
            var iteration = new IterationService();
            string key = KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            var ui = new WebUiHandler(key);
            using var puppet = new WebPuppet(key);
            puppet.Start();

            return HeadlessHost.Run(BusRoles.I, "127.0.0.1:19181", args, bus =>
            {
                bus.Busy = () => loop.IsBusy;
                bus.Use(puppet.HandleApi)
                   .Use(new LoopApiHandler(loop, iteration, key).Handle)
                   .Use(ui.Handle);
            });
        }
    }
}
