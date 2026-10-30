namespace OpenLoop.App
{
    /// <summary>工作目录路径归一：同一目录的不同写法（盘符大小写、斜杠、尾分隔符）算同一个项目。</summary>
    internal static class Paths
    {
        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()).TrimEnd('\\', '/'); }
            catch { return path.Trim().TrimEnd('\\', '/'); }
        }

        internal static bool Same(string a, string b)
            => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>按归一化路径去重，保留第一次出现的写法。</summary>
        internal static List<string> Dedup(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var path in paths ?? [])
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (seen.Add(Normalize(path))) result.Add(path);
            }
            return result;
        }
    }
}
