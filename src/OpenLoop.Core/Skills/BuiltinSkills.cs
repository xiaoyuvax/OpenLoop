using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenLoop.Core.Skills
{
    /// <summary>
    /// 内置技能：不落盘、不进技能库与设置页，只在环境满足时由宿主拼进激活技能。
    /// </summary>
    public static class BuiltinSkills
    {
        public const string ZCurlName = "zcurl";

        private const string ZCurlBody = """
# 用 zcurl 代替 curl

Windows 中文环境下发 HTTP 请求一律用 zcurl，不要用 curl：curl 在 PowerShell 里的中文载荷、引号与编码会被破坏。

- 项目：https://github.com/xiaoyuvax/zcurl
- 用法：`zcurl <METHOD> <URL>`；请求体走 stdin（bash: `printf '%s' "$J" | zcurl POST <url>`）或 `--body-env NAME`（PowerShell：`$env:BODY='...'; zcurl POST <url> --body-env BODY`）
- argv 只收 ASCII：中文载荷必须走 stdin / --body-env；中文 URL 参数先用 `zcurl enc` 编码
- 响应字节原样写 stdout；落盘用 `--out FILE`；退出码 0=2xx、1=HTTP/网络失败、2=用法错误
- `zcurl -h` 看全部参数；`zcurl selftest` 自检
- Puppet 端点：`zcurl registry|manifest|get|set|invoke|action <实例> ...`；`action` 的 body 是参数对象本身
- 不支持 multipart / 文件上传 / 代理，这些场景才回退 curl
""";

        /// <summary>pathVariable / uiCulture 只用于测试覆盖，宿主不传。</summary>
        public static IReadOnlyList<SkillInfo> For(string pathVariable = null, CultureInfo uiCulture = null)
        {
            if (!OperatingSystem.IsWindows()) return [];
            uiCulture ??= CultureInfo.CurrentUICulture;
            if (!string.Equals(uiCulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
                return [];
            if (!OnPath(pathVariable ?? Environment.GetEnvironmentVariable("PATH"), ZCurlName)) return [];

            return
            [
                new SkillInfo
                {
                    Name = ZCurlName,
                    Description = "Windows 中文环境下用 zcurl 代替 curl 发 HTTP 请求（中文载荷、引号与编码已处理）。",
                    Body = ZCurlBody,
                    Source = "builtin/zcurl",
                    Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ZCurlBody))).ToLowerInvariant()
                }
            ];
        }

        /// <summary>把当前环境适用的内置技能并进激活技能；同名技能已存在时保留用户那份。</summary>
        public static IReadOnlyList<SkillInfo> Merge(IReadOnlyList<SkillInfo> skills)
        {
            var builtins = For();
            if (builtins.Count == 0) return skills ?? [];
            var names = new HashSet<string>(
                (skills ?? []).Select(s => s?.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            return (skills ?? []).Concat(builtins.Where(b => names.Add(b.Name))).ToList();
        }

        // shortcut: 只认 PATH 里的 zcurl.exe，不解析 PATHEXT 与 App Paths，够用即可
        private static bool OnPath(string pathVariable, string fileName)
        {
            foreach (string raw in (pathVariable ?? "").Split(Path.PathSeparator))
            {
                string dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                try
                {
                    if (File.Exists(Path.Combine(dir, fileName + ".exe"))) return true;
                }
                catch
                {
                    // 非法路径（含通配符等）跳过
                }
            }
            return false;
        }
    }
}
