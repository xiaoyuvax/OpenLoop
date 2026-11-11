using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    public sealed class ReadFileTool : ITool
    {
        private const long MaxBytes = 2 * 1024 * 1024;
        private const int MaxChars = 6000;
        private const int DefaultLimit = 400;

        private readonly WorkspaceScope _scope;

        public ReadFileTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "ReadFile";
        public string Description => "读取工作目录内的文本文件，按行返回（带行号）。";
        public string ParamsSchema => "path: string（相对工作目录的路径）; offset: number 可选（起始行，从 1 起）; limit: number 可选（最多行数，默认 400）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string path = args.Str("path");
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 path"));

            if (!_scope.TryResolve(path, out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            if (!File.Exists(full))
                return Task.FromResult(AgentToolResult.Failure($"文件不存在：{path}"));

            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
                return Task.FromResult(AgentToolResult.Failure(
                    $"文件过大（{info.Length} 字节，上限 {MaxBytes}）：{path}"));

            string[] lines;
            try
            {
                lines = File.ReadAllLines(full);
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"读取失败：{ex.Message}"));
            }

            int offset = Math.Max(1, args.Int("offset", 1));
            int limit = Math.Clamp(args.Int("limit", DefaultLimit), 1, 5000);
            int start = Math.Min(offset - 1, lines.Length);
            int end = Math.Min(lines.Length, start + limit);

            var sb = new StringBuilder();
            for (int i = start; i < end; i++)
                sb.Append(i + 1).Append(": ").AppendLine(lines[i]);

            string relative = _scope.Relative(full);
            string text = sb.ToString();
            string header = $"{relative}（第 {start + 1}-{end} 行，共 {lines.Length} 行）\n";
            if (text.Length <= MaxChars)
                return Task.FromResult(AgentToolResult.Success(header + text));

            int cut = text.LastIndexOf('\n', MaxChars);
            if (cut <= 0)
                return Task.FromResult(AgentToolResult.Success(
                    $"{relative}（第 {start + 1} 行，共 {lines.Length} 行；单行超过 {MaxChars} 字符，已截断）\n"
                    + ToolText.Truncate(text, MaxChars)));

            string shown = text[..(cut + 1)];
            int shownLines = shown.Count(c => c == '\n');
            int shownEnd = start + shownLines;
            return Task.FromResult(AgentToolResult.Success(
                $"{relative}（第 {start + 1}-{shownEnd} 行，共 {lines.Length} 行；输出上限 {MaxChars} 字符，" +
                $"第 {shownEnd + 1}-{end} 行未显示，继续读请设 offset={shownEnd + 1}）\n" + shown));
        }
    }
}
