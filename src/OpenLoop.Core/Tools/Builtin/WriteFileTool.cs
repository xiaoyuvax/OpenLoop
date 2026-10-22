using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    public sealed class WriteFileTool : ITool
    {
        private readonly WorkspaceScope _scope;

        public WriteFileTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "write_file";
        public string Description => "写入工作目录内的文本文件（覆盖）。父目录不存在时自动创建。";
        public string ParamsSchema => "path: string（相对工作目录的路径）; content: string（完整内容）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string path = args.Str("path");
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 path"));

            if (!_scope.TryResolve(path, out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            string content = args.Str("content", "") ?? "";

            try
            {
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, content, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"写入失败：{ex.Message}"));
            }

            return Task.FromResult(AgentToolResult.Success(
                $"已写入 {content.Length} 字符 → {_scope.Relative(full)}"));
        }
    }
}
