using System.Diagnostics;

namespace OpenLoop.App
{
    /// <summary>
    /// 聊天区点击路径后的分流：带点的目录名（如 …\src\OpenLoop.A）会被当成文件链接，
    /// 按文件预览只会报「文件不存在」；这里先按「文件夹 / 文件 / 不存在」判定，再决定动作。
    /// </summary>
    internal static class ChatPathActions
    {
        /// <summary>
        /// 文件夹 → 直接用资源管理器打开；不存在 → 提示并问是否复制路径；文件存在则返回 false（交给文件预览）。
        /// 返回 true 表示点击已被这里处理掉。
        /// </summary>
        internal static bool HandleClick(IWin32Window owner, string target, string workspace)
        {
            string full = Resolve(target, workspace);
            if (Directory.Exists(full))
            {
                Shell(owner, full, Localization.T("打开文件夹"));
                return true;
            }
            if (File.Exists(full)) return false;
            AskMissing(owner, full);
            return true;
        }

        /// <summary>预览不了的普通文件（二进制、太大、解码失败）：问一句再用系统默认程序打开。</summary>
        internal static void AskSystemOpen(IWin32Window owner, string target, string workspace, string reason)
        {
            string full = Resolve(target, workspace);
            string text = (string.IsNullOrWhiteSpace(reason) ? "" : reason + "\n\n") + Localization.T("AskOpenWithDefault") + full;
            if (MessageBox.Show(owner, text, Localization.T("打开文件"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Shell(owner, full, Localization.T("打开文件"));
        }

        /// <summary>用资源管理器打开一个目录（不存在时提示并问是否复制路径）。</summary>
        internal static void OpenFolder(IWin32Window owner, string folder)
            => HandleClick(owner, folder, folder);

        private static string Resolve(string target, string workspace)
        {
            try
            {
                return Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(workspace ?? "", target));
            }
            catch
            {
                return target;   // 连路径都不合法：按「不存在」处理，让用户复制原文
            }
        }

        private static void Shell(IWin32Window owner, string full, string title)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, Localization.T("打开失败：") + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void AskMissing(IWin32Window owner, string full)
        {
            if (MessageBox.Show(owner, string.Format(Localization.T("AskCopyPath"), full), Localization.T("路径不存在"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try { Clipboard.SetText(full); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, Localization.T("复制失败：") + ex.Message, Localization.T("路径不存在"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
