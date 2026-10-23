using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Skills;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 按需读取技能（渐进披露）：不带 file 返回正文与参考文件清单，
    /// 带 file 只读技能目录内 references/ 或 scripts/ 下的文件。
    /// </summary>
    public sealed class ReadSkillTool : ITool
    {
        private const long MaxBytes = 1024 * 1024;
        private const int MaxFiles = 100;

        private readonly IReadOnlyList<SkillInfo> _skills;

        public ReadSkillTool(IReadOnlyList<SkillInfo> skills) => _skills = skills ?? [];

        public string Name => "read_skill";
        public string Description => "读取一个可用技能：不带 file 返回做法全文与参考文件清单；带 file 读取技能内 references/ 或 scripts/ 下的文件。只在任务与技能描述相关时调用。";
        public string ParamsSchema => "name: string（技能名，见「可用技能」列表）; file: string 可选（技能内 references/… 或 scripts/… 的相对路径）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string name = args.Str("name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 name"));

            var skill = Find(name);
            if (skill == null)
            {
                string available = _skills.Count == 0 ? "（无）" : string.Join("、", _skills.Select(s => s.Name));
                return Task.FromResult(AgentToolResult.Failure($"未知技能：{name}。可用：{available}"));
            }

            string file = args.Str("file")?.Trim();
            return Task.FromResult(string.IsNullOrEmpty(file)
                ? ReadBody(skill)
                : ReadFile(skill, file));
        }

        private AgentToolResult ReadBody(SkillInfo skill)
        {
            var sb = new StringBuilder();
            sb.Append("# 技能 ").AppendLine(skill.Name).AppendLine();
            sb.AppendLine(skill.Body ?? "");

            var files = ListFiles(skill);
            if (files.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 参考文件（用 read_skill(name, file) 读取）");
                foreach (string path in files) sb.Append("- ").AppendLine(path);
            }

            return AgentToolResult.Success(ToolText.Truncate(sb.ToString(), 6000));
        }

        private AgentToolResult ReadFile(SkillInfo skill, string file)
        {
            var scope = new WorkspaceScope(skill.Directory, denied: []);
            if (!scope.TryResolve(file, out string full, out string error))
                return AgentToolResult.Failure(error);

            string relative = Path.GetRelativePath(skill.Directory, full).Replace('\\', '/');
            if (!relative.StartsWith("references/", StringComparison.OrdinalIgnoreCase)
                && !relative.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
                return AgentToolResult.Failure("只允许读取技能内 references/ 或 scripts/ 下的文件");

            if (!File.Exists(full))
                return AgentToolResult.Failure($"技能文件不存在：{relative}");

            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
                return AgentToolResult.Failure($"文件过大（{info.Length} 字节，上限 {MaxBytes}）：{relative}");

            try
            {
                string text = File.ReadAllText(full, Encoding.UTF8);
                return AgentToolResult.Success($"# {skill.Name}/{relative}\n\n{ToolText.Truncate(text, 6000)}");
            }
            catch (Exception ex)
            {
                return AgentToolResult.Failure($"读取失败：{ex.Message}");
            }
        }

        private static IReadOnlyList<string> ListFiles(SkillInfo skill)
        {
            var list = new List<string>();
            foreach (string sub in new[] { "references", "scripts" })
            {
                string dir = Path.Combine(skill.Directory ?? "", sub);
                if (!Directory.Exists(dir)) continue;
                foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                                                  .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (list.Count >= MaxFiles) return list;
                    list.Add(Path.GetRelativePath(skill.Directory, path).Replace('\\', '/'));
                }
            }
            return list;
        }

        private SkillInfo Find(string name)
            => _skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
