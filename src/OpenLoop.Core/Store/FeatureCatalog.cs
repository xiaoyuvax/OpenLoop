using System.Text.Json;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 种子特征与「激活记录 → 运行时形态」的映射。
    /// 映射表是代码，因为消费者是代码（规范 §5.3：表示形式由消费者决定）。
    /// </summary>
    public static class FeatureCatalog
    {
        public const string RepeatCall = "guard.repeat_call";
        public const string Exploration = "guard.exploration";
        public const string Claim = "guard.claim";
        public const string Promise = "guard.promise";
        public const string ArgRepair = "repair.args";
        public const string MalformedRetry = "retry.malformed";
        public const string MaxSteps = "limit.steps";
        public const string ToolResultLimit = "limit.tool_result";
        public const string CompressAt = "compress.at";

        public static IReadOnlyList<FeatureRecord> SeedFeatures() =>
        [
            new FeatureRecord
            {
                Id = RepeatCall, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型连续用完全相同的参数调用同一工具，步数被白白烧掉。",
                Payload = J(new { enabled = true, max = 3 })
            },
            new FeatureRecord
            {
                Id = Exploration, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型连续只读侦察、迟迟不落笔，任务停在原地。",
                Payload = J(new { enabled = true, max = 30 })
            },
            new FeatureRecord
            {
                Id = Claim, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型没调用任何工具就声称已写入/已修改，结果不可信。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = Promise, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型只回「请稍候」这类占位话术，不真正执行。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = ArgRepair, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型把参数类型写错导致工具失败，本可自愈的一次错误却中断任务。",
                Payload = J(new { enabled = true })
            },
            new FeatureRecord
            {
                Id = MalformedRetry, Kind = "guard", Source = "user", CreatedAt = Now(),
                Problem = "模型输出半截 JSON，工具调用解析失败，整轮直接作废。",
                Payload = J(new { enabled = true, max = 2 })
            },
            new FeatureRecord
            {
                Id = MaxSteps, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "任务无进展时限不住步数，成本失控。",
                Payload = J(new { value = 100 })
            },
            new FeatureRecord
            {
                Id = ToolResultLimit, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "单个工具结果过长，挤占上下文、推高成本。",
                Payload = J(new { value = 6000 })
            },
            new FeatureRecord
            {
                Id = CompressAt, Kind = "limit", Source = "user", CreatedAt = Now(),
                Problem = "上下文超过窗口后请求直接失败。",
                Payload = J(new { value = 0.8 })
            },
            new FeatureRecord
            {
                Id = "rule.read_before_write", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "不先读现场就写，覆盖掉不知道的内容。",
                Payload = JsonSerializer.SerializeToElement("写文件前先用 list_dir / read_file 看清现场；不确定是否存在或内容时，先读再改。")
            },
            new FeatureRecord
            {
                Id = "rule.answer_first", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "回答绕圈子，用户要翻过三段铺垫才看到结论。",
                Payload = JsonSerializer.SerializeToElement("最终回答先给结论或结果，再给依据与后续建议。")
            },
            new FeatureRecord
            {
                Id = "rule.ask_when_unsure", Kind = "rule", Source = "user", CreatedAt = Now(),
                Problem = "参数拿不准时硬猜，产生删除或覆盖这类破坏性操作。",
                Payload = JsonSerializer.SerializeToElement("可能删除或覆盖内容、且参数拿不准时，先问一句再动手。")
            }
        ];

        public static ActivationRecord SeedActivation() => new()
        {
            Version = 1,
            At = Now(),
            By = "user",
            Note = "v0 默认集",
            Features =
            [
                Ref(RepeatCall, ("enabled", true), ("max", 3)),
                Ref(Exploration, ("enabled", true), ("max", 30)),
                Ref(Claim, ("enabled", true)),
                Ref(Promise, ("enabled", true)),
                Ref(ArgRepair, ("enabled", true)),
                Ref(MalformedRetry, ("enabled", true), ("max", 2)),
                Ref(MaxSteps, ("value", 100)),
                Ref(ToolResultLimit, ("value", 6000)),
                Ref(CompressAt, ("value", 0.8)),
                Ref("rule.read_before_write"),
                Ref("rule.answer_first"),
                Ref("rule.ask_when_unsure")
            ]
        };

        public static AgentPolicy PolicyFrom(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var policy = new AgentPolicy
            {
                RepeatCallGuard = false,
                ExplorationGuard = false,
                ClaimGuard = false,
                PromiseGuard = false,
                ArgRepair = false,
                MalformedRetry = false
            };
            if (activation == null) return policy;

            foreach (var reference in activation.Features)
            {
                if (reference?.Id == null || !features.TryGetValue(reference.Id, out var feature)) continue;
                switch (reference.Id)
                {
                    case RepeatCall:
                        policy.RepeatCallGuard = Flag(reference, feature, "enabled");
                        policy.MaxRepeatCalls = Number(reference, feature, "max", policy.MaxRepeatCalls);
                        break;
                    case Exploration:
                        policy.ExplorationGuard = Flag(reference, feature, "enabled");
                        policy.MaxExplorationSteps = Number(reference, feature, "max", policy.MaxExplorationSteps);
                        break;
                    case Claim:
                        policy.ClaimGuard = Flag(reference, feature, "enabled");
                        break;
                    case Promise:
                        policy.PromiseGuard = Flag(reference, feature, "enabled");
                        break;
                    case ArgRepair:
                        policy.ArgRepair = Flag(reference, feature, "enabled");
                        break;
                    case MalformedRetry:
                        policy.MalformedRetry = Flag(reference, feature, "enabled");
                        policy.MaxMalformedRetries = Number(reference, feature, "max", policy.MaxMalformedRetries);
                        break;
                    case MaxSteps:
                        policy.MaxSteps = Number(reference, feature, "value", policy.MaxSteps);
                        break;
                    case ToolResultLimit:
                        policy.ToolResultLimit = Number(reference, feature, "value", policy.ToolResultLimit);
                        break;
                    case CompressAt:
                        policy.CompressAt = Real(reference, feature, "value", policy.CompressAt);
                        break;
                }
            }
            return policy;
        }

        /// <summary>按激活顺序取出规则文本（顺序即提示词顺序）。</summary>
        public static IReadOnlyList<string> RulesFrom(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var rules = new List<string>();
            if (activation == null) return rules;

            foreach (var reference in activation.Features)
            {
                if (reference?.Id == null || !features.TryGetValue(reference.Id, out var feature)) continue;
                if (feature.Kind != "rule" || feature.Payload.ValueKind != JsonValueKind.String) continue;
                string text = feature.Payload.GetString();
                if (!string.IsNullOrWhiteSpace(text)) rules.Add(text.Trim());
            }
            return rules;
        }

        private static FeatureRef Ref(string id, params (string Name, object Value)[] parameters) => new()
        {
            Id = id,
            Params = parameters.Length == 0
                ? null
                : parameters.ToDictionary(p => p.Name, p => J(p.Value))
        };

        private static JsonElement J(object value) => value switch
        {
            bool b => JsonSerializer.SerializeToElement(b),
            int i => JsonSerializer.SerializeToElement(i),
            double d => JsonSerializer.SerializeToElement(d),
            string s => JsonSerializer.SerializeToElement(s),
            _ => JsonSerializer.SerializeToElement(value)
        };

        private static bool Flag(FeatureRef reference, FeatureRecord feature, string name)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return own.GetBoolean();
            return feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var fallback)
                && fallback.ValueKind is JsonValueKind.True or JsonValueKind.False
                && fallback.GetBoolean();
        }

        private static int Number(FeatureRef reference, FeatureRecord feature, string name, int fallback)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind == JsonValueKind.Number && own.TryGetInt32(out int value))
                return value;
            if (feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var def)
                && def.ValueKind == JsonValueKind.Number && def.TryGetInt32(out int fallbackValue))
                return fallbackValue;
            return fallback;
        }

        private static double Real(FeatureRef reference, FeatureRecord feature, string name, double fallback)
        {
            if (reference.Params != null && reference.Params.TryGetValue(name, out var own)
                && own.ValueKind == JsonValueKind.Number && own.TryGetDouble(out double value))
                return value;
            if (feature.Payload.ValueKind == JsonValueKind.Object
                && feature.Payload.TryGetProperty(name, out var def)
                && def.ValueKind == JsonValueKind.Number && def.TryGetDouble(out double fallbackValue))
                return fallbackValue;
            return fallback;
        }

        private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    }
}
