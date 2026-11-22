namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 护栏的「柔性收尾」文案。护栏命中时不再把整轮判失败（用户只会看到一片空白），
    /// 而是把已经做完的进度与下一步建议交回宿主当最终答复：用户看得见发生了什么，也接得下去。
    /// </summary>
    internal static class GuardStopText
    {
        /// <summary>输出退化/思维链发散：重试仍不行时的收尾文案（带已执行步骤）。</summary>
        public static string Degenerate(string reason, AgentState state)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(OpenLoop.Core.Messages.ServerNotice.GuardStopped);
            sb.Append("模型这次在思考阶段跑偏，已就地收住（").Append(Short(reason)).Append("）。");
            sb.Append("\n\n").Append(Progress(state));
            return sb.ToString();
        }

        /// <summary>退化后要求模型收敛的那条提示（保留「输出退化」措辞，测试与用户都按它认）。</summary>
        public static string DegenerateRetry()
            => "（系统提示）你上一条输出退化（复读同一段标记，或思维链一路发散却没产出正文），已丢弃。"
               + "请继续当前任务：先用一两句话说明你接下来要做什么，再给出下一步（工具调用或结论）；不要复述刚才的内容，也不要跳过说明直接连发工具调用。";

        /// <summary>最终正文为空时的兜底：宁可给一句可操作的提示，也不要留一条空白回复。</summary>
        public static string Answer(string content, string finishReason)
        {
            if (!string.IsNullOrWhiteSpace(content)) return content.Trim();
            bool blocked = finishReason != null
                && (finishReason.Contains("content_filter", System.StringComparison.OrdinalIgnoreCase)
                    || finishReason.Contains("refusal", System.StringComparison.OrdinalIgnoreCase)
                    || finishReason.Contains("blocked", System.StringComparison.OrdinalIgnoreCase));
            return blocked
                ? "模型这次被安全策略拦下了（没有返回正文）。换个说法再试，或换一个模型。"
                : "模型这次没有返回正文。直接说「继续」会接着做；也可以换一个模型，或把任务拆小一点。";
        }

        /// <summary>已执行进度：用户判断「值不值得继续」靠它，不能只丢一句「已停止」。</summary>
        public static string Progress(AgentState state)
        {
            var steps = state.Result?.Steps;
            if (steps == null || steps.Count == 0)
                return "还没有执行任何工具。说「继续」会重新开一轮；也可以把任务拆小一点再试。";

            var sb = new System.Text.StringBuilder();
            sb.Append("已执行 ").Append(steps.Count).Append(" 步，最近几步：");
            int from = System.Math.Max(0, steps.Count - 5);
            for (int i = from; i < steps.Count; i++)
            {
                var step = steps[i];
                sb.Append('\n').Append(step.Index).Append(". ").Append(step.Tool)
                  .Append('(').Append(step.ArgsSummary).Append(") → ").Append(step.Ok ? "成功" : "失败");
            }
            sb.Append("\n说「继续」就接着做；要换做法就把要求说得更具体一点。");
            return sb.ToString();
        }

        private static string Short(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "模型输出退化";
            string text = reason.Trim();
            return text.Length <= 120 ? text : text[..120] + "…";
        }
    }
}
