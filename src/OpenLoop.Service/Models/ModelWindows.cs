using System.Text.Json;

namespace OpenLoop.Service.Models
{
    /// <summary>
    /// 服务商报的上下文窗口：<c>/models</c> 返回里的窗口字段，加上插件清单里声明的兜底值。
    /// 各家字段名不一样（<c>context_length</c>/<c>context_window</c>/<c>max_input_tokens</c> …），
    /// 这里统一读掉，免得模型落回 <c>AgentContext.DefaultContextLimit</c>（32K）把长上下文截断。
    /// </summary>
    public static class ModelWindows
    {
        private static readonly Dictionary<string, int> Reported = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Declared = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> DeclaredPrefixes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new();

        /// <summary>
        /// 清单声明的 provider 级上限。前缀本身就是归属声明，所以只写前缀也能盖住目录里新发现的模型；
        /// <paramref name="limit"/> 不大于 0 表示撤掉声明。
        /// </summary>
        public static void Declare(string prefix, IEnumerable<string> models, int limit)
        {
            string head = prefix ?? "";
            lock (Sync)
            {
                if (head.Length > 0)
                {
                    if (limit > 0) DeclaredPrefixes[head] = limit;
                    else DeclaredPrefixes.Remove(head);
                }

                foreach (string id in models ?? [])
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    string key = Strip(id, head);
                    if (limit > 0) Declared[key] = limit;
                    else Declared.Remove(key);
                }
            }
        }

        /// <summary>目录里报的值；<paramref name="limit"/> 不大于 0 表示这家没报，忽略。</summary>
        public static void Report(string model, int limit)
        {
            if (limit <= 0 || string.IsNullOrWhiteSpace(model)) return;
            lock (Sync) Reported[model.Trim()] = limit;
        }

        /// <summary>清单声明（按界面上看到的 id 找，带前缀也行）：只有 provider 声明过才返回，否则 0。</summary>
        public static int DeclaredLimit(string model)
        {
            string id = model?.Trim();
            if (string.IsNullOrEmpty(id)) return 0;
            string leaf = Leaf(id);

            lock (Sync)
            {
                if (TryMatch(Declared, id, leaf) is int byModel) return byModel;

                // 最长前缀优先，避免 / 这类短前缀抢走更具体的声明
                string best = null;
                int bestLimit = 0;
                foreach (var (prefix, limit) in DeclaredPrefixes)
                    if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && (best == null || prefix.Length > best.Length))
                    {
                        best = prefix;
                        bestLimit = limit;
                    }
                return bestLimit;
            }
        }

        /// <summary>服务商在 <c>/models</c> 里报的值；没报返回 0。</summary>
        public static int ReportedLimit(string model)
        {
            string id = model?.Trim();
            if (string.IsNullOrEmpty(id)) return 0;
            string leaf = Leaf(id);
            lock (Sync) return TryMatch(Reported, id, leaf) ?? 0;
        }

        /// <summary>从 <c>/models</c> 的一条模型记录里读上下文窗口；读不到返回 0。</summary>
        public static int Read(JsonElement model)
        {
            if (model.ValueKind != JsonValueKind.Object) return 0;

            foreach (var property in model.EnumerateObject())
            {
                if (!Names.Any(n => string.Equals(n, property.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                int limit = Number(property.Value);
                if (limit > 0) return limit;
            }
            return 0;
        }

        /// <summary>先在表里找整串，再找最后一段（服务商常只报不带厂商前缀的 id）。</summary>
        private static int? TryMatch(Dictionary<string, int> table, string id, string leaf)
        {
            if (table.TryGetValue(id, out int exact)) return exact;
            if (leaf.Length == 0 || leaf == id) return null;
            if (table.TryGetValue(leaf, out int byLeaf)) return byLeaf;
            return null;
        }

        private static string Leaf(string id) => id[(id.LastIndexOf('/') + 1)..];

        private static string Strip(string model, string prefix)
        {
            string id = model.Trim();
            return prefix.Length > 0 && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? id[prefix.Length..]
                : id;
        }

        private static readonly string[] Names =
            ["context_length", "context_window", "max_context_length", "max_input_tokens"];

        private static int Number(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out int n) ? n : 0,
            JsonValueKind.String => int.TryParse(value.GetString(), out int n) ? n : 0,
            _ => 0
        };
    }
}
