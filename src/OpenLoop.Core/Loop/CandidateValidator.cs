using System.Text.Json;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Loop
{
    /// <summary>
    /// 候选校验：只能引用账本里已有的具名特征，参数在允许范围内。
    /// 判据不在这里——判据由 User 提供，Loop 碰不到。
    /// </summary>
    public static class CandidateValidator
    {
        public static IReadOnlyList<string> Validate(
            ActivationRecord candidate, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var problems = new List<string>();
            if (candidate == null) { problems.Add("候选为空"); return problems; }
            if (candidate.Features is not { Count: > 0 })
            {
                problems.Add("候选没有启用任何特征");
                return problems;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in candidate.Features)
            {
                if (reference?.Id == null) { problems.Add("候选里有空引用"); continue; }
                if (!seen.Add(reference.Id)) { problems.Add($"特征重复：{reference.Id}"); continue; }
                if (!features.TryGetValue(reference.Id, out var feature))
                {
                    problems.Add($"账本里没有特征：{reference.Id}（不得凭空命名）");
                    continue;
                }

                if (feature.Kind == "rule")
                {
                    if (reference.Params is { Count: > 0 })
                        problems.Add($"{reference.Id} 是规则，不接受参数");
                    if (feature.Payload.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(feature.Payload.GetString()))
                        problems.Add($"{reference.Id} 的规则文本为空");
                    continue;
                }

                var allowed = AllowedKeys(reference.Id);
                foreach (string key in reference.Params?.Keys ?? Enumerable.Empty<string>())
                    if (!allowed.Contains(key))
                        problems.Add($"{reference.Id} 不认识参数：{key}");

                if (reference.Params != null && reference.Params.TryGetValue("enabled", out var enabled)
                    && enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    problems.Add($"{reference.Id} 的 enabled 必须是布尔值");

                switch (reference.Id)
                {
                    case FeatureCatalog.RepeatCall:
                        CheckNumber(problems, reference, feature, "max", 2, 20);
                        break;
                    case FeatureCatalog.Exploration:
                        CheckNumber(problems, reference, feature, "max", 2, 200);
                        break;
                    case FeatureCatalog.MalformedRetry:
                        CheckNumber(problems, reference, feature, "max", 0, 5);
                        break;
                    case FeatureCatalog.MaxSteps:
                        CheckNumber(problems, reference, feature, "value", 1, 100);
                        break;
                    case FeatureCatalog.ToolResultLimit:
                        CheckNumber(problems, reference, feature, "value", 500, 200_000);
                        break;
                    case FeatureCatalog.CompressAt:
                        CheckNumber(problems, reference, feature, "value", 0.1, 0.95);
                        break;
                }
            }
            return problems;
        }

        private static string[] AllowedKeys(string id) => id switch
        {
            FeatureCatalog.RepeatCall or FeatureCatalog.Exploration or FeatureCatalog.MalformedRetry
                => ["enabled", "max"],
            FeatureCatalog.MaxSteps or FeatureCatalog.ToolResultLimit or FeatureCatalog.CompressAt
                => ["value"],
            _ => ["enabled"]
        };

        private static void CheckNumber(
            List<string> problems, FeatureRef reference, FeatureRecord feature,
            string name, double min, double max)
        {
            if (!TryNumber(reference, feature, name, out double value))
            {
                problems.Add($"{reference.Id} 的 {name} 不是数字");
                return;
            }
            if (value < min || value > max)
                problems.Add($"{reference.Id} 的 {name}={value} 超出允许范围 [{min}, {max}]");
        }

        private static bool TryNumber(
            FeatureRef reference, FeatureRecord feature, string name, out double value)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind == JsonValueKind.Number && own.TryGetDouble(out value))
                return true;
            if (feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var fallback)
                && fallback.ValueKind == JsonValueKind.Number && fallback.TryGetDouble(out value))
                return true;
            value = 0;
            return false;
        }
    }
}
