using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// Loop 专用：把观察到的问题起草成考核题草稿。只写 loop/task-drafts，
    /// 用户确认后才进 eval/tasks；工具本身没有入库能力。
    /// </summary>
    public sealed class ProposeTaskTool : ITool
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _workspace;

        public ProposeTaskTool(string workspace) => _workspace = workspace;

        public string Name => "propose_task";
        public string Description => "把观察到、可复现的失败模式起草成一道考核题（草稿）。只写 .openloop/loop/task-drafts/，用户确认后才进入题集；不要直接写 eval/tasks。";
        public string ParamsSchema =>
            "task: object（必需：{id, problem, prompt, seed?: {路径: 内容}, scope?: [路径], checks: [{type: file_exists|file_missing|file_contains|file_equals|command, path?, text?, args?, expectExit?}]}）; " +
            "reason: string（这题防哪个失败模式）; " +
            "source: object 可选 {finding?, trace?, session?}（至少一个非空；也可传字符串）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            if (!args.Raw.TryGetValue("task", out var rawTask) || rawTask == null)
                return Task.FromResult(AgentToolResult.Failure("缺少参数 task（对象）"));

            EvalTask task;
            try
            {
                task = ReadTask(rawTask);
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("task 不是合法对象：" + ex.Message));
            }

            TaskDraftSource source = ReadSource(args);
            if (source.IsEmpty)
                return Task.FromResult(AgentToolResult.Failure(
                    "缺少来源：source 或 finding/trace/session 至少给一个（指向发现或 trace）"));

            try
            {
                var draft = TaskDrafts.Add(_workspace, task, args.Str("reason"), source);
                return Task.FromResult(AgentToolResult.Success(
                    $"已起草 {draft.Id}（题 id {draft.Task.Id}），等待用户确认；不要直接改 eval/tasks。"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("起草失败：" + ex.Message));
            }
        }

        private static EvalTask ReadTask(object raw)
        {
            if (raw is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                    return JsonSerializer.Deserialize<EvalTask>(je.GetRawText(), Json);
                if (je.ValueKind == JsonValueKind.String)
                    return JsonSerializer.Deserialize<EvalTask>(je.GetString(), Json);
            }
            if (raw is string text)
                return JsonSerializer.Deserialize<EvalTask>(text, Json);
            throw new InvalidDataException("task 必须是 JSON 对象");
        }

        private static TaskDraftSource ReadSource(ToolArgs args)
        {
            var source = new TaskDraftSource();
            if (args.Raw.TryGetValue("source", out var raw) && raw != null)
            {
                if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
                    source = JsonSerializer.Deserialize<TaskDraftSource>(je.GetRawText(), Json) ?? source;
                else if (raw is JsonElement js && js.ValueKind == JsonValueKind.String)
                    source.Finding = js.GetString();
                else
                    source.Finding = raw.ToString();
            }

            if (string.IsNullOrWhiteSpace(source.Finding)) source.Finding = args.Str("finding");
            if (string.IsNullOrWhiteSpace(source.Trace)) source.Trace = args.Str("trace");
            if (string.IsNullOrWhiteSpace(source.Session)) source.Session = args.Str("session");
            return source;
        }
    }
}
