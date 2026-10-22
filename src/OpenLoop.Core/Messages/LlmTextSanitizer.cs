using System.Text.RegularExpressions;

namespace OpenLoop.Core.Messages
{
    /// <summary>
    /// 清洗模型输出中的供应商元数据：部分 OpenRouter 上游会在正文末尾追加
    /// “User Safety: safe / Response Safety: safe” 这类安全判定行。
    /// </summary>
    public static class LlmTextSanitizer
    {
        private static readonly Regex SafetyLine = new(
            @"^\s*(user|response)\s*safety\s*:\s*\S+\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string StripSafetyLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>(lines.Length);
            foreach (string line in lines)
                if (!SafetyLine.IsMatch(line)) kept.Add(line);

            return kept.Count == lines.Length
                ? text
                : string.Join("\n", kept).Trim();
        }
    }
}
