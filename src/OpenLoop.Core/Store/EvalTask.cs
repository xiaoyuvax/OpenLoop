using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Store
{
    /// <summary>一道考核题：提示词 + 起始文件 + 可自动判定的检查。由 User 提供。</summary>
    public sealed class EvalTask
    {
        public string Id { get; init; }
        public string Problem { get; init; }
        public string Prompt { get; init; }
        public string Source { get; init; } = "user";
        public Dictionary<string, string> Seed { get; init; } = [];
        public List<string> Scope { get; init; } = [];
        public List<TaskCheck> Checks { get; init; } = [];
    }

    public sealed class TaskCheck
    {
        public string Type { get; init; }
        public string Path { get; init; }
        public string Text { get; init; }
        public List<string> Args { get; init; } = [];
        public int ExpectExit { get; init; }
    }

    /// <summary>任务集：固定、held-out、判据确定。不随候选变化。</summary>
    public sealed class TaskSet
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private TaskSet(IReadOnlyList<EvalTask> tasks) => Tasks = tasks;

        public IReadOnlyList<EvalTask> Tasks { get; }

        public static TaskSet Empty { get; } = new([]);

        public static TaskSet Load(string evalDir)
        {
            string dir = Path.Combine(evalDir, "tasks");
            if (!Directory.Exists(dir)) return Empty;

            var tasks = new List<EvalTask>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                EvalTask task;
                try
                {
                    task = JsonSerializer.Deserialize<EvalTask>(File.ReadAllText(file, Encoding.UTF8), Json);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"任务文件无法解析：{file}", ex);
                }
                Validate(task, file);
                if (!ids.Add(task.Id))
                    throw new InvalidDataException($"任务 id 重复：{task.Id}");
                tasks.Add(Normalize(task));
            }
            return new TaskSet(tasks);
        }

        /// <summary>范围指纹：作用范围内文件内容 + 检查定义的摘要。内容变了却复用旧结果，等于自欺。</summary>
        public string Fingerprint(EvalTask task)
        {
            var sb = new StringBuilder();
            foreach (string path in Paths(task).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string content = task.Seed != null && task.Seed.TryGetValue(path, out var seeded) ? seeded : "";
                sb.Append(path).Append('\u0000').Append(content).Append('\u0001');
            }
            sb.Append(JsonSerializer.Serialize(task.Checks, Json));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        private static IEnumerable<string> Paths(EvalTask task)
        {
            if (task.Scope is { Count: > 0 }) return task.Scope;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (task.Seed != null) foreach (string key in task.Seed.Keys) paths.Add(key);
            if (task.Checks != null)
                foreach (var check in task.Checks)
                    if (!string.IsNullOrWhiteSpace(check.Path)) paths.Add(check.Path);
            return paths;
        }

        private static void Validate(EvalTask task, string file)
        {
            if (task == null) throw new InvalidDataException($"任务文件为空：{file}");
            if (string.IsNullOrWhiteSpace(task.Id)) throw new InvalidDataException($"任务缺少 id：{file}");
            if (string.IsNullOrWhiteSpace(task.Prompt)) throw new InvalidDataException($"任务缺少 prompt：{file}");
            if (task.Checks is not { Count: > 0 }) throw new InvalidDataException($"任务缺少 checks：{file}");

            foreach (var check in task.Checks)
            {
                bool needsPath = check.Type is "file_exists" or "file_missing" or "file_contains" or "file_equals";
                if (needsPath && string.IsNullOrWhiteSpace(check.Path))
                    throw new InvalidDataException($"任务 {task.Id} 的 {check.Type} 缺少 path");
                if (check.Type is not ("file_exists" or "file_missing" or "file_contains" or "file_equals" or "command"))
                    throw new InvalidDataException($"任务 {task.Id} 有未知检查类型：{check.Type}");
                if (check.Type == "command" && check.Args is not { Count: > 0 })
                    throw new InvalidDataException($"任务 {task.Id} 的 command 检查缺少 args");
            }
        }

        private static EvalTask Normalize(EvalTask task) => new()
        {
            Id = task.Id,
            Problem = task.Problem,
            Prompt = task.Prompt,
            Source = string.IsNullOrWhiteSpace(task.Source) ? "user" : task.Source,
            Seed = task.Seed == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(task.Seed, StringComparer.OrdinalIgnoreCase),
            Scope = task.Scope ?? [],
            Checks = task.Checks
        };
    }

    public static class TaskJudge
    {
        public static (bool Passed, string Detail) Evaluate(string workspace, IReadOnlyList<TaskCheck> checks)
        {
            var scope = new WorkspaceScope(workspace);
            var failures = new List<string>();
            foreach (var check in checks ?? [])
            {
                if (!EvaluateOne(scope, check, out string detail))
                    failures.Add(detail);
            }
            return failures.Count == 0
                ? (true, "全部通过")
                : (false, string.Join("；", failures));
        }

        private static bool EvaluateOne(WorkspaceScope scope, TaskCheck check, out string detail)
        {
            detail = null;
            if (check == null) { detail = "空检查项"; return false; }

            if (check.Type == "command") return RunCommand(scope, check, out detail);

            if (!scope.TryResolve(check.Path, out string full, out string error))
            {
                detail = error;
                return false;
            }

            switch (check.Type)
            {
                case "file_exists":
                    if (File.Exists(full)) return true;
                    detail = $"{check.Path} 不存在";
                    return false;
                case "file_missing":
                    if (!File.Exists(full)) return true;
                    detail = $"{check.Path} 仍然存在";
                    return false;
                case "file_contains":
                    if (!File.Exists(full)) { detail = $"{check.Path} 不存在"; return false; }
                    string body = File.ReadAllText(full, Encoding.UTF8);
                    if (body.Contains(check.Text ?? "", StringComparison.Ordinal)) return true;
                    detail = $"{check.Path} 不含「{check.Text}」";
                    return false;
                case "file_equals":
                    if (!File.Exists(full)) { detail = $"{check.Path} 不存在"; return false; }
                    string actual = Normalize(File.ReadAllText(full, Encoding.UTF8));
                    string expected = Normalize(check.Text ?? "");
                    if (actual == expected) return true;
                    detail = $"{check.Path} 内容不符（期望「{check.Text}」，实际「{Truncate(actual, 120)}」）";
                    return false;
                default:
                    detail = $"未知检查类型：{check.Type}";
                    return false;
            }
        }

        private static bool RunCommand(WorkspaceScope scope, TaskCheck check, out string detail)
        {
            detail = null;
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = check.Args[0],
                    WorkingDirectory = scope.Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                for (int i = 1; i < check.Args.Count; i++) info.ArgumentList.Add(check.Args[i]);

                using var process = Process.Start(info);
                if (process == null) { detail = "命令无法启动"; return false; }
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(120_000))
                {
                    try { process.Kill(true); } catch { }
                    detail = $"命令超时：{string.Join(" ", check.Args)}";
                    return false;
                }
                if (process.ExitCode == check.ExpectExit) return true;
                detail = $"命令退出码 {process.ExitCode}（期望 {check.ExpectExit}）：{Truncate((stdout + stderr).Trim(), 200)}";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"命令执行失败：{ex.Message}";
                return false;
            }
        }

        private static string Normalize(string text)
            => (text ?? "").Replace("\r\n", "\n").TrimEnd();

        private static string Truncate(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";
    }
}
