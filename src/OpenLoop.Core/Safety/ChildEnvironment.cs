namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// 子进程环境变量白名单：密钥等敏感值绝不进入子进程。
    /// </summary>
    public static class ChildEnvironment
    {
        private static readonly string[] Allowed =
        [
            "PATH", "PATHEXT", "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR", "COMSPEC",
            "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
            "APPDATA", "LOCALAPPDATA", "PROGRAMDATA", "PROGRAMFILES", "PROGRAMFILES(X86)",
            "LANG", "LC_ALL", "LC_CTYPE", "TZ", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE",
            "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "MSYSTEM"
        ];

        public static IReadOnlyDictionary<string, string> Build()
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Allowed)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) env[name] = value;
            }
            return env;
        }

        public static void Apply(System.Diagnostics.ProcessStartInfo psi)
        {
            psi.Environment.Clear();
            foreach (var (key, value) in Build()) psi.Environment[key] = value;
        }

        /// <summary>诊断：环境变量里是否还有疑似密钥（测试用）。</summary>
        public static bool LooksSecret(string name)
        {
            string n = name.ToUpperInvariant();
            return n.Contains("KEY") || n.Contains("TOKEN") || n.Contains("SECRET") || n.Contains("PASSWORD");
        }
    }
}
