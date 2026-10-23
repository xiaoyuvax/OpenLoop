using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>
    /// Loop 专用：在指定会话的对话流里插入一张调查卡（不弹窗）。
    /// 只传语义字段，样式由前端决定；每会话最多 3 张待答。
    /// </summary>
    public sealed class AskUserTool : ITool
    {
        public const int MaxPending = 3;

        private static readonly string[] Kinds = ["choice", "multi", "text", "scale"];

        private readonly string _workspace;
        private readonly Func<(string Id, int Turn)> _target;

        public AskUserTool(string workspace, Func<(string Id, int Turn)> target)
        {
            _workspace = workspace;
            _target = target;
        }

        public string Name => "ask_user";
        public string Description =>
            "在用户当前会话里插入一张调查卡，问一个具体问题（会出现在被评价的那一轮之后）。只问 agent 执行行为（工具使用、顺序、验证、范围、效率、偷懒、停止时机），不问答案内容；只问需要人类判断的，能自查的用 record_finding。每会话最多 3 张待答。";
        public string ParamsSchema =>
            "title: string（一句话问题）; body: string 可选（发生了什么+为什么可疑，1-2 句）; dimension: string（tool_selection|tool_usage|sequencing|verification|scope|efficiency|diligence|stopping|delegation）; kind: string 可选（choice|multi|text|scale，默认 choice）; options: string[] 可选（默认 [\"合理\",\"有问题\",\"说不清\"]）; anchorTurn: number 可选（默认当前最后一轮）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string title = args.Str("title");
            if (string.IsNullOrWhiteSpace(title))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 title"));

            var (sessionId, currentTurn) = _target?.Invoke() ?? ("", -1);
            if (string.IsNullOrWhiteSpace(sessionId))
                return Task.FromResult(AgentToolResult.Failure("没有活动会话：先让用户打开或新建一个会话"));

            var pending = SurveyStore.Load(_workspace, sessionId).Count(s => s.State == "pending");
            if (pending >= MaxPending)
                return Task.FromResult(AgentToolResult.Failure($"待答调查已达上限 {MaxPending} 条，先等用户回答再提问。"));

            string dimension = args.Str("dimension", "other");
            string kind = args.Str("kind", "choice");
            if (!Kinds.Contains(kind)) kind = "choice";

            var options = args.StrList("options");
            if (options.Count == 0) options = ["合理", "有问题", "说不清"];

            int anchor = args.Has("anchorTurn") ? args.Int("anchorTurn", currentTurn) : currentTurn;

            var fields = new List<SurveyFieldRecord>
            {
                new()
                {
                    Key = dimension,
                    Label = "",
                    Kind = kind,
                    Options = options.ToList(),
                    Placeholder = args.Str("placeholder", "")
                }
            };

            var record = SurveyStore.Add(
                _workspace, sessionId, title, args.Str("body", ""), anchor, AccentFor(dimension), fields);
            if (record == null)
                return Task.FromResult(AgentToolResult.Failure("创建调查卡失败"));

            return Task.FromResult(AgentToolResult.Success(
                $"已插入调查卡 {record.Id}（会话 {sessionId}，锚定第 {anchor + 1} 轮，维度 {dimension}）。等用户回答后再继续。"));
        }

        private static string AccentFor(string dimension) => dimension switch
        {
            "tool_usage" or "tool_selection" => "#FA8C16",
            "diligence" or "verification" => "#F5222D",
            "efficiency" or "sequencing" => "#1677FF",
            "scope" => "#722ED1",
            _ => "#722ED1"
        };
    }
}
