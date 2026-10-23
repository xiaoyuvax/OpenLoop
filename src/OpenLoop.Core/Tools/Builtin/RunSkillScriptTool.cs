using OpenLoop.Core.Agent;
using OpenLoop.Core.Skills;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 执行技能 scripts/ 下的脚本。路径锁在技能目录内，argv 不经 shell，
    /// 且每次都必须经用户批准（脚本是任意代码）。
    /// </summary>
    public sealed class RunSkillScriptTool : ITool
    {
        private readonly string _workspaceRoot;
        private readonly IReadOnlyList<SkillInfo> _skills;
        private readonly Func<CommandRequest, bool> _approve;
        private readonly bool _inheritEnvironment;

        public RunSkillScriptTool(
            string workspaceRoot, IReadOnlyList<SkillInfo> skills, Func<CommandRequest, bool> approve,
            bool inheritEnvironment = false)
        {
            _workspaceRoot = workspaceRoot;
            _skills = skills ?? [];
            _approve = approve;
            _inheritEnvironment = inheritEnvironment;
        }

        public string Name => "run_skill_script";
        public string Description => "执行技能 scripts/ 下的脚本（.ps1/.cmd/.bat/.sh/.py/.js/.exe），工作目录为工作区根；每次执行都需要用户批准。";
        public string ParamsSchema => "name: string（技能名）; script: string（scripts/ 下的相对路径）; args: string[] 可选（附加参数）; timeoutSeconds: number 可选（默认 60，最大 600）";
        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string name = args.Str("name")?.Trim();
            string script = args.Str("script")?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(script))
                return AgentToolResult.Failure("缺少参数 name 或 script");

            var skill = _skills.FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (skill == null)
                return AgentToolResult.Failure($"未知技能：{name}");

            string relative = script.Replace('\\', '/').TrimStart('/');
            if (!relative.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
                relative = "scripts/" + relative;

            var scope = new WorkspaceScope(skill.Directory, denied: []);
            if (!scope.TryResolve(relative, out string full, out string error))
                return AgentToolResult.Failure(error);

            string actual = Path.GetRelativePath(skill.Directory, full).Replace('\\', '/');
            if (!actual.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
                return AgentToolResult.Failure("脚本必须位于技能的 scripts/ 目录下");
            if (!File.Exists(full))
                return AgentToolResult.Failure($"脚本不存在：{actual}");

            var argv = BuildArgv(full);
            if (argv == null)
                return AgentToolResult.Failure("不支持的脚本类型（可用 .ps1/.cmd/.bat/.sh/.py/.js/.exe）");
            argv.AddRange(args.StrList("args"));

            if (_approve == null)
                return AgentToolResult.Failure($"需要用户批准才能执行技能脚本：{string.Join(" ", argv)}");
            var request = new CommandRequest(argv, _workspaceRoot, $"技能 {skill.Name} 的脚本 {actual}");
            if (!_approve(request))
                return AgentToolResult.Failure($"用户拒绝执行：{request.Line}");

            int timeout = Math.Clamp(args.Int("timeoutSeconds", 60), 1, 600);
            return await ProcessRunner.RunAsync(argv, _workspaceRoot, timeout, ct, _inheritEnvironment);
        }

        private static List<string> BuildArgv(string full)
        {
            string ext = Path.GetExtension(full).ToLowerInvariant();
            return ext switch
            {
                ".ps1" => ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", full],
                ".cmd" or ".bat" => ["cmd.exe", "/c", full],
                ".sh" => ["bash", full],
                ".py" => ["python", full],
                ".js" => ["node", full],
                ".exe" => [full],
                _ => null
            };
        }
    }
}
