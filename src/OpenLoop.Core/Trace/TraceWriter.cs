using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Skills;

namespace OpenLoop.Core.Trace
{
    /// <summary>
    /// 一次运行的证据目录（append-only）：
    /// run.json / messages.jsonl / steps.jsonl / final.md。
    /// 不复制工作目录里的任何文件，只记录发生过的动作与结果。
    /// </summary>
    public sealed class TraceWriter : IDisposable
    {
        private static readonly JsonSerializerOptions LineJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions PrettyJson = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly object _gate = new();
        private readonly string _workspace;
        private readonly string _model;
        private readonly string _tag;
        private readonly string _prompt;
        private readonly DateTime _startedAt = DateTime.Now;

        private StreamWriter _messages;
        private StreamWriter _steps;
        private IReadOnlyList<SkillInfo> _skills;
        private IReadOnlyList<string> _skillProblems;
        private string _systemPromptHash;
        private int _systemPromptChars;
        private bool _finished;

        private TraceWriter(string directory, string workspace, string model, string tag, string prompt)
        {
            DirectoryPath = directory;
            _workspace = workspace;
            _model = model;
            _tag = tag;
            _prompt = prompt;

            Directory.CreateDirectory(directory);
            _messages = new StreamWriter(Path.Combine(directory, "messages.jsonl"), false, new UTF8Encoding(false));
            _steps = new StreamWriter(Path.Combine(directory, "steps.jsonl"), false, new UTF8Encoding(false));
        }

        public string DirectoryPath { get; }

        public int MessageCount { get; private set; }

        public int StepCount { get; private set; }

        public static TraceWriter Start(string workspaceRoot, string tag, string prompt, string model)
        {
            string id = $"T-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
            string directory = Path.Combine(workspaceRoot, ".openloop", "traces", id);
            return new TraceWriter(directory, workspaceRoot, model, tag, prompt);
        }

        /// <summary>记录本次运行可见的技能、内容哈希与解析问题（用于回指与归因）。</summary>
        public void SkillSet(IReadOnlyList<SkillInfo> skills, IReadOnlyList<string> problems = null)
        {
            _skills = skills;
            _skillProblems = problems;
        }

        /// <summary>记录实际注入的系统提示词指纹（原文可能含用户约定，不落盘）。</summary>
        public void SystemPrompt(string prompt)
        {
            _systemPromptHash = SystemPromptBuilder.Fingerprint(prompt);
            _systemPromptChars = prompt?.Length ?? 0;
        }

        public void Message(LlmMessage message) => Message(message?.Role, message?.Content);

        public void Message(string role, string content)
        {
            lock (_gate)
            {
                if (_finished) return;
                _messages.WriteLine(JsonSerializer.Serialize(new { role, content }, LineJson));
                _messages.Flush();
                MessageCount++;
            }
        }

        public void Step(AgentStepRecord record)
        {
            lock (_gate)
            {
                if (_finished) return;
                _steps.WriteLine(JsonSerializer.Serialize(new
                {
                    index = record.Index,
                    tool = record.Tool,
                    args = record.ArgsFull,
                    ok = record.Ok,
                    durationMs = record.DurationMs,
                    result = record.Summary
                }, LineJson));
                _steps.Flush();
                StepCount++;
            }
        }

        public void Finish(string outcome, string finalAnswer, decimal? cost = null, AgentGuardStats guards = null)
        {
            lock (_gate)
            {
                if (_finished) return;
                _finished = true;

                _messages?.Dispose();
                _steps?.Dispose();
                _messages = null;
                _steps = null;

                File.WriteAllText(Path.Combine(DirectoryPath, "final.md"), finalAnswer ?? "",
                    new UTF8Encoding(false));

                var run = new
                {
                    id = Path.GetFileName(DirectoryPath),
                    startedAt = _startedAt,
                    endedAt = DateTime.Now,
                    workspace = _workspace,
                    model = _model,
                    tag = _tag,
                    prompt = _prompt,
                    cost,
                    outcome,
                    messages = MessageCount,
                    steps = StepCount,
                    skills = (_skills ?? []).Select(s => new { name = s.Name, sha256 = s.Sha256, source = s.Source }).ToArray(),
                    skillProblems = _skillProblems ?? [],
                    guards = guards ?? new AgentGuardStats(),
                    systemPrompt = new { sha256 = _systemPromptHash, chars = _systemPromptChars },
                    binary = RuntimeInfo.Version
                };
                File.WriteAllText(Path.Combine(DirectoryPath, "run.json"),
                    JsonSerializer.Serialize(run, PrettyJson), new UTF8Encoding(false));
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _messages?.Dispose();
                _steps?.Dispose();
                _messages = null;
                _steps = null;
                _finished = true;
            }
        }
    }
}
