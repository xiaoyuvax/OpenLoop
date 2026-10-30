using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatFlow;

namespace OpenLoop.App
{
    /// <summary>
    /// 对话历史按工作目录分开存放，放 %APPDATA%，不进工作区。
    /// 只存展示所需字段；工具轨迹保留，恢复后仍能展开。
    /// </summary>
    internal static class ChatStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop", "chats");

        public static List<ChatTurn> Load(string workspace, string kind = null)
        {
            var turns = new List<ChatTurn>();
            if (string.IsNullOrWhiteSpace(workspace)) return turns;
            try
            {
                string path = PathFor(workspace, kind);
                if (!File.Exists(path)) return turns;
                var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path, Encoding.UTF8), Json);
                foreach (var entry in entries ?? [])
                {
                    if (entry == null || (string.IsNullOrEmpty(entry.User) && string.IsNullOrEmpty(entry.Assistant)))
                        continue;
                    turns.Add(new ChatTurn
                    {
                        User = new ChatMessage("user", entry.User ?? ""),
                        Assistant = new ChatMessage("assistant", entry.Assistant ?? ""),
                        ToolCallLines = entry.ToolLines ?? "",
                        ToolTraceText = entry.ToolLines ?? "",
                        ToolStepCount = entry.Steps,
                        ToolTotalMs = entry.Ms
                    });
                }
            }
            catch
            {
            }
            return turns;
        }

        public static void Save(string workspace, IEnumerable<ChatTurn> turns, string kind = null)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            try
            {
                var entries = (turns ?? []).Select(t => new Entry
                {
                    User = t.UserText,
                    Assistant = t.AssistantText,
                    ToolLines = t.ToolCallLines,
                    Steps = t.ToolStepCount,
                    Ms = t.ToolTotalMs
                }).ToList();

                Directory.CreateDirectory(Dir);
                string path = PathFor(workspace, kind);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json), new UTF8Encoding(false));
                File.Move(temp, path, true);
                if (!string.IsNullOrWhiteSpace(kind) && kind != LoopKind) Touch(workspace, kind);
            }
            catch
            {
            }
        }

        /// <summary>Loop 会话与普通会话共用一个目录，但恢复时只认普通会话。</summary>
        private const string LoopKind = "loop";

        /// <summary>删除一个会话的对话文件与索引项。</summary>
        public static void Delete(string workspace, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(sessionId)) return;
            try
            {
                File.Delete(PathFor(workspace, sessionId));
                var all = ReadIndex();
                if (all.RemoveAll(e => e.Id == sessionId) > 0)
                {
                    string temp = IndexPath + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                    File.Move(temp, IndexPath, true);
                }
            }
            catch
            {
            }
        }

        /// <summary>会话索引：文件名是哈希，重启后没有索引就无从把会话找回来。</summary>
        public static void Touch(string workspace, string sessionId, string title = null)
        {
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(sessionId)) return;
            try
            {
                var all = ReadIndex();
                string full = Path.GetFullPath(workspace.Trim());
                var item = all.FirstOrDefault(e => e.Id == sessionId);
                if (item == null) all.Add(new IndexEntry { Id = sessionId, Workspace = full, Title = title ?? "" });
                else
                {
                    item.Workspace = full;
                    if (title != null) item.Title = title;
                }
                Directory.CreateDirectory(Dir);
                string temp = IndexPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                File.Move(temp, IndexPath, true);
            }
            catch
            {
            }
        }

        /// <summary>有内容的已存会话（会话 id + 工作目录 + 标题），最近的在前。</summary>
        public static List<(string Id, string Workspace, string Title)> SavedSessions()
        {
            var result = new List<(string, string, string)>();
            try
            {
                foreach (var item in ReadIndex().OrderByDescending(e => e.UpdatedAt))
                {
                    if (item == null || string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Workspace)) continue;
                    if (Load(item.Workspace, item.Id).Count == 0) continue;
                    result.Add((item.Id, item.Workspace, item.Title ?? ""));
                }
            }
            catch
            {
            }
            return result;
        }

        private static string IndexPath => Path.Combine(Dir, "index.json");

        private static List<IndexEntry> ReadIndex()
        {
            try
            {
                string path = IndexPath;
                if (!File.Exists(path)) return [];
                return JsonSerializer.Deserialize<List<IndexEntry>>(File.ReadAllText(path, Encoding.UTF8), Json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static string PathFor(string workspace, string kind = null)
        {
            string normalized = Path.GetFullPath(workspace.Trim()).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(kind)) normalized += "|" + kind;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16]
                .ToLowerInvariant();
            return Path.Combine(Dir, hash + ".json");
        }

        private sealed class Entry
        {
            public string User { get; set; }
            public string Assistant { get; set; }
            public string ToolLines { get; set; }
            public int Steps { get; set; }
            public long Ms { get; set; }
        }

        private sealed class IndexEntry
        {
            public string Id { get; set; }
            public string Workspace { get; set; }
            public string Title { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }
    }
}
