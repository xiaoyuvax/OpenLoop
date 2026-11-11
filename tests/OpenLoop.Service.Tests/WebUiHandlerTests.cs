using System.Text;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>浏览器界面：静态文件 + ?key= 换 Cookie，/api/* 认 Cookie、/bus/* 不认。</summary>
    public class WebUiHandlerTests : IDisposable
    {
        private const string Key = "test-key";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "openloop-ui-" + Guid.NewGuid().ToString("N"));

        public WebUiHandlerTests()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "index.html"), "<html>ok</html>");
            File.WriteAllText(Path.Combine(_root, "app.js"), "console.log(1)");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private WebRequest Get(string path, string queryKey = null, string cookie = null, string header = null)
        {
            var req = new WebRequest { Method = "GET", InputStream = new MemoryStream() };
            req.Path = path;
            if (queryKey != null) req.Queries["key"] = queryKey;
            if (cookie != null) req.Headers["Cookie"] = $"{WebUiHandler.CookieName}={cookie}";
            if (header != null) req.Headers[BusProtocol.KeyHeader] = header;
            req.Response = new WebResponse();
            return req;
        }

        [Fact]
        public void Serves_index_for_query_key_and_sets_cookie()
        {
            var req = Get("/", queryKey: Key);

            Assert.True(new WebUiHandler(Key, _root).Handle(req));
            Assert.Contains("<html>ok</html>", Encoding.UTF8.GetString(req.Response.Buffer));
            Assert.Contains($"{WebUiHandler.CookieName}={Key}", req.Response.Headers["Set-Cookie"]);
            Assert.Contains("HttpOnly", req.Response.Headers["Set-Cookie"]);
        }

        [Fact]
        public void Cookie_replaces_the_key_in_the_url()
        {
            var req = Get("/app.js", cookie: Key);

            Assert.True(new WebUiHandler(Key, _root).Handle(req));
            Assert.Contains("console.log(1)", Encoding.UTF8.GetString(req.Response.Buffer));
            Assert.Null(req.Response.Headers["Set-Cookie"]);
        }

        [Fact]
        public void Without_any_key_it_is_401()
        {
            var req = Get("/", queryKey: "wrong");

            Assert.True(new WebUiHandler(Key, _root).Handle(req));
            Assert.Equal(401, req.Response.StatusCode);
        }

        [Fact]
        public void Missing_file_and_path_escape_are_404()
        {
            var missing = Get("/nope.js", cookie: Key);
            Assert.True(new WebUiHandler(Key, _root).Handle(missing));
            Assert.Equal(404, missing.Response.StatusCode);

            var escape = Get("/../secrets.txt", cookie: Key);
            Assert.True(new WebUiHandler(Key, _root).Handle(escape));
            Assert.Equal(404, escape.Response.StatusCode);
        }

        [Fact]
        public void Api_and_bus_paths_belong_to_the_api_handlers()
        {
            var handler = new WebUiHandler(Key, _root);

            Assert.False(handler.Handle(Get("/api/capabilities", cookie: Key)));
            Assert.False(handler.Handle(Get("/bus/health", cookie: Key)));
        }

        [Fact]
        public void Api_accepts_the_ui_cookie_but_bus_does_not()
        {
            var cookieOnly = Get("/api/capabilities", cookie: Key);
            Assert.True(BusAuth.IsAuthorized(cookieOnly, Key, allowCookie: true));
            Assert.False(BusAuth.IsAuthorized(cookieOnly, Key));

            var header = Get("/api/capabilities", header: Key);
            Assert.True(BusAuth.IsAuthorized(header, Key));
        }

        [Fact]
        public void Both_hosts_advertise_the_loop_and_only_a_the_worker()
        {
            var a = Get("/api/capabilities", header: Key);
            Assert.True(new WorkerApiHandler(
                new OpenLoop.Service.Worker.WorkerSessionHost((_, _) => Task.FromResult(
                    new OpenLoop.Service.Worker.WorkerRunResult { Outcome = "ok" })), Key).Handle(a));
            string aBody = Encoding.UTF8.GetString(a.Response.Buffer);
            Assert.Contains("\"worker\":true", aBody);
            Assert.Contains("\"loop\":true", aBody);

            var i = Get("/api/capabilities", header: Key);
            Assert.True(new LoopApiHandler(new LoopService(), new IterationService(), Key).Handle(i));
            string iBody = Encoding.UTF8.GetString(i.Response.Buffer);
            Assert.Contains("\"worker\":false", iBody);
            Assert.Contains("\"loop\":true", iBody);
        }
    }
}
