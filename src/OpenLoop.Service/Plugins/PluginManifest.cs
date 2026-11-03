using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// 插件清单：目录里的 <c>plugin.json</c>。贡献与入口都写在这里，
    /// 宿主只按清单启动子进程，不加载插件代码。
    /// </summary>
    public sealed class PluginManifest
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public PluginEntry Entry { get; set; } = new();
        public PluginContributions Contributions { get; set; } = new();
        public PluginPermissions Permissions { get; set; } = new();

        /// <summary>可选：接一个 MCP 服务端（Streamable HTTP 或 stdio 子进程），工具在运行时发现</summary>
        public PluginMcpContribution Mcp { get; set; }

        /// <summary>可选：一个本地服务（宿主按清单拉起子进程，provider 的地址可以写 auto）</summary>
        public PluginServiceContribution Service { get; set; }

        /// <summary>清单所在目录（入口的相对参数与工作目录都以它为基准）。</summary>
        [JsonIgnore]
        public string Root { get; set; }

        public static PluginManifest TryLoad(string dir)
        {
            try
            {
                string file = Path.Combine(dir, "plugin.json");
                if (!File.Exists(file)) return null;

                var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file), Json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) return null;

                bool hasTools = (manifest.Contributions?.Tools ?? []).Count > 0;
                bool hasProviders = (manifest.Contributions?.Providers ?? []).Count > 0;
                bool hasMcp = !string.IsNullOrWhiteSpace(manifest.Mcp?.Url)
                              || !string.IsNullOrWhiteSpace(manifest.Mcp?.Command);
                if (!hasTools && !hasProviders && !hasMcp) return null;

                // 只有工具贡献才需要子进程；纯 provider 清单不用起进程
                if (hasTools && string.IsNullOrWhiteSpace(manifest.Entry?.Command)) return null;

                manifest.Root = Path.GetFullPath(dir);

                // 清单里的 {root} 指向清单所在目录：服务参数、密钥文件都据此定位
                foreach (var provider in manifest.Contributions?.Providers ?? [])
                {
                    provider.BaseUrl = Expand(provider.BaseUrl, manifest.Root);
                    provider.ModelsUrl = Expand(provider.ModelsUrl, manifest.Root);
                    provider.KeyFile = Expand(provider.KeyFile, manifest.Root);
                }

                return manifest;
            }
            catch
            {
                return null;
            }
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static string Expand(string value, string root)
            => string.IsNullOrEmpty(value)
                ? value
                : value.Replace("{root}", root, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class PluginEntry
    {
        public string Command { get; set; }
        public List<string> Args { get; set; } = [];

        /// <summary>单次调用上限；插件卡死时宿主据此杀进程。</summary>
        public int TimeoutMs { get; set; } = 60000;
    }

    public sealed class PluginContributions
    {
        public List<PluginToolContribution> Tools { get; set; } = [];
        public List<PluginProviderContribution> Providers { get; set; } = [];
    }

    /// <summary>工具贡献：名字/描述/参数提示与内置 <c>ITool</c> 保持同一形状。</summary>
    public sealed class PluginToolContribution
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Params { get; set; }
        public bool ReadOnly { get; set; }
    }

    /// <summary>提供商贡献：一个 OpenAI 兼容端点，宿主直接用现成的推理通道。</summary>
    public sealed class PluginProviderContribution
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public List<string> Models { get; set; } = [];

        /// <summary>密钥所在环境变量名；空表示不带鉴权。</summary>
        public string KeyEnv { get; set; }

        /// <summary>密钥来自本地凭据文件（支持 <c>%USERPROFILE%</c> 与 <c>~/</c>）；KeyEnv 优先。</summary>
        public string KeyFile { get; set; }

        /// <summary>
        /// 可选：<c>GET</c> 该地址（Bearer 同一个密钥）取 <c>data[].id</c> 当模型清单，
        /// 用于模型 id 由上游目录决定、写死在清单里会腐坏的情况。
        /// </summary>
        public string ModelsUrl { get; set; }

        /// <summary>
        /// 模型 id 的界面前缀（如 <c>ofm/</c>）：让人能区分「同一 id 由谁提供」，
        /// 走 wire 时会去掉，不会发给上游。
        /// </summary>
        public string ModelPrefix { get; set; }

        /// <summary>密钥文件是 JSON 时，取哪个字段（空表示整份文件就是密钥）。</summary>
        public string KeyField { get; set; }

        public string ResolveKey() => PluginSecret.Resolve(KeyEnv, KeyFile, KeyField);

        /// <summary>界面上的模型 id → 上游期望的模型 id。</summary>
        public string WireModel(string model)
            => string.IsNullOrEmpty(ModelPrefix) || model == null ? model
                : model.StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase) ? model[ModelPrefix.Length..]
                : model;

        internal static string ExpandPath(string path) => PluginSecret.ExpandPath(path);
    }

    /// <summary>
    /// 可选：一个本地服务插件。宿主按清单拉起子进程，从 stdout 里认出它的 URL，
    /// provider 的 <c>baseUrl</c>/<c>modelsUrl</c> 写 <c>auto</c> 就用这个地址。
    /// 生命周期跟宿主进程一致（退出时杀掉）。
    /// </summary>
    public sealed class PluginServiceContribution
    {
        public string Command { get; set; }
        public List<string> Args { get; set; } = [];

        /// <summary>从 stdout 每行里提取服务地址的正则；第一个分组是 URL（没有分组就取整行 0 号）。</summary>
        public string UrlPattern { get; set; } = @"(https?://[^\s""']+)";

        /// <summary>等 URL 出现的上限。</summary>
        public int TimeoutMs { get; set; } = 15000;

        public Dictionary<string, string> Env { get; set; } = [];
    }

    /// <summary>
    /// 可选：一个 MCP 服务端。<c>url</c> 走 Streamable HTTP（POST JSON-RPC，无会话头，Puppet 的 <c>/mcp</c> 就是这种）；
    /// <c>command</c> 走本地 stdio 子进程（每行一条 JSON-RPC）。
    /// 工具由 <c>tools/list</c> 在运行时发现。
    /// </summary>
    public sealed class PluginMcpContribution
    {
        public string Url { get; set; }

        /// <summary>stdio：启动命令（与 url 二选一）。</summary>
        public string Command { get; set; }

        public List<string> Args { get; set; } = [];

        public string KeyEnv { get; set; }
        public string KeyFile { get; set; }
        public int TimeoutMs { get; set; } = 60000;

        public string ResolveKey() => PluginSecret.Resolve(KeyEnv, KeyFile);
    }

    /// <summary>provider / mcp 共用的凭据解析：环境变量优先，其次本地文件（可选 JSON 字段）。</summary>
    internal static class PluginSecret
    {
        public static string Resolve(string keyEnv, string keyFile, string keyField = null)
        {
            if (!string.IsNullOrWhiteSpace(keyEnv) &&
                Environment.GetEnvironmentVariable(keyEnv) is { Length: > 0 } fromEnv)
                return fromEnv;

            if (string.IsNullOrWhiteSpace(keyFile)) return "";
            try
            {
                string path = ExpandPath(keyFile);
                if (!File.Exists(path)) return "";
                string text = File.ReadAllText(path).Trim();
                if (string.IsNullOrWhiteSpace(keyField) || text.Length == 0 || text[0] != '{') return text;

                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.TryGetProperty(keyField, out var value) &&
                       value.ValueKind == JsonValueKind.String
                    ? value.GetString()?.Trim() ?? ""
                    : "";
            }
            catch
            {
                return "";
            }
        }

        public static string ExpandPath(string path)
        {
            string expanded = Environment.ExpandEnvironmentVariables(path ?? "");
            if (expanded.StartsWith("~"))
                expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    expanded.TrimStart('~', '/', '\\'));
            return expanded;
        }
    }

    /// <summary>
    /// 权限只是声明，宿主不做沙箱：插件就是本机进程，能做什么取决于操作系统。
    /// 留这个字段是为了让清单能说清意图，别把它当保证。
    /// </summary>
    public sealed class PluginPermissions
    {
        public bool Network { get; set; }
        public bool Shell { get; set; }
        public string Workspace { get; set; } = "read";
    }
}
