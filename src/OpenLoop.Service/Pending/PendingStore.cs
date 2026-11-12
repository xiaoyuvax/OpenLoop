using System.Text;
using System.Text.Json;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Pending
{
    /// <summary>
    /// 待发送队列落到会话旁边：&lt;chats&gt;/&lt;hash&gt;.pending.json。
    /// 队列原来只在内存里，进程异常退出就会丢掉用户已经排好的消息；这里只负责读写，时机由宿主决定。
    /// </summary>
    public static class PendingStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public sealed class Payload
        {
            public bool Paused { get; set; }
            public List<PendingItem> Items { get; set; } = [];
        }

        public static string PathFor(string workspace, string kind)
            => ChatHistory.PathFor(workspace, kind) + ".pending.json";

        public static void Save(string workspace, string kind, PendingQueue queue)
        {
            if (string.IsNullOrWhiteSpace(workspace) || queue == null) return;
            try
            {
                string path = PathFor(workspace, kind);
                if (queue.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);   // 空了就别留残文件
                    return;
                }
                var payload = new Payload { Paused = queue.Paused, Items = queue.Items.ToList() };
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(payload, Json), Encoding.UTF8);
            }
            catch
            {
                // 队列落盘失败不该影响发送
            }
        }

        public static Payload Load(string workspace, string kind)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return new Payload();
            try
            {
                string path = PathFor(workspace, kind);
                if (!File.Exists(path)) return new Payload();
                return JsonSerializer.Deserialize<Payload>(File.ReadAllText(path, Encoding.UTF8), Json)
                       ?? new Payload();
            }
            catch
            {
                return new Payload();
            }
        }
    }
}
