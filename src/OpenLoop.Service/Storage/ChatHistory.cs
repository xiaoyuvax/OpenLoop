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
            }
            catch
            {
            }
        }
    }
}
