using OpenLoop.Core.Agent;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// 派生子 agent：独立上下文、受限工具、独立步数预算，只把结论带回父循环。
    /// 默认开启；子 agent 不能再派生（允许列表里没有 spawn_agent），且默认没有命令执行权。
    /// </summary>
    public sealed class SpawnAgentTool : ITool
    {
        public static readonly string[] AllowedChildTools = ["read_file", "write_file", "list_dir", "grep"];

        private readonly Func<IReasoner> _reasoner;
        private readonly Func<IReadOnlyList<string>, ToolRegistry> _registry;
        private readonly string _workspace;
        private readonly Func<(string Id, int Turn)> _target;

        public SpawnAgentTool(
            Func<IReasoner> reasoner,
            Func<IReadOnlyList<string>, ToolRegistry> registry,
            string workspace = null,
            Func<(string Id, int Turn)> target = null)
        {
            _reasoner = reasoner;
            _registry = registry;
            _workspace = workspace;
            _target = target;
        }

        public string Name => "spawn_agent";
        public string Description =>
            "派生一个子 agent 处理可独立完成的子任务（它看不到当前对话，只看到你给的任务描述），返回结论与成本。子 agent 工具受限（只读/写/列目录/搜索，无命令执行），不能再次派生。";
        public string ParamsSchema =>
            "task: string（自包含的任务描述，含路径与预期产出）; tools: string[] 可选（read_file|write_file|list_dir|grep，默认全部）; maxSteps: number 可选（默认 8，最大 20）";
        public bool IsReadOnly => false;

        public async Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string task = args.Str("task");
            if (string.IsNullOrWhiteSpace(task))
                return AgentToolResult.Failure("缺少参数 task（子任务描述必须是自包含的）");

            int maxSteps = Math.Clamp(args.Int("maxSteps", 8), 1, 20);

            var requested = args.StrList("tools");
            var names = requested.Count == 0
                ? AllowedChildTools.ToList()
                : requested.Where(n => AllowedChildTools.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (names.Count == 0)
                return AgentToolResult.Failure(
                    "tools 不含任何允许的子 agent 工具：" + string.Join(", ", AllowedChildTools));

            var registry = _registry(names);
            if (registry.Tools.Count == 0)
                return AgentToolResult.Failure("子 agent 工具集为空");

            var loop = new AgentLoop(registry, _reasoner(), new AgentPolicy { MaxSteps = maxSteps });

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            AgentRunResult result;
            try
            {
                result = await loop.RunAsync([], task, linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return AgentToolResult.Failure("子 agent 超时（3 分钟），已终止");
            }

            var (sessionId, turn) = _target?.Invoke() ?? ("", -1);
            if (!string.IsNullOrEmpty(_workspace))
            {
                OpenLoop.Core.Store.AgentTranscriptStore.Add(_workspace, new OpenLoop.Core.Store.AgentTranscript
                {
                    SessionId = sessionId,
                    AnchorTurn = turn,
                    Task = task,
                    Answer = result.FinalAnswer ?? "",
                    Steps = result.Steps.Select(s => new OpenLoop.Core.Store.AgentStepRecord
                    {
                        Tool = s.Tool,
                        Args = s.ArgsFull,
                        Ok = s.Ok,
                        Ms = s.DurationMs
                    }).ToList()
                });
            }

            string answer = ToolText.Truncate(result.FinalAnswer ?? "", 4000);
            return AgentToolResult.Success(
                $"子 agent 完成：steps={result.Steps.Count} cost={result.ChatCost ?? 0m:0.0000}" +
                (result.BudgetExhausted ? "（达到步数上限）" : "") +
                "\n--- 结论 ---\n" + answer);
        }
    }
}
