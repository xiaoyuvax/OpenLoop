using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// A 把 <c>/api/loop/*</c> 原样转给 I：浏览器/界面只认一个端口、一套鉴权。
    /// I 没起时返回 503，不假装成功。只在 A 挂这个处理器；I 自己实现这些路径。
    /// </summary>
    public sealed class LoopProxyHandler : IDisposable
    {
        private readonly string _key;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

        public LoopProxyHandler(string key) => _key = key ?? "";

        public bool Handle(WebRequest req)
        {
            string path = req?.Path ?? "";
            if (req == null || !path.StartsWith("/api/loop", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!BusAuth.IsAuthorized(req, _key, allowCookie: true))
            {
                req.Response.StatusCode = 404;
                req.Response.Buffer = Encoding.UTF8.GetBytes("not found");
                return true;
            }

            string endpoint = "http://" + LoopApiClient.Discover();
            try
            {
                using var message = new HttpRequestMessage(new HttpMethod(req.Method ?? "GET"), endpoint + path + Query(req));
                message.Headers.TryAddWithoutValidation(BusProtocol.KeyHeader, _key);
                if (req.InputStream != null && req.InputStream.CanRead &&
                    req.ContentLength is > 0)
                    message.Content = new StreamContent(req.InputStream);

                using var response = _http.SendAsync(message).GetAwaiter().GetResult();
                req.Response.StatusCode = (int)response.StatusCode;
                string contentType = response.Content.Headers.ContentType?.ToString();
                if (!string.IsNullOrEmpty(contentType)) req.Response.Headers.Set("Content-Type", contentType);
                req.Response.Buffer = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                req.Response.StatusCode = 503;
                req.Response.Headers.Set("Content-Type", "application/json; charset=utf-8");
                req.Response.Buffer = JsonSerializer.SerializeToUtf8Bytes(
                    new { ok = false, error = "Loop 未就绪：" + ex.Message }, BusProtocol.Json);
            }
            return true;
        }

        private static string Query(WebRequest req)
        {
            var queries = req.Queries;
            if (queries == null || queries.Count == 0) return "";
            var pairs = new List<string>();
            foreach (string key in queries)
                pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(queries[key] ?? ""));
            return "?" + string.Join("&", pairs);
        }

        public void Dispose() => _http.Dispose();
    }
}
