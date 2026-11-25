using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// Loop 专用只读通道：看运行态证据（账本、defs、traces、loop 发现），
    /// 但不暴露考核题（.openloop/eval）。
    /// </summary>
    public sealed class ReadEvidenceTool : ITool
    {
        private const long MaxBytes = 2 * 1024 * 1024;
        private const int DefaultLimit = 200;
        private const int MaxEntries = 200;

        /// <summary>
        /// 单次读取的字符上限。账本是逐行 JSON，6000 字符只够 8-10 条 finding，
        /// 模型只能反复试探分块读（84 条要走十几次），一轮的工具步数被迫膨胀。
        /// 这里给到 16000，并在任何截断处明确写出续读 offset。
        /// </summary>
        private const int ReadMaxChars = 16000;

        private readonly WorkspaceScope _scope;

        public ReadEvidenceTool(string workspaceRoot)
            => _scope = new WorkspaceScope(
                Path.Combine(workspaceRoot, ".openloop"), denied: ["eval"]);

        public string Name => "ReadEvidence";
        public string Description =>
            "只读运行态证据（账本、defs、traces、loop/findings.jsonl、loop/triage.jsonl、loop/dispatch/*.md）；考核题不可见。path 可为文件或目录。";
        public string ParamsSchema => "path: string 可选（相对 .openloop，默认根）; offset: number 可选（起始行）; limit: number 可选（最多行数/条目，默认 200）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string path = args.Str("path");
            if (!_scope.TryResolve(path, out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            if (Directory.Exists(full)) return Task.FromResult(ListDir(full, ct));
            if (!File.Exists(full))
                return Task.FromResult(AgentToolResult.Failure($"证据不存在：{_scope.Relative(full)}"));
            return Task.FromResult(ReadFile(full, args));
        }

        private AgentToolResult ListDir(string dir, CancellationToken ct)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly)
                                              .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (_scope.IsDenied(entry)) continue;
                if (count >= MaxEntries) { sb.AppendLine($"…（已截断，最多 {MaxEntries} 项）"); break; }
                count++;
                if (Directory.Exists(entry)) sb.Append("d ").AppendLine(_scope.Relative(entry));
                else sb.Append("f ").Append(_scope.Relative(entry)).Append("  (").Append(new FileInfo(entry).Length).AppendLine(" B)");
            }

            if (count == 0) sb.AppendLine("（空目录）");
            return AgentToolResult.Success(ToolText.Truncate(sb.ToString(), 6000));
        }

        private AgentToolResult ReadFile(string full, ToolArgs args)
        {
            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
                return AgentToolResult.Failure($"文件过大（{info.Length} 字节，上限 {MaxBytes}）：{_scope.Relative(full)}");

            string[] lines;
            try { lines = File.ReadAllLines(full, Encoding.UTF8); }
            catch (Exception ex) { return AgentToolResult.Failure($"读取失败：{ex.Message}"); }

            int offset = Math.Max(1, args.Int("offset", 1));
            int limit = Math.Clamp(args.Int("limit", DefaultLimit), 1, 5000);
            int start = Math.Min(offset - 1, lines.Length);
            int end = Math.Min(lines.Length, start + limit);
            string relative = _scope.Relative(full);

            var sb = new StringBuilder();
            for (int i = start; i < end; i++)
                sb.Append(i + 1).Append(": ").AppendLine(lines[i]);
            string text = sb.ToString();

            // limit 用完但文件还有剩余：明确给出续读 offset，别让模型猜
            if (end < lines.Length && text.Length <= ReadMaxChars)
            {
                return AgentToolResult.Success(
                    $"{relative}（第 {start + 1}-{end} 行，共 {lines.Length} 行；limit 到顶，"
                    + $"第 {end + 1}-{lines.Length} 行未显示，继续读请设 offset={end + 1}）\n" + text);
            }

            if (text.Length <= ReadMaxChars)
            {
                return AgentToolResult.Success(
                    $"{relative}（第 {start + 1}-{end} 行，共 {lines.Length} 行）\n" + text);
            }

            // 字符上限截断按行边界切：不要在一条 finding 的 JSON 中间断掉，
            // 并把续读 offset 直接写进首行，免得模型靠反复试探找出该从哪一行接着读。
            int cut = text.LastIndexOf('\n', ReadMaxChars);
            if (cut <= 0)
            {
                return AgentToolResult.Success(
                    $"{relative}（第 {start + 1} 行，共 {lines.Length} 行；单行超过 {ReadMaxChars} 字符，已截断）\n"
                    + ToolText.Truncate(text, ReadMaxChars));
            }

            string shown = text[..(cut + 1)];
            int shownLines = shown.Count(c => c == '\n');
            int shownEnd = start + shownLines;
            return AgentToolResult.Success(
                $"{relative}（第 {start + 1}-{shownEnd} 行，共 {lines.Length} 行；单次上限 {ReadMaxChars} 字符，"
                + $"第 {shownEnd + 1}-{end} 行未显示，继续读请设 offset={shownEnd + 1}）\n" + shown);
        }
    }
}
