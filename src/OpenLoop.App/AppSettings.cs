using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenLoop.App
{
    /// <summary>
    /// 应用设置，存放在 %APPDATA%\OpenLoop\settings.json。
    /// 密钥用当前用户 DPAPI 保护后保存，绝不写进工作目录。
    /// </summary>
    internal sealed class AppSettings
    {
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
        public string Model { get; set; } = "openrouter/free";
        public string KeyProtected { get; set; } = "";
        public string Workspace { get; set; } = "";

        /// <summary>已使用的项目（工作区路径），用于启动时恢复项目列表。</summary>
        public List<string> Projects { get; set; } = [];

        /// <summary>配色：light / dark / grayblue。</summary>
        public string Theme { get; set; } = "light";

        /// <summary>界面语言：zh / en（卫星程序集）。</summary>
        public string Language { get; set; } = "zh";

        /// <summary>模型下拉默认只列免费模型。</summary>
        public bool OnlyFreeModels { get; set; } = true;

        /// <summary>推理强度：low / medium / high，空表示交给模型默认。</summary>
        public string ReasoningEffort { get; set; } = "";

        /// <summary>非空时，Loop 的构思交给这个外部进程（argv 数组，stdin 收提示词）。</summary>
        public List<string> LoopCommand { get; set; } = [];

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop");

        private static string FilePath => Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
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
