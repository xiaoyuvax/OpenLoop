using OpenLoop.Core.Tools;
using OpenLoop.Service.Config;
using OpenLoop.Service.OpenUI;

namespace OpenLoop.Service.Worker
{
    /// <summary>
    /// Worker 与 Loop 共用的两条 LoopApp 接线：文件边界（<see cref="Allowed"/>）与工具暴露。
    /// </summary>
    public static class WorkerScopeRules
    {
        /// <summary>
        /// 受保护目录 <c>.openloop</c> 里放行的例外（相对工作区根）。
        /// LoopApp 落在 <c>.openloop/tmp/LoopApps</c>，与 Loop 侧 <c>LoopScope</c> 同一口径。
        /// </summary>
        public static readonly string[] Allowed = { ".openloop/tmp" };

        /// <summary>
        /// 本轮要不要把 LoopApp 工具摆给 agent：有桌面壳、且工作区里装了 openui 技能。
        /// 两条都不满足时连工具名都不出现，免得模型对着一个必然失败的工具反复试。
        /// </summary>
        /// <param name="globalSkills">全局技能库根；省略时用当前设置的那份。</param>
        public static IEnumerable<ITool> LoopApp(string workspace, string sessionId, string globalSkills = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return [];
            if (LoopAppPaths.FindSkill(workspace, globalSkills ?? AppSettings.GlobalSkillsPath) == null) return [];
            return LoopAppTools.For(workspace, sessionId);
        }
    }
}
