using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Safety;

namespace OpenLoop.Service.Source
{
    /// <summary>版本图里的一个节点：版本声明文件、参与编译的源码目录、依赖的上游节点。</summary>
    public sealed record VersionNode(
        string Id, string Label, string RepoRoot, string SourceDir, string VersionFile,
        string[] Upstream, bool WholeRepo = false);

    /// <summary>依赖项身份：包取解析后的包版本，项目引用取它自己的 &lt;Version&gt; + 指纹。</summary>
    public sealed record NodeDep(string Id, string Kind, string Version, string Fingerprint);

    public sealed record NodeFingerprint(string Value, List<NodeDep> Deps);

    /// <summary>
    /// 指纹与依赖图：内容 + 解析后的包闭包 + 上游的版本与指纹，拼成一个哈希。
    /// 上游变了（内容或号变了）下游的哈希必然变，级联不需要额外的判断。
    /// 源码树哈希走 git tree 对象（含未跟踪、不含忽略文件），没有 git 时退化成只看依赖。
    /// </summary>
    public sealed class VersionScan
    {
        private static readonly string[] BuildInputs =
        [
            "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
            "global.json", "NuGet.config", "nuget.config", "OpenLoop.slnx"
        ];

        private readonly Dictionary<string, string> _trees = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>七个版本节点；外部组件的号由 I 维护。</summary>
        public static IReadOnlyList<VersionNode> Nodes()
        {
            string root = SourceSync.SourceRoot;
            string shellDir = OperatingSystem.IsWindows() ? "src/OpenLoop.App" : "src/OpenLoop.Shell";
            return
            [
                new("core", "Core", root, "src/OpenLoop.Core",
                    "src/OpenLoop.Core/OpenLoop.Core.csproj", []),
                new("service", "Service", root, "src/OpenLoop.Service",
                    "src/OpenLoop.Service/OpenLoop.Service.csproj", ["core"]),
                new("puppetcore", "PuppetCore", SourceSync.PuppetRoot, "Puppet.Core",
                    "Puppet.Common.targets", [], WholeRepo: true),
                new("chatflow", "ChatFlow", SourceSync.ChatFlowRoot, "ChatFlow",
                    "ChatFlow/ChatFlow.csproj", [], WholeRepo: true),
                new("a", "Worker（A）", root, "src/OpenLoop.A",
                    "src/OpenLoop.A/OpenLoop.A.csproj", ["service"]),
                new("i", "Loop（I）", root, "src/OpenLoop.I",
                    "src/OpenLoop.I/OpenLoop.I.csproj", ["service"]),
                new("shell", "壳", root, shellDir, "ShellVersion.props", ["service", "chatflow"])
            ];
        }

        /// <summary>
        /// 只有以源码方式参与编译的外部组件才是版本节点；走包时它的包版本已经在消费者指纹里，
        /// 再单独给它升号只会让「包引用」和「源码版本」对不上。
        /// </summary>
        public static IReadOnlyList<VersionNode> ActiveNodes()
        {
            var all = Nodes();
            string ProjectDir(string id) => Path.Combine(SourceSync.SourceRoot, all.First(n => n.Id == id).SourceDir);

            bool puppetSource = Libraries(ProjectDir("service")).Any(l => l.Type == "project" && l.Id == "Puppet.Core");
            bool chatSource = Libraries(ProjectDir("shell")).Any(l => l.Type == "project" && l.Id == "ChatFlow");

            var active = all.Where(n => n.Id switch
            {
                "puppetcore" => puppetSource,
                "chatflow" => chatSource,
                _ => true
            }).ToList();

            var ids = active.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return active.Select(n => n with { Upstream = n.Upstream.Where(ids.Contains).ToArray() }).ToList();
        }

        /// <summary>这个节点的指纹与直接依赖清单；上游的版本/指纹取「本轮升完之后」的值。</summary>
        public NodeFingerprint Fingerprint(
            VersionNode node,
            IReadOnlyDictionary<string, string> upstreamFingerprints,
            IReadOnlyDictionary<string, string> upstreamVersions)
        {
            string tree = SourceTree(node);
            string config = string.Equals(node.RepoRoot, SourceSync.SourceRoot, StringComparison.OrdinalIgnoreCase)
                ? BuildConfig()
                : "";

            var packages = Libraries(Path.Combine(node.RepoRoot, node.SourceDir))
                .Where(l => l.Type == "package")
                .OrderBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Version, StringComparer.OrdinalIgnoreCase)
                .Select(l => l.Id + "@" + l.Version)
                .ToList();

