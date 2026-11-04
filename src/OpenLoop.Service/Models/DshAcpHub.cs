using System.Diagnostics;
using OpenLoop.Core.Acp;
using OpenLoop.Core.Reasoning;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Models
{
    /// <summary>
    /// dsh 的 ACP 通道：一个进程、按工作区缓存会话；模型 id 用 <c>dsh/&lt;provider&gt;/&lt;model&gt;</c>。
    /// 命令解析要绕开 npm 的 <c>.cmd</c> 垫片——CreateProcess 只会补 <c>.exe</c>。
    /// </summary>
    public static class DshAcpHub
    {
        private static readonly object Gate = new();
        private static DshAcpHost _host;
        private static string _key;
        private static readonly object ModelsLock = new();
        private static (DateTimeOffset At, List<string> Ids) _models;
        private static readonly TimeSpan ModelsTtl = TimeSpan.FromMinutes(5);

        public static bool IsDsh(string model) => DshAcpIds.IsDsh(model);

        public static IReasoner Reasoner(AppSettings settings, string model, string workspace)
        {
            var host = Host(settings);
            string cwd = string.IsNullOrWhiteSpace(workspace) ? settings.Workspace : workspace;
            return new DshAcpReasoner(host, model, cwd);
        }

        /// <summary>模型清单（探测一次，缓存 5 分钟）；拉不到就返回空，不抛。</summary>
        public static IReadOnlyList<string> Models(AppSettings settings)
        {
            lock (ModelsLock)
            {
                if (_models.Ids != null && DateTimeOffset.Now - _models.At < ModelsTtl)
                    return _models.Ids;
            }

            var ids = new List<string>();
            try
            {
                var host = Host(settings);
                string cwd = string.IsNullOrWhiteSpace(settings.Workspace)
                    ? Path.GetTempPath()
                    : settings.Workspace;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var models = host.ModelsAsync(cwd, cts.Token).GetAwaiter().GetResult();
                ids.AddRange(models.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                AppLog.Warn("dsh 模型清单拉取失败：" + ex.Message);
            }

            lock (ModelsLock) _models = (DateTimeOffset.Now, ids);
            SaveCache(ids);
            return ids;
        }

        private static string CachePath =>
            Path.Combine(OpenLoop.Service.Runtime.AppPaths.RuntimeDir, "dsh-models.json");

        private static void SaveCache(List<string> ids)
        {
            try
            {
                OpenLoop.Service.Runtime.AppPaths.EnsureDirectory(
                    OpenLoop.Service.Runtime.AppPaths.RuntimeDir);
                File.WriteAllText(CachePath,
                    System.Text.Json.JsonSerializer.Serialize(new { at = DateTimeOffset.Now, ids }),
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>界面用的同步读：只读缓存文件（拉清单要起 dsh，不能卡 UI）。</summary>
        public static IReadOnlyList<string> CachedModels()
        {
            try
            {
                if (!File.Exists(CachePath)) return [];
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(CachePath));
                if (!doc.RootElement.TryGetProperty("ids", out var ids) ||
                    ids.ValueKind != System.Text.Json.JsonValueKind.Array) return [];

                var list = new List<string>();
                foreach (var id in ids.EnumerateArray())
                    if (id.ValueKind == System.Text.Json.JsonValueKind.String)
                        list.Add(id.GetString());
                return list;
            }
            catch
            {
                return [];
            }
        }

        private static DshAcpHost Host(AppSettings settings)
        {
            string command = string.IsNullOrWhiteSpace(settings.DshCommand) ? "dsh" : settings.DshCommand.Trim();
            string profile = string.IsNullOrWhiteSpace(settings.DshProfile) ? "acp" : settings.DshProfile.Trim();
            string key = command + "|" + profile;

            lock (Gate)
            {
                if (_host != null && _key == key) return _host;
                _host?.Dispose();
                var (exe, prefix) = ResolveCommand(command);
                _host = new DshAcpHost(exe, prefix, profile, () => settings.FullAccess);
                _key = key;
                return _host;
            }
        }

        /// <summary>
        /// 把 <c>dsh</c> 解析成真正能 CreateProcess 的东西：.exe 直接跑；
        /// npm 的 .cmd/.bat 垫片交给 <c>cmd.exe /c &lt;shim&gt;</c>（CreateProcess 只补 .exe）。
        /// </summary>
        internal static (string Command, List<string> PrefixArgs) ResolveCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return (command, []);
            bool hasPath = command.Contains(Path.DirectorySeparatorChar) || command.Contains('/');

            if (hasPath)
            {
                bool shim = command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                            command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
                return shim ? ("cmd.exe", ["/c", command]) : (command, []);
            }

            bool windows = OperatingSystem.IsWindows();
            string[] extensions = windows ? [".exe", ".cmd", ".bat", ""] : [""];
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (string extension in extensions)
                {
                    string candidate = Path.Combine(dir.Trim('"'), command + extension);
                    if (!File.Exists(candidate)) continue;
                    if (extension is ".cmd" or ".bat") return ("cmd.exe", ["/c", candidate]);
                    return (candidate, []);
                }
            }
            return (command, []);
        }
    }
}
