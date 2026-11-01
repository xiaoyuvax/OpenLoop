using System.Net.Http.Json;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Bus
{
    /// <summary>访问另一个进程的 /bus/*。loopback，超时 5 秒，不做重试。</summary>
    public sealed class BusClient : IDisposable
    {
        private readonly HttpClient _http;

        public BusClient(string busEndpoint, string key = null)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri("http://" + busEndpoint),
                Timeout = TimeSpan.FromSeconds(5)
            };
            string actualKey = key ?? KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
            if (!string.IsNullOrEmpty(actualKey))
                _http.DefaultRequestHeaders.TryAddWithoutValidation(BusProtocol.KeyHeader, actualKey);
        }

        public async Task<RuntimeInfo> HealthAsync(CancellationToken ct = default)
            => await _http.GetFromJsonAsync<RuntimeInfo>("/bus/health", BusProtocol.Json, ct);

        public async Task<BusStatus> StatusAsync(CancellationToken ct = default)
            => await _http.GetFromJsonAsync<BusStatus>("/bus/status", BusProtocol.Json, ct);

        public async Task<RuntimeInfo> HelloAsync(RuntimeInfo self, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("/bus/hello", self, BusProtocol.Json, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RuntimeInfo>(BusProtocol.Json, ct);
        }

        public async Task<bool> ShutdownAsync(string reason = "", CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("/bus/shutdown", new { reason }, BusProtocol.Json, ct);
            return (int)response.StatusCode is >= 200 and < 300;
        }

        public void Dispose() => _http.Dispose();
    }
}
