using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Worker;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// Worker 鐨勪細璇?API锛圓 杩涚▼锛夈€俉inForms 涓庢祻瑙堝櫒璧板悓涓€缁勭鐐癸紝
    /// 鍥犳鐣岄潰閫昏緫鍙湁涓€浠姐€?    /// </summary>
    public sealed class WorkerApiHandler
    {
        private readonly WorkerSessionHost _host;
        private readonly string _key;

        public WorkerApiHandler(WorkerSessionHost host, string key)
        {
            _host = host;
            _key = key;
        }

        public bool Handle(WebRequest req)
        {
            if (req?.Path == null || !req.Path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!BusAuth.IsAuthorized(req, _key, allowCookie: true)) return NotFound(req);

            string method = (req.Method ?? "GET").ToUpperInvariant();
            string[] parts = req.Path.Trim('/').Split('/');
            // parts: api / sessions / {id} / {action}

            if (method == "GET" && parts.Length == 2 && parts[1] == "capabilities")
                return Json(req, new { worker = true, loop = true });

            // 鍒殑涓€寰嬩笉璁わ紙/api/loop/* 鐢卞弽浠ｅ鐞嗗櫒鎺ヨ蛋锛夛紝浜ょ粰鍚庨潰鐨勫鐞嗗櫒
            if (parts.Length < 2 || parts[1] != "sessions") return false;

            if (method == "GET" && parts.Length == 2)
                return Json(req, _host.List());

            if (method == "POST" && parts.Length == 2)
            {
                var body = ReadBody<SessionsRequest>(req);
                if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                var snapshot = _host.Create(body.Workspace ?? "", body.Model ?? "", body.Effort ?? "",
                    AppSettings.Load, body.Id);
                return Json(req, snapshot, 201);
            }

            if (parts.Length < 3) return NotFound(req);
            string id = parts[2];

            if (method == "GET" && parts.Length == 3)
            {
                var snapshot = _host.Get(id);
                return snapshot == null ? NotFound(req) : Json(req, snapshot);
            }

            if (method != "POST" || parts.Length != 4) return NotFound(req);

            switch (parts[3].ToLowerInvariant())
            {
                case "messages":
                {
                    var body = ReadBody<MessageRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    return _host.Send(id, body.Tag, body.Text)
                        ? Json(req, _host.Get(id))
                        : NotFound(req);
                }
                case "stop":
                    return _host.Stop(id) ? Json(req, _host.Get(id)) : NotFound(req);
                case "resume":
                    return _host.Resume(id) ? Json(req, _host.Get(id)) : NotFound(req);
                case "model":
                {
                    var body = ReadBody<ModelRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    return _host.SetModel(id, body.Model, body.Effort) ? Json(req, _host.Get(id)) : NotFound(req);
                }
                case "approval":
                {
                    var body = ReadBody<ApprovalAnswer>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    return _host.ResolvePrompt(id, body.Id, body.Decision) ? Json(req, _host.Get(id)) : NotFound(req);
                }
                default:
                    return NotFound(req);
            }
        }

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

        private sealed class SessionsRequest
        {
            public string Id { get; set; }
            public string Workspace { get; set; }
            public string Model { get; set; }
            public string Effort { get; set; }
        }

        private sealed class ModelRequest
        {
            public string Model { get; set; }
            public string Effort { get; set; }
        }

        private sealed class MessageRequest
        {
            public string Text { get; set; }
            public string Tag { get; set; }
        }

        private sealed class ApprovalAnswer
        {
            public string Id { get; set; }
            public int Decision { get; set; }
        }
    }
}

