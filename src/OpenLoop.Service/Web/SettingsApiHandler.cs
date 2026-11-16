using System.Text;
using System.Text.Json;
using Wima.Web;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Service.Bus;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Models;

namespace OpenLoop.Service.Web
{
    /// <summary>
    /// 浏览器设置页：读写同一份 AppSettings，提供商与技能复用 WinForms 设置页的逻辑。
    /// 密钥只回 hasKey，不回明文；保存时 key 缺省表示沿用旧值。
    /// </summary>
    public sealed class SettingsApiHandler
    {
        private readonly string _key;

        public SettingsApiHandler(string key) => _key = key ?? "";

        public bool Handle(WebRequest req)
        {
            string path = req?.Path ?? "";
            if (!path.StartsWith("/api/settings", StringComparison.OrdinalIgnoreCase)) return false;
            if (!BusAuth.IsAuthorized(req, _key, allowCookie: true)) return NotFound(req);

            string method = (req.Method ?? "GET").ToUpperInvariant();
            string[] parts = path.Trim('/').Split('/');
            string tail = parts.Length > 2 ? string.Join("/", parts.Skip(2)) : "";

            switch (tail)
            {
                case "":
                    if (method == "GET") return Json(req, View());
                    return method == "POST" ? Save(req) : NotFound(req);
                case "provider/fetch":
                    return method == "POST" ? FetchModels(req) : NotFound(req);
                case "provider/test":
                    return method == "POST" ? TestProvider(req) : NotFound(req);
                case "skills":
                    if (method == "GET") return Json(req, SkillRows(req));
                    return method == "POST" ? ToggleSkill(req) : NotFound(req);
                case "skills/install":
                    return method == "POST" ? InstallSkill(req) : NotFound(req);
                case "skills/uninstall":
                    return method == "POST" ? UninstallSkill(req) : NotFound(req);
                default:
                    return NotFound(req);
            }
        }

        // ---------------- 设置读写 ----------------

        private static object View()
        {
            var settings = AppSettings.Load();
            return new
            {
                workspace = settings.Workspace,
                language = settings.Language,
                theme = settings.Theme,
                fullAccess = settings.FullAccess,
                autoApprove = settings.AutoApprove,
                openCodeAskPermissions = settings.OpenCodeAskPermissions,
                webFetchMode = settings.WebFetchMode,
                useAgentsMd = settings.UseAgentsMd,
                thinkingExpandedByDefault = settings.ThinkingExpandedByDefault,
                autoEvolve = settings.AutoEvolve,
                autoEvolveRuns = settings.AutoEvolveRuns,
                autoEvolveCooldownMinutes = settings.AutoEvolveCooldownMinutes,
                autoEvolvePerDay = settings.AutoEvolvePerDay,
                providers = (settings.Providers ?? []).Select(p => new
                {
                    name = p.Name,
                    kind = p.Kind,
                    baseUrl = p.BaseUrl,
                    hasKey = !string.IsNullOrEmpty(p.KeyProtected),
                    models = p.Models ?? []
                }).ToArray(),
                modelProps = settings.ModelProps ?? new Dictionary<string, ModelProps>()
            };
        }

