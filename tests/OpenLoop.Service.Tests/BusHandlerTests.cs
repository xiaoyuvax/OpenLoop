using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class BusHandlerTests
    {
        private const string Key = "test-key";

        private static RuntimeInfo Self() => new()
        {
            Role = BusRoles.A,
            Pid = Environment.ProcessId,
            Version = "0.1.0",
            Protocol = BusProtocol.Version,
            Bus = "127.0.0.1:19180",
            StartedAt = DateTimeOffset.Now.ToString("O")
        };

        private static Wima.Web.WebRequest Request(string method, string path, string key, string body = null)
        {
            var req = new Wima.Web.WebRequest
            {
                Method = method,
                InputStream = new MemoryStream(body == null ? Array.Empty<byte>() : System.Text.Encoding.UTF8.GetBytes(body))
            };
            req.Path = path;
            if (key != null) req.Headers[BusProtocol.KeyHeader] = key;
            req.Response = new Wima.Web.WebResponse();
            return req;
        }

        [Fact]
        public void Non_bus_path_is_not_handled()
        {
            var req = Request("GET", "/api/sessions", Key);
            var handled = new BusHandler(Self, () => null, Key, null).Handle(req);

            Assert.False(handled);
            Assert.Null(req.Response.Buffer);
        }

        [Fact]
        public void Wrong_key_returns_404()
        {
            var req = Request("GET", "/bus/health", "wrong");
            var handled = new BusHandler(Self, () => null, Key, null).Handle(req);

            Assert.True(handled);
            Assert.Equal(404, req.Response.StatusCode);
        }

        [Fact]
        public void Health_returns_self_without_explicit_status()
        {
            var req = Request("GET", "/bus/health", Key);
            var handled = new BusHandler(Self, () => null, Key, null).Handle(req);

            Assert.True(handled);
            Assert.Equal(0, req.Response.StatusCode);
            var json = System.Text.Encoding.UTF8.GetString(req.Response.Buffer);
            Assert.Contains("\"role\":\"a\"", json);
            Assert.Contains("\"protocol\":1", json);
        }

        [Fact]
        public void Shutdown_endpoint_invokes_callback()
        {
            bool called = false;
            var req = Request("POST", "/bus/shutdown", Key, "{}");
            var handled = new BusHandler(Self, () => null, Key, () => called = true).Handle(req);

            Assert.True(handled);
            Assert.True(called);
            Assert.Equal(202, req.Response.StatusCode);
        }

        [Fact]
        public void Unknown_bus_path_returns_404()
        {
            var req = Request("GET", "/bus/nope", Key);
            new BusHandler(Self, () => null, Key, null).Handle(req);

            Assert.Equal(404, req.Response.StatusCode);
        }
    }
}
