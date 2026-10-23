using System.Text;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 读取约定注入：用户级全局文件（可选）+ 工作区 AGENTS.md（大小写不敏感）。
    /// 顺序固定为全局在前、项目在后；合并后仍受 MaxChars 上限。都没有时返回 null。
    /// </summary>
    public static class AgentsInstructions
    {
        public const int MaxChars = 32000;

        public static string Load(string workspace, string globalPath = null)
        {
            string global = ReadFile(globalPath);
            string project = ReadWorkspaceFile(workspace);

            string text = global != null && project != null
                ? "## 全局约定\n" + global + "\n\n## 项目约定\n" + project
                : global ?? project;

            if (string.IsNullOrWhiteSpace(text)) return null;
            if (text.Length > MaxChars)
                text = text[..MaxChars] + "\n\n（AGENTS.md 过长，已截断）";
            return text;
        }

        private static string ReadWorkspaceFile(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;

            foreach (string name in new[] { "AGENTS.md", "agents.md", "Agents.md" })
            {
                string path = Path.Combine(workspace, name);
                if (File.Exists(path)) return ReadFile(path);
            }
            return null;
        }

        private static string ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }
    }
}
