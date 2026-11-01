namespace OpenLoop.Service.Runtime
{
    /// <summary>
    /// 用户级路径。运行态（runtime/、versions/）不进仓库，也不进工作区。
    /// Unix 上 SpecialFolder.ApplicationData 映射到 XDG 配置目录。
    /// </summary>
    public static class AppPaths
    {
        public static string DataRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenLoop");

        public static string RuntimeDir => Path.Combine(DataRoot, "runtime");
        public static string VersionsDir => Path.Combine(DataRoot, "versions");
        public static string BusKeyPath => Path.Combine(DataRoot, "bus.key");

        /// <summary>静态界面：与可执行文件同目录的 wwwroot（A/I 各自带一份）。可用环境变量覆盖。</summary>
        public static string WwwRoot { get; } = ResolveWwwRoot();

        private static string ResolveWwwRoot()
        {
            string custom = Environment.GetEnvironmentVariable("OPENLOOP_WWWROOT");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(AppContext.BaseDirectory, "wwwroot")
                : Path.GetFullPath(custom);
        }

        public static string RuntimeFile(string role) => Path.Combine(RuntimeDir, role + ".json");

        internal static void EnsureDirectory(string path) => Directory.CreateDirectory(path);
    }
}
