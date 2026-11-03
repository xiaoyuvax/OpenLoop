using System.Net.Http.Headers;
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
        private readonly Dictionary<string, McpClient> _mcp = new(StringComparer.OrdinalIgnoreCase);
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

        /// <summary>按模型名找贡献了它的提供商（工作区优先）：先看清单 models，再看 modelsUrl 拉到的目录。</summary>
        public bool TryProvider(string model, out PluginProviderContribution provider)
        {
            string id = model?.Trim();
            provider = null;
            if (string.IsNullOrEmpty(id)) return false;

            var declared = _plugins
                .SelectMany(p => p.Contributions?.Providers ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.BaseUrl))
                .ToList();

            provider = declared.FirstOrDefault(p => (p.Models ?? [])
                .Any(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase)));
            if (provider != null) return true;

            provider = declared.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ModelsUrl) &&
                Discover(p).Any(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase)));
            return provider != null;
        }

        /// <summary>所有 provider 贡献的模型 id（含发现结果），给模型选择器用。</summary>
        public IReadOnlyList<string> Models()
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in _plugins.SelectMany(p => p.Contributions?.Providers ?? []))
            {
                foreach (string id in (provider.Models ?? []).Concat(Discover(provider)))
                    if (!string.IsNullOrWhiteSpace(id) && seen.Add(id)) ids.Add(id);
            }
            return ids;
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
        private static readonly object DiscoveryLock = new();
        private static readonly Dictionary<string, (DateTimeOffset At, List<string> Ids)> Discovered =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromMinutes(5);

        /// <summary>拉一次 <c>/v1/models</c>；失败也缓存（空清单），免得每次调用都等超时。</summary>
        private static List<string> Discover(PluginProviderContribution provider)
        {
            if (string.IsNullOrWhiteSpace(provider.ModelsUrl)) return [];

            string url = provider.ModelsUrl;
            lock (DiscoveryLock)
            {
                if (Discovered.TryGetValue(url, out var cached) && DateTimeOffset.Now - cached.At < DiscoveryTtl)
                    return cached.Ids;
            }

            var ids = new List<string>();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                string key = provider.ResolveKey();
                if (!string.IsNullOrWhiteSpace(key))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = Http.Send(request);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        foreach (var item in data.EnumerateArray())
                            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                                ids.Add(id.GetString());
                }
                else
                {
                    AppLog.Warn($"插件模型目录不可用：{url} HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"插件模型目录不可用：{url} {ex.Message}");
            }

            lock (DiscoveryLock) Discovered[url] = (DateTimeOffset.Now, ids);
            return ids;
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

                if (plugin.Mcp is not { } mcp || string.IsNullOrWhiteSpace(mcp.Url)) continue;
                var client = Mcp(plugin);
                foreach (var spec in client.ListTools())
                    yield return new McpTool(client, spec);
            }
        }

        private McpClient Mcp(PluginManifest plugin)
        {
            if (_mcp.TryGetValue(plugin.Id, out var client)) return client;
            client = new McpClient(plugin.Mcp);
            _mcp[plugin.Id] = client;
            return client;
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
