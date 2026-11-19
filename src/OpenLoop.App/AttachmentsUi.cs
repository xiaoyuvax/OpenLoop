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

        public static List<AttachmentPayload> Read(IReadOnlyList<string> paths, out string error)
        {
            error = null;
            var list = new List<AttachmentPayload>();
            foreach (string path in paths ?? [])
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) { error = "附件不存在：" + path; return null; }
                    if (info.Length > Attachments.MaxBytes)
                    {
                        error = $"附件过大（上限 {Attachments.MaxBytes / 1024 / 1024}MB）：{info.Name}";
                        return null;
                    }
                    list.Add(new AttachmentPayload
                    {
                        Name = info.Name,
                        Data = Convert.ToBase64String(File.ReadAllBytes(path))
                    });
                }
                catch (Exception ex)
                {
                    error = "读取附件失败：" + ex.Message;
                    return null;
                }
            }
            return list;
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
