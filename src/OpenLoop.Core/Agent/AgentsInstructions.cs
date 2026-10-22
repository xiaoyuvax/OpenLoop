using System.Text;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 读取工作区的 AGENTS.md（大小写不敏感地找 AGENTS.md / agents.md），
    /// 作为系统提示词的“项目约定”段落注入。没有文件时返回 null。
    /// </summary>
    public static class AgentsInstructions
    {
        public const int MaxChars = 32000;

        public static string Load(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;

            foreach (string name in new[] { "AGENTS.md", "agents.md", "Agents.md" })
            {
                string path = Path.Combine(workspace, name);
                if (!File.Exists(path)) continue;

                try
                {
                    string text = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (text.Length == 0) return null;
                    if (text.Length > MaxChars)
                        text = text[..MaxChars] + "\n\n（AGENTS.md 过长，已截断）";
                    return text;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
