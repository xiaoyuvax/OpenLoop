using System.Text;
using OpenLoop.Core.Agent;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 写入工作目录内的文本文件。默认 overwrite（整文件覆盖）；mode=append 追加到文件末尾，
    /// 供大内容分段写入（第一段 overwrite 建文件，后续段 append），避免单次输出被截断后反复重试。
    /// </summary>
    public sealed class WriteFileTool : ITool
    {
        private readonly WorkspaceScope _scope;

        public WriteFileTool(WorkspaceScope scope) => _scope = scope;

        public string Name => "WriteFile";
        public string Description =>
            "写入工作目录内的文本文件。默认覆盖整个文件（overwrite）；mode=\"append\" 把 content 追加到文件末尾。" +
            "内容较长（如超过 3000 字符）时建议分段写：第一段用默认模式建文件，后续各段用 mode=\"append\" 续写，" +
            "避免单次输出过大被截断后整段重写。父目录不存在时自动创建。";
        public string ParamsSchema =>
            "path: string（相对工作目录的路径）; content: string（完整内容）; " +
            "mode: string 可选（overwrite=覆盖整个文件，默认；append=追加到文件末尾）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string path = args.Str("path");
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 path"));

            if (!_scope.TryResolve(path, out string full, out string error))
                return Task.FromResult(AgentToolResult.Failure(error));

            string mode = args.Str("mode", "overwrite")?.Trim().ToLowerInvariant() ?? "overwrite";
            if (mode is not ("overwrite" or "append"))
                return Task.FromResult(AgentToolResult.Failure(
                    $"未知 mode：{mode}（可选 overwrite=覆盖整个文件、append=追加到文件末尾）"));

            string content = args.Str("content", "") ?? "";

            try
            {
                _scope.Undo?.Record(full);
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var encoding = new UTF8Encoding(false);
                if (mode == "append") File.AppendAllText(full, content, encoding);
                else File.WriteAllText(full, content, encoding);
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"写入失败：{ex.Message}"));
            }

            return Task.FromResult(AgentToolResult.Success(
                $"{(mode == "append" ? "已追加" : "已写入")} {content.Length} 字符 → {_scope.Relative(full)}"));
        }
    }
}
