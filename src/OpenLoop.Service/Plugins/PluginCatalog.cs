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

        /// <summary>
        /// 解析 provider 的地址：字面量直接用；写 <c>auto</c> 表示「用这个插件自己的服务」
        /// （清单里 <c>service</c> 描述怎么起、怎么从 stdout 认地址）。
        /// </summary>
        private string Endpoint(PluginManifest plugin, PluginProviderContribution provider, string suffix,
            bool start = false)
        {
            string value = suffix == null ? provider.BaseUrl : provider.ModelsUrl;
            if (string.IsNullOrWhiteSpace(value)) return value;

            bool isAuto = value.Equals("auto", StringComparison.OrdinalIgnoreCase);
            bool isAutoPath = value.StartsWith("auto/", StringComparison.OrdinalIgnoreCase);
            if (!isAuto && !isAutoPath) return value;

            // 真要用它（发一轮对话）才允许阻塞等握手；列清单/填下拉走后台预热
            string baseUrl = start
                ? LocalServices.BaseUrl(plugin.Id, plugin.Service, Workspace, plugin.Root)
                : WarmAndKnown(plugin);
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            if (isAutoPath) return baseUrl + value[4..];
            return baseUrl + (suffix ?? "/v1");
        }

        /// <summary>界面路径：服务没起就在后台起，这一轮先不列它的模型。</summary>
        private string WarmAndKnown(PluginManifest plugin)
        {
            LocalServices.Warm(plugin.Id, plugin.Service, Workspace, plugin.Root);
            return LocalServices.Known(plugin.Id);
        }

        /// <summary>按模型名找贡献了它的提供商（工作区优先）：先看清单 models，再看 modelsUrl 拉到的目录。</summary>
        public bool TryProvider(string model, out PluginProviderContribution provider)
        {
            string id = model?.Trim();
            provider = null;
            if (string.IsNullOrEmpty(id)) return false;

            foreach (var plugin in _plugins)
            {
                foreach (var candidate in plugin.Contributions?.Providers ?? [])
                {
                    string prefix = candidate.ModelPrefix ?? "";
                    bool byPrefix = prefix.Length > 0 && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                    string bare = byPrefix ? id[prefix.Length..] : id;

                    bool declared = (candidate.Models ?? [])
                        .Any(m => string.Equals(m, bare, StringComparison.OrdinalIgnoreCase));
                    // 前缀就是归属声明：认得前缀就归它，哪怕模型目录还没拉到（服务刚起、目录为空）
                    bool discovered = !byPrefix && !declared && !string.IsNullOrWhiteSpace(candidate.ModelsUrl) &&
                        Discover(Endpoint(plugin, candidate, "/v1/models", start: true), candidate.ResolveKey(), wait: true)
                            .Any(m => string.Equals(m, bare, StringComparison.OrdinalIgnoreCase));
                    if (!byPrefix && !declared && !discovered) continue;

                    string baseUrl = Endpoint(plugin, candidate, null, start: true);
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        AppLog.Warn($"服务型插件 {plugin.Id} 没有就绪，模型 {id} 用不了");
                        candidate.BaseUrl = "";
                        provider = candidate;
                        return true;
                    }

                    candidate.BaseUrl = baseUrl;
                    provider = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>所有 provider 贡献的模型 id（含发现结果、带界面前缀），给模型选择器用。</summary>
        public IReadOnlyList<string> Models()
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in _plugins)
            {
                foreach (var provider in plugin.Contributions?.Providers ?? [])
                {
                    foreach (string id in All(plugin, provider))
                        if (!string.IsNullOrWhiteSpace(id) && seen.Add(id)) ids.Add(id);
                }
            }
            return ids;
        }

        /// <summary>某个 provider 的完整模型清单（声明的 + modelsUrl 拉到的，统一加界面前缀）。</summary>
        private IEnumerable<string> All(PluginManifest plugin, PluginProviderContribution provider)
        {
            string prefix = provider.ModelPrefix ?? "";
            return (provider.Models ?? [])
                .Concat(Discover(Endpoint(plugin, provider, "/v1/models"), provider.ResolveKey()))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? id : prefix + id);
        }

        /// <summary>后台还有服务预热或模型目录刷新在跑：界面填完下拉后据此决定要不要过一会儿重填。</summary>
        public static bool AnyPending
        {
            get
            {
                if (LocalServices.AnyStarting) return true;
                lock (DiscoveryLock) return Discovering.Count > 0;
            }
        }

        /// <summary>给提供商下拉用的分组：插件声明的 provider 名 + 它的模型，选中后只列这些模型。</summary>
        public IReadOnlyList<(string Name, List<string> Models)> ProviderGroups()
        {
            var groups = new List<(string, List<string>)>();
            foreach (var plugin in _plugins)
            {
                foreach (var provider in plugin.Contributions?.Providers ?? [])
                {
                    var ids = All(plugin, provider)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (ids.Count == 0) continue;
                    groups.Add((string.IsNullOrWhiteSpace(provider.Name) ? provider.Id : provider.Name, ids));
                }
            }
            return groups;
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
        private static readonly object DiscoveryLock = new();
        private static readonly Dictionary<string, (DateTimeOffset At, List<string> Ids)> Discovered =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Discovering = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EmptyDiscoveryTtl = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 拉一次 <c>/v1/models</c>；失败也缓存（空清单），免得每次调用都等超时。
        /// <paramref name="wait"/> 为假时（界面路径）只给缓存，过期就后台刷，绝不在调用方线程上等 HTTP。
        /// </summary>
        private static List<string> Discover(string url, string key, bool wait = false)
        {
            if (string.IsNullOrWhiteSpace(url)) return [];

            lock (DiscoveryLock)
            {
                if (Discovered.TryGetValue(url, out var cached) &&
                    DateTimeOffset.Now - cached.At < (cached.Ids.Count == 0 ? EmptyDiscoveryTtl : DiscoveryTtl))
                    return cached.Ids;
            }

            if (wait) Fetch(url, key);

            // 界面路径（列模型、填下拉）不能等 HTTP：过期就后台刷，先给旧值，没有就空
            else WarmDiscovery(url, key);

            lock (DiscoveryLock)
                return Discovered.TryGetValue(url, out var stale) ? stale.Ids : [];
        }

        /// <summary>后台刷新某个模型目录（同一地址只刷一份）。</summary>
        private static void WarmDiscovery(string url, string key)
        {
            lock (DiscoveryLock)
            {
                if (!Discovering.Add(url)) return;
            }

            _ = Task.Run(() =>
            {
                try { Fetch(url, key); }
                catch (Exception ex) { AppLog.Warn($"插件模型目录刷新失败：{url} {ex.Message}"); }
                finally { lock (DiscoveryLock) Discovering.Remove(url); }
            });
        }

        private static void Fetch(string url, string key)
        {
            var ids = new List<string>();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(key))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = Http.Send(request);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync()
                        .ConfigureAwait(false).GetAwaiter().GetResult());
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

                if (plugin.Mcp is not { } mcp
                    || (string.IsNullOrWhiteSpace(mcp.Url) && string.IsNullOrWhiteSpace(mcp.Command)))
                    continue;
                var client = Mcp(plugin);
                foreach (var spec in client.ListTools())
                    yield return new McpTool(client, spec);
            }
        }

        private McpClient Mcp(PluginManifest plugin)
        {
            if (_mcp.TryGetValue(plugin.Id, out var client)) return client;
            client = new McpClient(plugin.Mcp, Workspace);
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
            foreach (var client in _mcp.Values) client.Dispose();
            _mcp.Clear();
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
