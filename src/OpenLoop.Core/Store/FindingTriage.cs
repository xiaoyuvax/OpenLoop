using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>一条 findings 的处置状态（追加式，按 id 取最后一条）。</summary>
    public sealed class TriageRecord
    {
        public string Id { get; init; }
        public string At { get; init; }
        public string Status { get; init; }
        public string Batch { get; init; }
        public string Note { get; init; }
        public string By { get; init; }
    }

    /// <summary>
    /// findings 的分诊账：.openloop/loop/triage.jsonl。
    /// findings.jsonl 只追加不改，状态另记在这里，按 id 取最后一条。
    /// </summary>
    public static class FindingTriage
    {
        public const string Open = "open";
        public const string Triaged = "triaged";
        public const string Dispatched = "dispatched";
        public const string Done = "done";
        public const string Dropped = "dropped";

        public const string Harness = "harness";
        public const string Source = "source";
        public const string Unknown = "unknown";

        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string FilePath(string workspace)
            => Path.Combine(FindingsLog.DirectoryPath(workspace), "triage.jsonl");

        public static string DispatchDir(string workspace)
            => Path.Combine(FindingsLog.DirectoryPath(workspace), "dispatch");

        public static void Append(
            string workspace, string id, string status,
            string batch = null, string note = null, string by = "loop")
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("工作区与 finding id 不能为空");

            string line = JsonSerializer.Serialize(new
            {
                id = id.Trim(),
                at = DateTimeOffset.UtcNow.ToString("O"),
                status = string.IsNullOrWhiteSpace(status) ? Open : status.Trim(),
                batch,
                note,
                by = string.IsNullOrWhiteSpace(by) ? "loop" : by.Trim()
            }, Json);

            lock (Gate)
            {
                Directory.CreateDirectory(FindingsLog.DirectoryPath(workspace));
                File.AppendAllText(FilePath(workspace), line + "\n", new UTF8Encoding(false));
            }
        }

        public static IReadOnlyList<TriageRecord> Read(string workspace)
        {
            string path = FilePath(workspace);
            if (!File.Exists(path)) return [];
            var list = new List<TriageRecord>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<TriageRecord>(line, Json);
                    if (record?.Id != null) list.Add(record);
                }
                catch (JsonException)
                {
                }
            }
            return list;
        }

        /// <summary>该 finding 的最后状态；从没分诊过返回 null。</summary>
        public static string StatusOf(string workspace, string id)
            => Read(workspace).LastOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal))?.Status;

        /// <summary>还没被处置的 findings（从没分诊过，或最后状态是 open）。</summary>
        public static IReadOnlyList<FindingRecord> OpenFindings(string workspace)
        {
            var status = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var record in Read(workspace)) status[record.Id] = record.Status ?? Open;
            return FindingsLog.Read(workspace)
                .Where(f => !status.TryGetValue(f.Id, out var s) || s == Open)
                .ToList();
        }

        /// <summary>把源码类 findings 写成交给下一次 Loop 会话处理的派单。</summary>
        // shortcut: 派单只写一次、处理完不回收，队列随批次累积；要清理时再加「处理完回写 done」
        public static string WriteDispatch(
            string workspace, string batch, IEnumerable<FindingRecord> findings)
        {
            var list = findings?.ToList() ?? [];
            if (list.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("# 源码待办 " + batch);
            sb.AppendLine();
            foreach (var finding in list)
            {
                sb.Append("- [").Append(finding.Object ?? "other").Append("] ")
                  .AppendLine(finding.Summary);
                if (!string.IsNullOrWhiteSpace(finding.Evidence))
                    sb.Append("  证据：").AppendLine(finding.Evidence);
                if (!string.IsNullOrWhiteSpace(finding.Suggestion))
                    sb.Append("  建议：").AppendLine(finding.Suggestion);
            }

            string dir = DispatchDir(workspace);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, batch + ".md");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        /// <summary>/report 的唯一落账入口：解析可选前缀后追加 findings，不触发演进。</summary>
        public static (bool Ok, string Message) Report(string workspace, string text)
        {
            var (target, objectKind, summary) = ParseReport(text);
            if (summary.Length == 0) return (false, "缺少内容");
            try
            {
                string id = FindingsLog.Append(workspace, summary,
                    source: "user", target: target, objectKind: objectKind);
                return (true, $"已记录 {id}（不立即处理，演进时分诊）");
            }
            catch (Exception ex)
            {
                return (false, "记录失败：" + ex.Message);
            }
        }

        /// <summary>
        /// /report 的可选前缀：harness / prompt / skill / rule / limit / guard / code / source。
        /// 不写前缀就整句入账、由演进时分诊。
        /// </summary>
        public static (string Target, string ObjectKind, string Summary) ParseReport(string text)
        {
            string body = (text ?? "").Trim();
            if (body.StartsWith("/report", StringComparison.OrdinalIgnoreCase))
                body = body[7..].Trim();
            if (body.Length == 0) return (Unknown, "other", "");

            int space = body.IndexOf(' ');
            string head = (space < 0 ? body : body[..space]).ToLowerInvariant();
            string rest = space < 0 ? "" : body[(space + 1)..].Trim();
            if (rest.Length == 0) return (Unknown, "other", body);

            switch (head)
            {
                case "harness":
                    return (Harness, "other", rest);
                case "prompt":
                case "skill":
                case "rule":
                case "limit":
                case "guard":
                    return (Harness, head, rest);
                case "code":
                case "source":
                    return (Source, "code", rest);
                default:
                    return (Unknown, "other", body);
            }
        }
    }
}
