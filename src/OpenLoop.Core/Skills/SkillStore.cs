using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Skills
{
    /// <summary>
    /// 全局技能开关（纯文本 JSON 里的 disabled 名单，没列出的就是启用）与技能目录的安装/卸载。
    /// 只动技能目录和这个开关文件，不碰账本：账本由 I 的 Sync 按目录现状收敛。
    /// </summary>
    public static class SkillStore
    {
        private static readonly string[] SkillFiles = ["SKILL.md", "skill.md", "Skill.md"];

        public static IReadOnlySet<string> LoadDisabled(string stateFile)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(stateFile) || !File.Exists(stateFile)) return set;
            try
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(stateFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (string name in state?.Disabled ?? [])
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
            }
            catch
            {
                //文件坏了按「全部启用」处理，下一次写入会覆盖
            }
            return set;
        }

        public static void SetEnabled(string stateFile, string name, bool enabled)
            => SetEnabled(stateFile, string.IsNullOrWhiteSpace(name) ? [] : [name], enabled);

        /// <summary>批量开关：一次读一次写，多选后一起启用/停用。</summary>
        public static void SetEnabled(string stateFile, IEnumerable<string> names, bool enabled)
        {
            var list = (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(stateFile) || list.Count == 0) return;

            var disabled = LoadDisabled(stateFile).ToList();
            foreach (string name in list)
                disabled.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (!enabled) disabled.AddRange(list);

            string parent = Path.GetDirectoryName(Path.GetFullPath(stateFile));
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(new State
            {
                Disabled = disabled.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            }), new UTF8Encoding(false));
        }

        /// <summary>把含 SKILL.md 的目录复制进技能库；同名目录已存在就拒绝（不覆盖用户已有的技能）。</summary>
        public static string Install(string sourceDir, string skillsRoot)
        {
            string source = Path.GetFullPath(sourceDir ?? "");
            if (!Directory.Exists(source)) throw new InvalidOperationException("技能目录不存在");
            if (!SkillFiles.Any(n => File.Exists(Path.Combine(source, n))))
                throw new InvalidOperationException("目录里没有 SKILL.md");

            string name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("无法确定技能名字");

            string root = Path.GetFullPath(skillsRoot);
            string target = Path.Combine(root, name);
            if (Directory.Exists(target)) throw new InvalidOperationException($"已存在同名技能：{name}");

            Directory.CreateDirectory(root);
            Copy(source, target);
            return target;
        }

        /// <summary>删除技能目录；只允许删技能库下面的目录，防止把别处的文件夹删掉。</summary>
        public static void Uninstall(SkillInfo skill, string skillsRoot)
        {
            if (string.IsNullOrWhiteSpace(skill?.Directory)) throw new InvalidOperationException("技能没有目录信息");
            string root = Path.GetFullPath(skillsRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string dir = Path.GetFullPath(skill.Directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) || dir.Length <= root.Length - 1)
                throw new InvalidOperationException("只能卸载技能库目录里的技能");

            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                Copy(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private sealed class State
        {
            public List<string> Disabled { get; set; } = [];
        }
    }
}
