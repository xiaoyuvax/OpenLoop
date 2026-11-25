using System.Text;
using System.Text.Json;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>/appagent/manifest 里的一条 action（Puppet 页清单与 /puppet 命令共用）。</summary>
    internal sealed class PuppetActionInfo
    {
        public string Name { get; init; } = "";
        public string Desc { get; init; } = "";
        public string Risk { get; init; } = "";
        public string Group { get; init; } = "";
        public IReadOnlyList<string> Parameters { get; init; } = [];

        /// <summary>参数名清单（必填的排前面）：清单里「参数」一列。</summary>
        public string ParamText => Parameters.Count == 0 ? "—" : string.Join(", ", Parameters);
    }

    /// <summary>一次清单快照：实例名 + actions + 失败原因（失败时 Actions 为空，Error 说明为什么）。</summary>
    internal sealed class PuppetActionSheet
    {
        public string Instance { get; init; } = PuppetActions.MainInstance;
        public IReadOnlyList<PuppetActionInfo> Actions { get; init; } = [];
        public string Error { get; init; }
        public DateTime At { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Puppet Action 清单：读壳自己的 B 面 manifest（<c>/appagent/manifest</c>）。
    /// 壳进程内直调 Hub（PuppetBridge.LocalHub），不经端口；Worker 会话页与 Loop 会话页共用这一份。
    /// </summary>
    internal static class PuppetActions
    {
        /// <summary>B 面绑定的实例名：壳的 MainForm（其余实例是运行期窗口，不多列表）。</summary>
        public const string MainInstance = "MainForm";

        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
        private static readonly object Gate = new();
        private static PuppetActionSheet _cache;

        /// <summary>清单（30 秒缓存）；force=true 强制重读。</summary>
        public static async Task<PuppetActionSheet> LoadAsync(bool force = false, CancellationToken ct = default)
        {
            lock (Gate)
            {
                if (!force && _cache != null && DateTime.UtcNow - _cache.At < CacheFor) return _cache;
            }

            PuppetActionSheet sheet;
            try
            {
                string json = await ManifestAsync(ct);
                sheet = Parse(json);
            }
            catch (Exception ex)
            {
                sheet = new PuppetActionSheet { Error = ex.Message };
            }

            lock (Gate) _cache = sheet;
            return sheet;
        }

        /// <summary>已取到的清单（可能已过期）：启用提示要在同步路径里拼，不能等异步。</summary>
        public static PuppetActionSheet Cached
        {
            get { lock (Gate) return _cache; }
        }

        /// <summary>清掉缓存：换版/新窗口注册实例后清单会变。</summary>
        public static void Invalidate()
        {
            lock (Gate) _cache = null;
        }

        /// <summary>
        /// /puppet 命令的提示文本：把用户的自然语言参数原样交给会话里的 agent，
        /// 由它按清单对应参数名，用 Puppet 工具执行并回报结果。
        /// </summary>
        public static string NoticeText(PuppetActionSheet sheet, string request, string target = "shell")
        {
            if (string.IsNullOrWhiteSpace(target)) target = "shell";
            string action = request ?? "";
            string args = "";
            int space = action.IndexOfAny([' ', '\t']);
            if (space > 0)
            {
                args = action[(space + 1)..].Trim();
                action = action[..space].Trim();
            }

            var match = sheet?.Actions?.FirstOrDefault(a =>
                string.Equals(a.Name, action, StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            sb.Append("[PuppetAction] 用户要求直接执行 Puppet action，不要改代码、不要自己发明参数。").AppendLine();
            sb.Append("实例：").Append(sheet?.Instance ?? MainInstance).AppendLine();
            if (match != null)
            {
                sb.Append("Action：").Append(match.Name).AppendLine();
                sb.Append("说明：").AppendLine(match.Desc);
                sb.Append("参数：").AppendLine(match.ParamText);
                if (match.Risk.Length > 0) sb.Append("风险：").AppendLine(match.Risk);
            }
            else
            {
                sb.Append("Action：").AppendLine(action);
                if (sheet?.Actions is { Count: > 0 })
                    sb.Append("（清单里没有这个名字，先用 /puppet 打开右侧清单核对；也可能是可用的别名）").AppendLine();
            }
            if (args.Length > 0)
                sb.Append("用户给的参数（自然语言，自行对应到参数名）：").AppendLine(args);
            else
                sb.AppendLine("用户没给参数：需要的参数按清单补齐，拿不准就先问，不要猜。");
            sb.AppendLine();
            sb.Append("执行方式：调用 Puppet 工具 target=").Append(target).Append("、action=invoke、instance=")
              .Append(sheet?.Instance ?? MainInstance)
              .Append("、method=").Append(action)
              .AppendLine("、args=[按 A 面位置顺序传参]；args 是 JSON 数组，不要包成对象。");
            sb.Append("清单与参数类型：Puppet 工具 target=").Append(target)
              .Append("、action=describe、instance=").Append(sheet?.Instance ?? MainInstance)
              .AppendLine("（或右侧「Puppet」页）。");
            sb.Append("执行完把结果一句话回报给用户；失败就原文给出错误，不要重试超过两次。");
            return sb.ToString().TrimEnd();
        }

        private static async Task<string> ManifestAsync(CancellationToken ct)
        {
            var hub = PuppetBridge.LocalHub;
            if (hub == null)
                throw new InvalidOperationException("本进程没有 Puppet Hub（壳未启用 Puppet），读不到 Action 清单");

            var query = new Dictionary<string, object> { ["name"] = MainInstance };
            var response = await hub.GetAsync("/appagent/manifest", query).WaitAsync(ct);
            if (response.Status == 200 && !string.IsNullOrWhiteSpace(response.Body)) return response.Body;
            throw new InvalidOperationException($"读 Action 清单失败（HTTP {response.Status}）：{Clip(response.Body)}");
        }

        private static PuppetActionSheet Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string instance = root.TryGetProperty("instance", out var inst) && inst.ValueKind == JsonValueKind.String
                ? inst.GetString()
                : MainInstance;

            var actions = new List<PuppetActionInfo>();
            if (root.TryGetProperty("actions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    actions.Add(new PuppetActionInfo
                    {
                        Name = Str(item, "name"),
                        Desc = Str(item, "desc"),
                        Risk = Str(item, "risk"),
                        Group = Str(item, "group"),
                        Parameters = Params(item)
                    });
                }
            }

            return new PuppetActionSheet
            {
                Instance = string.IsNullOrWhiteSpace(instance) ? MainInstance : instance,
                Actions = actions.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static IReadOnlyList<string> Params(JsonElement action)
        {
            if (!action.TryGetProperty("parameters", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            var result = new List<(int Rank, string Text)>();
            foreach (var p in list.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                string name = Str(p, "name");
                if (name.Length == 0) continue;
                bool required = p.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
                result.Add((required ? 0 : 1, required ? name : name + "?"));
            }
            return result.Select(r => r.Text).ToList();
        }

        private static string Str(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static string Clip(string text)
            => string.IsNullOrEmpty(text) ? "(空)" : text.Length <= 400 ? text : text[..400] + "…";
    }
}
