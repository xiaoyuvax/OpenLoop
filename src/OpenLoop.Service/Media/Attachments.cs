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
        // shortcut: 单文件 64MB、单条消息 128MB。本机壳直接复制文件（不走 HTTP 请求体），
        // 只有 Web 页面还按 base64 传（约 27MB 封顶，再大要改成流式上传）。
        public const long MaxBytes = 64L * 1024 * 1024;
        public const long MaxTotalBytes = 128L * 1024 * 1024;

        /// <summary>附件尺寸检查（壳挑文件与这里落盘用同一判据）：合格返回 null。</summary>
        public static string CheckSize(IEnumerable<(string Name, long Size)> files)
        {
            long total = 0;
            foreach (var file in files ?? [])
            {
                if (file.Size > MaxBytes)
                    return $"附件过大（上限 {MaxBytes / 1024 / 1024}MB）：{file.Name}";
                total += file.Size;
                if (total > MaxTotalBytes)
                    return $"附件总量过大（上限 {MaxTotalBytes / 1024 / 1024}MB）";
            }
            return null;
        }

        /// <summary>保存附件并返回路径；超过数量/大小限制返回 null 并给出原因。</summary>
        public static List<string> Save(string workspace, string sessionId, IEnumerable<AttachmentPayload> files,
            int limit, out string error)
        {
            error = null;
            var input = files?.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Data)).ToList() ?? [];
            if (input.Count == 0) return [];
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

            var decoded = new List<(string Name, byte[] Bytes)>();
            foreach (var file in input)
            {
                try { decoded.Add((Safe(Path.GetFileName(file.Name), "file"), Convert.FromBase64String(file.Data))); }
                catch
                {
                    error = "附件解码失败：" + file.Name;
                    return null;
                }
            }
            error = CheckSize(decoded.Select(f => (f.Name, (long)f.Bytes.Length)));
            if (error != null) return null;

            string dir = Prepare(workspace, sessionId, out string stamp);
            var saved = new List<string>();
            foreach (var file in decoded)
            {
                string path = Unique(Path.Combine(dir, stamp + "_" + file.Name));
                File.WriteAllBytes(path, file.Bytes);
                saved.Add(path);
            }
            return saved;
        }

        /// <summary>按路径保存（本机壳用）：直接复制文件，不受 HTTP 请求体大小限制。</summary>
        public static List<string> SaveFiles(string workspace, string sessionId, IReadOnlyList<string> paths,
            int limit, out string error)
        {
            error = null;
            var input = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
            if (input.Count == 0) return [];
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

            var infos = new List<FileInfo>();
            foreach (string path in input)
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    error = "附件不存在：" + path;
                    return null;
                }
                infos.Add(info);
            }
            error = CheckSize(infos.Select(i => (i.Name, i.Length)));
            if (error != null) return null;

            try
            {
                string dir = Prepare(workspace, sessionId, out string stamp);
                var saved = new List<string>();
                foreach (var info in infos)
                {
                    string path = Unique(Path.Combine(dir, stamp + "_" + Safe(info.Name, "file")));
                    File.Copy(info.FullName, path);
                    saved.Add(path);
                }
                return saved;
            }
            catch (Exception ex)
            {
                error = "保存附件失败：" + ex.Message;
                return null;
            }
        }

        private static string Prepare(string workspace, string sessionId, out string stamp)
        {
            string dir = Path.Combine(workspace, ".openloop", "tmp", "attachments",
                Safe(sessionId, "session"));
            Directory.CreateDirectory(dir);
            stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return dir;
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
