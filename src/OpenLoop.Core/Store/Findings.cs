using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 一条观察。只记「还没处置、且能分派到某个可改进对象」的问题；
    /// 当场修好的、环境故障、一次性事故不写进来。
    /// </summary>
    public sealed class FindingRecord
    {
        public string Id { get; init; }
        public string At { get; init; }

        /// <summary>谁报的：user | loop | worker。</summary>
        public string Source { get; init; }

        /// <summary>分派方向：harness | source | unknown。</summary>
        public string Target { get; init; }

        /// <summary>可改进对象：prompt | skill | rule | limit | guard | code | other。</summary>
        public string Object { get; init; }

        public string Kind { get; init; }
        public string Severity { get; init; }
        public string Summary { get; init; }
        public string Evidence { get; init; }
        public string Suggestion { get; init; }
    }

    /// <summary>
    /// Loop 观察结论的 append-only 账：.openloop/loop/findings.jsonl。
    /// 只追加，不修改账本与 trace；Worker 的工具看不到这个目录。
    /// 处置状态不原地改，另记在 <see cref="FindingTriage"/>。
    /// </summary>
    public static class FindingsLog
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string DirectoryPath(string workspace)
            => Path.Combine(workspace, ".openloop", "loop");

        public static string FilePath(string workspace)
            => Path.Combine(DirectoryPath(workspace), "findings.jsonl");

        public static string Append(
            string workspace, string summary,
            string source = "loop", string target = "unknown", string objectKind = "other",
            string kind = "other", string severity = "info",
            string evidence = null, string suggestion = null)
        {
            if (string.IsNullOrWhiteSpace(workspace))
                throw new ArgumentException("工作区不能为空", nameof(workspace));
            if (string.IsNullOrWhiteSpace(summary))
                throw new ArgumentException("发现摘要不能为空", nameof(summary));

            string id = "F-" + Guid.NewGuid().ToString("N")[..8];
            string line = JsonSerializer.Serialize(new
            {
                id,
                at = DateTimeOffset.UtcNow.ToString("O"),
                source = string.IsNullOrWhiteSpace(source) ? "loop" : source.Trim(),
                target = string.IsNullOrWhiteSpace(target) ? "unknown" : target.Trim(),
                @object = string.IsNullOrWhiteSpace(objectKind) ? "other" : objectKind.Trim(),
                kind = string.IsNullOrWhiteSpace(kind) ? "other" : kind.Trim(),
                severity = string.IsNullOrWhiteSpace(severity) ? "info" : severity.Trim(),
                summary = summary.Trim(),
                evidence = evidence?.Trim(),
                suggestion = suggestion?.Trim()
            }, Json);

            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath(workspace));
                File.AppendAllText(FilePath(workspace), line + "\n", new UTF8Encoding(false));
            }
            return id;
        }

        /// <summary>按落盘顺序读回；坏行跳过（旧行没有 source/target/object 时按默认值读）。</summary>
        public static IReadOnlyList<FindingRecord> Read(string workspace)
        {
            string path = FilePath(workspace);
            if (!File.Exists(path)) return [];
            var list = new List<FindingRecord>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<FindingRecord>(line, Json);
                    if (record?.Id != null) list.Add(record);
                }
                catch (JsonException)
                {
                    // 一条坏行不影响后面的证据
                }
            }
            return list;
        }
    }
}
