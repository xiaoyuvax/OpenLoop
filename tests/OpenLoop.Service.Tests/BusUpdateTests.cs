using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>/bus/stage 只校验登记，/bus/restart 忙就 409、不忙才写交接单并退出。</summary>
    public class BusUpdateTests : IDisposable
    {
        private const string Key = "test-key";
        private const string Role = "t";

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "openloop-update-" + Guid.NewGuid().ToString("N"));

        public BusUpdateTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, VersionStore.ExeName(Role)), "stub");
        }

        public void Dispose()
        {
            UpdateRequest.Clear(Role);
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static WebRequest Post(string path, object body)
        {
            var req = new WebRequest
            {
                Method = "POST",
                InputStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, BusProtocol.Json)))
            };
            req.Path = path;
            req.Headers[BusProtocol.KeyHeader] = Key;
            req.Response = new WebResponse();
            return req;
        }

        private static (BusHandler Handler, Func<bool> Shutdown) Build(bool busy)
        {
            bool stopped = false;
            var handler = new BusHandler(
                () => new RuntimeInfo { Role = Role, Pid = Environment.ProcessId, Version = "0.1.0" },
                () => null, Key, () => stopped = true, Role, () => busy);
            return (handler, () => stopped);
        }

        private object StageBody(string dir, int protocol = 0, string sha = null)
            => new { version = "9.9.9", dir, sha256 = sha, protocol };

        private static string Error(WebRequest req)
            => JsonSerializer.Deserialize<JsonElement>(req.Response.Buffer, BusProtocol.Json)
                .GetProperty("error").GetString();

        [Fact]
        public void Stage_rejects_a_protocol_mismatch()
        {
            var (handler, _) = Build(false);
            var req = Post("/bus/stage", StageBody(_dir, protocol: BusProtocol.Version + 1));

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.Contains("协议号", Error(req));
        }

        [Fact]
        public void Stage_rejects_a_dir_without_the_executable()
        {
            var (handler, _) = Build(false);
            string empty = Path.Combine(_dir, "empty");
            Directory.CreateDirectory(empty);
            var req = Post("/bus/stage", StageBody(empty));

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.Contains("可执行文件", Error(req));
        }

        [Fact]
        public void Stage_rejects_a_sha256_mismatch()
        {
            var (handler, _) = Build(false);
            var req = Post("/bus/stage", StageBody(_dir, sha: "deadbeef"));

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.Contains("sha256", Error(req));
        }

        [Fact]
        public void Stage_uses_the_manifest_protocol_not_the_callers()
        {
            // 旧壳推新协议包：请求里写的是本进程的协议号，包里 manifest 声明的是另一个
            VersionManifest.Write(_dir, Role, "9.9.9", BusProtocol.Version + 1);
            var (handler, _) = Build(false);
            var req = Post("/bus/stage", StageBody(_dir, protocol: BusProtocol.Version));

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.Contains("协议号", Error(req));
        }

        [Fact]
        public void Stage_rejects_a_package_whose_payload_was_tampered_after_manifest()
        {
            VersionManifest.Write(_dir, Role, "9.9.9", BusProtocol.Version);
            File.WriteAllText(Path.Combine(_dir, "OpenLoop.Service.dll"), "tampered");
            var (handler, _) = Build(false);
            var req = Post("/bus/stage", StageBody(_dir));

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.Contains("manifest", Error(req));
        }

        [Fact]
        public void Stage_accepts_a_matching_manifest_and_takes_its_version()
        {
            VersionManifest.Write(_dir, Role, "7.7.7", BusProtocol.Version);
            var (handler, _) = Build(false);
            var req = Post("/bus/stage", StageBody(_dir));

            Assert.True(handler.Handle(req));
            Assert.True(req.Response.StatusCode is 0 or 200);   // 0 = 默认成功码，真实服务端会写成 200
            Assert.True(handler.Handle(Post("/bus/restart", new { reason = "test" })));
            Assert.Equal("7.7.7", UpdateRequest.TryLoad(Role)?.Version);
        }

        [Fact]
        public void Restart_without_a_stage_is_409_and_keeps_running()
        {
            var (handler, stopped) = Build(false);
            var req = Post("/bus/restart", new { reason = "test" });

            Assert.True(handler.Handle(req));
            Assert.Equal(409, req.Response.StatusCode);
            Assert.False(stopped());
            Assert.Null(UpdateRequest.TryLoad(Role));
        }

        [Fact]
        public void Restart_while_busy_is_409_unless_forced()
        {
            var (handler, stopped) = Build(true);
            Assert.True(handler.Handle(Post("/bus/stage", StageBody(_dir))));

            var busy = Post("/bus/restart", new { reason = "test" });
            Assert.True(handler.Handle(busy));
            Assert.Equal(409, busy.Response.StatusCode);
            Assert.Contains("busy", Error(busy));
            Assert.False(stopped());
            Assert.Null(UpdateRequest.TryLoad(Role));

            var forced = Post("/bus/restart", new { reason = "test", force = true });
            Assert.True(handler.Handle(forced));
            Assert.Equal(202, forced.Response.StatusCode);
            Assert.True(stopped());
            Assert.Equal("9.9.9", UpdateRequest.TryLoad(Role)?.Version);
        }

        [Fact]
        public void Restart_writes_the_handover_and_asks_the_host_to_exit()
        {
            var (handler, stopped) = Build(false);
            Assert.True(handler.Handle(Post("/bus/stage", StageBody(_dir))));

            var req = Post("/bus/restart", new { reason = "unit" });
            Assert.True(handler.Handle(req));

            Assert.Equal(202, req.Response.StatusCode);
            Assert.True(stopped());

            var request = UpdateRequest.TryLoad(Role);
            Assert.NotNull(request);
            Assert.Equal(_dir, request.Dir);
            Assert.Equal("unit", request.RequestedBy);
        }
    }
}
