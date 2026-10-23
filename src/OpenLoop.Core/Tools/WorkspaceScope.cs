using OpenLoop.Core.Safety;

namespace OpenLoop.Core.Tools
{
    /// <summary>
    /// 工作目录边界：所有文件类工具只能在这个范围之下活动。
    /// 路径先做 realpath（解析 symlink/junction）再判前缀；按平台决定大小写敏感性。
    /// </summary>
    public sealed class WorkspaceScope
    {
        private static readonly StringComparison PathComparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private readonly string[] _denied;
        private readonly bool _unrestricted;

        /// <param name="denied">相对 root 的受保护子路径；默认保护运行态证据 <c>.openloop</c>。</param>
        /// <param name="unrestricted">完全许可：不限制路径边界，也不过滤受保护子目录（仅交互 Worker 使用）。</param>
        public WorkspaceScope(string root, IEnumerable<string> denied = null, bool unrestricted = false)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("工作目录不能为空", nameof(root));
            Root = RealPath(Path.GetFullPath(root));
            _unrestricted = unrestricted;
            _denied = unrestricted
                ? []
                : (denied ?? [".openloop"])
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => RealPath(Path.Combine(Root, d)))
                    .ToArray();
        }

        public bool Unrestricted => _unrestricted;

        /// <summary>可选的文件写入撤销队列；write_file 写入前记录旧内容。</summary>
        public FileUndoLog Undo { get; set; }

        public string Root { get; }

        public bool IsDenied(string path)
        {
            if (string.IsNullOrEmpty(path) || _denied.Length == 0) return false;
            string full = RealPath(Path.GetFullPath(path));
            foreach (string denied in _denied)
            {
                if (full.Equals(denied, PathComparison)) return true;
                if (full.StartsWith(denied + Path.DirectorySeparatorChar, PathComparison)) return true;
            }
            return false;
        }

        public bool TryResolve(string path, out string full, out string error)
        {
            full = null;
            error = null;
            try
            {
                string candidate = string.IsNullOrWhiteSpace(path)
                    ? Root
                    : (Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
                full = RealPath(Path.GetFullPath(candidate));
            }
            catch (Exception ex)
            {
                error = $"路径无效：{ex.Message}";
                return false;
            }

            string rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar)
                ? Root
                : Root + Path.DirectorySeparatorChar;

            if (!_unrestricted
                && !full.Equals(Root, PathComparison) && !full.StartsWith(rootWithSep, PathComparison))
            {
                error = $"路径越界：{path} 不在工作目录内";
                full = null;
                return false;
            }

            if (!_unrestricted && IsDenied(full))
            {
                error = $"路径受保护（运行态证据）：{path}";
                full = null;
                return false;
            }

            return true;
        }

        public string Relative(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            return full.StartsWith(Root, PathComparison)
                ? Path.GetRelativePath(Root, full).Replace('\\', '/')
                : full;
        }

        /// <summary>
        /// 解析到真实路径：从最深的已存在祖先开始解析链接链，再拼回不存在的尾部。
        /// 这样「写一个还不存在的文件」也能拿到正确边界。
        /// </summary>
        internal static string RealPath(string full)
        {
            string current = full;
            var tail = new Stack<string>();
            while (!string.IsNullOrEmpty(current) && !Directory.Exists(current) && !File.Exists(current))
            {
                string name = Path.GetFileName(current);
                if (string.IsNullOrEmpty(name)) break;
                tail.Push(name);
                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }

            string real = string.IsNullOrEmpty(current) ? full : ResolveChain(current);
            while (tail.Count > 0) real = Path.Combine(real, tail.Pop());
            return Path.GetFullPath(real);
        }

        private static string ResolveChain(string path)
        {
            for (int i = 0; i < 32; i++)
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (!info.Exists) return path;
                FileSystemInfo target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null) return path;
                path = target.FullName;
            }
            return path;
        }
    }
}
