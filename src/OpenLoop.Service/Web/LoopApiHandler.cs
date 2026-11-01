using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Loop;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// Loop 的 API（I 进程）：演进是长任务，POST 开跑、GET 轮询；回滚与规则登记是同步的短操作。
    /// </summary>
    public sealed class LoopApiHandler
    {
        private readonly LoopService _loop;
        private readonly string _key;

        public LoopApiHandler(LoopService loop, string key)
        {
            _loop = loop;
            _key = key;
        }

        public bool Handle(WebRequest req)
        {
            if (req?.Path == null || !req.Path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!BusAuth.IsAuthorized(req, _key, allowCookie: true)) return NotFound(req);

            string method = (req.Method ?? "GET").ToUpperInvariant();
            string[] parts = req.Path.Trim('/').Split('/');
            // parts: api / loop / {action} [/ {sub}] 或 api / capabilities

            if (method == "GET" && parts.Length == 2 && parts[1] == "capabilities")
                return Json(req, new { worker = false, loop = true });

            if (parts.Length < 3 || parts[1] != "loop") return NotFound(req);

            string action = parts[2];
            switch (action)
            {
                case "evolve":
                    if (method == "GET" && parts.Length == 3)
                        return Json(req, _loop.GetState(Query(req, "workspace")));

                    if (method == "POST" && parts.Length == 3)
                    {
                        var body = ReadBody<EvolveRequest>(req);
                        if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                        return Json(req, _loop.StartEvolve(body.Workspace, body.Model, body.Effort), 202);
                    }

                    if (method == "POST" && parts.Length == 4 && parts[3] == "stop")
                    {
                        var body = ReadBody<WorkspaceRequest>(req);
                        if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                        return Json(req, new { stopped = _loop.StopEvolve(body.Workspace) });
                    }
                    return NotFound(req);

                case "rollback":
                {
                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<RollbackRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    try
                    {
                        var restored = _loop.Rollback(body.Workspace, body.Version);
                        return Json(req, new { ok = true, version = restored.Version });
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException)
                    {
                        return Json(req, new { ok = false, error = ex.Message }, 400);
                    }
                }

                case "rule":
                {
                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<RuleRequest>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Name))
                        return Json(req, new { ok = false, error = "invalid body" }, 400);
                    var feature = _loop.AddRule(body.Workspace, body.Name, body.Text);
                    return Json(req, new { ok = true, id = feature.Id });
                }

                case "activation":
                    if (method != "GET") return NotFound(req);
                    return Json(req, _loop.LoadActivation(Query(req, "workspace")));

                case "skills":
                {
                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<SkillRequest>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Name))
                        return Json(req, new { ok = false, error = "invalid body" }, 400);
                    _loop.SetSkill(body.Workspace, body.Name, body.Enabled);
                    return Json(req, new { ok = true });
                }

                case "spool":
                {
                    if (method != "POST" || parts.Length != 4 || parts[3] != "drain") return NotFound(req);
                    var body = ReadBody<WorkspaceRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    return Json(req, new { drained = _loop.DrainSpool(body.Workspace) });
                }

                default:
                    return NotFound(req);
            }
        }

        private static string Query(WebRequest req, string name)
            => req.Queries?[name] ?? "";

        private static T ReadBody<T>(WebRequest req) where T : class
        {
            try
            {
                if (req.InputStream == null || !req.InputStream.CanRead) return null;
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8, false, 4096, leaveOpen: true);
                return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), BusProtocol.Json);
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

        private sealed class EvolveRequest
        {
            public string Workspace { get; set; }
            public string Model { get; set; }
            public string Effort { get; set; }
        }

        private sealed class WorkspaceRequest
        {
            public string Workspace { get; set; }
        }

        private sealed class RollbackRequest
        {
            public string Workspace { get; set; }
            public int? Version { get; set; }
        }

        private sealed class RuleRequest
        {
            public string Workspace { get; set; }
            public string Name { get; set; }
            public string Text { get; set; }
        }

        private sealed class SkillRequest
        {
            public string Workspace { get; set; }
            public string Name { get; set; }
            public bool Enabled { get; set; }
        }
    }
}
