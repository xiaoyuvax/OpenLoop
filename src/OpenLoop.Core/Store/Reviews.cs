using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLoop.Core.Store
{
    /// <summary>Loop 提出的评价点：等待用户答一句（可跳过、可稍后补答）。</summary>
    public sealed class ReviewItem
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public string Dimension { get; set; } = "";
        public string Evidence { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }

    /// <summary>
    /// 评价点的持久化：待答队列 .openloop/loop/reviews.pending.json，
    /// 已答/记录 .openloop/loop/reviews.jsonl（只追加）。
    /// </summary>
    public static class ReviewStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string Dir(string workspace) => Path.Combine(workspace, ".openloop", "loop");
        private static string PendingPath(string workspace) => Path.Combine(Dir(workspace), "reviews.pending.json");
        private static string LogPath(string workspace) => Path.Combine(Dir(workspace), "reviews.jsonl");

        public static IReadOnlyList<ReviewItem> Pending(string workspace)
        {
            try
            {
                string path = PendingPath(workspace);
                if (!File.Exists(path)) return [];
                return JsonSerializer.Deserialize<List<ReviewItem>>(File.ReadAllText(path), Json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public static string Add(string workspace, string question, string dimension = "", string evidence = "")
        {
            if (string.IsNullOrWhiteSpace(question)) return null;
            var items = Pending(workspace).ToList();
            var item = new ReviewItem
            {
                Id = "r-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + (items.Count + 1),
                Question = question.Trim(),
                Dimension = dimension ?? "",
                Evidence = evidence ?? "",
                CreatedAt = DateTime.Now.ToString("s")
            };
            items.Add(item);
            Save(workspace, items);
            return item.Id;
        }

        public static void Answer(string workspace, string id, string verdict, string note, string attribution = "user")
        {
            var items = Pending(workspace).ToList();
            var item = items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            items.Remove(item);
            Save(workspace, items);
            AppendLog(workspace, new
            {
                id = item.Id,
                dimension = item.Dimension,
                evidence = item.Evidence,
                question = item.Question,
                verdict,
                note = note ?? "",
                attribution,
                at = DateTime.Now.ToString("s")
            });
        }

        private static void Save(string workspace, List<ReviewItem> items)
        {
            string path = PendingPath(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(items, Json));
        }

        private static void AppendLog(string workspace, object record)
        {
            string path = LogPath(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
    }
}
