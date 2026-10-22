namespace OpenLoop.Core.Tools
{
    internal static class ToolText
    {
        public static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text[..max] + $"…（截断，共 {text.Length} 字符）";
        }
    }
}
