using OpenLoop.Service.Media;

namespace OpenLoop.App
{
    /// <summary>Worker 与 Loop 共同的文件选择/读取（两边的附件行为必须一致）。</summary>
    internal static class AttachmentsUi
    {
        public static List<string> Pick(IWin32Window owner, IReadOnlyList<string> current, int limit, out bool overflow)
        {
            overflow = false;
            var result = new List<string>(current ?? []);
            using var dialog = new OpenFileDialog
            {
                Title = $"选择附件（最多 {limit} 个）",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

            foreach (string path in dialog.FileNames)
            {
                if (result.Count >= limit) { overflow = true; break; }
                if (!result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
            }
            return result;
        }

        /// <summary>把拖放进来的路径追加到附件（去重、按上限截断）；返回是否有路径因超过上限被丢弃。</summary>
        public static bool Add(List<string> current, IEnumerable<string> paths, int limit)
        {
            bool overflow = false;
            foreach (string path in paths ?? [])
            {
                if (current.Count >= limit) { overflow = true; break; }
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                if (!current.Contains(path, StringComparer.OrdinalIgnoreCase)) current.Add(path);
            }
            return overflow;
        }

        /// <summary>
        /// 让输入区整棵控件树接受文件拖放：WinForms 只把 DragDrop 发给 AllowDrop 的最深控件，
        /// 所以子控件（含 AntdUI 内嵌文本框，以及之后才加进来的）都要挂。
        /// </summary>
        public static void EnableDrop(Control root, Action<string[]> onFiles)
        {
            if (root == null) return;
            root.AllowDrop = true;
            root.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
            root.DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) onFiles(paths);
            };
            root.ControlAdded += (_, e) => EnableDrop(e.Control, onFiles);
            foreach (Control child in root.Controls) EnableDrop(child, onFiles);
        }

        public static string Label(IReadOnlyList<string> paths, int limit)
        {
            if (paths == null || paths.Count == 0) return "";
            string names = string.Join("、", paths.Select(Path.GetFileName).Take(3));
            if (paths.Count > 3) names += "…";
            return $"📎 {paths.Count}/{limit}：{names}（点此清空）";
        }
    }
}
