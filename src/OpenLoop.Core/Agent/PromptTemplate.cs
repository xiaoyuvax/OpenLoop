using System.Text;
using System.Text.RegularExpressions;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 系统提示词模板：静态正文在这里，动态部分由 <see cref="SystemPromptBuilder"/> 以占位符填入。
    /// 模板本身是工作区可演进对象（见 Store/PromptCatalog）：改了模板就是改 agent 行为。
    /// </summary>
    public static class PromptTemplate
    {
        /// <summary>认得的占位符；模板里写错的名字原样留在渲染结果里，方便一眼看出。</summary>
        public static readonly string[] Placeholders =
        [
            "role", "environment", "access", "puppet", "collab", "rules",
            "tools", "skills", "protocol", "safety", "instructions", "max_parallel"
        ];

        private static readonly Regex Slot = new(@"\{\{([a-z_]+)\}\}", RegexOptions.Compiled);

        /// <summary>把 {{name}} 换成对应分节；空分节连同它所在的多余空行一起收掉。</summary>
        public static string Render(string template, IReadOnlyDictionary<string, string> sections)
        {
            string text = string.IsNullOrWhiteSpace(template) ? Default : template;
            string rendered = Slot.Replace(text, match =>
            {
                string key = match.Groups[1].Value;
                return sections != null && sections.TryGetValue(key, out string value) ? value ?? "" : match.Value;
            });

            // 空分节会留下连续的三个以上换行，收成两个（段落间距）
            rendered = Regex.Replace(rendered, @"\n{3,}", "\n\n");
            return rendered.Trim() + "\n";
        }

        /// <summary>
        /// 内置默认模板。等同一个完整 agent 提示词：角色 + 环境 + 边界 + 流程 + 协议 + 纪律 + 约定。
        /// 工作区没有自定义模板时用它；自定义模板只需覆写需要改的部分。
        /// </summary>
        public const string Default = """
{{role}}

{{environment}}
{{access}}

{{puppet}}{{collab}}{{rules}}
# 任务流程
- 先理解再动手：用只读工具（ListDir / Grep / ReadFile）弄清现状和相关代码，再决定怎么做。
- 动手前先看邻近代码：模仿既有风格与命名，优先复用本仓库已有的工具函数与模式，不要重复造轮子。
- 不要假设某个库/框架可用：先确认仓库里已经在用，再引入。
- 改完要验证：有构建/测试命令就运行；没有就如实说明如何验证。绝不假设测试框架存在。
- 一次专注一个任务；发现任务之外的额外问题，提出来让用户决定，不要顺手改。
- 多个相互独立的只读调用（如同时读几个文件）可在一轮内并行发出，每行一个 tool-call，一轮最多 {{max_parallel}} 个；含写操作时一轮只发一个。

# 每轮如何收尾
- 每轮只做两件事之一：输出 tool-call 行去执行，或给出面向用户的最终结论；不要只输出思考过程、计划或“让我继续看看”就结束。
- 还没做完就不要收尾：拿到 tool-result 后继续推进，直到任务完成、确实被卡住、或接近步数上限（若设置了上限）。
- 工具调用报错（tool-result ok:false）不是收尾理由：换参数/换工具继续，或明确写出失败原因与卡点再结束；不要静默停在工具刚失败的地方。
- 确实被卡住时（缺信息/缺权限/环境不支持），用最终回答说清楚缺什么、你已经试过什么，并给出可行的下一步；不要原地重复同样的调用。
- 思考内容只服务于你自己：最终回答里不要粘贴推理过程，直接给结论与改动清单。

# 输出与协作
- 正文语言跟随用户：用户用中文提问就用中文回答，用英文就用英文，其他语言同理；不要因为工具输出、代码或旧对话是英文就切换。内部推理/思维链不受语言限制。
- 回答直接给结论，不要铺垫和收尾客套；能用一句话说清就不用一段。
- 引用具体位置时给出 文件路径:行号，方便用户跳转。
- 不确定就说不确定，不要编造文件内容或工具结果。
- 只讲任务结果与依据；不要附加「我跳过了…」「风险：…」「未验证…」这类过程自述，也不要在正文里复述工具被拒绝或权限提示。

{{tools}}
{{skills}}
{{protocol}}
{{safety}}
{{instructions}}
""";

        /// <summary>默认模板的渲染（给「模板没定制」时展示与比对用）。</summary>
        public static string RenderDefault(IReadOnlyDictionary<string, string> sections)
            => Render(Default, sections);

        /// <summary>模板指纹：改动可回指到具体文本。</summary>
        public static string Fingerprint(string template)
            => SystemPromptBuilder.Fingerprint(template ?? Default);

        /// <summary>把一段文本缩成一行，用于列表/日志。</summary>
        public static string OneLine(string text, int max = 120)
        {
            string flat = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= max ? flat : flat[..max] + "…";
        }

        /// <summary>自定义模板没写的分节（占位符都删了）用默认模板补齐，避免一刀切掉协议与纪律。</summary>
        public static string Complete(string custom)
        {
            if (string.IsNullOrWhiteSpace(custom)) return Default;
            var present = new HashSet<string>(
                Slot.Matches(custom).Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            var missing = new StringBuilder();
            foreach (string key in new[] { "protocol", "safety" })
                if (!present.Contains(key)) missing.Append("{{" + key + "}}\n");
            return missing.Length == 0 ? custom : custom.TrimEnd() + "\n\n" + missing;
        }
    }
}
