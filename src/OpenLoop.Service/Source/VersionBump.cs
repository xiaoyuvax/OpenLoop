using System.Text.RegularExpressions;

namespace OpenLoop.Service.Source
{
    /// <summary>
    /// 迭代自动升版：源码版本号不高于运行版本时，patch +1 生成新目标版本，
    /// 并在编译前把新号写回该节点自己的版本文件，保证产物与登记版本真正升版。
    /// 七个节点的号各自独立，按依赖边级联（见 <see cref="VersionScan"/>）。
    /// </summary>
    public static class VersionBump
    {
        /// <summary>节点 id → 该节点承载 &lt;Version&gt; 的文件；外部组件返回绝对路径。</summary>
        public static string FileFor(string role) => (role ?? "").Trim().ToLowerInvariant() switch
        {
            "core" => "src/OpenLoop.Core/OpenLoop.Core.csproj",
            "service" => "src/OpenLoop.Service/OpenLoop.Service.csproj",
            "a" => "src/OpenLoop.A/OpenLoop.A.csproj",
            "i" => "src/OpenLoop.I/OpenLoop.I.csproj",
            // 壳的两种产物共用一个号，只写这一处
            "shell" => "ShellVersion.props",
            "puppetcore" => Path.Combine(SourceSync.PuppetRoot, "Puppet.Common.targets"),
            "chatflow" => Path.Combine(SourceSync.ChatFlowRoot, "ChatFlow", "ChatFlow.csproj"),
            _ => null
        };

        /// <summary>算出下一版本：源码版本可解析且高于运行版本就用源码版本，否则在运行版本上 patch+1；都无法解析返回 null。</summary>
        public static string NextVersion(string sourceVersion, string runningVersion)
        {
            if (System.Version.TryParse(Clean(sourceVersion), out var source))
            {
                if (System.Version.TryParse(Clean(runningVersion), out var running) && source <= running)
                    return Bump(running);
                return Clean(sourceVersion);
            }
            return System.Version.TryParse(Clean(runningVersion), out var run) ? Bump(run) : null;
        }

        internal static string Bump(Version version)
            => $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0) + 1}";

        /// <summary>把版本号写回该角色版本文件的 &lt;Version&gt; 节点；成功返回 true。</summary>
        public static bool WriteVersion(string role, string root, string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            try
            {
                string relative = FileFor(role);
                if (relative == null) return false;
                string path = Path.IsPathRooted(relative)
                    ? relative
                    : Path.Combine(string.IsNullOrWhiteSpace(root) ? SourceSync.SourceRoot : root, relative);
                if (!File.Exists(path)) return false;

                string text = File.ReadAllText(path);
                // 不能用 "$1" + version：版本号以数字开头时会被拼成 "$10.1.1"，.NET 把 $10 当组引用
                // （组不存在时按字面量输出），文件里就留下 "$10.1.1</Version>"，下次读版本直接读不动。
                string updated = Regex.Replace(
                    text, "(<Version\\s*>)[^<]*(</Version>)",
                    m => m.Groups[1].Value + version.Trim() + m.Groups[2].Value,
                    RegexOptions.IgnoreCase);
                if (updated == text) return false;

                File.WriteAllText(path, updated, new System.Text.UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Clean(string value)
        {
            string text = (value ?? "").Trim();
            int plus = text.IndexOf('+');
            if (plus >= 0) text = text[..plus];
            int dash = text.IndexOf('-');
            if (dash > 0 && System.Version.TryParse(text[..dash], out _)) text = text[..dash];
            return text;
        }
    }
}
