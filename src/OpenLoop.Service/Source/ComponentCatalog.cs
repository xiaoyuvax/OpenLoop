using System.Text.Json;

namespace OpenLoop.Service.Source
{
    /// <summary>谁把这个组件编进来：仓库目录名（openloop 或另一个组件的 <see cref="ComponentSpec.Dir"/>）与项目目录。</summary>
    public sealed record ComponentConsumer
    {
        public string Repo { get; init; } = "";
        public string Project { get; init; } = "";
    }

    /// <summary>
    /// 一个以源码方式参与迭代的外部组件（components.json 的一条）。
    /// 换 fork 只改 <see cref="Url"/>；其余字段缺省时由 <see cref="ComponentCatalog.Defaults"/> 按 id 补齐。
    /// </summary>
    public sealed record ComponentSpec
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";

        /// <summary>上游地址：缺省是官方仓库，换成自己的 fork 时改这里。</summary>
        public string Url { get; init; } = "";

        /// <summary>与源码根同级的检出目录名。</summary>
        public string Dir { get; init; } = "";

        /// <summary>判定「已检出」与项目目录的标记文件（相对组件根）。</summary>
        public string Marker { get; init; } = "";

        /// <summary>承载 &lt;Version&gt; 的文件（相对组件根）。</summary>
        public string VersionFile { get; init; } = "";

        /// <summary>编译产物程序集名：引用方以 project 引用出现它 = 走源码，否则走包（不参与迭代）。</summary>
        public string Assembly { get; init; } = "";

        /// <summary>参与编译的平台（windows/linux/macos/all）；空 = 所有平台。</summary>
        public string[] Platforms { get; init; } = [];

        /// <summary>留在清单里但不再参与迭代（比如已经收编成包）。</summary>
        public bool Retired { get; init; }

        /// <summary>谁引用它：这些项目就是它的下游，依赖方向由此反推。</summary>
        public List<ComponentConsumer> Consumers { get; init; } = [];

