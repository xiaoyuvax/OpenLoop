using System.Text.RegularExpressions;

namespace OpenLoop.Service.Models
{
    /// <summary>
    /// 内置上下文表：服务商的 /models 不报上下文（例如 TeamRouter 只给 id/owned_by），
    /// 这些模型靠这张表回填；设置里的逐模型覆盖优先于它。
    /// 只收已确认的数值——填错会让压缩阈值失效，宁可留空回退默认值。
    /// </summary>
    public static partial class KnownContext
    {
        private static readonly Dictionary<string, int> Exact = new(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek-flash-1017"] = 1_000_000,
        };

        /// <summary>按模型 id 查内置表；provider 前缀（ofm/）与 @provider 后缀先剥掉。</summary>
        public static int Lookup(string model)
        {
            string id = Normalize(model);
            if (id.Length == 0) return 0;
            if (Exact.TryGetValue(id, out int exact)) return exact;

            // id 自带窗口标记（kimi-k3[1M]、xxx[128K]），以标记为准。
            var match = SizeTag().Match(id);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
            {
                int scale = match.Groups[2].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? 1_000_000 : 1024;
                return value * scale;
            }
            return 0;
        }

        private static string Normalize(string model)
        {
            string id = model?.Trim() ?? "";
            int at = id.LastIndexOf('@');
            if (at > 0) id = id[..at];
            int slash = id.LastIndexOf('/');
            if (slash >= 0) id = id[(slash + 1)..];
            return id;
        }

        [GeneratedRegex(@"\[(\d+)([km])\]", RegexOptions.IgnoreCase)]
        private static partial Regex SizeTag();
    }
}