        private static bool Save(WebRequest req)
        {
            var body = ReadBody<SettingsRequest>(req);
            if (body == null) return Bad(req, "invalid body");

            var settings = AppSettings.Load();
            if (body.Language != null) settings.Language = body.Language;
            if (body.Theme != null) settings.Theme = body.Theme;
            if (body.WebFetchMode != null) settings.WebFetchMode = NormalizeWebFetch(body.WebFetchMode);
            if (body.FullAccess.HasValue) settings.FullAccess = body.FullAccess.Value;
            if (body.AutoApprove.HasValue) settings.AutoApprove = body.AutoApprove.Value;
            if (body.OpenCodeAskPermissions.HasValue)
                settings.OpenCodeAskPermissions = body.OpenCodeAskPermissions.Value;
            if (body.UseAgentsMd.HasValue) settings.UseAgentsMd = body.UseAgentsMd.Value;
            if (body.ThinkingExpandedByDefault.HasValue)
                settings.ThinkingExpandedByDefault = body.ThinkingExpandedByDefault.Value;
            if (body.AutoEvolve.HasValue) settings.AutoEvolve = body.AutoEvolve.Value;
            if (body.AutoEvolveRuns is > 0) settings.AutoEvolveRuns = body.AutoEvolveRuns.Value;
            if (body.AutoEvolveCooldownMinutes is > 0)
                settings.AutoEvolveCooldownMinutes = body.AutoEvolveCooldownMinutes.Value;
            if (body.AutoEvolvePerDay is > 0) settings.AutoEvolvePerDay = body.AutoEvolvePerDay.Value;
            if (body.ModelProps != null)
                settings.ModelProps = body.ModelProps
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value ?? new ModelProps(),
                        StringComparer.OrdinalIgnoreCase);

            if (body.Providers != null)
            {
                var old = settings.Providers ?? [];
                var list = new List<ProviderConfig>();
                foreach (var item in body.Providers)
                {
                    if (string.IsNullOrWhiteSpace(item?.Name)) continue;
                    string keep = item.Key == null
                        ? old.FirstOrDefault(p =>
                            string.Equals(p.Name, item.Name, StringComparison.OrdinalIgnoreCase))?.KeyProtected ?? ""
                        : AppSettings.Protect(item.Key);
                    list.Add(new ProviderConfig
                    {
                        Name = item.Name.Trim(),
                        Kind = string.IsNullOrWhiteSpace(item.Kind) ? "custom" : item.Kind.Trim(),
                        BaseUrl = item.BaseUrl?.Trim() ?? "",
                        KeyProtected = keep,
                        Models = (item.Models ?? [])
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .Select(m => m.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    });
                }
                settings.Providers = list;

                // 与 WinForms 保存一致：旧的三件套回退字段跟随第一个提供商
                var first = list.FirstOrDefault(p => p.Models.Count > 0) ?? list.FirstOrDefault();
                if (first != null)
                {
                    settings.BaseUrl = first.BaseUrl;
                    settings.Model = first.Models.FirstOrDefault() ?? settings.Model;
                    settings.KeyProtected = first.KeyProtected;
                }
            }

            settings.Save();
            return Json(req, new { ok = true });
        }

        private static string NormalizeWebFetch(string mode) => (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "off" => "off",
            "ask" => "ask",
            _ => "allow"
        };

        // ---------------- 提供商 ----------------

        private static bool FetchModels(WebRequest req)
        {
            var body = ReadBody<ProviderRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.BaseUrl))
                return Bad(req, "缺少 baseUrl");

