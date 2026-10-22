namespace OpenLoop.Core.Tools
{
    /// <summary>
    /// 工作目录边界：所有文件类工具只能在这个根之下活动。
    /// 越界一律返回错误，不抛异常（错误要能作为工具结果回喂给模型）。
    /// </summary>
    public sealed class WorkspaceScope
    {
        public WorkspaceScope(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("工作目录不能为空", nameof(root));
            Root = Path.GetFullPath(root);
        }

        public string Root { get; }

        public bool TryResolve(string path, out string full, out string error)
        {
            full = null;
            error = null;
            try
            {
                string candidate = string.IsNullOrWhiteSpace(path)
                    ? Root
                    : (Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
                full = Path.GetFullPath(candidate);
            }
            catch (Exception ex)
            {
                error = $"路径无效：{ex.Message}";
                return false;
            }

            string rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar)
                ? Root
                : Root + Path.DirectorySeparatorChar;

            if (!full.Equals(Root, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                error = $"路径越界：{path} 不在工作目录内";
                full = null;
                return false;
            }

            return true;
        }

        public string Relative(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            return full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(Root, full).Replace('\\', '/')
                : full;
        }
    }
}
