using System.Text.Json;

namespace OpenLoop.Service.Source
{
    /// <summary>源码不可用时靠包编译的组件：包 id、参考版本、注入消费方 csproj 的 MSBuild 属性名。</summary>
    public sealed record PackageFallbackPlan(string PackageId, string Reference, string Property);

    /// <summary>
    /// 包兜底的版本策略：清单里的 packageVersion 只是参考版本（下限，也是最新版编不过时的退路），
    /// 实际引用 nuget.org 上 &gt;= 参考版本的最高稳定版。清单里没有包兜底的组件仍是硬失败。
    /// </summary>
    public static class PackageFallback
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>注入用的 MSBuild 属性名：包 id 只留字母数字（Vax-FCTB → OpenLoopPkg_VaxFCTB）。</summary>
        public static string PropertyName(string packageId)
            => "OpenLoopPkg_" + new string((packageId ?? "").Where(char.IsLetterOrDigit).ToArray());

        /// <summary>清单里没有源码检出、又写了包兜底的组件：这些得按包编译。</summary>
        public static IReadOnlyList<PackageFallbackPlan> Plans()
        {
            var plans = new List<PackageFallbackPlan>();
            foreach (var spec in ComponentCatalog.Iterable())
            {
                string package = (spec.PackageId ?? "").Trim();
                if (package.Length == 0) continue;

                // 源码检出在就编源码（与消费方 csproj 的 Exists 条件同一个判据），包兜底不参与
                string root = SourceSync.ComponentRoot(spec.Dir);
                if (File.Exists(Path.Combine(root, spec.Marker))) continue;

                plans.Add(new(package, (spec.PackageVersion ?? "").Trim(), PropertyName(package)));
            }
            return plans;
        }

        /// <summary>
        /// nuget.org 上 ≥ 参考版本的最高稳定版；查不到（离线、包不存在）就用参考版本，
        /// 不因为查版本把构建挡掉。
        /// </summary>
        public static async Task<string> LatestAsync(
            string packageId, string reference, CancellationToken ct = default)
        {
            string id = (packageId ?? "").Trim().ToLowerInvariant();
            if (id.Length == 0) return reference ?? "";
            try
            {
                string url = "https://api.nuget.org/v3-flatcontainer/" + Uri.EscapeDataString(id) + "/index.json";
                using var response = await Http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return reference ?? "";
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("versions", out var versions)) return reference ?? "";
                return Best(versions.EnumerateArray().Select(v => v.GetString() ?? ""), reference);
            }
            catch
            {
                return reference ?? "";
            }
        }

        /// <summary>候选里挑 ≥ 参考版本的最高稳定版（预发布跳过）；没有更高的就用参考版本。</summary>
        public static string Best(IEnumerable<string> versions, string reference)
        {
            Version floor = Version.TryParse((reference ?? "").Trim(), out var parsed) ? Pad(parsed) : null;
            string best = "";
            Version bestVersion = null;
            foreach (string raw in versions ?? [])
            {
                string text = (raw ?? "").Trim();
                if (text.Length == 0 || text.Contains('-')) continue;
                if (!Version.TryParse(text, out var version)) continue;

                version = Pad(version);
                if (floor != null && version < floor) continue;
                if (bestVersion != null && version <= bestVersion) continue;

                best = text;
                bestVersion = version;
            }
            return best.Length > 0 ? best : reference ?? "";
        }

        /// <summary>System.Version 把缺的段当 -1，2.19.0 与 2.19.0.0 会比出不相等；补齐成 0 再比。</summary>
        private static Version Pad(Version version)
            => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
