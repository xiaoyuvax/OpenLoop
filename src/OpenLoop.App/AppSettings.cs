using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLoop.App
{
    /// <summary>
    /// 应用设置，存放在 %APPDATA%\OpenLoop\settings.json。
    /// 密钥用当前用户 DPAPI 保护后保存，绝不写进工作目录。
    /// </summary>
    internal sealed class ProviderConfig
    {
        public string Name { get; set; } = "OpenRouter";
        public string Kind { get; set; } = "openrouter";
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
        public string KeyProtected { get; set; } = "";
        public List<string> Models { get; set; } = [];

        /// <summary>内置支持的提供商可一键拉取模型；自定义提供商只手动添加。</summary>
        [JsonIgnore]
        public bool AutoModels => !string.Equals(Kind, "custom", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class AppSettings
    {
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
        public string Model { get; set; } = "openrouter/free";
        public string KeyProtected { get; set; } = "";
        public string Workspace { get; set; } = "";

        /// <summary>多提供商配置；旧的单 BaseUrl/KeyProtected 仍作为回退。</summary>
        public List<ProviderConfig> Providers { get; set; } = [];

        /// <summary>已使用的项目（工作区路径），用于启动时恢复项目列表。</summary>
        public List<string> Projects { get; set; } = [];

        /// <summary>配色：light / dark / grayblue。</summary>
        public string Theme { get; set; } = "light";

        /// <summary>界面语言：zh / en（卫星程序集）。</summary>
        public string Language { get; set; } = "";

        /// <summary>模型下拉默认只列免费模型。</summary>
        public bool OnlyFreeModels { get; set; } = true;

        /// <summary>新回话的「思考与工具过程」默认展开。</summary>
        public bool ThinkingExpandedByDefault { get; set; }

        /// <summary>完全许可：关闭路径边界、命令审批与环境隔离（仅交互 Worker）。</summary>
        public bool FullAccess { get; set; }

        /// <summary>是否把 AGENTS.md（全局 + 项目）注入系统提示词。</summary>
        public bool UseAgentsMd { get; set; } = true;

        /// <summary>推理强度：low / medium / high，空表示交给模型默认。</summary>
        public string ReasoningEffort { get; set; } = "";

        /// <summary>非空时，Loop 的构思交给这个外部进程（argv 数组，stdin 收提示词）。</summary>
        public List<string> LoopCommand { get; set; } = [];

        /// <summary>上次窗口位置/尺寸（多屏下用于重启后回到同一屏幕）。</summary>
        public int WindowX { get; set; } = int.MinValue;
        public int WindowY { get; set; } = int.MinValue;
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop");

        private static string FilePath => Path.Combine(Dir, "settings.json");

        /// <summary>用户级全局约定：%APPDATA%\OpenLoop\AGENTS.md。</summary>
        public static string GlobalAgentsPath => Path.Combine(Dir, "AGENTS.md");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return Migrate(new AppSettings());
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
                return Migrate(settings);
            }
            catch
            {
                return new AppSettings();
            }
        }

        private static AppSettings Migrate(AppSettings settings)
        {
            settings.Providers ??= [];
            foreach (var provider in settings.Providers) provider.Models ??= [];

            if (settings.Providers.Count == 0 && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                var preset = ProviderPresets.Match(settings.BaseUrl);
                settings.Providers.Add(new ProviderConfig
                {
                    Name = preset?.Name ?? "默认",
                    Kind = preset?.Kind ?? "custom",
                    BaseUrl = settings.BaseUrl,
                    KeyProtected = settings.KeyProtected,
                    Models = string.IsNullOrWhiteSpace(settings.Model) ? [] : [settings.Model]
                });
            }
            return settings;
        }

        /// <summary>找到包含该模型的提供商。</summary>
        public ProviderConfig ProviderFor(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            return Providers.FirstOrDefault(p =>
                (p.Models ?? []).Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>该模型应使用的接口地址与密钥（找不到提供商时回退旧字段）。</summary>
        public (string BaseUrl, string Key) EndpointFor(string model)
        {
            var provider = ProviderFor(model)
                ?? Providers.FirstOrDefault(p =>
                    string.Equals(p.Kind, "openrouter", StringComparison.OrdinalIgnoreCase));
            if (provider == null) return (BaseUrl, ResolveKey(this));
            return (provider.BaseUrl, ResolveKey(provider));
        }

        public static string ResolveKey(ProviderConfig provider)
        {
            string key = Unprotect(provider?.KeyProtected);
            if (!string.IsNullOrWhiteSpace(key)) return key;
            return (provider?.Kind ?? "").Trim().ToLowerInvariant() switch
            {
                "openrouter" => Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "",
                "openai" => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
                "teamorouter" => Environment.GetEnvironmentVariable("TEAMOROUTER_API_KEY")
                    ?? Environment.GetEnvironmentVariable("TEMOROUTER_API_KEY") ?? "",
                _ => ""
            };
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }

        /// <summary>界面用的密钥：优先 DPAPI 存的那份，其次环境变量。</summary>
        public static string ResolveKey(AppSettings settings)
        {
            string key = Unprotect(settings?.KeyProtected);
            if (!string.IsNullOrWhiteSpace(key)) return key;
            return Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? "";
        }

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] bytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                return "";
            }
        }

        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return "";
            try
            {
                byte[] bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
