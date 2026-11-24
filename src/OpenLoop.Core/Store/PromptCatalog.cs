using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    /// <summary>
    /// 工作区的 Prompt 类 Harness 对象：系统提示词模板（.openloop/prompts/system.md）与 AGENTS.md。
    /// 文件是编辑面，内容按内容哈希登记进账本（kind=prompt，id 带哈希），激活即生效。
    /// 直配模式文件改了就登记；A/B 评测模式改的是候选包（.openloop/eval/candidates/pending/），
    /// 只有考核题严格更好、被采纳后才覆盖正式文件。
    /// 只读方法不建目录；首次落盘由显式编辑入口承担。
    /// </summary>
    public static class PromptCatalog
    {
        public const string FeaturePrefix = "prompt.template.";
        public const string AgentsFeaturePrefix = "prompt.agents.";
        public const string Name = "system";
        public const string AgentsName = "agents";

        /// <summary>TargetCentricSession 的目标种类。</summary>
        public const string SystemKind = "prompt";
        public const string AgentsKind = "agents";

        public static string DirectoryPath(string workspace)
            => Path.Combine(workspace, ".openloop", "prompts");

        public static string FilePath(string workspace)
            => Path.Combine(DirectoryPath(workspace), Name + ".md");

        /// <summary>工作区约定文件（可被 TargetCentricSession 编辑的那个）。</summary>
        public static string AgentsPath(string workspace)
            => Path.Combine(workspace, "AGENTS.md");

        /// <summary>候选包目录：A/B 评测模式下 prompt / agents 的暂存位置。</summary>
        public static string CandidateDirectory(string workspace)
            => Path.Combine(workspace, ".openloop", "eval", "candidates", "pending");

        public static string CandidatePath(string workspace, string kind)
            => Path.Combine(CandidateDirectory(workspace),
                (kind == AgentsKind ? "agents" : "system") + ".md");

        private static string LivePath(string workspace, string kind)
            => kind == AgentsKind ? AgentsPath(workspace) : FilePath(workspace);

        /// <summary>目标面板要编辑的文件：直配=正式文件，评测=候选包（首次从正式文件播种）。</summary>
        /// <param name="seedSource">候选包的播种来源；null = 本工作区的正式文件。</param>
        public static string EditingPath(string workspace, string kind, string seedSource = null)
        {
            if (!HarnessMode.IsEval(workspace))
            {
                // 打开编辑器 = 显式写入口：系统提示词没有就从内置默认起
                if (kind == SystemKind) EnsureFile(workspace);
                return LivePath(workspace, kind);
            }

            string path = CandidatePath(workspace, kind);
            if (File.Exists(path)) return path;

            string seed = ReadText(seedSource ?? LivePath(workspace, kind))
                          ?? (kind == AgentsKind ? "" : Agent.PromptTemplate.Default);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, seed, new UTF8Encoding(false));
            return path;
        }

        /// <summary>模板文件原文；没有返回 null（= 用内置默认模板）。</summary>
        public static string ReadFile(string workspace) => ReadText(FilePath(workspace));

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
                return Resolve(workspace, ledger.Current,
                    ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal), SystemKind);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按一次激活集解析模板：评测里基线/候选各用自己那份，互不污染。</summary>
        public static string ResolveTemplate(
            string workspace, ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
            => Resolve(workspace, activation, features, SystemKind);

        /// <summary>按一次激活集解析 AGENTS.md；没有对应特征时返回 null（调用方读正式文件）。</summary>
        public static string ResolveAgents(
            string workspace, ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
            => Resolve(workspace, activation, features, AgentsKind);

        /// <summary>
        /// 把当前模板文件登记为账本特征并激活（内容没变就什么都不做）。
        /// 返回登记后的特征 id；文件不存在返回 null。
        /// </summary>
        public static string Sync(Ledger ledger, string workspace)
            => SyncKind(ledger, workspace, SystemKind);

        /// <summary>AGENTS.md 走同一条路（直配模式下文件改了就登记并激活）。</summary>
        public static string SyncAgents(Ledger ledger, string workspace)
            => SyncKind(ledger, workspace, AgentsKind);

        /// <summary>候选包里有内容且与正式文件不同 = 待评估。</summary>
        public static IReadOnlyList<string> PendingKinds(string workspace)
        {
            var kinds = new List<string>();
            foreach (string kind in new[] { SystemKind, AgentsKind })
            {
                string staged = ReadText(CandidatePath(workspace, kind));
                if (staged == null) continue;
                string live = ReadText(LivePath(workspace, kind))
                              ?? (kind == AgentsKind ? "" : Agent.PromptTemplate.Default);
                if (!string.Equals(staged.Trim(), live?.Trim(), StringComparison.Ordinal))
                    kinds.Add(kind);
            }
            return kinds;
        }

        /// <summary>
        /// 把待评估的候选包叠到候选激活集上（只换 Prompt 类引用），并登记候选特征。
        /// 没有候选包时原样返回。
        /// </summary>
        public static ActivationRecord OverlayPending(
            Ledger ledger, string workspace, ActivationRecord candidate,
            Dictionary<string, FeatureRecord> features)
        {
            var staged = PendingKinds(workspace);
            if (ledger == null || candidate == null || staged.Count == 0) return candidate;

            var refs = (candidate.Features ?? []).ToList();
            string note = candidate.Note;
            bool changed = false;
            foreach (string kind in staged)
            {
                string text = ReadText(CandidatePath(workspace, kind));
                if (text == null) continue;
                var feature = Register(ledger, workspace, kind, text, staged: true, out string id);
                if (feature == null) continue;
                features[id] = feature;
                refs.RemoveAll(r => r?.Id != null && IsNamed(features, r.Id, kind));
                refs.Add(new FeatureRef { Id = id });
                changed = true;
            }
            if (!changed) return candidate;

            int version = candidate.Version > 0 ? candidate.Version : 1;
            return new ActivationRecord
            {
                Version = version,
                At = candidate.At ?? DateTimeOffset.UtcNow.ToString("O"),
                By = "user",
                Note = string.IsNullOrWhiteSpace(note) ? "候选提示词包" : note + "（含候选提示词包）",
                Features = refs
            };
        }

        /// <summary>候选被采纳后把暂存文件覆盖到正式文件（账本里已留下候选特征与文本）。</summary>
        public static void CommitPending(string workspace)
        {
            foreach (string kind in PendingKinds(workspace))
            {
                string text = ReadText(CandidatePath(workspace, kind));
                if (text == null) continue;
                string live = LivePath(workspace, kind);
                Directory.CreateDirectory(Path.GetDirectoryName(live));
                File.WriteAllText(live, text, new UTF8Encoding(false));
            }
        }

        private static string SyncKind(Ledger ledger, string workspace, string kind)
        {
            string text = ReadText(LivePath(workspace, kind));
            if (ledger == null || text == null) return null;

            var feature = Register(ledger, workspace, kind, text, staged: false, out string id);
            if (feature == null) return null;

            var current = ledger.Current;
            if (current == null)
            {
                // 显式编辑入口：工作区还没初始化时先建账本，否则模板登记了却没人激活
                ledger.EnsureInitialized();
                current = ledger.Current;
            }
            if (current == null) return id;

            if (!current.Features.Any(r => string.Equals(r.Id, id, StringComparison.Ordinal)))
            {
                var refs = current.Features
                    .Where(r => r?.Id != null && !IsNamed(
                        ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal), r.Id, kind))
                    .ToList();
                refs.Add(new FeatureRef { Id = id });
                ledger.Activate(refs, "user", kind == AgentsKind ? "更新 AGENTS.md" : "更新系统提示词模板");
            }
            return id;
        }

        private static FeatureRecord Register(
            Ledger ledger, string workspace, string kind, string text, bool staged, out string id)
        {
            id = null;
            if (ledger == null || string.IsNullOrWhiteSpace(text)) return null;

            string sha = Sha(text);
            string name = kind == AgentsKind ? AgentsName : Name;
            string prefix = kind == AgentsKind ? AgentsFeaturePrefix : FeaturePrefix;
            id = prefix + name + "@" + sha[..8];

            var existing = ledger.FindFeature(id);
            if (existing != null) return existing;

            string path = Path.GetRelativePath(workspace,
                staged ? CandidatePath(workspace, kind) : LivePath(workspace, kind)).Replace('\\', '/');
            var feature = new FeatureRecord
            {
                Id = id,
                Kind = "prompt",
                Problem = kind == AgentsKind
                    ? "AGENTS.md：" + Agent.PromptTemplate.OneLine(text, 80)
                    : "系统提示词模板：" + Agent.PromptTemplate.OneLine(text, 80),
                Payload = JsonSerializer.SerializeToElement(new
                {
                    name,
                    path,
                    sha256 = sha,
                    text,
                    staged
                }),
                Source = "user",
                CreatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            try
            {
                ledger.AppendFeature(feature);
            }
            catch (InvalidOperationException)
            {
                return ledger.FindFeature(id);
            }
            return feature;
        }

        private static string Resolve(
            string workspace, ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features,
            string kind)
        {
            if (activation == null || features == null) return null;
            string prefix = kind == AgentsKind ? AgentsFeaturePrefix : FeaturePrefix;
            string name = kind == AgentsKind ? AgentsName : Name;

            foreach (var reference in activation.Features ?? [])
            {
                if (reference?.Id == null
                    || !reference.Id.StartsWith(prefix, StringComparison.Ordinal)
                    || !features.TryGetValue(reference.Id, out var feature)
                    || feature.Kind != "prompt")
                    continue;
                if (!string.Equals(Payload(feature, "name") ?? name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = Payload(feature, "text");
                bool staged = Payload(feature, "staged") == "true";
                if (staged && text != null) return text;

                string path = Payload(feature, "path") ?? Path.GetRelativePath(workspace,
                    kind == AgentsKind ? AgentsPath(workspace) : FilePath(workspace)).Replace('\\', '/');
                string full = Path.IsPathRooted(path) ? path : Path.Combine(workspace, path);
                string file = ReadText(full);
                if (file != null)
                {
                    string sha = Payload(feature, "sha256");
                    return string.IsNullOrEmpty(sha)
                           || string.Equals(Sha(file), sha, StringComparison.OrdinalIgnoreCase)
                        ? file
                        : null;   // 文件改过但还没登记：宁可回默认，也不悄悄用未经登记的文本
                }
                return text;
            }
            return null;
        }

        private static bool IsNamed(
            IReadOnlyDictionary<string, FeatureRecord> features, string id, string kind)
            => features.TryGetValue(id, out var feature)
               && feature.Kind == "prompt"
               && string.Equals(Payload(feature, "name"),
                   kind == AgentsKind ? AgentsName : Name, StringComparison.OrdinalIgnoreCase);

        private static string Payload(FeatureRecord feature, string name)
        {
            if (feature.Payload.ValueKind != JsonValueKind.Object
                || !feature.Payload.TryGetProperty(name, out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static string ReadText(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
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

        private static string Sha(string text)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
