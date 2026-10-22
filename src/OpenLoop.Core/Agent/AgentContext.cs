using System.Text;
using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 上下文统计与压缩（纯逻辑，无网络）。
    /// token 估算用字符启发式（CJK≈1/字，其余≈1/4 字），只求量级正确以触发压缩阈值。
    /// </summary>
    public static class AgentContext
    {
        public const int DefaultContextLimit = 32768;
        public const double CompressThreshold = 0.8;
        public const int KeepRecentMessages = 6;

        public const string SummaryPrefix = "【此前对话摘要（系统自动压缩）】\n";

        public const string SummaryInstruction =
            "请把以下 agent 与用户的对话压缩为简洁中文摘要，保留：用户的目标与偏好、已完成的工具操作及其结果" +
            "（尤其是操作过的文件路径、关键参数与数字）、未决事项与后续步骤。不要遗漏文件路径与数字。" +
            "只输出摘要正文，不要客套话。\n\n";

        public static int EstimateTokens(IEnumerable<LlmMessage> messages)
        {
            if (messages == null) return 0;
            int total = 0;
            foreach (var m in messages)
            {
                total += 4;
                total += EstimateTokens(m?.Content);
            }
            return total;
        }

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double t = 0;
            foreach (char c in text) t += IsCjk(c) ? 1.0 : 0.25;
            return (int)Math.Ceiling(t);
        }

        private static bool IsCjk(char c) =>
            (c >= 0x2E80 && c <= 0x9FFF)
            || (c >= 0xAC00 && c <= 0xD7AF)
            || (c >= 0xF900 && c <= 0xFAFF)
            || (c >= 0xFF00 && c <= 0xFFEF);

        /// <summary>
        /// 把消息序列切分为 头部(system)/中部(可压缩)/尾部(最近保留)。
        /// 尾部起点不落在 tool-result 消息上，避免拆散 assistant(tool-call)+user(tool-result) 对。
        /// </summary>
        public static (List<LlmMessage> head, List<LlmMessage> middle, List<LlmMessage> tail) SplitForCompression(
            IReadOnlyList<LlmMessage> messages, int keepRecent)
        {
            var head = new List<LlmMessage>();
            int i = 0;
            while (i < messages.Count && string.Equals(messages[i].Role, "system", StringComparison.OrdinalIgnoreCase))
                head.Add(messages[i++]);
            var rest = messages.Skip(i).ToList();
            int keep = Math.Max(1, keepRecent);
            if (rest.Count <= keep) return (head, new List<LlmMessage>(), rest);
            int start = rest.Count - keep;
            while (start > 0 && IsToolResult(rest[start])) start--;
            var tail = rest.Skip(start).ToList();
            var middle = rest.Take(start).ToList();
            return (head, middle, tail);
        }

        public static bool IsToolResult(LlmMessage m)
            => m?.Content != null && m.Content.StartsWith("tool-result:", StringComparison.Ordinal);

        public static string RenderForSummary(IEnumerable<LlmMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var m in messages)
            {
                sb.Append('[').Append(m.Role).Append("] ");
                sb.AppendLine(m.Content);
            }
            return sb.ToString();
        }
    }
}
