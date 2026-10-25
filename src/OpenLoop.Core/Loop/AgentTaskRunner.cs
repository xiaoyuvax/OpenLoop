using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Loop
{
    /// <summary>
    /// 用真实 Worker 跑一道考核题：隔离目录 → 播种 → agent 执行 → 自动判定。
    /// 证据留在 .openloop/eval/runs/，可回指。
    /// </summary>
    public sealed class AgentTaskRunner : ITaskRunner
    {
        private readonly Func<string, IReasoner> _reasonerFor;
        private readonly IReadOnlyDictionary<string, FeatureRecord> _features;
        private readonly string _workspaceRoot;
        private readonly string _evidenceRoot;
        private readonly string _globalAgentsPath;
        private readonly bool _useAgentsMd;
        private readonly IReadOnlyList<SkillInfo> _catalog;

        public AgentTaskRunner(
            Func<string, IReasoner> reasonerFor,
            IReadOnlyDictionary<string, FeatureRecord> features,
            string workspaceRoot, string evidenceRoot, string globalAgentsPath = null,
            bool useAgentsMd = true)
        {
            _reasonerFor = reasonerFor ?? throw new ArgumentNullException(nameof(reasonerFor));
            _features = features ?? throw new ArgumentNullException(nameof(features));
            _workspaceRoot = Path.GetFullPath(workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot)));
            _evidenceRoot = Path.GetFullPath(evidenceRoot ?? throw new ArgumentNullException(nameof(evidenceRoot)));
            _globalAgentsPath = globalAgentsPath;
            _useAgentsMd = useAgentsMd;
            _catalog = SkillCatalog.Load(_workspaceRoot);
        }

        public async Task<TaskOutcome> RunAsync(
            EvalTask task, ActivationRecord activation, CancellationToken ct)
        {
            string name = $"{Safe(task.Id)}-v{activation.Version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
            string dir = Path.GetFullPath(Path.Combine(_evidenceRoot, "runs", name));
            Directory.CreateDirectory(dir);
            var scope = new WorkspaceScope(dir);

            foreach (var pair in task.Seed ?? [])
            {
                if (!scope.TryResolve(pair.Key, out string full, out string error))
                    throw new InvalidOperationException($"任务 {task.Id} 的起始文件越界：{error}");
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(full, pair.Value ?? "", new UTF8Encoding(false));
            }

            var skills = SkillCatalog.Active(activation, _features, _catalog, out var skillProblems);
            if (skillProblems.Count > 0)
                throw new InvalidOperationException(
                    $"任务 {task.Id} 的技能不可用：{string.Join("；", skillProblems)}");

            var tools = ToolRegistry.Workspace(scope, null, skills);
            var loop = new AgentLoop(tools, _reasonerFor(dir),
                FeatureCatalog.PolicyFrom(activation, _features), skills: skills);
            string systemPrompt = loop.BuildSystemPrompt(
                null, FeatureCatalog.RulesFrom(activation, _features),
                _useAgentsMd ? AgentsInstructions.Load(_workspaceRoot, _globalAgentsPath) : null,
                _workspaceRoot);
            var history = new List<LlmMessage> { new("system", systemPrompt) };

            var result = await loop.RunAsync(history, task.Prompt, ct, AgentContext.DefaultContextLimit);
            var (passed, detail) = TaskJudge.Evaluate(dir, task.Checks);
            string final = result.FinalAnswer ?? "";

            File.WriteAllText(Path.Combine(dir, "final.md"), final, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "run.json"), JsonSerializer.Serialize(new
            {
                task = task.Id,
                version = activation.Version,
                outcome = result.BudgetExhausted ? "budget" : result.StoppedByUser ? "stopped" : "ok",
                steps = result.Steps.Count,
                cost = result.ChatCost,
                passed,
                detail,
                skills = skills.Select(s => new { name = s.Name, sha256 = s.Sha256, source = s.Source }).ToArray(),
                guards = result.Guards,
                systemPrompt = new { sha256 = SystemPromptBuilder.Fingerprint(systemPrompt), chars = systemPrompt.Length },
                binary = RuntimeInfo.Version
            }, ReportJson), new UTF8Encoding(false));

            return new TaskOutcome
            {
                Passed = passed,
                Detail = detail,
                Cost = result.ChatCost,
                Steps = result.Steps.Count,
                EvidenceDir = Path.GetRelativePath(_workspaceRoot, dir).Replace('\\', '/')
            };
        }

        private static readonly JsonSerializerOptions ReportJson = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string Safe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name?.Length ?? 0);
            foreach (char c in name ?? "task") sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString();
        }
    }
}