            var deps = new List<NodeDep>();
            foreach (string up in node.Upstream)
            {
                string version = upstreamVersions.TryGetValue(up, out var value) ? value : "";
                string fingerprint = upstreamFingerprints.TryGetValue(up, out var print) ? print : "";
                deps.Add(new NodeDep(up, "project", version, fingerprint));
            }

            return new NodeFingerprint(Combine(tree, config, packages, deps), deps);
        }

        /// <summary>指纹公式：内容 + 构建配置 + 包闭包 + 上游版本/指纹，各段排序后取 SHA256。</summary>
        internal static string Combine(
            string tree, string config, IEnumerable<string> packages, IEnumerable<NodeDep> upstream)
        {
            var sb = new StringBuilder();
            sb.Append("tree:").Append(tree ?? "").Append('\n');
            sb.Append("cfg:").Append(config ?? "").Append('\n');
            foreach (string package in (packages ?? []).OrderBy(p => p, StringComparer.Ordinal))
                sb.Append("pkg:").Append(package).Append('\n');
            foreach (var dep in (upstream ?? []).OrderBy(d => d.Id, StringComparer.Ordinal))
                sb.Append("up:").Append(dep.Id).Append('@').Append(dep.Version)
                  .Append('#').Append(dep.Fingerprint).Append('\n');
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        private string SourceTree(VersionNode node)
        {
            string repo = RepoTree(node.RepoRoot);
            if (repo == null) return "";

            var entries = Snapshots.ListTree(node.RepoRoot, repo, node.WholeRepo ? "" : node.SourceDir);
            string versionPath = (Path.IsPathRooted(node.VersionFile)
                    ? Path.GetRelativePath(node.RepoRoot, node.VersionFile)
                    : node.VersionFile)
                .Replace('\\', '/').Trim('/');

            var sb = new StringBuilder();
            foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
            {
                // 版本号那一行不算内容：否则每次把号写回文件都会让指纹变，下一轮又升一版，无限循环
                string blob = string.Equals(entry.Path, versionPath, StringComparison.OrdinalIgnoreCase)
                    ? VersionNeutralHash(Path.Combine(node.RepoRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar)))
                    : entry.Sha;
                sb.Append(blob).Append(' ').Append(entry.Path).Append('\n');
            }
            return Hash(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static readonly System.Text.RegularExpressions.Regex VersionTag = new(
            "<Version\\s*>[^<]*</Version>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        internal static string VersionNeutralHash(string path)
        {
            try
            {
                string text = VersionTag.Replace(File.ReadAllText(path), "<Version/>");
                return Hash(Encoding.UTF8.GetBytes(text));
            }
            catch
            {
                return "";
            }
        }

        private string RepoTree(string root)
        {
            if (_trees.TryGetValue(root, out string cached)) return cached;
            string tree = Snapshots.Tree(root);
            _trees[root] = tree;
            return tree;
        }

        /// <summary>根目录的构建配置（CPM、全局 props、SDK 约束）：改了同样会改变产物。</summary>
        private static string BuildConfig()
        {
            var sb = new StringBuilder();
            foreach (string name in BuildInputs)
            {
                string path = Path.Combine(SourceSync.SourceRoot, name);
                if (!File.Exists(path)) continue;
                sb.Append(name).Append('=').Append(Hash(File.ReadAllBytes(path))).Append('\n');
            }
            return sb.ToString();
        }

        private static string Hash(byte[] bytes)
            => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        /// <summary>restore 之后的解析结果：直接 + 传递依赖都在里面（含项目引用，type=project）。</summary>
        internal static (string Id, string Version, string Type)[] Libraries(string projectDir)
        {
            string path = Path.Combine(projectDir, "obj", "project.assets.json");
            if (!File.Exists(path)) return [];
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                if (!doc.RootElement.TryGetProperty("libraries", out var libraries)) return [];

                var list = new List<(string, string, string)>();
                foreach (var entry in libraries.EnumerateObject())
                {
                    int slash = entry.Name.LastIndexOf('/');
                    if (slash <= 0) continue;
                    string type = entry.Value.TryGetProperty("type", out var value) ? value.GetString() ?? "" : "";
                    list.Add((entry.Name[..slash], entry.Name[(slash + 1)..], type));
                }
                return list.ToArray();
            }
            catch
            {
                return [];
            }
        }
    }
}
