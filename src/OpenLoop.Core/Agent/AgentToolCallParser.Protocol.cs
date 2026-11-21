using System.Text;
using System.Text.RegularExpressions;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 工具调用协议识别（invoke 块解析 + 装饰归一化）。
    ///
    /// 各家模型与网关吐出的标记形态并不一致：<c>&lt;|DSML| invoke&gt;</c>、<c>&lt;｜｜DSML｜｜ invoke&gt;</c>、
    /// <c>&lt;｜｜parameter&gt;</c>、<c>&lt;antml:invoke&gt;</c> …… 逐家加正则永远追不上。
    /// 这里先把「标签名前的装饰前缀」统一吃掉，再只按标签结构解析一次：
    /// 新模型换一种装饰，解析路径不用改。
    ///
    /// 另一条硬约束：<b>不同 invoke 的参数绝不混在一起</b>。旧实现用
    /// <c>InvokeParamRegex.Matches(text)</c> 扫全文，一次输出里有多条调用时，
    /// 后一条的参数会被算进前一条（实测：ReadFile 行里挂上 Shell 的 argv，
    /// 参数值里还带着 <c>&lt;｜｜DSML｜｜" string="false"&gt;266</c> 这样的协议残渣）。
    /// </summary>
    public static partial class AgentToolCallParser
    {
        /// <summary>标签前的装饰词（命名空间 / 特殊 token 名），大小写不敏感。</summary>
        private static readonly string[] DecorationWords =
            ["dsml", "｜DSML｜", "antml", "anthropic", "deepseek"];

        /// <summary>invoke 开始标签（不含结束标签），<c>attrs</c> 为标签内其余文本。</summary>
        private static readonly Regex InvokeHeadRegex = new(
            @"<(?!\s*/)\s*invoke\b(?<attrs>[^<>]*)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>invoke 块边界：本块的结束标签，或下一个 invoke 的开始标签。</summary>
        private static readonly Regex InvokeBoundaryRegex = new(
            @"<\s*/\s*invoke\s*>|<(?!\s*/)\s*invoke\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>parameter 标签（开/闭都算）：模型常把下一个参数写在 <c>&lt;/…parameter name="x"&gt;</c> 里。</summary>
        private static readonly Regex ParamTagRegex = new(
            @"<\s*/?\s*parameter\b(?<attrs>[^<>]*)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>标签里的 name 属性：双引号、单引号、裸值三种写法。</summary>
        private static readonly Regex NameAttrRegex = new(
            @"\bname\s*=\s*(?:[""](?<q>[^""]*)[""]|'(?<q>[^']*)'|(?<bare>[^\s""'/>]+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 装饰归一化：把 <c>&lt;|DSML|</c> / <c>&lt;｜｜DSML｜｜</c> / <c>&lt;｜｜</c> / <c>&lt;antml:</c>
        /// 之类前缀统一换成 <c>&lt;</c>，只留标签名，后续只看标签结构。
        /// 只处理 <c>&lt;</c> / <c>&lt;/</c> 之后紧跟的装饰，正文里的普通尖括号不动。
        /// </summary>
        internal static string NormalizeTags(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] != '<')
                {
                    sb.Append(text[i++]);
                    continue;
                }

                int nameStart = i + 1;
                bool closing = nameStart < text.Length && text[nameStart] == '/';
                if (closing) nameStart++;

                int tagStart = SkipDecorations(text, nameStart);
                if (tagStart == nameStart)
                {
                    sb.Append('<');
                    i++;
                    continue;
                }

                sb.Append(closing ? "</" : "<");
                i = tagStart;
            }
            return sb.ToString();
        }

        /// <summary>吃掉装饰前缀与随后空白；返回第一个标签名字符的位置。</summary>
        private static int SkipDecorations(string text, int start)
        {
            int p = start;
            bool sawPipe = false;
            while (p < text.Length)
            {
                char c = text[p];
                if (c is '|' or '｜' or '▁' or '▏' or '▕')
                {
                    sawPipe = true;
                    p++;
                    continue;
                }
                if (sawPipe && (c is ':' or '：' or '_' or '-' or '.' or '·'))
                {
                    p++;
                    continue;
                }
                if (char.IsLetterOrDigit(c))
                {
                    int k = p;
                    while (k < text.Length && (char.IsLetterOrDigit(text[k]) || text[k] is '_' or '▁')) k++;
                    if (DecorationWords.Contains(text[p..k], StringComparer.OrdinalIgnoreCase))
                    {
                        sawPipe = true;
                        p = k;
                        continue;
                    }
                }
                break;
            }

            if (sawPipe)
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
            return p;
        }

        /// <summary>
        /// 逐个 invoke 块还原调用（一次输出里常见多条）：参数不出块；值结束于下一个 parameter 标签、
        /// 本块结束标签或下一个 invoke 头。模型把新参数写进 <c>&lt;/parameter name="x"&gt;</c> 时按新参数处理，
        /// 不让上一个参数把下一段内容连同标签残渣一起吞掉（实测 ReadFile 的 path 里带上 DSML 残渣）。
        /// </summary>
        internal static List<AgentToolCall> ParseInvokeBlocks(string text)
        {
            var calls = new List<AgentToolCall>();
            int search = 0;
            while (search < text.Length)
            {
                var head = InvokeHeadRegex.Match(text, search);
                if (!head.Success) break;

                string tool = AttrValue(head.Groups["attrs"].Value);
                var args = new Dictionary<string, object>(StringComparer.Ordinal);
                int cursor = head.Index + head.Length;
                int blockEnd = text.Length;
                while (cursor < text.Length)
                {
                    var boundary = InvokeBoundaryRegex.Match(text, cursor);
                    var param = ParamTagRegex.Match(text, cursor);
                    if (boundary.Success && (!param.Success || boundary.Index <= param.Index))
                    {
                        // 下一个 invoke 头从它的位置重新起算，否则会把 "<invoke" 自己跳过去、丢掉这条调用
                        blockEnd = boundary.Value.Contains('/') ? boundary.Index + boundary.Length : boundary.Index;
                        break;
                    }
                    if (!param.Success) break;                     // 块内没有更多参数

                    string key = AttrValue(param.Groups["attrs"].Value);
                    int valueStart = param.Index + param.Length;
                    if (string.IsNullOrEmpty(key)) { cursor = valueStart; continue; }   // 纯结束标签：只是边界

                    var nextParam = ParamTagRegex.Match(text, valueStart);
                    var nextBoundary = InvokeBoundaryRegex.Match(text, valueStart);
                    int valueEnd = text.Length;
                    if (nextParam.Success) valueEnd = nextParam.Index;
                    if (nextBoundary.Success && nextBoundary.Index < valueEnd) valueEnd = nextBoundary.Index;

                    args[key] = ConvertValue(text[valueStart..valueEnd].Trim());
                    cursor = valueEnd;
                }

                if (!string.IsNullOrEmpty(tool) && NormalizeWrappedArgs(args))
                    calls.Add(new AgentToolCall { Tool = tool, Args = args });

                search = blockEnd > head.Index ? blockEnd : head.Index + head.Length;
            }
            return calls;
        }

        /// <summary>标签属性表里的 name 值（双引号 / 单引号 / 裸值）。</summary>
        private static string AttrValue(string attrs)
        {
            if (string.IsNullOrEmpty(attrs)) return null;
            var m = NameAttrRegex.Match(attrs);
            if (!m.Success) return null;
            string value = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["bare"].Value;
            return value?.Trim();
        }
    }
}
