using System.Runtime.CompilerServices;
using OpenLoop.Service.Plugins;

namespace OpenLoop.App
{
    /// <summary>
    /// 服务型插件要起进程、拉模型目录，dsh 也要起进程探清单，这些都放在后台。界面填完下拉时它们往往还没好，
    /// 所以这里补几拍重填——不然用户得自己再点开一次才看得见新分组。
    /// </summary>
    internal static class PluginRefill
    {
        private const int Attempts = 8;
        private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);
        private static readonly ConditionalWeakTable<Control, object> Running = new();

        public static void WhenReady(Control owner, Action refresh)
        {
            if (owner == null || refresh == null || owner.IsDisposed) return;
            if (!Pending) return;
            if (Running.TryGetValue(owner, out _)) return;

            Running.Add(owner, new object());
            _ = LoopAsync(owner, refresh);
        }

        private static bool Pending => PluginCatalog.AnyPending || DshAcpHub.Warming;

        private static async Task LoopAsync(Control owner, Action refresh)
        {
            try
            {
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    await Task.Delay(Delay);
                    if (owner.IsDisposed) return;

                    if (owner.InvokeRequired) owner.Invoke(refresh);
                    else refresh();

                    if (!Pending) return;
                }

                AppLog.Warn($"插件下拉重填了 {Attempts} 次仍有服务没就绪，等下次打开再试");
            }
            catch (Exception ex)
            {
                AppLog.Warn("插件下拉重填失败：" + ex.Message);
            }
            finally
            {
                Running.Remove(owner);
            }
        }
    }
}