        public string Note { get; init; } = "";
    }

    /// <summary>
    /// 参与迭代的组件清单：仓库根下的 components.json。文件缺失、解析失败或漏写内置项时，
    /// 按 id 用 <see cref="Defaults"/> 补齐并从官方地址克隆——老安装升级后不能因为少了这份文件就没了组件。
    /// </summary>
    public static class ComponentCatalog
    {
        public const string FileName = "components.json";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        internal sealed class CatalogFile
        {
            public List<ComponentSpec> Components { get; set; } = [];
        }

        /// <summary>硬编码兜底：当前部署的组件构成（官方仓库）。</summary>
        public static IReadOnlyList<ComponentSpec> Defaults() =>
        [
            new()
            {
                Id = "puppetcore", Label = "PuppetCore",
                Url = SourceSync.PuppetCoreUrl, Dir = "PuppetCore",
                Marker = "Puppet.Core/Puppet.Core.csproj",
                VersionFile = "Puppet.Common.targets",
                Assembly = "Puppet.Core",
                Consumers = [new() { Repo = "openloop", Project = "src/OpenLoop.Service" }]
            },
            new()
            {
                Id = "chatflow", Label = "ChatFlow",
                Url = SourceSync.ChatFlowUrl, Dir = "ChatFlow",
                Marker = "ChatFlow/ChatFlow.csproj",
                VersionFile = "ChatFlow/ChatFlow.csproj",
                Assembly = "ChatFlow",
                Platforms = ["windows"],
                Consumers = [new() { Repo = "openloop", Project = "src/OpenLoop.App" }]
            }
        ];

        /// <summary>清单文件的默认位置：源码根下。</summary>
        public static string DefaultPath => Path.Combine(SourceSync.SourceRoot, FileName);

        /// <summary>参与迭代的组件：没退休、适用于当前平台；顺序是「被引用者在前」。</summary>
        public static IReadOnlyList<ComponentSpec> Iterable()
            => Load(DefaultPath).Where(c => !c.Retired && PlatformOk(c)).ToList();

        public static ComponentSpec Find(string id)
            => Iterable().FirstOrDefault(c => Same(c.Id, id));

        public static string DefaultUrl(string id) => Defaults().FirstOrDefault(c => Same(c.Id, id))?.Url ?? "";

        /// <summary>读清单文件；不存在或损坏时回退内置默认项。</summary>
        public static IReadOnlyList<ComponentSpec> Load(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return Merge(Parse(File.ReadAllText(path)));
            }
            catch
            {
                // 读不到就当没有清单
            }
            return Defaults();
        }

        /// <summary>解析清单正文；不是合法 JSON 时返回 null。</summary>
        public static List<ComponentSpec> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonSerializer.Deserialize<CatalogFile>(text, Json)?.Components;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 合并解析结果：空/损坏 → 内置默认项；漏写的内置组件按 id 补回；缺字段按 id 用默认值补；
        /// 最后排成「被引用者在前」的顺序并去掉退休/不适用平台的条目。
        /// </summary>
        public static IReadOnlyList<ComponentSpec> Merge(IEnumerable<ComponentSpec> parsed)
        {
            var defaults = Defaults();
            var list = (parsed ?? []).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)).ToList();
            if (list.Count == 0) return defaults;

            foreach (var fallback in defaults)
                if (!list.Any(c => Same(c.Id, fallback.Id))) list.Add(fallback);

            return Order(list
                .Select(c => Fill(c, defaults.FirstOrDefault(d => Same(d.Id, c.Id))))
                .Where(c => !c.Retired && PlatformOk(c))
                .ToList());
        }

        /// <summary>两个远端地址指向同一仓库：忽略协议、主机与 .git（https / ssh 克隆都算同一个）。</summary>
        public static bool SameUrl(string left, string right)
        {
            string a = RepoPath(left);
            return a.Length > 0 && string.Equals(a, RepoPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool PlatformOk(ComponentSpec spec)
        {
            var platforms = spec?.Platforms ?? [];
            if (platforms.Length == 0) return true;
            foreach (string platform in platforms)
            {
                string name = (platform ?? "").Trim().ToLowerInvariant();
                if (name == "all") return true;
                if (name == "windows" && OperatingSystem.IsWindows()) return true;
                if (name == "linux" && OperatingSystem.IsLinux()) return true;
                if (name == "macos" && OperatingSystem.IsMacOS()) return true;
            }
            return false;
        }

        /// <summary>缺的字段按默认项补；没有默认项的新组件用 id 当目录名。</summary>
        private static ComponentSpec Fill(ComponentSpec spec, ComponentSpec fallback)
        {
            if (fallback == null)
                return spec with
                {
                    Id = spec.Id.Trim(),
                    Dir = Blank(spec.Dir) ? spec.Id.Trim() : spec.Dir.Trim(),
                    Note = spec.Note ?? ""
                };

            return spec with
            {
                Id = spec.Id.Trim(),
                Dir = Blank(spec.Dir) ? fallback.Dir : spec.Dir.Trim(),
                Label = Blank(spec.Label) ? fallback.Label : spec.Label,
                Url = Blank(spec.Url) ? fallback.Url : spec.Url.Trim(),
                Marker = Blank(spec.Marker) ? fallback.Marker : spec.Marker.Trim(),
                VersionFile = Blank(spec.VersionFile) ? fallback.VersionFile : spec.VersionFile.Trim(),
                Assembly = Blank(spec.Assembly) ? fallback.Assembly : spec.Assembly.Trim(),
                Platforms = spec.Platforms is { Length: > 0 } ? spec.Platforms : fallback.Platforms,
                Consumers = spec.Consumers is { Count: > 0 } ? spec.Consumers : fallback.Consumers,
                Note = spec.Note ?? ""
            };
        }

        /// <summary>拓扑序：被别的组件引用的排前面（指纹要先拿到上游的版本与指纹）；有环就按原顺序收尾。</summary>
        private static List<ComponentSpec> Order(List<ComponentSpec> specs)
        {
            var ordered = new List<ComponentSpec>(specs.Count);
            var remaining = new List<ComponentSpec>(specs);
            while (remaining.Count > 0)
            {
                var next = remaining.FirstOrDefault(s => !remaining.Any(other => !ReferenceEquals(other, s) && DependsOn(s, other)))
                           ?? remaining[0];
                ordered.Add(next);
                remaining.Remove(next);
            }
            return ordered;
        }

        /// <summary>consumer 是否引用了 dependency：dependency 的 consumers 里写着 consumer 的仓库。</summary>
        private static bool DependsOn(ComponentSpec consumer, ComponentSpec dependency)
            => (dependency.Consumers ?? []).Any(c => Same(c.Repo, consumer.Dir) || Same(c.Repo, consumer.Id));

        private static string RepoPath(string url)
        {
            string text = (url ?? "").Trim().TrimEnd('/');
            if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) text = text[..^4];
            text = text.Replace('\\', '/');

            // scp 风格 git@host:owner/repo → 去掉 host 与冒号
            int colon = text.IndexOf(':');
            if (colon > 0 && !text.Contains("://", StringComparison.Ordinal)) text = text[(colon + 1)..];

            var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[^2] + "/" + parts[^1] : "";
        }

        private static bool Blank(string value) => string.IsNullOrWhiteSpace(value);

        private static bool Same(string left, string right)
            => string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
