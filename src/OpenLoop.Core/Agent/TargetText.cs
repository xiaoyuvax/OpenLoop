namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// TCF 目标面板的行号编辑（纯函数，面板与单测共用）：行号 1-based 闭区间，toLine=0 表示到末尾。
    /// 越界直接失败、不静默钳制；缓冲与入参的行分隔符统一成 \n。
    /// </summary>
    public static class TargetText
    {
        /// <summary>TargetRead 单页字符上限（翻页粒度；远低于回填模型的工具结果上限）。</summary>
        public const int PageChars = 4000;

        /// <summary>替换 [fromLine, toLine]；fromLine=toLine+1 即插入，text="" 即删除。</summary>
        public static (bool Ok, string Text, string Error) Replace(string buffer, int fromLine, int toLine, string text)
        {
            string[] lines = (buffer ?? "").Split('\n');
            int total = lines.Length;
            bool insert = fromLine == toLine + 1;
            int end;
            if (insert)
            {
                if (fromLine < 1 || fromLine > total + 1)
                    return (false, null, $"fromLine 越界：{fromLine}（插入位置 1..{total + 1}）");
                end = fromLine - 1;
            }
            else
            {
                end = toLine <= 0 ? total : toLine;
                if (fromLine < 1 || fromLine > total)
                    return (false, null, $"fromLine 越界：{fromLine}（1..{total}）");
                if (end > total) return (false, null, $"toLine 越界：{toLine}（0..{total}）");
                if (end < fromLine)
                    return (false, null, $"区间为空：fromLine={fromLine} > toLine={toLine}（插入请用 fromLine=toLine+1）");
            }

            var replacement = Block(text);
            var merged = new List<string>(total + replacement.Count);
            merged.AddRange(lines.Take(fromLine - 1));
            merged.AddRange(replacement);
            merged.AddRange(lines.Skip(end));
            return (true, string.Join("\n", merged), "");
        }

        /// <summary>从 fromLine 起读一页；next=0 表示已读完。</summary>
        public static (string Text, int From, int To, int Next, int Total) Page(string buffer, int fromLine, int toLine)
        {
            string[] lines = (buffer ?? "").Split('\n');
            int start = fromLine < 1 ? 1 : fromLine;
            if (start > lines.Length) return ("", start, 0, 0, lines.Length);

            int end = toLine <= 0 ? lines.Length : Math.Min(toLine, lines.Length);
            var sb = new System.Text.StringBuilder();
            int i = start - 1;
            for (; i < end; i++)
            {
                int add = lines[i].Length + 1;
                if (i > start - 1 && sb.Length + add > PageChars) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return (sb.ToString(), start, i, i < lines.Length ? i + 1 : 0, lines.Length);
        }

        /// <summary>入参按行块给：末尾换行是行块分隔符，不额外产生空行。</summary>
        private static List<string> Block(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.EndsWith('\n')) normalized = normalized[..^1];
            result.AddRange(normalized.Split('\n'));
            return result;
        }
    }
}
