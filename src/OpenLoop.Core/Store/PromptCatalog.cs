using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 工作区的系统提示词模板：文件在 &lt;workspace&gt;/.openloop/prompts/system.md，
    /// 内容以内容哈希登记进账本（kind=prompt，id 带哈希），激活即生效。
    /// 与技能同一条路：文件是编辑面，账本是可回指的版本证据。
    /// 只读方法不建目录；首次落盘由显式入口（编辑器打开时）承担。
    /// </summary>
    public static class PromptCatalog
    {
        public const string FeaturePrefix = "prompt.template.";
        public const string Name = "system";

        public static string DirectoryPath(string workspace)
            => Path.Combine(workspace, ".openloop", "prompts");

        public static string FilePath(string workspace)
            => Path.Combine(DirectoryPath(workspace), Name + ".md");

        /// <summary>工作区约定文件（可被 TargetCentricSession 编辑的那个）。</summary>
        public static string AgentsPath(string workspace)
            => Path.Combine(workspace, "AGENTS.md");

        /// <summary>模板文件原文；没有返回 null（= 用内置默认模板）。</summary>
        public static string ReadFile(string workspace)
        {
            string path = FilePath(workspace);
            if (!File.Exists(path)) return null;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>编辑器入口：没有模板文件时把内置默认落盘，agent 从这里改起。</summary>
        public static string EnsureFile(string workspace)
        {
            string existing = ReadFile(workspace);
            if (existing != null) return existing;

            Directory.CreateDirectory(DirectoryPath(workspace));
            File.WriteAllText(FilePath(workspace), Agent.PromptTemplate.Default, new UTF8Encoding(false));
            return Agent.PromptTemplate.Default;
        }

        /// <summary>当前生效的模板；没有自定义或用的是坏文件时返回 null（调用方回退内置默认）。</summary>
        public static string ActiveTemplate(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return null;
            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Current;
                if (current == null) return null;
                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);

                foreach (var reference in current.Features)
                {
                    if (reference?.Id == null
                        || !reference.Id.StartsWith(FeaturePrefix, StringComparison.Ordinal)
                        || !features.TryGetValue(reference.Id, out var feature)
                        || feature.Kind != "prompt")
                        continue;

                    if (!string.Equals(Payload(feature, "name") ?? Name, Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string text = ReadFile(workspace);
                    if (text == null) return null;
                    string sha = Payload(feature, "sha256");
                    return string.IsNullOrEmpty(sha)
                           || string.Equals(Sha(text), sha, StringComparison.OrdinalIgnoreCase)
                        ? text
                        : null;   // 文件改过但还没登记：宁可回默认，也不悄悄用未经登记的文本
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// 把当前模板文件登记为账本特征并激活（内容没变就什么都不做）。
        /// 返回登记后的特征 id；文件不存在返回 null。
        /// </summary>
        public static string Sync(Ledger ledger, string workspace)
        {
            string text = ReadFile(workspace);
            if (ledger == null || text == null) return null;

            string sha = Sha(text);
            string id = FeaturePrefix + Name + "@" + sha[..8];
            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var current = ledger.Current;
            if (current == null)
            {
                // 显式编辑入口：工作区还没初始化时先建账本，否则模板登记了却没人激活
                ledger.EnsureInitialized();
                features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                current = ledger.Current;
            }

            bool active = current?.Features.Any(r => string.Equals(r.Id, id, StringComparison.Ordinal)) == true;
            if (!features.ContainsKey(id))
            {
                ledger.AppendFeature(new FeatureRecord
                {
                    Id = id,
                    Kind = "prompt",
                    Problem = "系统提示词模板：" + Agent.PromptTemplate.OneLine(text, 80),
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        name = Name,
                        path = Path.GetRelativePath(workspace, FilePath(workspace)).Replace('\\', '/'),
                        sha256 = sha
                    }),
                    Source = "user",
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            if (!active && current != null)
            {
                var refs = current.Features
                    .Where(r => r?.Id != null && !IsPromptFeature(features, r.Id))
                    .ToList();
                refs.Add(new FeatureRef { Id = id });
                ledger.Activate(refs, "user", "更新系统提示词模板");
            }
            return id;
        }

        private static bool IsPromptFeature(
            IReadOnlyDictionary<string, FeatureRecord> features, string id)
            => features.TryGetValue(id, out var feature) && feature.Kind == "prompt";

        private static string Payload(FeatureRecord feature, string name)
            => feature.Payload.ValueKind == JsonValueKind.Object
               && feature.Payload.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string Sha(string text)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
