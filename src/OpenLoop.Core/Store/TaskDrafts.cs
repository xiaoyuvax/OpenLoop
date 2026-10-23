using System.Text;
using System.Text.Json;

namespace OpenLoop.Core.Store
{
    public sealed class TaskDraftSource
    {
        public string Finding { get; set; }
        public string Trace { get; set; }
        public string Session { get; set; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Finding) && string.IsNullOrWhiteSpace(Trace) && string.IsNullOrWhiteSpace(Session);

        public string Summary
        {
            get
            {
                var parts = new[] { Finding, Trace, Session }
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join(" · ", parts);
            }
        }
    }

    public sealed class TaskDraft
    {
        public string Id { get; set; } = "";
        public string Status { get; set; } = TaskDrafts.StatusDraft;
        public string Reason { get; set; } = "";
        public TaskDraftSource Source { get; set; } = new();
        public EvalTask Task { get; set; }
        public string CreatedAt { get; set; } = "";
        public string DecidedAt { get; set; }
        public string DecidedBy { get; set; }
    }

    /// <summary>
    /// Loop 起草的考核题草稿：&lt;workspace&gt;/.openloop/loop/task-drafts/&lt;id&gt;.json。
    /// Loop 只能写这里；用户确认后才复制进 eval/tasks（入库即 held-out）。
    /// </summary>
    public static class TaskDrafts
    {
        public const string StatusDraft = "draft";
        public const string StatusAccepted = "accepted";
        public const string StatusRejected = "rejected";

        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions TaskJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static string DirectoryPath(string workspace)
            => Path.Combine(workspace, ".openloop", "loop", "task-drafts");

        public static string DirectoryPath(Ledger ledger) => DirectoryPath(ledger.Workspace);

