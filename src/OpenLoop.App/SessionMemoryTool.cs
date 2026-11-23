using System.Text;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 专用：直接读壳内存里打开的会话（标题、轮次、工具调用）。
    /// chats 文件按工作区整表落盘、常几十 MB，ReadFile 有 2MB 上限，也没法按轮定位。
    /// </summary>
    internal sealed class SessionMemoryTool : ITool
    {
        private readonly Func<string, int, int, string> _read;

        public SessionMemoryTool(Func<string, int, int, string> read) => _read = read;

        public string Name => "ReadSession";
        public string Description =>
            "读内存里打开的会话（/complain、/debug 定位问题先用它了解上下文；协作时也用它读协作会话）：" +
            "不给 session 时列出打开的会话与各自当前状态（运行中 / 已正常结束 / 异常结束及错误信息）；" +
            "给了就按轮返回用户输入、工具调用与回复。会话已关闭时再用 ReadFile 读记录文件。";
        public string ParamsSchema =>
            "session: string 可选（会话标题或 id 前缀，省略=列出打开的会话）; " +
            "offset: number 可选（从第几轮开始，1 起；省略=最近的若干轮）; " +
            "count: number 可选（最多轮数，默认 10，上限 50）";
        public bool IsReadOnly => true;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            try
            {
                string text = _read(args.Str("session"), args.Int("offset", 0), args.Int("count", 10));
                const int max = 12000;
                if (text != null && text.Length > max) text = text[..max] + "\n…（已截断，继续读请设 offset）";
                return Task.FromResult(AgentToolResult.Success(text ?? ""));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure("读取内存会话失败：" + ex.Message));
            }
        }

        /// <summary>会话清单：标题与 id 都要给，Loop 下次才找得到（标题可能带「Loop工作区」前缀）。</summary>
        internal static string Listing(IReadOnlyList<Session> sessions)
        {
            if (sessions == null || sessions.Count == 0) return "（没有打开的会话）";
            var sb = new StringBuilder("打开的会话（session 可填标题或 id 前缀）：");
            foreach (var session in sessions)
            {
                sb.AppendLine();
                sb.Append("- ").Append(session.Title).Append(" · ").Append(session.Id)
                    .Append(" · ").Append(LoopCollabHost.StateText(session.State)).Append(" · ")
                    .Append(session.Book.Turns.Count).Append(" 轮 · ").Append(session.Workspace);
                // 异常结束的会话在清单里就要能看到原因，不必再逐条读轮次
                var last = session.Book.Turns.LastOrDefault();
                if (session.State == SessionState.Interrupted
                    && OpenLoop.Core.Messages.ServerNotice.IsNotice(last?.AssistantText))
                    sb.Append(" · 错误：").Append(Cut(last.AssistantText, 160));
            }
            return sb.ToString();
        }

        /// <param name="offset">1 起；&lt;=0 表示「最近 count 轮」。</param>
        internal static string Describe(Session session, int offset, int count)
            => DescribeTurns(session.Title, LoopCollabHost.StateText(session.State),
                session.Book.Turns, offset, count);

        /// <summary>按轮渲染一段对话（@ 引用与 ReadSession 共用同一段格式）。</summary>
        internal static string DescribeTurns(string title, string state,
            IReadOnlyList<ChatTurn> turns, int offset, int count)
        {
            turns ??= [];
            if (turns.Count == 0) return $"会话「{title}」（{state}）还没有轮次。";

            count = Math.Clamp(count, 1, 50);
            if (offset > turns.Count) return $"会话「{title}」（{state}）共 {turns.Count} 轮，offset={offset} 超出范围。";
            int start = offset > 0 ? offset : Math.Max(1, turns.Count - count + 1);
            int end = Math.Min(turns.Count + 1, start + count);

            var sb = new StringBuilder();
            sb.AppendLine($"会话「{title}」第 {start}-{end - 1} 轮（共 {turns.Count} 轮，{state}）：");
            for (int i = start; i < end; i++)
            {
                var turn = turns[i - 1];
                sb.Append(i).Append(". 用户：").AppendLine(Cut(turn.UserText, 300));
                if (!string.IsNullOrWhiteSpace(turn.ToolCallLines))
                    sb.Append("   工具：").AppendLine(Cut(turn.ToolCallLines, 600));
                sb.Append("   回复：").AppendLine(Cut(turn.AssistantText, 500));
            }
            if (end <= turns.Count) sb.Append("（还有 ").Append(turns.Count - end + 1).Append(" 轮，继续读请设 offset=").Append(end).Append('）');
            return sb.ToString().TrimEnd();
        }

        private static string Cut(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";
    }
}
