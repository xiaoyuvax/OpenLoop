using System.Text;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 通用系统提示：定位 + 工具清单 + 协议与安全。
    /// 不含任何领域词汇；领域规则由宿主以后作为账本特征注入。
    /// </summary>
    public static class SystemPromptBuilder
    {
        public static string Build(
            IReadOnlyList<ITool> tools, int maxSteps, string role = null,
            IEnumerable<string> rules = null, string instructions = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.IsNullOrWhiteSpace(role)
                ? "你是一个在文本工作目录里工作的 agent。"
                : role);
            sb.AppendLine();
            sb.AppendLine("# 你的能力边界");
            sb.AppendLine("- 你只能通过下面列出的工具访问环境：不能联网，不能读写工作目录之外的文件。");
            sb.AppendLine("- 拿不准时先用只读工具了解现场，不要猜参数。");
            sb.AppendLine();

            var activeRules = rules?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (activeRules is { Count: > 0 })
            {
                sb.AppendLine("# 经验规则");
                foreach (string rule in activeRules)
                    sb.Append("- ").AppendLine(rule.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("# 可用工具");
            foreach (var t in tools)
            {
                sb.Append("- ").Append(t.Name).Append(": ").AppendLine(t.Description);
                if (!string.IsNullOrWhiteSpace(t.ParamsSchema))
                    sb.Append("  参数: ").AppendLine(t.ParamsSchema);
            }
            sb.AppendLine();
            sb.AppendLine("# 工具调用协议");
            sb.AppendLine("需要使用工具时，输出一行且仅一行（不要包在代码围栏里，不要加解释文字）：");
            sb.AppendLine("tool-call: {\"tool\":\"工具名\",\"args\":{\"参数名\":\"值\"}}");
            sb.AppendLine("一轮只调用一个工具。下一轮你会收到一行 tool-result: 开头的 JSON：");
            sb.AppendLine("成功为 {\"ok\":true,\"data\":...}，失败为 {\"ok\":false,\"error\":...}。");
            sb.AppendLine("任务完成或无需工具时，直接输出面向用户的最终中文回答（不要输出 tool-call 行）。");
            sb.AppendLine();
            sb.AppendLine("# 安全与纪律");
            sb.AppendLine("- 只做用户请求范围内的事。");
            sb.AppendLine($"- 单次任务步数上限 {maxSteps} 步；接近上限时先给出已完成部分的结论。");
            sb.AppendLine("- 同一工具重复调用且无新信息时立即停止该方向；连续侦察无果就转为回答或询问用户。");
            sb.AppendLine("- 破坏性操作先说明再执行。");
            sb.AppendLine("- 工具结果 ok:false 时如实说明原因，不要虚构成功。");
            sb.AppendLine("- 禁止谎报完成：没有真正调用工具并拿到成功结果之前，不说「已写入/已修改/已完成」。");
            sb.AppendLine("- 禁止只回复「收到」「请稍候」这类占位话术：要执行就输出 tool-call；要收尾就直接给结论。");

            if (!string.IsNullOrWhiteSpace(instructions))
            {
                sb.AppendLine();
                sb.AppendLine("# 项目约定（AGENTS.md）");
                sb.AppendLine(instructions.Trim());
            }

            return sb.ToString();
        }
    }
}