        public static IReadOnlyList<TaskDraft> Load(string workspace)
        {
            string dir = DirectoryPath(workspace);
            if (!Directory.Exists(dir)) return [];
            var list = new List<TaskDraft>();
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var draft = JsonSerializer.Deserialize<TaskDraft>(File.ReadAllText(file, Encoding.UTF8), Json);
                    if (draft?.Task != null && !string.IsNullOrWhiteSpace(draft.Id)) list.Add(draft);
                }
                catch
                {
                    // 坏草稿不阻塞其余条目；需要时人工检查文件。
                }
            }
            return list;
        }

        public static TaskDraft LoadOne(string workspace, string id)
            => Load(workspace).FirstOrDefault(d => d.Id == id);

        public static int PendingCount(string workspace)
            => Load(workspace).Count(d => d.Status == StatusDraft);

        /// <summary>Loop 起草。字段非法、来源为空、与现有题/草稿重复都直接拒绝。</summary>
        public static TaskDraft Add(
            string workspace, EvalTask task, string reason, TaskDraftSource source)
        {
            lock (Gate)
            {
                if (source == null || source.IsEmpty)
                    throw new InvalidOperationException("草稿必须引用来源（finding/trace/session 至少一个）");

                EvalTask normalized = Prepare(task, workspace);
                var drafts = Load(workspace).ToList();

                string existingTask = FindDuplicateTask(workspace, normalized);
                if (existingTask != null)
                    throw new InvalidOperationException($"题集里已有相同题目：{existingTask}");

                string duplicateDraft = FindDuplicateDraft(drafts, normalized);
                if (duplicateDraft != null)
                    throw new InvalidOperationException($"已有相同草稿：{duplicateDraft}");

                var record = new TaskDraft
                {
                    Id = NewId(drafts),
                    Status = StatusDraft,
                    Reason = (reason ?? "").Trim(),
                    Source = source,
                    Task = normalized,
                    CreatedAt = DateTimeOffset.Now.ToString("O")
                };
                Write(workspace, record);
                return record;
            }
        }

        /// <summary>用户编辑草稿（仍是草稿，不自动入库）。</summary>
        public static TaskDraft Update(string workspace, string id, EvalTask task, string reason = null)
        {
            lock (Gate)
            {
                var record = LoadOne(workspace, id)
                    ?? throw new InvalidOperationException($"草稿不存在：{id}");
                if (record.Status != StatusDraft)
                    throw new InvalidOperationException("只能编辑 draft 状态的草稿");

                EvalTask normalized = Prepare(task, workspace);
                var others = Load(workspace).Where(d => d.Id != id).ToList();

                string existingTask = FindDuplicateTask(workspace, normalized);
                if (existingTask != null)
                    throw new InvalidOperationException($"题集里已有相同题目：{existingTask}");
                string duplicateDraft = FindDuplicateDraft(others, normalized);
                if (duplicateDraft != null)
                    throw new InvalidOperationException($"已有相同草稿：{duplicateDraft}");

                record.Task = normalized;
                if (reason != null) record.Reason = reason.Trim();
                Write(workspace, record);
                return record;
            }
        }

        /// <summary>用户采纳：写 eval/tasks（Source 固定为 user），再标记 accepted。</summary>
        public static string Accept(string workspace, string id)
        {
            lock (Gate)
            {
                var record = LoadOne(workspace, id)
                    ?? throw new InvalidOperationException($"草稿不存在：{id}");
                if (record.Status != StatusDraft)
                    throw new InvalidOperationException($"草稿状态为 {record.Status}，不能再次采纳");

                EvalTask task = Prepare(record.Task, workspace);
                task = TaskSet.NormalizeTask(new EvalTask
                {
                    Id = task.Id,
                    Problem = task.Problem,
                    Prompt = task.Prompt,
                    Source = "user",
                    Seed = task.Seed,
                    Scope = task.Scope,
                    Checks = task.Checks
                });

                string tasksDir = Path.Combine(workspace, ".openloop", "eval", "tasks");
                Directory.CreateDirectory(tasksDir);
                string path = Path.Combine(tasksDir, SafeFile(task.Id) + ".json");
                if (File.Exists(path))
                    throw new InvalidOperationException($"任务 id 已存在：{task.Id}");

                var existing = TaskSet.Load(Path.Combine(workspace, ".openloop", "eval"));
                string duplicate = FindDuplicate(existing.Tasks, task);
                if (duplicate != null)
                    throw new InvalidOperationException($"题集里已有相同题目：{duplicate}");

                File.WriteAllText(path, JsonSerializer.Serialize(task, TaskJson), new UTF8Encoding(false));
                record.Task = task;
                record.Status = StatusAccepted;
                record.DecidedAt = DateTimeOffset.Now.ToString("O");
                record.DecidedBy = "user";
                Write(workspace, record);
                return path;
            }
        }

        public static void Reject(string workspace, string id, string by = "user")
        {
            lock (Gate)
            {
                var record = LoadOne(workspace, id)
                    ?? throw new InvalidOperationException($"草稿不存在：{id}");
                if (record.Status != StatusDraft)
                    throw new InvalidOperationException($"草稿状态为 {record.Status}，不能丢弃");
                record.Status = StatusRejected;
                record.DecidedAt = DateTimeOffset.Now.ToString("O");
                record.DecidedBy = by;
                Write(workspace, record);
            }
        }

        private static EvalTask Prepare(EvalTask task, string workspace)
        {
            TaskSet.ValidateTask(task, "draft");
            EvalTask normalized = TaskSet.NormalizeTask(task);
            if (string.IsNullOrWhiteSpace(normalized.Problem))
                normalized.Problem = normalized.Id;
            return normalized;
        }

        private static string FindDuplicateTask(string workspace, EvalTask task)
        {
            var existing = TaskSet.Load(Path.Combine(workspace, ".openloop", "eval"));
            return FindDuplicate(existing.Tasks, task);
        }

        private static string FindDuplicateDraft(IEnumerable<TaskDraft> drafts, EvalTask task)
        {
            string fingerprint = TaskSet.Empty.Fingerprint(task);
            foreach (var draft in drafts)
            {
                if (draft.Status == StatusRejected || draft.Task == null) continue;
                if (TaskSet.Empty.Fingerprint(TaskSet.NormalizeTask(draft.Task)) == fingerprint)
                    return draft.Id;
            }
            return null;
        }

        private static string FindDuplicate(IEnumerable<EvalTask> tasks, EvalTask task)
        {
            string fingerprint = TaskSet.Empty.Fingerprint(task);
            foreach (var other in tasks)
            {
                if (TaskSet.Empty.Fingerprint(other) == fingerprint) return other.Id;
            }
            return null;
        }

        private static string NewId(IReadOnlyList<TaskDraft> drafts)
        {
            string stamp = "d-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (int i = 1; ; i++)
            {
                string id = stamp + "-" + i;
                if (drafts.All(d => d.Id != id)) return id;
            }
        }

        private static void Write(string workspace, TaskDraft draft)
        {
            string dir = DirectoryPath(workspace);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, SafeFile(draft.Id) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(draft, Json), new UTF8Encoding(false));
        }

        private static string SafeFile(string id)
        {
            string safe = new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
            return safe.Length == 0 ? "draft" : safe;
        }
    }
}
