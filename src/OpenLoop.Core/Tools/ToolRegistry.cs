using OpenLoop.Core.Agent;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Skills;

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

        /// <summary>v0 的五个通用工作目录工具；有技能时追加 read_skill。</summary>
        public static ToolRegistry Workspace(
            WorkspaceScope scope, Func<Builtin.CommandRequest, bool> approve = null,
            IReadOnlyList<SkillInfo> skills = null, bool fullAccess = false)
            => new(Common(scope, approve, skills, fullAccess));

        /// <summary>Loop 的工具集：通用工具 + 只读证据通道 + 发现落账。</summary>
        public static ToolRegistry Loop(
            WorkspaceScope scope, string workspaceRoot, Func<Builtin.CommandRequest, bool> approve = null,
            IReadOnlyList<SkillInfo> skills = null)
            => new(Common(scope, approve, skills, fullAccess: false).Concat(
            [
                new Builtin.ReadEvidenceTool(workspaceRoot),
                new Builtin.RecordFindingTool(workspaceRoot),
                new Builtin.ProposeTaskTool(workspaceRoot),
            ]));

        private static IEnumerable<ITool> Common(
            WorkspaceScope scope, Func<Builtin.CommandRequest, bool> approve, IReadOnlyList<SkillInfo> skills,
            bool fullAccess)
        {
            yield return new Builtin.ReadFileTool(scope);
            yield return new Builtin.WriteFileTool(scope);
            yield return new Builtin.ListDirTool(scope);
            yield return new Builtin.GrepTool(scope);
            yield return new Builtin.RunCommandTool(scope, approve, fullAccess);
            if (skills is { Count: > 0 })
            {
                yield return new Builtin.ReadSkillTool(skills);
                yield return new Builtin.RunSkillScriptTool(scope.Root, skills, approve, fullAccess);
            }
        }
    }
}
