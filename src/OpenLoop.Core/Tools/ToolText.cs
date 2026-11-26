using System.Text;

namespace OpenLoop.Core.Tools
{
    public static class ToolText
    {
        /// <summary>回填模型的工具结果默认上限，对齐 OpenCode 的 tool_output（2000 行 / 51200 字节）。</summary>
        public const int ModelLines = 2000;
        public const int ModelBytes = 51200;

        public static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text[..max] + $"…（截断，共 {text.Length} 字符）";
        }

        /// <summary>
        /// 超长时保留头尾两段、中间标出省略量：命令/测试输出的结论通常在尾部，只留开头会把结论丢掉。
        /// </summary>
        public static string HeadTail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return "";
            if (text.Length <= maxChars) return text;
            int head = maxChars / 2;
            int tail = maxChars - head;
            if (head > 0 && char.IsHighSurrogate(text[head - 1])) head--;
            if (tail > 0 && char.IsLowSurrogate(text[^tail])) tail++;
            return text[..head] + $"…（省略 {text.Length - maxChars} 字符）…" + text[^tail..];
        }

        /// <summary>按行数与 UTF-8 字节上限截断（先到先截），末尾标出原始尺寸。</summary>
        public static string Clip(string text, int maxBytes = ModelBytes, int maxLines = ModelLines)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (maxBytes <= 0) maxBytes = ModelBytes;
            if (maxLines <= 0) maxLines = ModelLines;

            string[] lines = text.Split('\n');
            int totalBytes = Encoding.UTF8.GetByteCount(text);
            int bytes = 0;
            int keep = 0;
            for (; keep < lines.Length && keep < maxLines; keep++)
            {
                int add = Encoding.UTF8.GetByteCount(lines[keep]) + (keep > 0 ? 1 : 0);
                if (keep > 0 && bytes + add > maxBytes) break;
                bytes += add;
            }
            if (keep == lines.Length && totalBytes <= maxBytes) return text;

            string head = keep == lines.Length ? text : string.Join("\n", lines.Take(keep));
            if (Encoding.UTF8.GetByteCount(head) > maxBytes) head = CutBytes(head, maxBytes);
            return head + $"…（截断，共 {lines.Length} 行 / {totalBytes} 字节）";
        }

        private static string CutBytes(string text, int maxBytes)
        {
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Encoding.UTF8.GetByteCount(text.AsSpan(0, mid)) > maxBytes) hi = mid - 1;
                else lo = mid;
            }
            if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
            return text[..lo];
        }
    }
}
