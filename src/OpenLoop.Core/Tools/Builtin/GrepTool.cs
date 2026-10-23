using System.Text;
using System.Text.RegularExpressions;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    public sealed class GrepTool : ITool
    {
        private const int MaxMatches = 200;
        private const long MaxFileBytes = 2 * 1024 * 1024;

        private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".json", ".jsonl", ".xml", ".md", ".csv", ".log", ".yaml", ".yml",
            ".ini", ".html", ".htm", ".cs", ".js", ".ts", ".py", ".go", ".rs", ".java",
            ".c", ".h", ".cpp", ".hpp", ".sql", ".ps1", ".sh", ".csproj", ".props", ".targets",
            ".sln", ".slnx", ".config", ".toml", ".gradle", ".kt", ".rb", ".php", ".css"
        };

        private readonly WorkspaceScope _scope;

        public GrepTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "grep";
        public string Description => "在工作目录内按正则搜索文本文件内容，返回 文件:行号: 内容。";
        public string ParamsSchema => "pattern: string（正则）; path: string 可选（默认根）; glob: string 可选（如 *.cs）; ignoreCase: boolean 可选（默认 true）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string pattern = args.Str("pattern");
            if (string.IsNullOrWhiteSpace(pattern))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 pattern"));

            if (!_scope.TryResolve(args.Str("path"), out string target, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            string glob = args.Str("glob");
            bool ignoreCase = args.Bool("ignoreCase", true);

            Regex regex;
            try
            {
                var options = RegexOptions.CultureInvariant
                    | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"正则无效：{ex.Message}"));
            }

            IEnumerable<string> files;
            try
            {
                if (File.Exists(target))
                {
                    files = [target];
                }
                else if (Directory.Exists(target))
                {
                    files = Directory.EnumerateFiles(target, string.IsNullOrWhiteSpace(glob) ? "*" : glob,
                        SearchOption.AllDirectories);
                }
                else
                {
                    return Task.FromResult(AgentToolResult.Failure($"路径不存在：{args.Str("path")}"));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"枚举文件失败：{ex.Message}"));
            }

            var sb = new StringBuilder();
            int matches = 0;
            bool truncated = false;

            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (matches >= MaxMatches) { truncated = true; break; }
                if (_scope.IsDenied(file)) continue;

                if (string.IsNullOrWhiteSpace(glob) && !TextExts.Contains(Path.GetExtension(file))) continue;

                long size;
                try { size = new FileInfo(file).Length; } catch { continue; }
                if (size > MaxFileBytes) continue;

                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    bool hit;
                    try { hit = regex.IsMatch(lines[i]); }
                    catch (RegexMatchTimeoutException) { hit = false; }
                    if (!hit) continue;

                    sb.Append(_scope.Relative(file)).Append(':').Append(i + 1).Append(": ")
                      .AppendLine(ToolText.Truncate(lines[i].Trim(), 200));
                    matches++;
                    if (matches >= MaxMatches) { truncated = true; break; }
                }
            }

            if (matches == 0) return Task.FromResult(AgentToolResult.Success("（无匹配）"));
            if (truncated) sb.AppendLine($"…（已截断，最多 {MaxMatches} 条）");

            return Task.FromResult(AgentToolResult.Success(ToolText.Truncate(sb.ToString(), 6000)));
        }
    }
}
