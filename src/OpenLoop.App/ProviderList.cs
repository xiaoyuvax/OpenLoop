using OpenLoop.Service.Plugins;

namespace OpenLoop.App
{
    /// <summary>
    /// 下拉里的「提供商」：设置里登记的 + OpenCode + 插件贡献的（如 ofm、ofm-dsh）+ dsh（装好且探测到模型才有）。
    /// 三个页面共用这一份，别再各拼一份。
    /// </summary>
    internal static class ProviderList
    {
        public static IEnumerable<(string Name, ProviderConfig Tag)> All(AppSettings settings)
        {
            foreach (var provider in settings?.Providers ?? [])
                yield return (provider.Name, provider);

            foreach (var provider in OpenCodeHub.Providers)
                yield return (provider.Name, provider);

            using (var plugins = PluginCatalog.Load(settings?.Workspace ?? ""))
            {
                foreach (var (name, models) in plugins.ProviderGroups())
                    yield return (name, new ProviderConfig { Name = name, Kind = "custom", BaseUrl = "", Models = models });
            }

            if (DshAcpHub.UiProvider(settings) is { } dsh)
                yield return (dsh.Name, dsh);
        }
    }
}
