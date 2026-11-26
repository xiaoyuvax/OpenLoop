namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 宿主拼在用户消息前面的说明块（技能展开、TCF 启用提示）：界面按宿主消息样式显示，
    /// 正文只留用户自己写的那段。标记与构造方共用，改首行文案时两边一起变。
    /// </summary>
    public static class HostPrompt
    {
        /// <summary>TCF 启用提示的首行标记（<c>TargetPane.Prompt</c> 用它开头）。</summary>
        public const string TargetLead = "[目标编辑面] ";

        /// <summary>「/技能名」展开的首行标记（<c>SlashCommands.SkillTask</c> 用它开头）。</summary>
        public const string SkillLead = "使用技能「";

        private static readonly string[] Leads = [TargetLead, SkillLead];

        /// <summary>
        /// 切出前面的宿主说明块与后面的用户正文；没有宿主块（或切完没有正文）时 Host 为空。
        /// shortcut: 以首个空行作为说明块与正文的分界，说明块内部加空行会切错，改 Prompt 构造时同步改这里。
        /// </summary>
        public static (string Host, string Body) Split(string text)
        {
            if (string.IsNullOrEmpty(text)) return ("", text ?? "");
            int at = 0;
            while (at < text.Length)
            {
                string rest = text[at..];
                if (!Array.Exists(Leads, lead => rest.StartsWith(lead, StringComparison.Ordinal))) break;
                int sep = BlankLineEnd(rest);
                if (sep < 0) break;
                at += sep;
            }
            string body = at == 0 ? text : text[at..].TrimStart('\r', '\n');
            // 只剩宿主说明、没有用户正文：不切，免得留下一个空的用户气泡
            return body.Length == 0 || at == 0 ? ("", text) : (text[..at].TrimEnd('\r', '\n'), body);
        }

        /// <summary>首个空行（两个连续换行）之后的偏移；没有返回 -1。CRLF/LF 都认。</summary>
        private static int BlankLineEnd(string text)
        {
            for (int i = 0; i < text.Length;)
            {
                int len = BreakLength(text, i);
                if (len == 0) { i++; continue; }
                int next = i + len;
                int second = BreakLength(text, next);
                if (second > 0) return next + second;
                i = next;
            }
            return -1;
        }

        /// <summary>位置 i 上的换行符长度（\r\n=2，\n 或 \r=1，其它=0）。</summary>
        private static int BreakLength(string text, int i)
            => i >= text.Length ? 0
                : text[i] == '\r' ? (i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1)
                : text[i] == '\n' ? 1
                : 0;
    }
}
