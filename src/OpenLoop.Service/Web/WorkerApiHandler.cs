using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Storage;
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
                return Json(req, new { worker = true, loop = true, workspace = AppSettings.Load()?.Workspace ?? "" });

            if (method == "GET" && parts.Length == 2 && parts[1] == "models")
            {
                if (Models.OpenCodeHub.Models.Count == 0) Models.OpenCodeHub.EnsureFresh();
                var settings = AppSettings.Load();
                var catalog = new List<(string Id, string Name, bool Free, int Context)>();
                foreach (var m in Models.OpenCodeHub.Models)
                    if (!string.IsNullOrWhiteSpace(m?.Id) && !settings.ModelDisabled(m.Id))
                        catalog.Add((m.Id, m.Name ?? m.Id, m.IsFree, m.ContextLength));
                var seen = new HashSet<string>(catalog.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                // 插件贡献的模型与设置页「模型」同源：停用的不出现在下拉里
                using (var plugins = Plugins.PluginCatalog.Load(settings?.Workspace ?? ""))
                    foreach (string modelId in plugins.Models())
                        if (!settings.ModelDisabled(modelId) && seen.Add(modelId))
                            catalog.Add((modelId, modelId, true, 0));
                return Json(req, new
                {
                    models = catalog
                        .Select(m => new { id = m.Id, name = m.Name, free = m.Free, context = m.Context })
                        .ToArray(),
                    images = Media.MediaModels.ImageModels(settings)
                        .Select(m => new { id = m.Id, name = m.Name ?? m.Id, free = m.IsFree })
                        .ToArray(),
                    recent = settings?.RecentModels ?? []
                });
            }

            // 鍒殑涓€寰嬩笉璁わ紙/api/loop/* 鐢卞弽浠ｅ鐞嗗櫒鎺ヨ蛋锛夛紝浜ょ粰鍚庨潰鐨勫鐞嗗櫒
            if (parts.Length < 2 || parts[1] != "sessions") return false;

            if (method == "GET" && parts.Length == 2)
                return Json(req, _host.List(summary: req.Queries?["summary"] is "1" or "true"));

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

            if (method == "DELETE" && parts.Length == 3)
            {
                var current = _host.Get(id);
                if (current == null || !_host.Remove(id)) return NotFound(req);
                // 界面删会话 = 连历史一起删，否则重启后又会从 chats/index.json 冒出来
                ChatHistory.Delete(current.Workspace, id);
                return Json(req, new { ok = true }, 200);
            }

            if (method != "POST" || parts.Length != 4) return NotFound(req);

            switch (parts[3].ToLowerInvariant())
            {
                case "messages":
                {
                    var body = ReadBody<MessageRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    var current = _host.Get(id);
                    if (current == null) return NotFound(req);
                    int limit = Media.MediaModels.AttachmentLimit(current.Model);
                    // 本机壳直接把文件复制进工作区再报路径（绕开 HTTP 请求体上限）；Web 页面仍按 base64 传
                    var saved = body.Paths is { Count: > 0 }
                        ? Media.Attachments.SaveFiles(current.Workspace, id, body.Paths, limit, out string attachmentError)
                        : Media.Attachments.Save(current.Workspace, id, body.Attachments, limit, out attachmentError);
                    if (saved == null) return Json(req, new { ok = false, error = attachmentError }, 400);
                    return _host.Send(id, body.Tag, body.Text, body.Immediate, body.ImageModel, saved)
                        ? Json(req, _host.Get(id))
                        : NotFound(req);
                }
                case "pending":
                {
                    var body = ReadBody<PendingRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    bool ok = string.Equals(body.Action, "now", StringComparison.OrdinalIgnoreCase)
                        ? _host.SendNow(id, body.Index)
                        : _host.RemovePending(id, body.Index);
                    return ok ? Json(req, _host.Get(id)) : NotFound(req);
                }
                case "stop":
                    return _host.Stop(id) ? Json(req, _host.Get(id)) : NotFound(req);
                case "interrupt":
                {
                    var body = ReadBody<InterruptRequest>(req);
                    return _host.Interrupt(id, body?.On ?? true) ? Json(req, _host.Get(id)) : NotFound(req);
                }
                case "resume":
                    return _host.Resume(id) ? Json(req, _host.Get(id)) : NotFound(req);
                case "truncate":
                {
                    var body = ReadBody<TruncateRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    return _host.Truncate(id, body.Keep)
                        ? Json(req, _host.Get(id))
                        : NotFound(req);
                }
                case "compact":
                {
                    var compact = _host.Compact(id);
                    return compact == null ? NotFound(req) : Json(req, compact);
                }
                case "model":
                {
                    var body = ReadBody<ModelRequest>(req);
                    if (body == null) return Json(req, new { ok = false, error = "invalid body" }, 400);
                    if (!_host.SetModel(id, body.Model, body.Effort)) return NotFound(req);
                    if (!string.IsNullOrWhiteSpace(body.Model)) RecordRecent(body.Model);
                    return Json(req, _host.Get(id));
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

        /// <summary>Web 壳选模型也算一次使用：与 WinForms 共用同一份 RecentModels。</summary>
        private static void RecordRecent(string model)
        {
            try
            {
                var settings = AppSettings.Load();
                if (settings == null) return;
                settings.RecordRecentModel(model);
                settings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Warn("记最近模型失败：" + ex.Message);
            }
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

            /// <summary>运行中为 true：打断本轮，收尾后先发这一条（Enter 的语义）。</summary>
            public bool Immediate { get; set; }

            public string ImageModel { get; set; }
            public List<Media.AttachmentPayload> Attachments { get; set; }

            /// <summary>壳已复制进工作区的附件路径（本机壳走这条，省掉 base64 与请求体上限）。</summary>
            public List<string> Paths { get; set; }
        }

        private sealed class PendingRequest
        {
            public int Index { get; set; }
            public string Action { get; set; }
        }

        private sealed class InterruptRequest
        {
            /// <summary>false = 收回软打断（排队项改回「等本轮」）；缺省 true。</summary>
            public bool On { get; set; } = true;
        }

        private sealed class TruncateRequest
        {
            public int Keep { get; set; }
        }

        private sealed class ApprovalAnswer
        {
            public string Id { get; set; }
            public int Decision { get; set; }
        }
    }
}

