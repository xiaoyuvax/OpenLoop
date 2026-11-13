using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenLoop.Core.Reasoning;
using OpenLoop.Service.Logging;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// 免费模型额度耗尽（provider.free-limit / 429 / 402）后让 ConnMan 换节点：
    /// 调它的 Puppet A 面动作 StartRecoveryAction（ConnMan 依次切换节点并真发一次免费模型请求验证），
    /// 期间轮询 RecoveryState，ready 后由这里给原会话发「继续」；exhausted 表示 ConnMan 本轮节点已试完，收工。
    /// ConnMan 没在运行或连不上都继续重试（它可能在更新/重启）：前 3 次间隔 5 秒，之后指数退避。
    /// 只有用户又动了这个会话（发消息、停、换模型…）才停。
    /// </summary>
    internal static class FreeLimitRecovery
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(10);

        private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly Dictionary<string, CancellationTokenSource> Loops = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();

        private enum WaitResult { Ready, Retry, Cancelled, Exhausted }

        /// <summary>
        /// 失败轮结束后调用。userEpoch 读会话的用户动作计数（会话已不存在返回 null）；
        /// 用户动过会话就停；sendContinue 在节点可用时发一条「继续」。
        /// </summary>
        public static void TryStart(string sessionId, string model, Func<int?> userEpoch, Func<string, bool> sendContinue)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(model)) return;
            // 只修 opencode 的免费额度；付费模型/别的后端不归 ConnMan 管
            if (!model.StartsWith("opencode/", StringComparison.OrdinalIgnoreCase)) return;

            var cts = new CancellationTokenSource();
            lock (Gate)
            {
                // 同一会话又失败一次（用户发过新消息后）：顶掉旧的重试
                if (Loops.Remove(sessionId, out var old)) old.Cancel();
                Loops[sessionId] = cts;
            }

            _ = Task.Run(() => RunAsync(sessionId, model, userEpoch, sendContinue, cts));
        }

        private static async Task RunAsync(string sessionId, string model, Func<int?> userEpoch,
            Func<string, bool> sendContinue, CancellationTokenSource cts)
        {
            try
            {
                int? baseline = userEpoch();
                if (baseline == null) return;

                for (int attempt = 0; ; attempt++)
                {
                    string endpoint = ReadEndpoint();
                    if (endpoint == null)
                    {
                        AppLog.Info("免费额度恢复：ConnMan 没在运行（缺 puppet.endpoint），稍后重试");
                    }
                    else
                    {
                        AppLog.Info($"免费额度恢复：请 ConnMan 换节点（{model}，会话 {sessionId}，第 {attempt + 1} 次）");
                        if (await InvokeAsync(endpoint, "StartRecoveryAction", [sessionId, model]))
                        {
                            var waited = await WaitAsync(endpoint, userEpoch, baseline, cts.Token);
                            if (waited == WaitResult.Cancelled) return;
                            if (waited == WaitResult.Exhausted)
                            {
                                AppLog.Info("免费额度恢复：ConnMan 本轮节点都试过了（未验证），不再重试");
                                return;
                            }
                            if (waited == WaitResult.Ready)
                            {
                                bool sent = sendContinue("继续");
                                AppLog.Info(sent
                                    ? "免费额度恢复：节点已就绪，已发「继续」"
                                    : "免费额度恢复：节点已就绪，但会话已不存在，没发「继续」");
                                return;
                            }
                        }
                    }

                    if (!await DelayAsync(RetryBackoff.Next(attempt), userEpoch, baseline, cts.Token)) return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warn("免费额度恢复异常：" + ex.Message);
            }
            finally
            {
                lock (Gate)
                {
                    if (Loops.TryGetValue(sessionId, out var current) && current == cts) Loops.Remove(sessionId);
                }
                cts.Dispose();
            }
        }

        /// <summary>等 ConnMan 报 ready；用户动过会话/被顶替返回 Cancelled，失败/超时/又连不上返回 Retry，
        /// 本轮节点已试完（exhausted）返回 Exhausted 让调用方收工。</summary>
        private static async Task<WaitResult> WaitAsync(string endpoint, Func<int?> userEpoch, int? baseline,
            CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + WaitTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!await DelayAsync(PollInterval, userEpoch, baseline, ct)) return WaitResult.Cancelled;

                string state = await GetStateAsync(endpoint);
                if (state == null) return WaitResult.Retry;
                if (state == "ready") return WaitResult.Ready;
                if (state == "exhausted") return WaitResult.Exhausted;
                if (state is "failed" or "idle") return WaitResult.Retry;
            }

            AppLog.Warn("免费额度恢复：等 ConnMan 换节点超时（10 分钟），稍后重试");
            return WaitResult.Retry;
        }

        /// <summary>边等边看用户有没有动会话；false = 该收工了。</summary>
        private static async Task<bool> DelayAsync(TimeSpan delay, Func<int?> userEpoch, int? baseline,
            CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + delay;
            while (true)
            {
                if (ct.IsCancellationRequested || userEpoch() != baseline) return false;
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return true;
                await Task.Delay(left < PollInterval ? left : PollInterval, ct);
            }
        }

        private static async Task<bool> InvokeAsync(string endpoint, string method, object[] args)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"http://{endpoint}/agent/invoke?name=ConnMan&method={method}");
                AddKey(request);
                request.Content = JsonContent.Create(args);
                using var response = await Http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    AppLog.Warn($"免费额度恢复：ConnMan {method} 失败 {response.StatusCode} {body}");
                    return false;
                }

                AppLog.Info($"免费额度恢复：ConnMan {method} 已接受");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"免费额度恢复：ConnMan {method} 连不上（{ex.Message}），稍后重试");
                return false;
            }
        }

        private static async Task<string> GetStateAsync(string endpoint)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"http://{endpoint}/agent/get?name=ConnMan&path=RecoveryState");
                AddKey(request);
                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("value", out var value) ? value.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void AddKey(HttpRequestMessage request)
        {
            string key = ReadFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConnMan", "puppet.key"));
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        /// <summary>%APPDATA%\ConnMan\puppet.endpoint（ConnMan 启动时写，退出时清空）。</summary>
        private static string ReadEndpoint()
        {
            string endpoint = ReadFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConnMan", "puppet.endpoint"));
            if (string.IsNullOrWhiteSpace(endpoint)) return null;
            endpoint = endpoint.Trim();
            return endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? endpoint["http://".Length..].TrimEnd('/')
                : endpoint.TrimEnd('/');
        }

        private static string ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
