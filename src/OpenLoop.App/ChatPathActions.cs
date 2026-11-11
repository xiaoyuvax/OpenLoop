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
        /// 文件夹 → 问一句再用资源管理器打开；不存在 → 提示并问是否复制路径；都存在时返回 false（交给文件预览）。
        /// 返回 true 表示点击已被这里处理掉。
        /// </summary>
        internal static bool HandleClick(IWin32Window owner, string target, string workspace)
        {
            string full;
            try
            {
                full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(workspace ?? "", target));
            }
            catch
            {
                full = target;   // 连路径都不合法：按「不存在」处理，让用户复制原文
            }

            if (Directory.Exists(full)) return AskOpenFolder(owner, full);
            if (File.Exists(full)) return false;
            AskMissing(owner, full);
            return true;
        }

        private static bool AskOpenFolder(IWin32Window owner, string full)
        {
            if (MessageBox.Show(owner, "这是文件夹，用资源管理器打开吗？\n" + full, "打开文件夹",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return true;

            try
            {
                Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "打开失败：" + ex.Message, "打开文件夹", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return true;
        }

        private static void AskMissing(IWin32Window owner, string full)
        {
            if (MessageBox.Show(owner, "路径不存在：\n" + full + "\n\n要复制这段路径吗？", "路径不存在",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try { Clipboard.SetText(full); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "复制失败：" + ex.Message, "路径不存在", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
