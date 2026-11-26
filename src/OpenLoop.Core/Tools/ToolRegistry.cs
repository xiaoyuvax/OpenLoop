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
            if (tool == null) return AgentToolResult.Failure(UnknownToolHint(name));

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

        /// <summary>
        /// 技能名被当成工具名调用是常见误用（模型看到「可用技能」就照着名字调）。
        /// 命中已加载技能时直接给出正确用法，别只回一句「未知工具」。
        /// </summary>
        private string UnknownToolHint(string name)
        {
            var skill = _tools.OfType<Builtin.ReadSkillTool>().FirstOrDefault()?.Skills
                ?.FirstOrDefault(s => string.Equals(s?.Name, name, StringComparison.OrdinalIgnoreCase));
            return skill == null
                ? $"未知工具：{name}"
                : $"未知工具：{name}（它是技能不是工具，用 ReadSkill(name=\"{skill.Name}\") 读取）";
        }

        /// <summary>v0 的五个通用工作目录工具；有技能时追加 ReadSkill；WebFetch 按模式决定是否提供。</summary>
        public static ToolRegistry Workspace(
            WorkspaceScope scope, ApprovalHandler approve = null,
            IReadOnlyList<SkillInfo> skills = null, bool fullAccess = false, IEnumerable<ITool> extra = null,
            string webFetch = null, int maxWebFetches = 8)
            => new(Common(scope, approve, skills, fullAccess, webFetch, maxWebFetches).Concat(extra ?? []));

        /// <summary>Loop 的工具集：读用 read 边界、写用 write 边界，外加只读证据通道 + 发现落账。</summary>
        public static ToolRegistry Loop(
            WorkspaceScope read, WorkspaceScope write, string workspaceRoot, ApprovalHandler approve = null,
            IReadOnlyList<SkillInfo> skills = null, IEnumerable<ITool> extra = null, string webFetch = null,
            int maxWebFetches = 8)
            => new(Common(read, write, approve, skills, fullAccess: false, webFetch, maxWebFetches).Concat(
            [
                new Builtin.ReadEvidenceTool(workspaceRoot),
                new Builtin.RecordFindingTool(workspaceRoot),
                new Builtin.ProposeTaskTool(workspaceRoot),
            ]).Concat(extra ?? []));

        private static IEnumerable<ITool> Common(
            WorkspaceScope scope, ApprovalHandler approve, IReadOnlyList<SkillInfo> skills,
            bool fullAccess, string webFetch, int maxWebFetches)
            => Common(scope, scope, approve, skills, fullAccess, webFetch, maxWebFetches);

        private static IEnumerable<ITool> Common(
            WorkspaceScope read, WorkspaceScope write, ApprovalHandler approve,
            IReadOnlyList<SkillInfo> skills, bool fullAccess, string webFetch, int maxWebFetches)
        {
            yield return new Builtin.ReadFileTool(read);
            yield return new Builtin.WriteFileTool(write);
            yield return new Builtin.EditFileTool(write);
            yield return new Builtin.ListDirTool(read);
            yield return new Builtin.GrepTool(read);
            yield return new Builtin.ShellTool(write, approve, fullAccess);

            string mode = Builtin.WebFetchTool.NormalizeMode(webFetch);
            if (mode != "off")
                yield return new Builtin.WebFetchTool(approve, mode == "ask", maxWebFetches);
            if (skills is { Count: > 0 })
            {
                yield return new Builtin.ReadSkillTool(skills);
                yield return new Builtin.RunSkillScriptTool(write.Root, skills, approve, fullAccess);
            }
        }
    }
}
