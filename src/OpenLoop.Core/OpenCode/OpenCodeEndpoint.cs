using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.OpenCode
{
    public enum OpenCodeStatus
    {
        Absent,
        Unreachable,
        Unauthorized,
        Incompatible,
        Ready
    }

    /// <summary>
    /// 本机 OpenCode v2 后台服务的发现与探活。凭据只留在内存里，不写日志、不进设置。
    /// </summary>
    public sealed class OpenCodeEndpoint
    {
        private static readonly HttpClient ProbeHttp = new(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public string Url { get; init; } = "";
        public string Password { get; init; } = "";
        public int Pid { get; init; }
        public string Version { get; init; } = "";
        public OpenCodeStatus Status { get; init; } = OpenCodeStatus.Absent;
        public string Detail { get; init; } = "";

        public bool IsReady => Status == OpenCodeStatus.Ready;

        public static string ServiceFile => Path.Combine(
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
            "opencode", "service.json");

        public OpenCodeEndpoint With(OpenCodeStatus status, string detail = null, string version = null, int pid = 0)
            => new()
            {
                Url = Url,
                Password = Password,
                Pid = pid == 0 ? Pid : pid,
                Version = version ?? Version,
                Status = status,
                Detail = detail ?? ""
            };

        public static OpenCodeEndpoint FromFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return new() { Status = OpenCodeStatus.Absent, Detail = "没有 service.json" };

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                var endpoint = new OpenCodeEndpoint
                {
                    Url = Str(root, "url"),
                    Password = Str(root, "password"),
                    Pid = Int(root, "pid"),
                    Version = Str(root, "version")
                };
                return endpoint.Url.Length == 0
                    ? endpoint.With(OpenCodeStatus.Unreachable, "service.json 里没有 url")
                    : endpoint;
            }
            catch (Exception ex)
            {
                return new() { Status = OpenCodeStatus.Unreachable, Detail = ex.Message };
            }
        }

        public static Task<OpenCodeEndpoint> DiscoverAsync(string path = null, CancellationToken ct = default)
            => DiscoverAsync(path, ProbeHttp, ct);

        internal static async Task<OpenCodeEndpoint> DiscoverAsync(string path, HttpClient http, CancellationToken ct)
        {
            var endpoint = FromFile(path ?? ServiceFile);
            return endpoint.Url.Length == 0 ? endpoint : await ProbeAsync(endpoint, http, ct);
        }

        public static Task<OpenCodeEndpoint> ProbeAsync(OpenCodeEndpoint endpoint, CancellationToken ct = default)
            => ProbeAsync(endpoint, ProbeHttp, ct);

        internal static async Task<OpenCodeEndpoint> ProbeAsync(OpenCodeEndpoint endpoint, HttpClient http, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url.TrimEnd('/') + "/api/info");
                request.Headers.Authorization = BasicAuth(endpoint.Password);
                using var response = await http.SendAsync(request, ct);

                if ((int)response.StatusCode == 401) return endpoint.With(OpenCodeStatus.Unauthorized, "密钥不匹配");
                if ((int)response.StatusCode == 404) return endpoint.With(OpenCodeStatus.Incompatible, "没有 /api/info（不是 v2 服务）");
                if (!response.IsSuccessStatusCode) return endpoint.With(OpenCodeStatus.Unreachable, "HTTP " + (int)response.StatusCode);

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                string version = Str(root, "version");
                int pid = Int(root, "pid");
                if (!version.StartsWith("2.", StringComparison.Ordinal))
                    return endpoint.With(OpenCodeStatus.Incompatible, "版本 " + (version.Length == 0 ? "未知" : version));
                if (endpoint.Pid != 0 && pid != 0 && pid != endpoint.Pid)
                    return endpoint.With(OpenCodeStatus.Unreachable, "pid 不匹配（service.json 已过期）");
                return endpoint.With(OpenCodeStatus.Ready, "", version, pid);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return endpoint.With(OpenCodeStatus.Unreachable, "探测超时");
            }
            catch (Exception ex)
            {
                return endpoint.With(OpenCodeStatus.Unreachable, ex.Message);
            }
        }

        public OpenCodeClient CreateClient() => new(Url, Password);

        internal static AuthenticationHeaderValue BasicAuth(string password)
            => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + (password ?? ""))));

        public override string ToString() => $"OpenCode {Status} {Url} v{Version}";

        internal static string Str(JsonElement node, string name)
            => node.ValueKind == JsonValueKind.Object
               && node.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        internal static int Int(JsonElement node, string name)
            => node.ValueKind == JsonValueKind.Object
               && node.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out int parsed)
                ? parsed
                : 0;
    }
}
