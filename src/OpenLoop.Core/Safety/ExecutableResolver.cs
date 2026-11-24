namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// 把 argv[0] 解析成 CreateProcess 真能启动的东西：按 PATH + PATHEXT 补扩展名。
    /// npm 装的 <c>.cmd</c>/<c>.bat</c> 垫片交给 <c>cmd.exe /c</c>（CreateProcess 只补 .exe，
    /// 直接启动 <c>ocr</c> 这类命令行会报「系统找不到指定的文件」）；
    /// 走 cmd 时参数里出现 shell 元字符就拒绝，避免参数被 cmd 二次解释。
    /// </summary>
    public static class ExecutableResolver
    {
        private static readonly string[] DefaultExtensions = [".COM", ".EXE", ".BAT", ".CMD", ""];

        private static readonly char[] ShellMeta = ['&', '|', '<', '>', '^', '%', '"', '\r', '\n'];

        /// <param name="command">argv[0]：裸名字走 PATH，含分隔符就按路径处理。</param>
        /// <param name="args">后续参数；只有走 cmd 垫片时才检查元字符。</param>
        /// <returns>Error 非空表示参数不安全，调用方应直接失败。</returns>
        public static (string FileName, List<string> PrefixArgs, string Error) Resolve(
            string command, IReadOnlyList<string> args = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return (command, [], null);

            bool hasPath = command.Contains(Path.DirectorySeparatorChar) || command.Contains('/');
            if (hasPath)
                return Shim("cmd.exe", ["/c", command], command, args);

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] extensions = OperatingSystem.IsWindows()
                ? ExtensionsFrom(Environment.GetEnvironmentVariable("PATHEXT"))
                : [""];

            foreach (string raw in path.Split(Path.PathSeparator))
            {
                string dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;

                foreach (string extension in extensions)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(dir, command + extension);
                    }
                    catch
                    {
                        // 非法路径（含通配符等）跳过
                        continue;
                    }

                    if (!File.Exists(candidate)) continue;
                    if (IsShim(candidate)) return Shim("cmd.exe", ["/c", candidate], candidate, args);
                    return (candidate, [], null);
                }
            }

            // 没找到：原样交出去，让调用方拿到「找不到文件」而不是静默换命令
            return (command, [], null);
        }

        private static (string FileName, List<string> PrefixArgs, string Error) Shim(
            string fileName, List<string> prefix, string resolved, IReadOnlyList<string> args)
        {
            if (!IsShim(resolved)) return (resolved, [], null);

            string bad = (args ?? []).FirstOrDefault(a => a != null && a.IndexOfAny(ShellMeta) >= 0);
            if (bad != null)
                return (resolved, [],
                    "参数不能经 cmd 执行 " + Path.GetFileName(resolved) + "：含 shell 元字符 " + bad);

            return (fileName, prefix, null);
        }

        private static bool IsShim(string file)
            => file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
               || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

        /// <summary>PATHEXT 是候选扩展名；末尾补一个空串，让「已经写全扩展名」的名字也能命中。</summary>
        private static string[] ExtensionsFrom(string pathext)
        {
            if (string.IsNullOrWhiteSpace(pathext)) return DefaultExtensions;
            var parts = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Where(p => p.Length > 0 && p.StartsWith('.'))
                .ToList();
            if (parts.Count == 0) parts.AddRange(DefaultExtensions);
            parts.Add("");
            return parts.ToArray();
        }
    }
}
