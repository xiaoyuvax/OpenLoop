using System.Text;

namespace OpenLoop.Service.Media
{
    /// <summary>界面传来的一个附件（base64）：WinForms 与浏览器走同一个 JSON 契约。</summary>
    public sealed class AttachmentPayload
    {
        public string Name { get; set; }
        public string Data { get; set; }
    }

    /// <summary>
    /// 附件落盘：随消息一起收，存在工作区 .openloop/tmp/attachments/&lt;会话&gt;/ 下。
    /// 路径会作为「附件」行拼进用户消息，模型用 ReadFile 读文本、用图像工具读图片。
    /// </summary>
    public static class Attachments
    {
        // shortcut: 单文件上限 20MB；要传大模型文件时再改成按模型上下文限制。
        public const int MaxBytes = 20 * 1024 * 1024;

        /// <summary>保存附件并返回路径；超过数量/大小限制返回 null 并给出原因。</summary>
        public static List<string> Save(string workspace, string sessionId, IEnumerable<AttachmentPayload> files,
            int limit, out string error)
        {
            error = null;
            var saved = new List<string>();
            var input = files?.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Data)).ToList() ?? [];
            if (input.Count == 0) return saved;
            if (string.IsNullOrWhiteSpace(workspace))
            {
                error = "工作区为空，无法保存附件";
                return null;
            }
            if (input.Count > limit)
            {
                error = $"附件最多 {limit} 个（当前模型）";
                return null;
            }

            string dir = Path.Combine(workspace, ".openloop", "tmp", "attachments",
                Safe(sessionId, "session"));
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            foreach (var file in input)
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(file.Data); }
                catch
                {
                    error = "附件解码失败：" + file.Name;
                    return null;
                }
                if (bytes.Length > MaxBytes)
                {
                    error = $"附件过大（上限 {MaxBytes / 1024 / 1024}MB）：{file.Name}";
                    return null;
                }

                string name = Safe(Path.GetFileName(file.Name), "file");
                string path = Unique(Path.Combine(dir, stamp + "_" + name));
                File.WriteAllBytes(path, bytes);
                saved.Add(path);
            }
            return saved;
        }

        /// <summary>拼在用户消息末尾的附件清单。</summary>
        public static string Describe(IEnumerable<string> paths)
        {
            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
            if (list.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("附件（工作区内的文件路径）：");
            foreach (string path in list) sb.Append("\n- ").Append(path);
            return sb.ToString();
        }

        /// <summary>编辑工具的默认源图：只取图片附件。</summary>
        public static List<string> Images(IEnumerable<string> paths)
            => paths?.Where(p => !string.IsNullOrWhiteSpace(p) && IsImage(Path.GetExtension(p))).ToList() ?? [];

        public static bool IsImage(string extension)
            => extension?.TrimStart('.').ToLowerInvariant() is "png" or "jpg" or "jpeg" or "webp" or "bmp" or "gif";

        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
        }

        /// <summary>去掉路径非法字符，避免名字里带目录分隔符把文件写到别处。</summary>
        private static string Safe(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string value = new string(chars).Trim('.', ' ');
            if (value.Length == 0) return fallback;
            return value.Length <= 80 ? value : value[..80];
        }
    }
}
