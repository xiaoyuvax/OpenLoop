using System.Text;
using System.Text.Json;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Loop
{
    public sealed class Proposal
    {
        public ActivationRecord Candidate { get; init; }
        public string Raw { get; init; }
        public string Error { get; init; }
        public IReadOnlyList<string> Problems { get; init; } = [];
    }

    /// <summary>
    /// Loop 的构思端：读账本、提候选，不裁决。
    /// 看不到考核题（O5 隔离），也改不了判据。
    /// </summary>
    public sealed class LoopAgent
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly IReasoner _reasoner;

        public LoopAgent(IReasoner reasoner)
            => _reasoner = reasoner ?? throw new ArgumentNullException(nameof(reasoner));

        public string BuildPacket(Ledger ledger, int runLimit = 20)
        {
            var current = ledger.Current;
            var features = ledger.Features();
            var enabled = current?.Features.Select(f => f.Id).ToHashSet(StringComparer.Ordinal)
                          ?? new HashSet<string>(StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.AppendLine($"# 当前版本 v{current?.Version ?? 0}");
            if (current != null)
            {
                sb.AppendLine("已启用：");
                foreach (var reference in current.Features)
                    sb.Append("- ").Append(reference.Id).Append(' ').AppendLine(RenderParams(reference));
            }
            else
            {
                sb.AppendLine("（没有激活记录）");
            }
            sb.AppendLine();

            sb.AppendLine("# 特征目录（id · 它解决什么问题）");
            foreach (var feature in features)
            {
                sb.Append("- ").Append(feature.Id).Append(" · ").AppendLine(feature.Problem);
                if (feature.Kind == "rule" && feature.Payload.ValueKind == JsonValueKind.String)
                    sb.Append("  规则正文：").AppendLine(Trim(feature.Payload.GetString(), 400));
            }
            sb.AppendLine();

            var dormant = features.Where(f => !enabled.Contains(f.Id)).ToList();
            sb.AppendLine("# 未启用（可考虑）");
            if (dormant.Count == 0) sb.AppendLine("（无）");
            foreach (var feature in dormant)
                sb.Append("- ").Append(feature.Id).AppendLine();
            sb.AppendLine();

            var runs = ledger.Runs();
            sb.AppendLine("# 最近运行");
            if (runs.Count == 0) sb.AppendLine("（无）");
            foreach (var run in runs.TakeLast(runLimit))
                sb.Append("- ")
                  .Append(run.Outcome).Append(" · ")
                  .Append(run.Steps).Append(" 步 · ")
                  .Append(run.Cost?.ToString("0.0000") ?? "成本未知").Append(" · 任务：")
                  .AppendLine(Trim(run.Task, 80));
            var counts = runs.GroupBy(r => r.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal);
            sb.Append("统计：").AppendLine(counts.Count() == 0
                ? "无"
                : string.Join("，", counts.Select(g => $"{g.Key}={g.Count()}")));
            sb.AppendLine();

            sb.AppendLine("# 你可以做的");
            sb.AppendLine("只改 Agent 的激活集：启用/停用已有特征、调整阈值、调整规则顺序。");
            sb.AppendLine("禁止：新增特征、改判据、改工具、改提示词协议。");
            sb.AppendLine("你看不到考核题，也不应该猜它。");
            sb.AppendLine();
            sb.AppendLine("# 输出格式");
            sb.AppendLine("只输出一个 JSON 对象，不要解释：");
            sb.AppendLine("{\"note\":\"一句话说明这轮想改善什么\",\"features\":[{\"id\":\"...\",\"params\":{\"enabled\":true}}]}");
            sb.AppendLine("features 是**完整快照**：要启用的全部列出；没列出的即停用。");
            sb.AppendLine("一个都不改就等于没有候选，这轮什么也不会发生。");
            return sb.ToString();
        }

        public async Task<Proposal> ProposeAsync(
            string packet, ActivationRecord baseline,
            IReadOnlyDictionary<string, FeatureRecord> features, CancellationToken ct = default)
        {
            var messages = new List<LlmMessage>
            {
                new("system", "你是一个只输出 JSON 的构思器。不输出解释，不输出代码围栏。"),
                new("user", packet)
            };
            var completion = await _reasoner.CompleteAsync(messages, ct);
            string raw = completion?.Content ?? "";
            string json = ExtractJson(raw);
            if (json == null) return new Proposal { Raw = raw, Error = "输出里没有 JSON 对象" };

            ProposalDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<ProposalDto>(json, Json);
            }
            catch (JsonException ex)
            {
                return new Proposal { Raw = raw, Error = "JSON 解析失败：" + ex.Message };
            }
            if (dto?.Features is not { Count: > 0 })
                return new Proposal { Raw = raw, Error = "候选没有 features" };

            var candidate = new ActivationRecord
            {
                Version = baseline.Version + 1,
                At = DateTimeOffset.UtcNow.ToString("O"),
                By = "loop",
                Note = dto.Note,
                Features = dto.Features
                    .Where(f => f?.Id != null)
                    .Select(f => new FeatureRef
                    {
                        Id = f.Id,
                        Params = features.TryGetValue(f.Id, out var known) && known.Kind == "rule"
                            ? null
                            : f.Params
                    })
                    .ToList()
            };

            var problems = CandidateValidator.Validate(candidate, features);
            return new Proposal
            {
                Candidate = candidate,
                Raw = raw,
                Problems = problems,
                Error = problems.Count > 0 ? "候选不合法" : null
            };
        }

        private static string RenderParams(FeatureRef reference)
            => reference.Params is not { Count: > 0 }
                ? ""
                : JsonSerializer.Serialize(reference.Params);

        private static string Trim(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            for (int start = 0; start < text.Length; start++)
            {
                if (text[start] != '{') continue;
                int depth = 0;
                bool inString = false, escape = false;
                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inString)
                    {
                        if (escape) escape = false;
                        else if (c == '\\') escape = true;
                        else if (c == '"') inString = false;
                        continue;
                    }
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0) return text[start..(i + 1)];
                    }
                }
            }
            return null;
        }

        private sealed class ProposalDto
        {
            public string Note { get; set; }
            public List<FeatureDto> Features { get; set; }
        }

        private sealed class FeatureDto
        {
            public string Id { get; set; }
            public Dictionary<string, JsonElement> Params { get; set; }
        }
    }
}
