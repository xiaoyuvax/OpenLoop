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
                Title = string.Format(Localization.T("PickAttachments"), limit),
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
        /// 输入框里 Ctrl+V：剪贴板是文件或图片就追加成附件并拦掉这次粘贴（返回 true）。
        /// 剪贴板带文字时按文字粘贴（Word/网页复制常同时带图，不能抢）。
        /// </summary>
        public static bool TryPaste(List<string> current, int limit, out bool overflow)
        {
            overflow = false;
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    overflow = Add(current, Clipboard.GetFileDropList().Cast<string>(), limit);
                    return true;
                }
                if (Clipboard.ContainsText() || !Clipboard.ContainsImage()) return false;
                using var image = Clipboard.GetImage();
                if (image == null) return false;
                if (current.Count >= limit) { overflow = true; return true; }
                // shortcut: 粘贴的图片落在 %TEMP%\OpenLoop-paste，发送后不清理；需要回收时再加
                string dir = Path.Combine(Path.GetTempPath(), "OpenLoop-paste");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"paste_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Add(current, [path], limit);
                return true;
            }
            catch (Exception)
            {
                // 剪贴板被别的进程占着等：退回普通文本粘贴
                return false;
            }
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

        public const string AddAttachmentTag = "add-attachment";

        /// <summary>附件按钮的下拉：第一项加附件，其余每项是一个待移除的附件。</summary>
        public static void FillMenu(AntdUI.Dropdown menu, IReadOnlyList<string> paths)
        {
            menu.Items.Clear();
            menu.Items.Add(new AntdUI.SelectItem(Localization.T("添加附件…")) { Tag = AddAttachmentTag });
            foreach (string path in paths ?? []) menu.Items.Add(new AntdUI.SelectItem("✕ " + Path.GetFileName(path)) { Tag = path });
        }

        /// <summary>点下拉里的附件：问一句再移除。</summary>
        public static void AskRemove(IWin32Window owner, List<string> paths, string path)
        {
            if (MessageBox.Show(owner, string.Format(Localization.T("RemoveAttachmentAsk"), Path.GetFileName(path)),
                    Localization.T("移除附件"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }

        public static string Label(IReadOnlyList<string> paths, int limit)
        {
            if (paths == null || paths.Count == 0) return "";
            return string.Format(Localization.T("AttachmentCount"), paths.Count, limit);
        }
    }
}
