using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Plugins
{
    /// <summary>
    /// 服务型插件：宿主按清单拉起一个本地子进程，从它的 stdout 里认出地址（例如
    /// <c>API：http://127.0.0.1:18900/v1</c>），provider 就能把 <c>baseUrl</c> 写成 <c>auto</c>。
    /// 同一个插件在进程内只起一份，随宿主退出一起收摊。
    /// </summary>
    internal static class LocalServices
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Running> Started = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Starting = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Warming = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTimeOffset> RetryAfter = new(StringComparer.OrdinalIgnoreCase);

        static LocalServices()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();
        }

        /// <summary>拿到服务基地址（含协议与端口，不含尾部斜杠）；起不来或超时返回 null。</summary>
        public static string BaseUrl(string pluginId, PluginServiceContribution spec, string cwd, string root = null)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Command)) return null;

            lock (Gate)
            {
                if (Started.TryGetValue(pluginId, out var running))
                {
                    if (running.Process != null && !running.Process.HasExited) return running.BaseUrl;
                    running.Dispose();
                    Started.Remove(pluginId);
                }

                // 冷却期内别再拉子进程：插件自己的 service.lock 没释放时会立刻失败，
                // 直接调用方（提供商/模型刷新）会被反复触发，日志一秒一条。
                if (RetryAfter.TryGetValue(pluginId, out var retryAt))
                {
                    if (DateTimeOffset.Now < retryAt) return Shared(pluginId);
                    RetryAfter.Remove(pluginId);
                }

                // 已经有人在起这份服务：不重复拉，也不在这里等
                if (!Starting.Add(pluginId)) return Shared(pluginId);
            }

            // 起进程（可能等 stdout 握手）不能占着 Gate：Known() 要能立刻回答，
            // 否则界面刷新会跟着等满一个 timeoutMs。
            Running started = null;
            try
            {
                started = Start(pluginId, spec, cwd, root);
            }
            finally
            {
                lock (Gate)
                {
                    Starting.Remove(pluginId);
                    if (started == null)
                    {
                        RetryAfter[pluginId] = DateTimeOffset.Now.AddMinutes(1);
                    }
                    else
                    {
                        RetryAfter.Remove(pluginId);
                        Started[pluginId] = started;
                        Save(pluginId, started.BaseUrl, started.Process.Id);
                    }
                }
            }

            // 起不来常见原因是「已有别的进程把它起好了」（插件自己的 service.lock）。
            // 宿主这边留了 runtime 握手文件，照着用就行。
            return started?.BaseUrl ?? Shared(pluginId);
        }

        /// <summary>还有服务在后台预热（界面据此决定要不要过一会儿重填下拉）。</summary>
        public static bool AnyStarting
        {
            get
            {
                lock (Gate) return Starting.Count > 0 || Warming.Count > 0;
            }
        }

        /// <summary>
        /// 只问「现在能不能用」：本进程起的，或者 runtime 握手文件里还活着的。**绝不拉起子进程**——
        /// 界面刷新走这条，否则会在 UI 线程上等 stdout 握手（最长一个 timeoutMs），整个界面僵住。
        /// </summary>
        public static string Known(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId)) return null;
            lock (Gate)
            {
                if (Started.TryGetValue(pluginId, out var running))
                {
                    if (running.Process != null && !running.Process.HasExited) return running.BaseUrl;
                    running.Dispose();
                    Started.Remove(pluginId);
                }
                return Shared(pluginId);
            }
        }

        /// <summary>在后台把服务起起来（同一插件只起一份，失败 1 分钟内不重试）；调用方立刻返回。</summary>
        public static void Warm(string pluginId, PluginServiceContribution spec, string cwd, string root = null)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Command) || string.IsNullOrWhiteSpace(pluginId))
                return;

            lock (Gate)
            {
                if (Warming.Contains(pluginId)) return;
                if (Starting.Contains(pluginId)) return;
                if (Started.TryGetValue(pluginId, out var running) && running.Process is { HasExited: false }) return;
                if (RetryAfter.TryGetValue(pluginId, out var at) && DateTimeOffset.Now < at) return;
                Warming.Add(pluginId);
            }

            _ = Task.Run(() =>
            {
                try
                {
                    BaseUrl(pluginId, spec, cwd, root);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"服务型插件 {pluginId} 后台启动失败：{ex.Message}");
                }
                finally
                {
                    lock (Gate) Warming.Remove(pluginId);
                }
            });
        }

        private static string HandshakePath(string pluginId) =>
            Path.Combine(AppPaths.RuntimeDir, "plugin-" + pluginId + ".json");

        private static void Save(string pluginId, string url, int pid)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.RuntimeDir);
                File.WriteAllText(HandshakePath(pluginId),
                    JsonSerializer.Serialize(new { url, pid }), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static string Shared(string pluginId)
        {
            try
            {
                string path = HandshakePath(pluginId);
                if (!File.Exists(path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("url", out var url) ||
                    url.ValueKind != JsonValueKind.String) return null;
                string value = url.GetString();
                if (string.IsNullOrWhiteSpace(value)) return null;

                int pid = doc.RootElement.TryGetProperty("pid", out var id) && id.ValueKind == JsonValueKind.Number
                    ? id.GetInt32() : 0;
                if (pid > 0)
                {
                    try { using var _ = Process.GetProcessById(pid); }
                    catch
                    {
                        // 握手文件指向的进程早没了：清掉，别让下一位继续拿它当「已有实例」
                        try { File.Delete(path); } catch { }
                        return null;
                    }
                }

                AppLog.Info($"服务型插件 {pluginId} 复用已在跑的实例：{value}");
                return value;
            }
            catch
            {
                return null;
            }
        }

        private static Running Start(string pluginId, PluginServiceContribution spec, string cwd, string root)
        {
            string Expand(string value)
                => string.IsNullOrEmpty(root) || string.IsNullOrEmpty(value)
                    ? value
                    : value.Replace("{root}", root, StringComparison.OrdinalIgnoreCase);

            Process process = null;
            try
            {
                var startInfo = new ProcessStartInfo(Expand(spec.Command))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // 不指定就按控制台代码页解码：UTF-8 的中文会把紧跟的 ASCII 一起吃掉（"http" 变 "ttp"）
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                foreach (string arg in spec.Args ?? []) startInfo.ArgumentList.Add(Expand(arg));
                foreach (var pair in spec.Env ?? []) startInfo.Environment[pair.Key] = pair.Value;
                if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
                    startInfo.WorkingDirectory = cwd;

                process = Process.Start(startInfo);
                if (process == null)
                {
                    AppLog.Warn($"服务型插件 {pluginId} 起不来：{spec.Command}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"服务型插件 {pluginId} 起不来：{ex.Message}");
                return null;
            }

            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Regex pattern = null;
            try { pattern = new Regex(spec.UrlPattern ?? @"(https?://[^\s""']+)", RegexOptions.Compiled); }
            catch (Exception ex) { AppLog.Warn($"服务型插件 {pluginId} 的 urlPattern 无效：{ex.Message}"); }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (pattern == null) return;
                var match = pattern.Match(e.Data);
                if (!match.Success) return;
                string url = (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).TrimEnd('/');
                // 只留协议+主机+端口：清单里的 /v1、/v1/models 由 provider 自己拼
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    url = uri.GetLeftPart(UriPartial.Authority);
                if (url.Length > 0) ready.TrySetResult(url);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) AppLog.Info($"[{pluginId}] {e.Data}");
            };
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => ready.TrySetResult(null);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            string baseUrl = null;
            try
            {
                int timeout = spec.TimeoutMs > 0 ? spec.TimeoutMs : 15000;
                baseUrl = ready.Task.Wait(timeout) ? ready.Task.Result : null;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"服务型插件 {pluginId} 等地址失败：{ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AppLog.Warn($"服务型插件 {pluginId} 没在约定时间里打印地址，已停掉");
                try { process.Kill(true); } catch { }
                try { process.Dispose(); } catch { }
                return null;
            }

            AppLog.Info($"服务型插件 {pluginId} 就绪：{baseUrl}");
            return new Running(process, baseUrl);
        }

        private static void StopAll()
        {
            lock (Gate)
            {
                foreach (var running in Started.Values) running.Dispose();
                Started.Clear();
            }
        }

        private sealed class Running(Process process, string baseUrl) : IDisposable
        {
            public Process Process { get; } = process;
            public string BaseUrl { get; } = baseUrl;

            public void Dispose()
            {
                try
                {
                    if (Process != null && !Process.HasExited) Process.Kill(true);
                }
                catch
                {
                }
                try { Process?.Dispose(); } catch { }
            }
        }
    }
}
