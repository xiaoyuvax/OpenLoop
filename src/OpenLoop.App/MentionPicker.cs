using System.Text;
using ChatFlow;

namespace OpenLoop.App
{
    /// <summary>
    /// @ 引用同工作区另一个会话：识别输入框末尾的 @token、列候选、拼给模型看的上下文。
    /// Worker 与 Loop 共用同一套判据，浮层复用命令联想（CommandPopup）。
    /// </summary>
    internal static class MentionPicker
    {
        /// <summary>末尾正在输入的 @token；不在 @ 输入状态返回 null。</summary>
        internal static (int Start, string Query)? Detect(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int at = text.LastIndexOf('@');
            if (at < 0) return null;
            string query = text[(at + 1)..];
            return query.Any(char.IsWhiteSpace) ? null : (at, query);
        }

        /// <summary>候选：标题或 id 命中 query（不区分大小写）；同标题只留一条。</summary>
        internal static List<(string Command, string Description)> Items(
            IEnumerable<MentionTarget> targets, string query)
        {
            var items = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets ?? [])
            {
                if (string.IsNullOrWhiteSpace(target?.Title) || !seen.Add(target.Title)) continue;
                if (query.Length > 0
                    && target.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && !(target.Id ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(("@" + target.Title,
                    $"{target.Turns} 轮 · {LoopCollabHost.StateText(target.State)}"));
            }
            return items;
        }

        /// <summary>把文本里命中的 @标题 展开成会话上下文；没有引用返回空串。</summary>
        internal static string Context(string text, IEnumerable<MentionTarget> targets, int turns)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            foreach (var target in targets ?? [])
            {
                if (string.IsNullOrWhiteSpace(target?.Title) || !HasToken(text, "@" + target.Title)) continue;
                var list = target.Read?.Invoke() ?? [];
                sb.AppendLine().Append("【引用会话「").Append(target.Title).Append("」】").AppendLine();
                sb.AppendLine(SessionMemoryTool.DescribeTurns(target.Title,
                    LoopCollabHost.StateText(target.State), list, 0, Math.Max(1, turns)));
            }
            return sb.Length == 0 ? "" : "参考上下文（用户 @ 引用的同工作区会话）：" + sb.ToString().TrimEnd();
        }

        /// <summary>token 要独立成词：后面是行尾、空白或标点，避免「@A」误命中「@AB」。</summary>
        private static bool HasToken(string text, string token)
        {
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = index + token.Length;
                if (end == text.Length || char.IsWhiteSpace(text[end]) || char.IsPunctuation(text[end]))
                    return true;
                index = end;
            }
            return false;
        }
    }

    /// <summary>可被 @ 引用的会话：标题用于插入与命中，<see cref="Read"/> 取该会话的轮次。</summary>
    internal sealed class MentionTarget
    {
        public string Title { get; init; }
        public string Id { get; init; }
        public int Turns { get; init; }
        public SessionState State { get; init; }
        public Func<IReadOnlyList<ChatTurn>> Read { get; init; }
    }
}
