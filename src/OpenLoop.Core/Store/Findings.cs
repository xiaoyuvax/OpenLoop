using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// Loop 观察结论的 append-only 账：.openloop/loop/findings.jsonl。
    /// 只追加，不修改账本与 trace；Worker 的工具看不到这个目录。
    /// </summary>
    public static class FindingsLog
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string DirectoryPath(string workspace)
            => Path.Combine(workspace, ".openloop", "loop");

        public static string FilePath(string workspace)
            => Path.Combine(DirectoryPath(workspace), "findings.jsonl");

        public static string Append(
            string workspace, string kind, string severity, string summary,
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
    }
}
