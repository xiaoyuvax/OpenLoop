using System.Text.Json;

namespace OpenLoop.Core.Store
{
    public sealed class AgentStepRecord
    {
        public string Tool { get; set; } = "";
        public string Args { get; set; } = "";
        public bool Ok { get; set; }
        public long Ms { get; set; }
    }

    public sealed class AgentTranscript
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public int AnchorTurn { get; set; } = -1;
        public string Title { get; set; } = "";
        public string Task { get; set; } = "";
        public string Answer { get; set; } = "";
        public string At { get; set; } = "";
        public List<AgentStepRecord> Steps { get; set; } = [];
    }

    /// <summary>子 agent 的只读记录：&lt;workspace&gt;/.openloop/agents/&lt;id&gt;.json。</summary>
    public static class AgentTranscriptStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string Dir(string workspace) => Path.Combine(workspace, ".openloop", "agents");

        public static AgentTranscript Add(string workspace, AgentTranscript transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript?.Task)) return null;
            transcript.Id = "a-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            transcript.At = DateTime.Now.ToString("s");
            transcript.Title = transcript.Title.Length > 0
                ? transcript.Title
                : (transcript.Task.Length > 40 ? transcript.Task[..40] + "…" : transcript.Task);

            string dir = Dir(workspace);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, transcript.Id + ".json"),
                JsonSerializer.Serialize(transcript, Json));
            return transcript;
        }

        public static AgentTranscript Load(string workspace, string id)
        {
            try
            {
                string path = Path.Combine(Dir(workspace), Safe(id) + ".json");
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<AgentTranscript>(File.ReadAllText(path), Json)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static IReadOnlyList<AgentTranscript> List(string workspace, string sessionId)
        {
            try
            {
                string dir = Dir(workspace);
                if (!Directory.Exists(dir)) return [];
                return Directory.EnumerateFiles(dir, "*.json")
                    .Select(f => { try { return JsonSerializer.Deserialize<AgentTranscript>(File.ReadAllText(f), Json); } catch { return null; } })
                    .Where(t => t != null && string.Equals(t.SessionId, sessionId, StringComparison.Ordinal))
                    .OrderBy(t => t.At, StringComparer.Ordinal)
                    .ToList();
            }
            catch
            {
                return [];
            }
        }

        private static string Safe(string id)
        {
            string safe = new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            return safe.Length == 0 ? "unknown" : safe;
        }
    }
}
