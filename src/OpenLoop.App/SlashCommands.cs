using OpenLoop.Core.Skills;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>
    /// 把当前工作区启用中的技能当作「/技能名」命令：列表里能唤出，发送时展开成让 Worker
    /// 先读技能再执行的任务。技能正文仍由 ReadSkill 按需读取，不在每个任务里塞全文。
    /// </summary>
    internal static class SlashCommands
    {
        internal static async Task<IReadOnlyList<(string Command, string Description)>> SkillsAsync(
            LoopApiClient loop, string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return [];
            try
            {
                var catalog = await Task.Run(() => SkillCatalog.Load(workspace, AppSettings.GlobalSkillsPath));
                var payload = await loop.GetActivationAsync(workspace);
                var enabled = new HashSet<string>(payload?.Skills ?? [], StringComparer.OrdinalIgnoreCase);
                return catalog
                    .Where(s => enabled.Contains(s.Name))
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(s => ("/" + s.Name, "技能：" + s.Description))
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取技能命令失败：" + ex.Message);
                return [];
            }
        }

        /// <summary>「/技能名 请求…」→ 发给 Worker 的任务；不是技能命令时返回 null。</summary>
        internal static string SkillTask(
            string text, IReadOnlyList<(string Command, string Description)> skills)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '/' || skills.Count == 0) return null;

            int end = text.IndexOfAny([' ', '\t', '\n'], 1);
            string name = (end < 0 ? text[1..] : text[1..end]).Trim();
            if (name.Length == 0) return null;
            bool known = skills.Any(s =>
                string.Equals(s.Command, "/" + name, StringComparison.OrdinalIgnoreCase));
            if (!known) return null;

            string request = end < 0 ? "" : text[(end + 1)..].Trim();
            string lead = $"使用技能「{name}」：先调用 ReadSkill 读取该技能的做法全文，再按技能执行。";
            return request.Length == 0 ? lead : lead + "\n\n" + request;
        }
    }
}
