using System.Net.Http.Json;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 访问 I 的 /api/loop/*。地址优先取 runtime/i.json，其次默认 19181；
    /// I 换端口（重启后顺延）时自动重新发现一次。
    /// </summary>
    public sealed class LoopApiClient : IDisposable
    {
        private readonly string _key;
        private HttpClient _http;

        public LoopApiClient(string endpoint = null, string key = null)
        {
            _key = key ?? KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? Discover() : endpoint;
            _http = NewClient(Endpoint);
        }

        public string Endpoint { get; private set; }

        public static string Discover()
        {
            var info = RuntimeInfo.TryLoad(AppPaths.RuntimeFile(BusRoles.I));
            return info is { IsAlive: true } && !string.IsNullOrWhiteSpace(info.Bus)
                ? info.Bus
                : "127.0.0.1:19181";
        }

        public async Task<LoopEvolveState> EvolveAsync(string workspace, string model, string effort,
            CancellationToken ct = default)
            => await PostAsync<LoopEvolveState>("/api/loop/evolve", new { workspace, model, effort }, ct);

        public async Task<LoopEvolveState> EvolveStateAsync(string workspace, CancellationToken ct = default)
            => await SendAsync<LoopEvolveState>(HttpMethod.Get,
                "/api/loop/evolve?workspace=" + Uri.EscapeDataString(workspace ?? ""), null, ct);

        public async Task<bool> StopEvolveAsync(string workspace, CancellationToken ct = default)
            => (await PostAsync<StopResult>("/api/loop/evolve/stop", new { workspace }, ct))?.Stopped == true;

        public async Task<int?> RollbackAsync(string workspace, int? version = null, CancellationToken ct = default)
            => (await PostAsync<RollbackResult>("/api/loop/rollback", new { workspace, version }, ct))?.Version;

        public async Task<string> AddRuleAsync(string workspace, string name, string text, CancellationToken ct = default)
            => (await PostAsync<RuleResult>("/api/loop/rule", new { workspace, name, text }, ct))?.Id;

        /// <summary>工具/权限策略与技能要用的账本投影；A 每轮运行前拉一次。</summary>
        public async Task<ActivationPayload> GetActivationAsync(string workspace, CancellationToken ct = default)
            => await SendAsync<ActivationPayload>(HttpMethod.Get,
                "/api/loop/activation?workspace=" + Uri.EscapeDataString(workspace ?? ""), null, ct);

        public async Task SetSkillAsync(string workspace, string name, bool enabled, CancellationToken ct = default)
            => await PostAsync<OkResult>("/api/loop/skills", new { workspace, name, enabled }, ct);

        /// <summary>把 A 投在 spool 里的运行记录并进账本。</summary>
        public async Task<int> DrainSpoolAsync(string workspace, CancellationToken ct = default)
            => (await PostAsync<DrainResult>("/api/loop/spool/drain", new { workspace }, ct))?.Drained ?? 0;

        /// <summary>给界面里那些同步调用的地方用（设置页、loop 面板）。</summary>
        public ActivationPayload GetActivation(string workspace)
            => GetActivationAsync(workspace).GetAwaiter().GetResult();

        public void SetSkill(string workspace, string name, bool enabled)
            => SetSkillAsync(workspace, name, enabled).GetAwaiter().GetResult();

        private Task<T> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
            => SendAsync<T>(HttpMethod.Post, path, body, ct);

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
            where T : class
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(method, path);
                    if (body != null) request.Content = JsonContent.Create(body, options: BusProtocol.Json);
                    using var response = await _http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        string detail = await response.Content.ReadAsStringAsync(ct);
                        throw new InvalidOperationException(
                            detail.Length > 0 && detail != "not found"
                                ? detail
                                : $"Loop 请求失败（{(int)response.StatusCode}）");
                    }
                    return await response.Content.ReadFromJsonAsync<T>(BusProtocol.Json, ct);
                }
                catch (HttpRequestException) when (attempt == 0 && Rediscover())
                {
                    // I 重启后可能换了端口：换到新地址再试一次
                }
            }
        }

        private bool Rediscover()
        {
            string next = Discover();
            if (string.Equals(next, Endpoint, StringComparison.OrdinalIgnoreCase)) return false;
            Endpoint = next;
            var old = _http;
            _http = NewClient(next);
            old.Dispose();
            return true;
        }

        private HttpClient NewClient(string endpoint)
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri("http://" + endpoint),
                Timeout = TimeSpan.FromSeconds(30)
            };
            if (!string.IsNullOrEmpty(_key))
                http.DefaultRequestHeaders.TryAddWithoutValidation(BusProtocol.KeyHeader, _key);
            return http;
        }

        public void Dispose() => _http.Dispose();

        private sealed class StopResult
        {
            public bool Stopped { get; set; }
        }

        private sealed class RollbackResult
        {
            public int? Version { get; set; }
        }

        private sealed class RuleResult
        {
            public string Id { get; set; }
        }

        private sealed class OkResult
        {
        }

        private sealed class DrainResult
        {
            public int Drained { get; set; }
        }
    }
}
