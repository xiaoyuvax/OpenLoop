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
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) ||
                    string.IsNullOrWhiteSpace(manifest.Entry?.Command))
                    return null;

                manifest.Root = Path.GetFullPath(dir);
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
