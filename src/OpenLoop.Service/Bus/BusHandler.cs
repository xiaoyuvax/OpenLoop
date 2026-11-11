using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;

namespace OpenLoop.Service.Bus
{
    /// <summary>
    /// /bus/* 端点：握手、健康、状态、退出、更新（stage / restart）。
    /// stage 只校验并登记，restart 才落地交接单然后退出——真正换进程的是壳。
    /// 鉴权失败返回 404（与 Puppet 一致，不暴露端点是否存在）。
    /// </summary>
    public sealed class BusHandler
    {
        private readonly Func<RuntimeInfo> _self;
        private readonly Func<RuntimeInfo> _peer;
        private readonly string _key;
        private readonly string _role;
        private readonly Func<bool> _busy;
        private readonly Action _shutdown;
        private UpdateRequest _staged;

        public BusHandler(Func<RuntimeInfo> self, Func<RuntimeInfo> peer, string key, Action shutdown,
            string role = null, Func<bool> busy = null)
        {
            _self = self;
            _peer = peer;
            _key = key ?? "";
            _shutdown = shutdown;
            _role = role ?? self?.Invoke()?.Role ?? BusRoles.A;
            _busy = busy;
        }

        public bool Handle(WebRequest req)
        {
            if (req == null || req.Path == null ||
                !req.Path.StartsWith("/bus", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!Authorized(req)) return NotFound(req);

            string path = req.Path.TrimEnd('/').ToLowerInvariant();
            string method = (req.Method ?? "GET").ToUpperInvariant();

            if (method == "GET" && (path == "/bus/health" || path == "/bus/status"))
            {
                if (path == "/bus/health") return Json(req, _self());

                return Json(req, new BusStatus
                {
                    Self = _self(),
                    Peer = _peer(),
                    Protocol = BusProtocol.Version
                });
            }

            if (method == "POST" && path == "/bus/hello")
            {
                var hello = ReadBody<RuntimeInfo>(req);
                if (hello == null) return Json(req, new { ok = false, error = "invalid hello" }, 400);
                return Json(req, _self());
            }

            if (method == "POST" && path == "/bus/shutdown")
            {
                _shutdown?.Invoke();
                return Json(req, new { ok = true }, 202);
            }

            if (method == "POST" && path == "/bus/stage") return Stage(req);
            if (method == "POST" && path == "/bus/restart") return Restart(req);

            return NotFound(req);
        }

        /// <summary>只校验并登记：协议号、目录、可执行文件、sha256。</summary>
        private bool Stage(WebRequest req)
        {
            var body = ReadBody<StageRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.Dir))
                return Json(req, new { ok = false, error = "invalid body" }, 400);

            if (body.Protocol != 0 && body.Protocol != BusProtocol.Version)
                return Json(req, new { ok = false, error = "协议号不一致，两边一起升级" }, 409);

            string dir;
            try
            {
                dir = Path.GetFullPath(body.Dir);
            }
            catch (Exception)
            {
                return Json(req, new { ok = false, error = "目录无效" }, 409);
            }

            string exe = VersionStore.ExePath(dir, _role);
            if (!File.Exists(exe))
                return Json(req, new { ok = false, error = "缺少可执行文件：" + exe }, 409);

            string sha = Sha256(exe);
            if (!string.IsNullOrWhiteSpace(body.Sha256) &&
                !string.Equals(sha, body.Sha256, StringComparison.OrdinalIgnoreCase))
                return Json(req, new { ok = false, error = "sha256 不匹配" }, 409);

            // 有清单就以包自己为准：请求里的 protocol 只代表发起方，查不出「旧壳推新协议包」。
            string version = body.Version;
            var manifest = VersionManifest.TryRead(dir);
            if (manifest != null)
            {
                if (manifest.Protocol != BusProtocol.Version)
                    return Json(req, new
                    {
                        ok = false,
                        error = $"协议号不一致（包 {manifest.Protocol}，本进程 {BusProtocol.Version}），两边一起升级"
                    }, 409);

                if (!string.IsNullOrWhiteSpace(manifest.Role) &&
                    !string.Equals(manifest.Role, _role, StringComparison.OrdinalIgnoreCase))
                    return Json(req, new { ok = false, error = $"包里是 {manifest.Role} 的产物，不能装到 {_role}" }, 409);

                string files = VersionManifest.ComputeFilesSha256(dir);
                if (!string.IsNullOrWhiteSpace(manifest.FilesSha256) &&
                    !string.Equals(files, manifest.FilesSha256, StringComparison.OrdinalIgnoreCase))
                    return Json(req, new { ok = false, error = "文件清单与 manifest 不一致（包被改过）" }, 409);

                if (!string.IsNullOrWhiteSpace(manifest.Version)) version = manifest.Version;
            }
            else if (body.Protocol != 0 && body.Protocol != BusProtocol.Version)
            {
                return Json(req, new { ok = false, error = "协议号不一致，两边一起升级" }, 409);
            }

            _staged = new UpdateRequest
            {
                Role = _role,
                Version = string.IsNullOrWhiteSpace(version)
                    ? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))
                    : version,
                Dir = dir,
                Sha256 = sha,
                At = DateTimeOffset.Now.ToString("O")
            };

            return Json(req, new { ok = true, staged = _staged });
        }

        /// <summary>忙就 409；不忙就写交接单并退出，由壳拉起新目录。</summary>
        private bool Restart(WebRequest req)
        {
            var body = ReadBody<RestartRequest>(req);
            if (_busy?.Invoke() == true && body?.Force != true)
                return Json(req, new { ok = false, error = "busy" }, 409);

            if (_staged == null)
                return Json(req, new { ok = false, error = "nothing staged" }, 409);

            _staged.RequestedBy = body?.Reason ?? "bus";
            _staged.At = DateTimeOffset.Now.ToString("O");
            _staged.Save();
            _shutdown?.Invoke();
            return Json(req, new { ok = true, accepted = true }, 202);
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private bool Authorized(WebRequest req) => BusAuth.IsAuthorized(req, _key);

        private static T ReadBody<T>(WebRequest req) where T : class
        {
            try
            {
                if (req.InputStream == null || !req.InputStream.CanRead) return null;
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8, false, 4096, leaveOpen: true);
                string text = reader.ReadToEnd();
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : JsonSerializer.Deserialize<T>(text, BusProtocol.Json);
            }
            catch
            {
                return null;
            }
        }

        private static bool Json(WebRequest req, object value, int status = 0)
        {
            req.Response.Headers.Set("Content-Type", "application/json; charset=utf-8");
            req.Response.Headers.Set("Cache-Control", "no-store");
            req.Response.Buffer = JsonSerializer.SerializeToUtf8Bytes(value, BusProtocol.Json);
            if (status != 0) req.Response.StatusCode = status;
            return true;
        }

        private static bool NotFound(WebRequest req)
        {
            req.Response.StatusCode = 404;
            req.Response.Buffer = Encoding.UTF8.GetBytes("not found");
            return true;
        }

        private sealed class StageRequest
        {
            public string Version { get; set; }
            public string Dir { get; set; }
            public string Sha256 { get; set; }
            public int Protocol { get; set; }
        }

        private sealed class RestartRequest
        {
            public string Version { get; set; }
            public string Reason { get; set; }
            public bool Force { get; set; }
        }
    }
}
