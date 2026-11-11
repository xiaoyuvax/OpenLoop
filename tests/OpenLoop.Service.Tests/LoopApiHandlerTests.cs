using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class LoopApiHandlerTests
    {
        private const string Key = "test-key";

        private static WebRequest Call(LoopService loop, string method, string path, string body = null,
            string workspace = null)
        {
            var req = new WebRequest
            {
                Method = method,
                InputStream = new MemoryStream(body == null ? [] : Encoding.UTF8.GetBytes(body))
            };
            req.Path = path;
            if (workspace != null) req.Queries["workspace"] = workspace;
            req.Headers[BusProtocol.KeyHeader] = Key;
            req.Response = new WebResponse();
            Assert.True(new LoopApiHandler(loop, new IterationService(), Key).Handle(req), $"{method} {path} 未被处理");
            return req;
        }

        [Fact]
        public void Unknown_workspace_starts_idle()
        {
            var req = Call(new LoopService(), "GET", "/api/loop/evolve", workspace: "C:/work");
            var state = JsonSerializer.Deserialize<LoopEvolveState>(req.Response.Buffer, BusProtocol.Json);

            Assert.Equal("idle", state.State);
        }

        [Fact]
        public void Rollback_without_versions_is_a_400()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "openloop-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                OpenLoop.Core.Store.Ledger.Open(workspace).EnsureInitialized();
                var req = Call(new LoopService(), "POST", "/api/loop/rollback",
                    $$"""{"workspace":"{{workspace.Replace("\\", "/")}}"}""");

                Assert.Equal(400, req.Response.StatusCode);
                var body = JsonSerializer.Deserialize<JsonElement>(req.Response.Buffer, BusProtocol.Json);
                Assert.Contains("没有更早的版本", body.GetProperty("error").GetString());
            }
            finally
            {
                try { Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void Capabilities_declare_the_loop_role()
        {
            var req = Call(new LoopService(), "GET", "/api/capabilities");
            string json = Encoding.UTF8.GetString(req.Response.Buffer);

            Assert.Contains("\"worker\":false", json);
            Assert.Contains("\"loop\":true", json);
        }
    }
}
