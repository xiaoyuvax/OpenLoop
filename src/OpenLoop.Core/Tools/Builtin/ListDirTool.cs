using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    public sealed class ListDirTool : ITool
    {
        private const int MaxEntries = 500;

        private readonly WorkspaceScope _scope;

        public ListDirTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "list_dir";
        public string Description => "列出工作目录内的文件与子目录（非递归时只列一层）。";
        public string ParamsSchema => "path: string 可选（默认工作目录根）; recursive: boolean 可选（默认 false）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            if (!_scope.TryResolve(args.Str("path"), out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            if (!Directory.Exists(full))
                return Task.FromResult(AgentToolResult.Failure($"目录不存在：{args.Str("path")}"));

            bool recursive = args.Bool("recursive", false);
            var sb = new StringBuilder();
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            int count = 0;
            bool truncated = false;

            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", option)
                                                  .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    if (_scope.IsDenied(entry)) continue;
                    if (count >= MaxEntries) { truncated = true; break; }
                    count++;

                    string rel = _scope.Relative(entry);
                    if (Directory.Exists(entry))
                    {
                        sb.Append("d ").AppendLine(rel);
                    }
                    else
                    {
                        long size = 0;
                        try { size = new FileInfo(entry).Length; } catch { }
                        sb.Append("f ").Append(rel).Append("  (").Append(size).AppendLine(" B)");
                    }
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"列目录失败：{ex.Message}"));
            }

            if (count == 0) sb.AppendLine("（空目录）");
            if (truncated) sb.AppendLine($"…（已截断，最多显示 {MaxEntries} 项）");

            return Task.FromResult(AgentToolResult.Success(ToolText.Truncate(sb.ToString(), 6000)));
        }
    }
}
