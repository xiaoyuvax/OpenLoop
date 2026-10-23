using System.Globalization;
using System.Resources;

namespace OpenLoop.App
{
    /// <summary>卫星程序集文案：Strings.resx（中性=中文）+ Strings.en.resx（英文）。</summary>
    internal static class Localization
    {
        private static readonly ResourceManager Rm =
            new("OpenLoop.App.Resources.Strings", typeof(Localization).Assembly);

        public static string T(string key)
        {
            try { return Rm.GetString(key) ?? key; }
            catch { return key; }
        }

        private static string SystemLanguage()
            => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "en" ? "en" : "zh";

        public static bool IsEnglish =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

        private static readonly (string Zh, string En)[] Phrases =
        [
            ("请选择工作目录", "Pick a working directory"),
            ("工作目录不存在：", "Working directory does not exist: "),
            ("缺少配置", "Missing configuration"),
            ("请先在设置里填写接口地址、模型。", "Set the base URL and model in Settings first."),
            ("读取账本失败", "Failed to read the ledger"),
            ("创建 trace 失败", "Failed to create trace"),
            ("任务完成", "Task finished"),
            ("预算用尽", "Budget exhausted"),
            ("本次执行到达步数上限。", "Step limit reached."),
            ("执行失败", "Run failed"),
            ("没有考核题", "No eval tasks"),
            ("先把考核题放进 ", "Put eval tasks into "),
            ("候选失败", "Proposal failed"),
            ("候选生成失败", "Proposal failed"),
            ("演进失败", "Evolve failed"),
            ("没有更早的版本", "No earlier version"),
            ("当前已是第一个版本。", "This is already the first version."),
            ("已回滚", "Rolled back"),
            ("当前版本 v", "Current version v"),
            ("回滚失败", "Rollback failed"),
            ("设置已保存", "Settings saved"),
            ("已写入本地。", "Saved locally."),
            ("保存失败", "Save failed"),
            ("模型目录已刷新", "Model catalog refreshed"),
            ("项目不可用", "Project unavailable"),
            ("工作区目录不存在：", "Workspace does not exist: "),
            ("Loop 执行失败", "Loop run failed"),
            ("评审 ", "Reviewing "),
            ("已采纳 v", "Adopted v"),
            ("保持旧版", "Kept previous version")
        ];

        /// <summary>把历史遗留的中文通知/状态前缀翻成当前语言（无匹配时原样返回）。</summary>
        public static string Translate(string text)
        {
            if (!IsEnglish || string.IsNullOrEmpty(text)) return text;
            foreach (var (zh, en) in Phrases)
                if (text.StartsWith(zh, StringComparison.Ordinal))
                    return en + text[zh.Length..];
            return text;
        }

        public static void Apply(string language)
        {
            string effective = string.IsNullOrWhiteSpace(language)
                ? SystemLanguage()
                : language;
            var culture = effective.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? new CultureInfo("en")
                : new CultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
    }
}
