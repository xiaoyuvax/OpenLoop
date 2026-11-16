using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>浏览器设置页依赖的 API 形状：鉴权、路由、WebFetch 模式的取值。</summary>
    public class SettingsApiHandlerTests
    {
        private const string Key = "test-key";

        private static WebRequest Call(string method, string path, bool authorized = true, string body = null)
        {
            var req = new WebRequest
            {
                Method = method,
                InputStream = new MemoryStream(body == null ? [] : Encoding.UTF8.GetBytes(body))
            };
            req.Path = path;
            req.Response = new WebResponse();
            if (authorized) req.Headers[BusProtocol.KeyHeader] = Key;
            new SettingsApiHandler(Key).Handle(req);
            return req;
        }

        [Fact]
        public void Rejects_requests_without_the_bus_key()
        {
            var req = Call("GET", "/api/settings", authorized: false);
            Assert.Equal(404, req.Response.StatusCode);
        }

        [Fact]
        public void View_reports_the_web_fetch_mode()
        {
            var req = Call("GET", "/api/settings");
            Assert.True(req.Response.Buffer.Length > 0);
            Assert.NotEqual(404, req.Response.StatusCode);

            using var doc = JsonDocument.Parse(req.Response.Buffer);
            string mode = doc.RootElement.GetProperty("webFetchMode").GetString();
            Assert.Contains(mode, new[] { "off", "ask", "allow" });
            Assert.True(doc.RootElement.TryGetProperty("providers", out _));
        }

        [Fact]
        public void Invalid_body_is_a_400_and_does_not_save()
        {
            var req = Call("POST", "/api/settings", body: "{not json");
            Assert.Equal(400, req.Response.StatusCode);
        }

        [Fact]
        public void Unknown_sub_path_is_404()
        {
            var req = Call("GET", "/api/settings/nope");
            Assert.Equal(404, req.Response.StatusCode);
        }

        [Fact]
        public void Other_api_paths_are_left_to_other_handlers()
        {
            var req = new WebRequest { Method = "GET", InputStream = new MemoryStream() };
            req.Path = "/api/sessions";
            req.Headers[BusProtocol.KeyHeader] = Key;
            req.Response = new WebResponse();
            Assert.False(new SettingsApiHandler(Key).Handle(req));
        }
    }
}
