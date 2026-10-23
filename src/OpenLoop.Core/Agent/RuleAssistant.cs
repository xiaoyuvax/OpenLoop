using System.Text.RegularExpressions;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 用户侧的规则编辑助手：只润色规则文本，不执行任务、不接触账本与激活集。
    /// 输出由用户确认后才登记；它没有写权限。
    /// </summary>
    public static class RuleAssistant
    {
        public const string SystemPrompt = """
你是 Worker 的规则编辑助手，只改规则文本，不执行任务、不调用工具、不解释过程。
输入是一条写给 Worker 的规则（可能带规则名）。输出优化后的规则正文，要求：
- 只输出一条规则，单行，不超过 120 个字符。
- 用祈使句直接说明做什么/不做什么；不要“请/建议/尽量/可以”这类软话，不要解释原因。
- 保留原意；原文有歧义时选更保守、可验证的表述。
- 不新增原文没有的工具名、文件路径或数字。
- 不要 Markdown、不要引号、不要编号、不要代码围栏。
只输出规则正文本身。
""";

        public static async Task<string> ImproveAsync(
            IReasoner reasoner, string name, string text, CancellationToken ct = default)
        {
            if (reasoner == null) throw new ArgumentNullException(nameof(reasoner));
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("规则正文不能为空", nameof(text));

            var messages = new List<LlmMessage>
            {
                new("system", SystemPrompt),
                new("user", $"规则名：{(string.IsNullOrWhiteSpace(name) ? "（未命名）" : name.Trim())}\n现有规则：\n{text.Trim()}")
            };

            var completion = await reasoner.CompleteAsync(messages, ct);
            return Normalize(completion?.Content);
        }

        internal static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var lines = raw.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal));
            string text = string.Join(" ", lines).Trim();

            text = text.Trim('`', '"', '\'', '“', '”', '‘', '’').Trim();
            if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
                text = text[2..].Trim();

            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length <= 1000 ? text : text[..1000];
        }
    }
}
