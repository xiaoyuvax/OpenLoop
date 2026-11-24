using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Core.Store;
using OpenLoop.Service.Config;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Source;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// Loop 的 API（I 进程）：演进是长任务，POST 开跑、GET 轮询；回滚与规则登记是同步的短操作。
    /// </summary>
    public sealed class LoopApiHandler
    {
        private readonly LoopService _loop;
        private readonly IterationService _iteration;
        private readonly string _key;

        public LoopApiHandler(LoopService loop, IterationService iteration, string key)
        {
            _loop = loop;
            _iteration = iteration;
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
                return Json(req, new { worker = false, loop = true, workspace = AppSettings.Load()?.Workspace ?? "" });

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

                case "iteration":
                {
                    if (method == "GET" && parts.Length == 3)
                        return Json(req, _iteration.GetState(Query(req, "workspace")));

                    if (method == "GET" && parts.Length == 4 && parts[3] == "plan")
                    {
                        try
                        {
                            return Json(req, _iteration.PlanAsync(Query(req, "workspace")).GetAwaiter().GetResult());
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Json(req, new { ok = false, error = ex.Message }, 409);
                        }
                    }

                    if (method == "POST" && parts.Length == 3)
                    {
                        var body = ReadBody<IterationRequest>(req);
                        if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                        try
                        {
                            return Json(req, _iteration.Start(body.Workspace, body.Shell, body.PublishMode), 202);
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Json(req, new { ok = false, error = ex.Message }, 409);
                        }
                    }

                    if (method == "POST" && parts.Length == 4 && parts[3] == "stop")
                        return Json(req, new { stopped = _iteration.Stop() });

                    return NotFound(req);
                }

                case "git":
                {
                    if (method == "GET" && parts.Length == 4 && parts[3] == "status")
                        return Json(req, GitService.Status());

                    if (method == "POST" && parts.Length == 4 && parts[3] == "commit")
                    {
                        var body = ReadBody<GitCommitRequest>(req);
                        if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                        bool lastRunFailed = _iteration.GetState("").State == "error";
                        return Json(req, GitService.Commit(body, lastRunFailed));
                    }

                    return NotFound(req);
                }

                case "auto":
                {
                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<AutoRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    var settings = AppSettings.Load();
                    settings.AutoEvolve = body.Enabled;
                    settings.Save();
                    return Json(req, new { ok = true, enabled = settings.AutoEvolve });
                }

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

                case "mode":
                {
                    if (method == "GET")
                    {
                        string workspace = Query(req, "workspace").Trim();
                        return Json(req, new
                        {
                            ok = true,
                            mode = HarnessMode.Get(workspace),
                            pending = workspace.Length == 0 ? [] : PromptCatalog.PendingKinds(workspace)
                        });
                    }

                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<ModeRequest>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Workspace) || body.Mode == null)
                        return Json(req, new { ok = false, error = "invalid body" }, 400);
                    HarnessMode.Set(body.Workspace, body.Mode);
                    return Json(req, new { ok = true, mode = HarnessMode.Get(body.Workspace) });
                }

                case "skills":
                {
                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<SkillRequest>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Name))
                        return Json(req, new { ok = false, error = "invalid body" }, 400);
                    _loop.SetSkill(body.Workspace, body.Name, body.Enabled);
                    return Json(req, new { ok = true });
                }

                case "objects":
                {
                    // 工作区 Harness 配置页：GET = 列某一类对象；POST = 改一件（或整类恢复默认）
                    if (method == "GET")
                    {
                        try
                        {
                            return Json(req, new
                            {
                                ok = true,
                                items = _loop.ListObjects(Query(req, "workspace"), Query(req, "kind"))
                            });
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
                        {
                            return Json(req, new { ok = false, error = ex.Message }, 400);
                        }
                    }

                    if (method != "POST") return NotFound(req);
                    var body = ReadBody<ObjectRequest>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Workspace) || string.IsNullOrWhiteSpace(body.Kind))
                        return Json(req, new { ok = false, error = "invalid body" }, 400);
                    try
                    {
                        if (string.Equals(body.Action, "reset", StringComparison.OrdinalIgnoreCase))
                            return Json(req, new { ok = true, version = _loop.ResetKind(body.Workspace, body.Kind) });
                        if (string.IsNullOrWhiteSpace(body.Id))
                            return Json(req, new { ok = false, error = "缺 id" }, 400);
                        return Json(req, new
                        {
                            ok = true,
                            version = _loop.SetObject(body.Workspace, body.Kind, body.Id, body.Action, body.Params)
                        });
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
                    {
                        return Json(req, new { ok = false, error = ex.Message }, 400);
                    }
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

        private sealed class IterationRequest
        {
            public string Workspace { get; set; }

            /// <summary>是否连壳一起换；Puppet 的无窗口迭代传 false。</summary>
            public bool Shell { get; set; } = true;

            /// <summary>发布形态：framework（默认，需本机装 .NET）/ selfcontained（自带框架）。</summary>
            public string PublishMode { get; set; }
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

        private sealed class ObjectRequest
        {
            public string Workspace { get; set; }
            public string Kind { get; set; }
            public string Id { get; set; }

            /// <summary>enable / disable / params / default / reset（整类恢复默认）。</summary>
            public string Action { get; set; }

            /// <summary>改参数时的「名字=值, 名字=值」文本；其他动作忽略。</summary>
            public string Params { get; set; }
        }

        private sealed class ModeRequest
        {
            public string Workspace { get; set; }
            public string Mode { get; set; }
        }

        private sealed class AutoRequest
        {
            public string Workspace { get; set; }
            public bool Enabled { get; set; }
        }
    }
}
