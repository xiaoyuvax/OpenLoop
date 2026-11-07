namespace OpenLoop.App
{
    /// <summary>
    /// 壳重启后要回到「刚才那一页」：重启脚本用 --loop &lt;工作区&gt; --iteration 拉起，
    /// 启动时据此打开 Loop 会话并把迭代报告弹出来。
    /// </summary>
    internal static class Startup
    {
        public static string Loop { get; private set; } = "";
        public static bool Iteration { get; private set; }

        public static void Parse(string[] args)
        {
            Loop = "";
            Iteration = false;
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                switch (args[i])
                {
                    case "--loop" when i + 1 < args.Length:
                        Loop = args[++i];
                        break;
                    case "--iteration":
                        Iteration = true;
                        break;
                }
            }
        }
    }
}