            var provider = BuildProvider(body);
            try
            {
                var ids = ProviderCatalog.FetchModelsAsync(provider).GetAwaiter().GetResult();
                if (string.Equals(provider.Kind, "openrouter", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var models = OpenRouterCatalog.FetchModelsAsync(AppSettings.ResolveKey(provider))
                            .GetAwaiter().GetResult();
                        if (models.Count > 0) ModelCatalog.Set(models);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("刷新免费模型目录失败：" + ex.Message);
                    }
                }
                return Json(req, new { ok = true, models = ids });
            }
            catch (Exception ex)
            {
                return Bad(req, "拉取失败：" + ex.Message);
            }
        }

        private static bool TestProvider(WebRequest req)
        {
            var body = ReadBody<ProviderRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.BaseUrl))
                return Bad(req, "缺少 baseUrl");
            if (string.IsNullOrWhiteSpace(body.Model))
                return Bad(req, "缺少 model");

            var provider = BuildProvider(body);
            try
            {
                // 与 WinForms 的「测试」一致：真的发一次最小补全
                var reasoner = new OpenAiCompatibleReasoner(
                    provider.BaseUrl, body.Model, AppSettings.ResolveKey(provider));
                var reply = reasoner.CompleteAsync([new LlmMessage("user", "只回复两个字母：ok")])
                    .GetAwaiter().GetResult();
                return Json(req, new { ok = true, reply = reply.Content?.Trim() ?? "" });
            }
            catch (Exception ex)
            {
                return Bad(req, "连接失败：" + ex.Message);
            }
        }

        /// <summary>界面传上来的密钥优先；没传就沿用同名提供商已保存的密钥。</summary>
        private static ProviderConfig BuildProvider(ProviderRequest body)
        {
            var settings = AppSettings.Load();
            var old = (settings.Providers ?? []).FirstOrDefault(p =>
                string.Equals(p.Name, body.Name, StringComparison.OrdinalIgnoreCase));
            return new ProviderConfig
            {
                Name = body.Name ?? old?.Name ?? "",
                Kind = string.IsNullOrWhiteSpace(body.Kind) ? old?.Kind ?? "custom" : body.Kind,
                BaseUrl = body.BaseUrl,
                KeyProtected = body.Key != null ? AppSettings.Protect(body.Key) : old?.KeyProtected ?? ""
            };
        }

        // ---------------- 技能 ----------------

        private static object SkillRows(WebRequest req)
        {
            string workspace = Query(req, "workspace");
            bool global = string.Equals(Query(req, "scope"), "global", StringComparison.Ordinal);
            if (!global && string.IsNullOrWhiteSpace(workspace))
                return new { root = "", rows = Array.Empty<object>(), error = "没有工作区：先在项目里打开一个会话" };

            string root = global ? AppSettings.GlobalSkillsPath : Path.Combine(workspace, ".openloop", "skills");
            IReadOnlyList<SkillInfo> catalog;
            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                catalog = SkillCatalog.Load(global ? null : workspace, AppSettings.GlobalSkillsPath);
                if (global)
                {
                    var disabled = SkillStore.LoadDisabled(AppSettings.GlobalSkillsStatePath);
                    foreach (var skill in catalog)
                        if (!disabled.Contains(skill.Name)) enabled.Add(skill.Name);
                }
                else
                {
                    using var loop = new LoopApiClient();
                    var payload = loop.GetActivationAsync(workspace).GetAwaiter().GetResult();
                    foreach (string name in payload?.Skills ?? []) enabled.Add(name);
                }
            }
            catch (Exception ex)
            {
                return new { root, rows = Array.Empty<object>(), error = "读取技能失败：" + ex.Message };
            }

            var rows = catalog
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => new
                {
                    name = s.Name,
                    description = s.Description,
                    global = s.Global,
                    source = s.Source,
                    directory = s.Directory,
                    enabled = enabled.Contains(s.Name),
                    opencode = (s.Source ?? "").StartsWith(".opencode/", StringComparison.Ordinal)
                })
                .ToArray();
            return new { root, rows };
        }

        private static bool ToggleSkill(WebRequest req)
        {
            var body = ReadBody<SkillRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return Bad(req, "缺少 name");

            try
            {
                if (string.Equals(body.Scope, "global", StringComparison.OrdinalIgnoreCase))
                {
                    SkillStore.SetEnabled(AppSettings.GlobalSkillsStatePath, body.Name, body.Enabled);
                }
                else
                {
                    using var loop = new LoopApiClient();
                    loop.SetSkillAsync(body.Workspace ?? "", body.Name, body.Enabled).GetAwaiter().GetResult();
                }
                return Json(req, new { ok = true });
            }
            catch (Exception ex)
            {
                return Bad(req, "切换失败：" + ex.Message);
            }
        }

        private static bool InstallSkill(WebRequest req)
        {
            var body = ReadBody<SkillRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.Path)) return Bad(req, "缺少技能目录 path");

            bool global = string.Equals(body.Scope, "global", StringComparison.OrdinalIgnoreCase);
            string workspace = body.Workspace ?? "";
            if (!global && string.IsNullOrWhiteSpace(workspace)) return Bad(req, "没有工作区");

            string root = global ? AppSettings.GlobalSkillsPath : Path.Combine(workspace, ".openloop", "skills");
            var others = global
                ? []
                : new[]
                {
                    Path.Combine(workspace, ".opencode", "skills"),
                    Path.Combine(workspace, ".openloop", "skills")
                }.Where(r => !string.Equals(
                    Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase));
            try
            {
                string target = SkillStore.Install(body.Path, root, others);
                return Json(req, new { ok = true, directory = target });
            }
            catch (Exception ex)
            {
                return Bad(req, "安装失败：" + ex.Message);
            }
        }

        private static bool UninstallSkill(WebRequest req)
        {
            var body = ReadBody<SkillRequest>(req);
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return Bad(req, "缺少 name");

            bool global = string.Equals(body.Scope, "global", StringComparison.OrdinalIgnoreCase);
            string workspace = body.Workspace ?? "";
            if (!global && string.IsNullOrWhiteSpace(workspace)) return Bad(req, "没有工作区");

            try
            {
                var catalog = SkillCatalog.Load(global ? null : workspace, AppSettings.GlobalSkillsPath);
                var skill = catalog.FirstOrDefault(s =>
                    string.Equals(s.Name, body.Name, StringComparison.OrdinalIgnoreCase));
                if (skill == null) return Bad(req, "找不到技能：" + body.Name);
                if (!global && skill.Global) return Bad(req, "全局技能请切到「全局」里卸载");
                if (!global && (skill.Source ?? "").StartsWith(".opencode/", StringComparison.Ordinal))
                    return Bad(req, "这个技能装在 .opencode/skills 里，由 opencode 安装，请在那里删除");

                string root = global ? AppSettings.GlobalSkillsPath : Path.GetDirectoryName(skill.Directory);
                SkillStore.Uninstall(skill, root);
                if (global) SkillStore.SetEnabled(AppSettings.GlobalSkillsStatePath, skill.Name, true);
                return Json(req, new { ok = true });
            }
            catch (Exception ex)
            {
                return Bad(req, "卸载失败：" + ex.Message);
            }
        }

        // ---------------- 基础设施 ----------------

        private static string Query(WebRequest req, string name) => req.Queries?[name] ?? "";

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

        private static bool Bad(WebRequest req, string error) => Json(req, new { ok = false, error }, 400);

        private static bool NotFound(WebRequest req)
        {
            req.Response.StatusCode = 404;
            req.Response.Buffer = Encoding.UTF8.GetBytes("not found");
            return true;
        }

        private sealed class SettingsRequest
        {
            public string Language { get; set; }
            public string Theme { get; set; }
            public string WebFetchMode { get; set; }
            public bool? FullAccess { get; set; }
            public bool? AutoApprove { get; set; }
            public bool? OpenCodeAskPermissions { get; set; }
            public bool? UseAgentsMd { get; set; }
            public bool? ThinkingExpandedByDefault { get; set; }
            public bool? AutoEvolve { get; set; }
            public int? AutoEvolveRuns { get; set; }
            public int? AutoEvolveCooldownMinutes { get; set; }
            public int? AutoEvolvePerDay { get; set; }
            public List<ProviderRequest> Providers { get; set; }
            public Dictionary<string, ModelProps> ModelProps { get; set; }
        }

        private sealed class ProviderRequest
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public string BaseUrl { get; set; }
            public string Key { get; set; }
            public string Model { get; set; }
            public List<string> Models { get; set; }
        }

        private sealed class SkillRequest
        {
            public string Workspace { get; set; }
            public string Scope { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public bool Enabled { get; set; }
        }
    }
}
