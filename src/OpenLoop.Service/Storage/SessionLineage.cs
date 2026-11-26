using System.Text;
using System.Text.Json;

namespace OpenLoop.Service.Storage
{
    /// <summary>
    /// 会话的衍生关系：这条会话由哪条源会话创建（/complain、/fork、/new、转交、协作、目标编辑的旁路会话、/btw 落成的会话）。
    /// 只记「衍生会话 → 源会话」一条边；按衍生会话 Id upsert，重建同名用途时以最新一条为准。
    /// </summary>
    public sealed class SessionLink
    {
        public string Id { get; set; } = "";
        public string Role { get; set; } = "";            // worker | loop
        public string Workspace { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceRole { get; set; } = "";
        public string SourceWorkspace { get; set; } = "";
        public string Purpose { get; set; } = "";         // complain | fork | transfer | collab | new | target-edit
        public string Target { get; set; } = "";          // 目标编辑面种类（prompt / agents），其它用途可空
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 衍生会话索引（%APPDATA%\OpenLoop\chats\lineage.json）：壳创建会话时登记，回溯与复用都读它。
    /// 与对话文件同目录、同为用户级：源会话与衍生会话可以跨工作区。
    /// </summary>
    public static class SessionLineage
    {
        public const string Complain = "complain";
        public const string Fork = "fork";
        public const string Transfer = "transfer";
        public const string Collab = "collab";
        public const string New = "new";
        public const string TargetEdit = "target-edit";
        public const string Btw = "btw";

        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string FilePath => Path.Combine(ChatHistory.Dir, "lineage.json");

        public static IReadOnlyList<SessionLink> All()
        {
            try
            {
                if (!File.Exists(FilePath)) return [];
                return JsonSerializer.Deserialize<List<SessionLink>>(
                           File.ReadAllText(FilePath, Encoding.UTF8), Json)
                       ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>这条会话是从哪来的；不是衍生会话返回 null。</summary>
        public static SessionLink Get(string id)
            => string.IsNullOrWhiteSpace(id)
                ? null
                : All().LastOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>某个源会话在某个用途下最近创建的那条衍生会话（旁路会话复用它）。</summary>
        public static SessionLink Find(string sourceId, string purpose)
            => string.IsNullOrWhiteSpace(sourceId)
                ? null
                : All().LastOrDefault(l => string.Equals(l.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(l.Purpose, purpose, StringComparison.OrdinalIgnoreCase));

        /// <summary>某个源会话的全部衍生会话（回溯用）。</summary>
        public static IReadOnlyList<SessionLink> Derived(string sourceId)
            => string.IsNullOrWhiteSpace(sourceId)
                ? []
                : All().Where(l => string.Equals(l.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>登记一条衍生关系；同一衍生会话 Id 覆盖旧记录。</summary>
        public static void Link(SessionLink link)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.Id) || string.IsNullOrWhiteSpace(link.SourceId)) return;
            lock (Gate)
            {
                var all = All().Where(l => !string.Equals(l.Id, link.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (link.CreatedAt == default) link.CreatedAt = DateTime.UtcNow;
                all.Add(link);
                string temp = null;
                try
                {
                    Directory.CreateDirectory(ChatHistory.Dir);
                    string path = FilePath;
                    temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
                    File.Move(temp, path, true);
                }
                catch
                {
                    // 衍生关系只是回溯用的旁证：写不进去不能挡住会话本身
                    try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
        }
    }
}
