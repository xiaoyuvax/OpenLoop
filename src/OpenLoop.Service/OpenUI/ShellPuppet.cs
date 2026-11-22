using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Web;

namespace OpenLoop.Service.OpenUI
{
    /// <summary>
    /// 往壳发一个 Puppet A 面调用（<c>POST /agent/invoke?name=..&amp;method=..</c>）。
    ///
    /// 两条路：壳进程内（Loop 会话）直调 <see cref="PuppetBridge.LocalHub"/>，不经端口；
    /// A 进程里的 Worker 会话走 HTTP 到壳的 Puppet 端口。结果统一成「失败抛、成功给 result」。
    /// </summary>
    public static class ShellPuppet
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

        public static async Task<string> InvokeAsync(string instance, string method, object[] args, CancellationToken ct)
        {
            string body = JsonSerializer.Serialize(args ?? []);

            if (PuppetBridge.SelfRole == "shell" && PuppetBridge.LocalHub is { } hub)
            {
                var response = await hub.CallAsync("POST", "/agent/invoke",
                    new Dictionary<string, object> { ["name"] = instance, ["method"] = method },
                    args ?? []);
                return Unwrap(response.Status, response.Body);
            }

            string endpoint = PuppetBridge.ShellEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("桌面壳未运行（找不到 %APPDATA%\\OpenLoop\\puppet.endpoint）");
            string key = PuppetBridge.Key;
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("读不到 Puppet key 文件");

            string url = (endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? endpoint : "http://" + endpoint)
                + "/agent/invoke?name=" + Uri.EscapeDataString(instance)
                + "&method=" + Uri.EscapeDataString(method);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);

            using var http = await Http.SendAsync(request, ct);
            string text = await http.Content.ReadAsStringAsync(ct);
            if (!http.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)http.StatusCode}：{Clip(text)}");
            return Unwrap((int)http.StatusCode, text);
        }

        /// <summary>A 面返回 <c>{ok, result, err}</c>：失败抛，成功取 result。</summary>
        private static string Unwrap(int status, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                return status is 200 or 204 ? Clip(body) : throw new InvalidOperationException(Clip(body));
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return Clip(body);
                if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                {
                    string err = root.TryGetProperty("err", out var e) && e.ValueKind == JsonValueKind.String
                        ? e.GetString()
                        : "调用失败";
                    throw new InvalidOperationException(err);
                }
                if (root.TryGetProperty("result", out var result))
                {
                    return result.ValueKind == JsonValueKind.String
                        ? Clip(result.GetString())
                        : Clip(result.GetRawText());
                }
                return Clip(body);
            }
        }

        private static string Clip(string text)
            => string.IsNullOrEmpty(text) ? "" : text.Length <= 4000 ? text : text[..4000] + "…";
    }
}
