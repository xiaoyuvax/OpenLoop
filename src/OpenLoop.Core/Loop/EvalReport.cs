using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Loop
{
    public sealed class EvalReportTask
    {
        public string Id { get; set; }
        public string Fingerprint { get; set; }
        public bool BaselinePassed { get; set; }
        public bool CandidatePassed { get; set; }
        public int BaselineSteps { get; set; }
        public int CandidateSteps { get; set; }
        public decimal? BaselineCost { get; set; }
        public decimal? CandidateCost { get; set; }
        public string BaselineDetail { get; set; }
        public string CandidateDetail { get; set; }
        public string BaselineEvidence { get; set; }
        public string CandidateEvidence { get; set; }
    }

    /// <summary>一次 Gate 评估的持久化结果；给人看，也给后续回归对照。</summary>
    public sealed class EvalReport
    {
        public string Id { get; set; }
        public string At { get; set; }
        public string Binary { get; set; }
        public int BaselineVersion { get; set; }
        public int? CandidateVersion { get; set; }
        public bool Adopted { get; set; }
        public string Reason { get; set; }
        public int BaselinePassed { get; set; }
        public int CandidatePassed { get; set; }
        public int Total { get; set; }
        public decimal? Cost { get; set; }
        public bool BuildOk { get; set; } = true;
        public string BuildDetail { get; set; }
        public IReadOnlyList<ConformanceItem> Conformance { get; set; } = [];
        public List<EvalReportTask> Tasks { get; set; } = [];

        /// <summary>相对工作区的报告目录，例如 eval/reports/r-...。</summary>
        public string DirectoryPath { get; set; }
    }

    /// <summary><workspace>/.openloop/eval/reports/&lt;id&gt;/{report.json,report.md}。</summary>
    public static class EvalReportStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Root(string workspace)
            => Path.Combine(workspace, ".openloop", "eval", "reports");

        public static EvalReport Save(string workspace, EvalReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            report.Id = string.IsNullOrWhiteSpace(report.Id) ? NewId() : report.Id;
            report.At = string.IsNullOrWhiteSpace(report.At) ? DateTimeOffset.UtcNow.ToString("O") : report.At;
            report.Total = report.Tasks.Count;

            string dir = Path.Combine(Root(workspace), report.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "report.json"),
                JsonSerializer.Serialize(report, Json), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "report.md"), RenderMarkdown(report), new UTF8Encoding(false));

            report.DirectoryPath = Path.GetRelativePath(workspace, dir).Replace('\\', '/');
            return report;
        }

        public static IReadOnlyList<EvalReport> List(string workspace)
        {
            string root = Root(workspace);
            if (!Directory.Exists(root)) return [];
            var list = new List<EvalReport>();
            foreach (string dir in Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                string file = Path.Combine(dir, "report.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var report = JsonSerializer.Deserialize<EvalReport>(File.ReadAllText(file, Encoding.UTF8), Json);
                    if (report == null) continue;
                    report.DirectoryPath = Path.GetRelativePath(workspace, dir).Replace('\\', '/');
                    list.Add(report);
                }
                catch
                {
                    // 坏报告跳过，不阻塞列表。
                }
            }
            return list;
        }

        public static string FullDirectory(string workspace, EvalReport report)
            => Path.Combine(workspace, (report.DirectoryPath ?? "").Replace('/', Path.DirectorySeparatorChar));

        private static string NewId()
            => "r-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];

        private static string RenderMarkdown(EvalReport report)
        {
            var sb = new StringBuilder();
            sb.Append("# 评估报告 ").Append(report.Id).AppendLine();
            sb.AppendLine();
            sb.Append("- 时间：").AppendLine(report.At ?? "");
            sb.Append("- 结论：").AppendLine(report.Adopted
                ? $"采纳 v{report.CandidateVersion}"
                : $"保留 v{report.BaselineVersion}");
            sb.Append("- 通过：").Append("基线 ").Append(report.BaselinePassed)
              .Append('/').Append(report.Total)
              .Append("，候选 ").Append(report.CandidatePassed)
              .Append('/').Append(report.Total).AppendLine();
            sb.Append("- 成本：").AppendLine(report.Cost?.ToString("0.0000") ?? "—");
            sb.Append("- 二进制：").AppendLine(report.Binary ?? "—");
            sb.AppendLine();
            sb.AppendLine("## 理由");
            sb.AppendLine();
            sb.AppendLine(report.Reason ?? "");
            sb.AppendLine();

            if (!report.BuildOk)
            {
                sb.AppendLine("## 构建未通过");
                sb.AppendLine();
                sb.AppendLine(report.BuildDetail ?? "");
                sb.AppendLine();
            }

            sb.AppendLine("## 逐题对比");
            sb.AppendLine();
            sb.AppendLine("| 题目 | 基线 | 候选 | 基线步数 | 候选步数 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var task in report.Tasks)
            {
                sb.Append("| ").Append(task.Id)
                  .Append(" | ").Append(task.BaselinePassed ? "通过" : "失败")
                  .Append(" | ").Append(task.CandidatePassed ? "通过" : "失败")
                  .Append(" | ").Append(task.BaselineSteps)
                  .Append(" | ").Append(task.CandidateSteps)
                  .AppendLine(" |");
            }
            sb.AppendLine();

            sb.AppendLine("## 证据");
            sb.AppendLine();
            foreach (var task in report.Tasks)
            {
                sb.Append("- ").Append(task.Id).Append("：基线 ").AppendLine(task.BaselineEvidence ?? "—");
                sb.Append("  候选 ").AppendLine(task.CandidateEvidence ?? "—");
                if (!string.IsNullOrWhiteSpace(task.BaselineDetail) || !string.IsNullOrWhiteSpace(task.CandidateDetail))
                {
                    sb.Append("  基线说明：").AppendLine(task.BaselineDetail ?? "—");
                    sb.Append("  候选说明：").AppendLine(task.CandidateDetail ?? "—");
                }
            }
            return sb.ToString();
        }
    }
}
