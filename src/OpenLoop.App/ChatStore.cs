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
            }
            catch
            {
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
    }
}
