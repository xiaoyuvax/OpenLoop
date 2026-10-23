using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools
{
    /// <summary>按名字查找并执行工具。未知工具与执行异常都转成失败结果，由循环回喂模型。</summary>
    public sealed class ToolRegistry
    {
        private readonly List<ITool> _tools;

        public ToolRegistry(IEnumerable<ITool> tools)
        {
            _tools = tools?.ToList() ?? [];
        }

        public IReadOnlyList<ITool> Tools => _tools;

        public ITool Find(string name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public async Task<AgentToolResult> ExecuteAsync(string name, Dictionary<string, object> args, CancellationToken ct)
        {
            var tool = Find(name);
            if (tool == null) return AgentToolResult.Failure($"未知工具：{name}");

            try
            {
                return await tool.ExecuteAsync(new ToolArgs(args), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"工具执行异常：{ex.Message}");
            }
        }

        /// <summary>v0 的五个通用工作目录工具。</summary>
        public static ToolRegistry Workspace(WorkspaceScope scope, Func<Builtin.CommandRequest, bool> approve = null) => new(
        [
            new Builtin.ReadFileTool(scope),
            new Builtin.WriteFileTool(scope),
            new Builtin.ListDirTool(scope),
            new Builtin.GrepTool(scope),
            new Builtin.RunCommandTool(scope, approve),
        ]);
    }
}
