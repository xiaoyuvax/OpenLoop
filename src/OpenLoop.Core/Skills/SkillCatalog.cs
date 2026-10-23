using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Skills
{
    /// <summary>一个技能：name/description 用于提示词索引，正文由 read_skill 按需读取。</summary>
    public sealed class SkillInfo
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public string Body { get; init; }
        public string Source { get; init; }
        public string Sha256 { get; init; }
        public string Directory { get; init; }
    }

    /// <summary>
    /// 扫描 &lt;workspace&gt;/.openloop/skills/&lt;name&gt;/SKILL.md。
    /// 只读取，不写账本；内容哈希随运行记录，保证同一版本可回指。
    /// </summary>
    public static class SkillCatalog
    {
        private const long MaxBytes = 1024 * 1024;
        public const string FeaturePrefix = "skill.";

        public static IReadOnlyList<SkillInfo> Load(string workspaceRoot)
        {
            var list = new List<SkillInfo>();
            if (string.IsNullOrWhiteSpace(workspaceRoot)) return list;

            string dir = Path.Combine(workspaceRoot, ".openloop", "skills");
            if (!Directory.Exists(dir)) return list;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in Directory.GetDirectories(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string file = new[] { "SKILL.md", "skill.md", "Skill.md" }
                    .Select(n => Path.Combine(folder, n))
                    .FirstOrDefault(File.Exists);
                if (file == null) continue;

                var info = TryLoad(file, folder);
                if (info == null || !names.Add(info.Name)) continue;
                list.Add(info);
            }
            return list;
        }

        private static SkillInfo TryLoad(string file, string folder)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > MaxBytes) return null;

                byte[] bytes = File.ReadAllBytes(file);
                string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                var (meta, body) = SplitFrontMatter(text);

                string name = meta.GetValueOrDefault("name");
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(folder);
                name = name.Trim();
                if (name.Length == 0) return null;

                string description = meta.GetValueOrDefault("description");
                if (string.IsNullOrWhiteSpace(description)) description = FirstLine(body);
                description = OneLine(description, 200);

                return new SkillInfo
                {
                    Name = name,
                    Description = description,
                    Body = body.Trim(),
                    Source = ".openloop/skills/" + Path.GetFileName(folder) + "/" + Path.GetFileName(file),
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    Directory = folder
                };
            }
            catch
            {
                return null;
            }
        }

        private static (Dictionary<string, string> Meta, string Body) SplitFrontMatter(string text)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---") return (meta, text);

            int end = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---") { end = i; break; }
            }
            if (end < 0) return (meta, text);

            for (int i = 1; i < end; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string key = lines[i][..colon].Trim();
                string value = lines[i][(colon + 1)..].Trim().Trim('"', '\'');
                if (key.Length > 0) meta[key] = value;
            }

            return (meta, string.Join("\n", lines.Skip(end + 1)));
        }

        private static string FirstLine(string body)
        {
            foreach (string raw in (body ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().TrimStart('#').Trim();
                if (line.Length > 0) return line;
            }
            return "";
        }

        private static string OneLine(string text, int max)
        {
            string flat = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= max ? flat : flat[..max] + "…";
        }

        /// <summary>技能特征是快照：id 带内容哈希，内容变更即新特征，激活不会悄悄指向新内容。</summary>
        public static string FeatureId(SkillInfo skill)
            => FeaturePrefix + skill.Name + "@" + (skill.Sha256 ?? "")[..Math.Min(8, skill.Sha256?.Length ?? 0)];

        /// <summary>把发现的技能登记为休眠特征（不激活）。已存在的 id 跳过。</summary>
        public static void RegisterFeatures(Ledger ledger, IReadOnlyList<SkillInfo> skills)
        {
            if (ledger == null || skills == null) return;
            foreach (var skill in skills)
            {
                string id = FeatureId(skill);
                if (ledger.FindFeature(id) != null) continue;
                try
                {
                    ledger.AppendFeature(new FeatureRecord
                    {
                        Id = id,
                        Kind = "skill",
                        Problem = string.IsNullOrWhiteSpace(skill.Description) ? skill.Name : skill.Description,
                        Payload = JsonSerializer.SerializeToElement(new { name = skill.Name, sha256 = skill.Sha256 }),
                        Source = "user",
                        CreatedAt = DateTimeOffset.UtcNow.ToString("O")
                    });
                }
                catch (InvalidOperationException)
                {
                    // id 已被并发登记：视为已存在
                }
            }
        }

        /// <summary>
        /// 解析激活集里启用的技能；内容哈希与登记不一致时拒绝该技能并给出问题。
        /// </summary>
        public static IReadOnlyList<SkillInfo> Active(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features,
            IReadOnlyList<SkillInfo> skills, out IReadOnlyList<string> problems)
        {
            var active = new List<SkillInfo>();
            var issues = new List<string>();

            foreach (var reference in activation?.Features ?? [])
            {
                if (reference?.Id == null
                    || !reference.Id.StartsWith(FeaturePrefix, StringComparison.Ordinal))
                    continue;

                if (features == null
                    || !features.TryGetValue(reference.Id, out var feature)
                    || feature.Kind != "skill")
                {
                    issues.Add($"{reference.Id}：账本里没有对应的技能特征");
                    continue;
                }

                string name = PayloadText(feature, "name");
                string sha = PayloadText(feature, "sha256");
                var skill = skills?.FirstOrDefault(s =>
                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (skill == null)
                {
                    issues.Add($"{name}：技能文件已删除");
                    continue;
                }
                if (!string.IsNullOrEmpty(sha)
                    && !string.Equals(skill.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"{name}：技能内容已变更，需重新登记并激活新版本");
                    continue;
                }

                active.Add(skill);
            }

            problems = issues;
            return active;
        }

        private static string PayloadText(FeatureRecord feature, string name)
            => feature.Payload.ValueKind == JsonValueKind.Object
               && feature.Payload.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }
}
