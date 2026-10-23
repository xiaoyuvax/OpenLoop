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

        private readonly WorkspaceScope _scope;

        public ReadEvidenceTool(string workspaceRoot)
            => _scope = new WorkspaceScope(
                Path.Combine(workspaceRoot, ".openloop"), denied: ["eval"]);

        public string Name => "read_evidence";
        public string Description => "只读运行态证据（账本、defs、traces、loop/findings.jsonl）；考核题不可见。path 可为文件或目录。";
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

            var sb = new StringBuilder();
            sb.AppendLine($"{_scope.Relative(full)}（第 {start + 1}-{end} 行，共 {lines.Length} 行）");
            for (int i = start; i < end; i++)
                sb.Append(i + 1).Append(": ").AppendLine(lines[i]);

            return AgentToolResult.Success(ToolText.Truncate(sb.ToString(), 6000));
        }
    }
}
