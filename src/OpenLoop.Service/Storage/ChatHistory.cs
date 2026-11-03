using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenLoop.Service.Storage
{
    public sealed class ChatEntry
    {
        public string User { get; set; }
        public string Assistant { get; set; }
        public string Reasoning { get; set; }
        public string ToolLines { get; set; }
        public int Steps { get; set; }
        public long Ms { get; set; }
        public List<ChatMessageEntry> Messages { get; set; }
    }

    public sealed class ChatMessageEntry
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    /// <summary>
    /// 对话历史文件（%APPDATA%\OpenLoop\chats）。路径与 JSON 形状必须与既有文件保持一致，
    /// 否则升级后旧会话打不开。
    /// </summary>
    public static class ChatHistory
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Root { get; set; }

        public static string Dir => Root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop", "chats");

        public static string PathFor(string workspace, string kind)
        {
            string normalized = Path.GetFullPath(workspace.Trim()).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(kind)) normalized += "|" + kind;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16]
                .ToLowerInvariant();
            return Path.Combine(Dir, hash + ".json");
        }

        public static List<ChatEntry> Read(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return [];
            try
            {
                string path = PathFor(workspace, kind);
                if (!File.Exists(path)) return [];
                return JsonSerializer.Deserialize<List<ChatEntry>>(File.ReadAllText(path, Encoding.UTF8), Json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public static void Write(string workspace, string kind, IEnumerable<ChatEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                Directory.CreateDirectory(Dir);
                string path = PathFor(workspace, kind);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(entries?.ToList() ?? [], Json), new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch
            {
            }
        }

        public static void Delete(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                string path = PathFor(workspace, kind);
                if (File.Exists(path)) File.Delete(path);
                if (!string.IsNullOrWhiteSpace(kind)) Forget(kind);
            }
            catch
            {
            }
        }

        // ---- 会话索引 ----
        //
        // 对话文件按「工作目录 + id」分桶，光靠目录扫不出来（哈希名），所以要一张索引。
        // 壳和 A 两个进程都会写：A 每轮结束 Touch 一次，壳自己也 Touch。
        // shortcut: 索引是「读-改-写」，跨进程并发时极端情况下可能丢一条录入；
        // 丢了只是列表少一条（对话文件还在，A 在线时壳的远端同步会补回来），要严格一致再加文件锁。

        private static readonly object IndexGate = new();

        public static string IndexPath => Path.Combine(Dir, "index.json");

        public static IReadOnlyList<ChatIndexEntry> Index()
        {
            try
            {
                if (!File.Exists(IndexPath)) return [];
                return JsonSerializer.Deserialize<List<ChatIndexEntry>>(File.ReadAllText(IndexPath, Encoding.UTF8), Json)
                       ?? [];
            }
            catch
            {
                return [];
            }
        }

        public static void Touch(string workspace, string kind, string title = null)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(kind)) return;
            try
            {
                lock (IndexGate)
                {
                    var all = Index().ToList();
                    string full = Path.GetFullPath(workspace.Trim());
                    var item = all.FirstOrDefault(e => e.Id == kind);
                    if (item == null)
                    {
                        all.Add(new ChatIndexEntry { Id = kind, Workspace = full, Title = title ?? "" });
                    }
                    else
                    {
                        item.Workspace = full;
                        item.UpdatedAt = DateTime.UtcNow;
                        if (title != null) item.Title = title;
                    }

                    Directory.CreateDirectory(Dir);
                    string temp = IndexPath + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                    File.Move(temp, IndexPath, true);
                }
            }
            catch
            {
            }
        }

        public static void Forget(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return;
            try
            {
                lock (IndexGate)
                {
                    var all = Index().Where(e => e.Id != kind).ToList();
                    Directory.CreateDirectory(Dir);
                    string temp = IndexPath + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                    File.Move(temp, IndexPath, true);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>chats/index.json 的一行：会话 id → 工作目录（哈希文件名反查用）。</summary>
    public sealed class ChatIndexEntry
    {
        public string Id { get; set; }
        public string Workspace { get; set; }
        public string Title { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
