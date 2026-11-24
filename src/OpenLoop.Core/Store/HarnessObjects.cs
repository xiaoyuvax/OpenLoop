using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>工作区 Harness 配置里的一件对象（列表用）：当前是否在激活集里、生效的参数是什么。</summary>
    public sealed class HarnessItem
    {
        public string Id { get; init; }
        public string Kind { get; init; }

        /// <summary>给人看的名字：技能取 payload 里的 name，其余类就是 id。</summary>
        public string Name { get; init; }

        /// <summary>特征登记时写的「解决什么问题」，界面当说明列用。</summary>
        public string Problem { get; init; }

        /// <summary>规则正文（只有 rule 有）。</summary>
        public string Text { get; init; }

        public bool Active { get; init; }

        /// <summary>生效的参数（引用的值覆盖特征里的默认值），已格式化成文本。</summary>
        public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);

        /// <summary>参数的默认值文本（恢复默认时回填）。</summary>
        public Dictionary<string, string> Defaults { get; init; } = new(StringComparer.Ordinal);

        /// <summary>参数一行文本：<c>max=2, enabled=true</c>（无参数为空串）。</summary>
        public string ParamsText => string.Join(", ", Values.Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>
    /// 工作区 Harness 配置的纯逻辑：列出某类对象、把参数文本解析成引用、按勾选与参数生成激活集。
    /// 不碰文件、不知道界面——表单与测试都只调这里。
    /// </summary>
    public static class HarnessObjects
    {
        public const string Skill = "skill";
        public const string Rule = "rule";
        public const string Limit = "limit";
        public const string Guard = "guard";

        /// <summary>配置窗体上的四类（Prompt 不在这里：它经目标会话由模型落盘）。</summary>
        public static readonly string[] Kinds = [Skill, Rule, Limit, Guard];

        public static IReadOnlyList<HarnessItem> List(Ledger ledger, string kind)
        {
            if (ledger == null || string.IsNullOrWhiteSpace(kind)) return [];
            var features = ledger.Features();
            var active = (ledger.Current?.Features ?? [])
                .Where(r => r?.Id != null)
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var items = new List<HarnessItem>();
            foreach (var feature in features
                         .Where(f => string.Equals(f.Kind, kind, StringComparison.Ordinal))
                         .OrderBy(f => f.Id, StringComparer.Ordinal))
            {
                active.TryGetValue(feature.Id, out var reference);
                items.Add(new HarnessItem
                {
                    Id = feature.Id,
                    Kind = feature.Kind,
                    Name = NameOf(feature),
                    Problem = feature.Problem,
                    Text = PayloadString(feature),
                    Active = reference != null,
                    Values = ValuesOf(feature, reference),
                    Defaults = ValuesOf(feature, null)
                });
            }
            return items;
        }

        /// <summary>这一类对象存在哪儿（配置窗体顶部那行路径）：技能是磁盘目录，其余类在账本里。</summary>
        public static string KindPath(string workspace, string kind)
        {
            string root = Path.Combine(workspace ?? "", ".openloop");
            return kind switch
            {
                Skill => Path.Combine(root, "skills") + "  ·  " + Path.Combine(root, "ledger.jsonl"),
                Rule => Path.Combine(root, "ledger.jsonl") + "  ·  种子里另两条在 FeatureCatalog",
                _ => Path.Combine(root, "ledger.jsonl") + "  ·  激活集在 " + Path.Combine(root, "defs")
            };
        }

        /// <summary>技能的 payload 里有 name；其余类显示 id 本身（id 就是它的名字）。</summary>
        public static string NameOf(FeatureRecord feature)
        {
            if (feature?.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                string text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return feature?.Id ?? "";
        }

        /// <summary>特征里声明的参数默认值（对象 payload 的每个键），规则正文这类字符串 payload 没有参数。</summary>
        public static Dictionary<string, string> ValuesOf(FeatureRecord feature, FeatureRef reference)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (feature?.Payload.ValueKind == JsonValueKind.Object)
                foreach (var property in feature.Payload.EnumerateObject())
                    values[property.Name] = Text(property.Value);
            if (reference?.Params != null)
                foreach (var pair in reference.Params)
                    values[pair.Key] = Text(pair.Value);
            return values;
        }

        /// <summary>
        /// 把「max=2, enabled=true」解析成引用参数；值的类型按特征里的默认值定，
        /// 特征里没有的键不收（写进去也没人读）。返回 null 表示文本非法，error 说明原因。
        /// </summary>
        public static Dictionary<string, JsonElement> ParseParams(
            FeatureRecord feature, string text, out string error)
        {
            error = null;
            var parsed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string chunk in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string pair = chunk.Trim();
                if (pair.Length == 0) continue;
                int index = pair.IndexOf('=');
                if (index <= 0 || index == pair.Length - 1)
                {
                    error = $"参数写法是「名字=值」：{pair}";
                    return null;
                }

                string name = pair[..index].Trim();
                string value = pair[(index + 1)..].Trim();
                if (!TryKindOf(feature, name, out JsonValueKind kind))
                {
                    error = $"这个对象没有参数「{name}」";
                    return null;
                }

                switch (kind)
                {
                    case JsonValueKind.True or JsonValueKind.False:
                        if (!bool.TryParse(value, out bool flag))
                        {
                            error = $"参数「{name}」只能是 true / false";
                            return null;
                        }
                        parsed[name] = JsonSerializer.SerializeToElement(flag);
                        break;
                    case JsonValueKind.Number:
                        if (int.TryParse(value, out int integer))
                            parsed[name] = JsonSerializer.SerializeToElement(integer);
                        else if (double.TryParse(value, out double real))
                            parsed[name] = JsonSerializer.SerializeToElement(real);
                        else
                        {
                            error = $"参数「{name}」只能是数字";
                            return null;
                        }
                        break;
                    default:
                        parsed[name] = JsonSerializer.SerializeToElement(value);
                        break;
                }
            }
            return parsed;
        }

        private static bool TryKindOf(FeatureRecord feature, string name, out JsonValueKind kind)
        {
            kind = JsonValueKind.Undefined;
            if (feature?.Payload.ValueKind != JsonValueKind.Object) return false;
            if (!feature.Payload.TryGetProperty(name, out var value)) return false;
            kind = value.ValueKind;
            return true;
        }

        /// <summary>
        /// 在现有激活集上换掉某一类的成员：其余类原样、原顺序保留，被替换的类按 replacement 的顺序接在后面。
        /// </summary>
        public static List<FeatureRef> Replace(
            ActivationRecord current, IReadOnlyDictionary<string, FeatureRecord> features,
            string kind, IEnumerable<FeatureRef> replacement)
        {
            var result = new List<FeatureRef>();
            foreach (var reference in current?.Features ?? [])
            {
                if (reference?.Id == null) continue;
                string actual = features.TryGetValue(reference.Id, out var feature)
                    ? feature.Kind
                    : KindOfSeedId(reference.Id);
                if (!string.Equals(actual, kind, StringComparison.Ordinal)) result.Add(reference);
            }
            result.AddRange(replacement ?? []);
            return result;
        }

        /// <summary>某一类的出厂默认引用（种子激活集里同类的那几条）；技能没有默认。</summary>
        public static IReadOnlyList<FeatureRef> Defaults(string kind)
            => string.Equals(kind, Skill, StringComparison.Ordinal)
                ? []
                : FeatureCatalog.SeedActivation().Features
                    .Where(r => r?.Id != null && KindOfSeedId(r.Id) == kind)
                    .ToList();

        /// <summary>种子 id 前缀就是它的类（guard./limit./rule.）；技能 id 带哈希，单列。</summary>
        public static string KindOfSeedId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("guard.", StringComparison.Ordinal)) return Guard;
            if (id.StartsWith("limit.", StringComparison.Ordinal)) return Limit;
            if (id.StartsWith("rule.", StringComparison.Ordinal)) return Rule;
            if (id.StartsWith("skill.", StringComparison.Ordinal)) return Skill;
            return "";
        }

        private static string PayloadString(FeatureRecord feature)
            => feature?.Payload.ValueKind == JsonValueKind.String ? feature.Payload.GetString() ?? "" : "";

        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.TryGetInt64(out long integer)
                ? integer.ToString()
                : value.GetDouble().ToString("0.###"),
            _ => value.ToString()
        };
    }
}
