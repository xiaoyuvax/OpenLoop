using System.Text.Json;

namespace OpenLoop.Core.Store
{
    public sealed class SurveyFieldRecord
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Kind { get; set; } = "choice";
        public List<string> Options { get; set; } = [];
        public string Placeholder { get; set; } = "";
    }

    public sealed class SurveyRecord
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public int AnchorTurn { get; set; } = -1;
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string Accent { get; set; } = "#722ED1";
        public string State { get; set; } = "pending";
        public string AnswerSummary { get; set; }
        public string CreatedAt { get; set; } = "";
        public List<SurveyFieldRecord> Fields { get; set; } = [];
    }

    /// <summary>
    /// 会话内嵌调查卡：每个会话一个文件 .openloop/loop/surveys/&lt;sessionId&gt;.json。
    /// Loop 追加，用户提交后回写 State/AnswerSummary；跳过的保持 pending。
    /// </summary>
    public static class SurveyStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>新增/作答后触发（workspace, sessionId）；前端据此重载卡片。</summary>
        public static event Action<string, string> Changed;

        /// <summary>用户作答后触发（workspace, sessionId, surveyId, summary）。</summary>
        public static event Action<string, string, string, string> Answered;

        private static string Dir(string workspace) => Path.Combine(workspace, ".openloop", "loop", "surveys");
        private static string PathFor(string workspace, string sessionId)
            => Path.Combine(Dir(workspace), Safe(sessionId) + ".json");

        public static IReadOnlyList<SurveyRecord> Load(string workspace, string sessionId)
        {
            try
            {
                string path = PathFor(workspace, sessionId);
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<List<SurveyRecord>>(File.ReadAllText(path), Json) ?? []
                    : [];
            }
            catch
            {
                return [];
            }
        }

        public static SurveyRecord Add(
            string workspace, string sessionId, string title, string body,
            int anchorTurn = -1, string accent = "#722ED1", List<SurveyFieldRecord> fields = null)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var items = Load(workspace, sessionId).ToList();
            if (items.Count(s => s.State == "pending") >= 3) return null;
            var record = new SurveyRecord
            {
                Id = "s-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + (items.Count + 1),
                SessionId = sessionId,
                AnchorTurn = anchorTurn,
                Title = title.Trim(),
                Body = body ?? "",
                Accent = accent ?? "#722ED1",
                CreatedAt = DateTime.Now.ToString("s"),
                Fields = fields ?? []
            };
            items.Add(record);
            Save(workspace, sessionId, items);
            Changed?.Invoke(workspace, sessionId);
            return record;
        }

        public static void Answer(string workspace, string sessionId, string surveyId, string summary)
        {
            var items = Load(workspace, sessionId).ToList();
            var item = items.FirstOrDefault(i => i.Id == surveyId);
            if (item == null) return;
            item.State = "answered";
            item.AnswerSummary = string.IsNullOrWhiteSpace(summary) ? "已提交" : summary;
            Save(workspace, sessionId, items);
            AppendReview(workspace, sessionId, item);
            Changed?.Invoke(workspace, sessionId);
            Answered?.Invoke(workspace, sessionId, item.Id, item.AnswerSummary);
        }

        private static void AppendReview(string workspace, string sessionId, SurveyRecord item)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Dir(workspace))!, "reviews.jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string line = JsonSerializer.Serialize(new
                {
                    id = item.Id,
                    session = sessionId,
                    dimension = item.Fields.FirstOrDefault()?.Key ?? "",
                    verdict = item.AnswerSummary,
                    title = item.Title,
                    attribution = "user",
                    at = DateTime.Now.ToString("s")
                });
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { }
        }

        private static void Save(string workspace, string sessionId, List<SurveyRecord> items)
        {
            string path = PathFor(workspace, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(items, Json));
        }

        private static string Safe(string id)
        {
            string safe = new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            return safe.Length == 0 ? "default" : safe;
        }
    }
}
