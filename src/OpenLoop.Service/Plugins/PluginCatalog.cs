using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// 工作区 + 用户级插件目录的清单集合。工作区同名插件优先，清单无效的目录跳过。
    /// 只读清单不启动进程；第一次调用工具时才拉起，运行结束 Dispose 收摊。
    /// </summary>
    public sealed class PluginCatalog : IDisposable
    {
        private readonly List<PluginManifest> _plugins;
        private readonly Dictionary<string, PluginHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        private PluginCatalog(string workspace, List<PluginManifest> plugins)
        {
            Workspace = workspace ?? "";
            _plugins = plugins;
        }

        public string Workspace { get; }
        public IReadOnlyList<PluginManifest> Plugins => _plugins;

        public static string GlobalRoot => Path.Combine(AppPaths.DataRoot, "plugins");

        public static PluginCatalog Load(string workspace, string globalRoot = null)
        {
            var found = new List<PluginManifest>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in Roots(workspace, globalRoot))
            {
                if (!Directory.Exists(root)) continue;

                List<string> dirs;
                try
                {
                    dirs = Directory.EnumerateDirectories(root).ToList();
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"插件目录读不了，已跳过 {root}：{ex.Message}");
                    continue;
                }

                foreach (string dir in dirs)
                {
                    var manifest = PluginManifest.TryLoad(dir);
                    if (manifest == null)
                    {
                        AppLog.Warn("插件清单无效，已跳过：" + dir);
                        continue;
                    }
                    if (!ids.Add(manifest.Id)) continue;
                    found.Add(manifest);
                }
            }

            return new PluginCatalog(workspace, found);
        }

        /// <summary>按模型名找贡献了它的提供商（工作区优先）。</summary>
        public bool TryProvider(string model, out PluginProviderContribution provider)
        {
            string id = model?.Trim();
            provider = null;
            if (string.IsNullOrEmpty(id)) return false;

            provider = _plugins
                .SelectMany(p => p.Contributions?.Providers ?? [])
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl) &&
                    (p.Models ?? []).Any(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase)));
            return provider != null;
        }

        /// <summary>一次查询用：读清单、找模型，不启动任何进程。</summary>
        public static bool TryProvider(string workspace, string model, out PluginProviderContribution provider,
            string globalRoot = null)
        {
            using var catalog = Load(workspace, globalRoot);
            return catalog.TryProvider(model, out provider);
        }

        public IEnumerable<ITool> Tools()
        {
            foreach (var plugin in _plugins)
            {
                foreach (var spec in plugin.Contributions?.Tools ?? [])
                {
                    if (string.IsNullOrWhiteSpace(spec.Name)) continue;
                    yield return new PluginTool(plugin, spec, Host(plugin), Workspace);
                }
            }
        }

        private PluginHost Host(PluginManifest plugin)
        {
            if (_hosts.TryGetValue(plugin.Id, out var host)) return host;
            host = new PluginHost(plugin);
            _hosts[plugin.Id] = host;
            return host;
        }

        private static IEnumerable<string> Roots(string workspace, string globalRoot)
        {
            if (!string.IsNullOrWhiteSpace(workspace))
                yield return Path.Combine(workspace, ".openloop", "plugins");
            yield return globalRoot ?? GlobalRoot;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var host in _hosts.Values) host.Dispose();
            _hosts.Clear();
        }
    }

    /// <summary>把一次工具调用转给插件进程；宿主不认识具体工具，只转发。</summary>
    public sealed class PluginTool : ITool
    {
        private readonly PluginToolContribution _spec;
        private readonly PluginHost _host;
        private readonly string _workspace;

        public PluginTool(PluginManifest plugin, PluginToolContribution spec, PluginHost host, string workspace)
        {
            _spec = spec;
            _host = host;
            _workspace = workspace ?? "";
        }

        public string Name => _spec.Name;
        public string Description => _spec.Description ?? "";
        public string ParamsSchema => _spec.Params ?? "";
        public bool IsReadOnly => _spec.ReadOnly;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
            => _host.CallToolAsync(_spec.Name, args.Raw, _workspace, ct);
    }
}
