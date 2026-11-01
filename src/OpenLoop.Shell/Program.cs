using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;

namespace OpenLoop.Shell
{
    /// <summary>
    /// 启动壳（Linux 形态）：拉起 I 与 A，等退出信号，退出时收摊。
    /// Windows 形态由 OpenLoop.Win 提供同一套 Supervisor，只是多了 WinForms 界面（M1）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string aPath = Resolve(args, "--a", "OPENLOOP_A_PATH", BusRoles.A);
            string iPath = Resolve(args, "--i", "OPENLOOP_I_PATH", BusRoles.I);
            if (aPath == null || iPath == null)
            {
                Console.Error.WriteLine(
                    "找不到 A/I 可执行文件。用 --a/--i 或 OPENLOOP_A_PATH/OPENLOOP_I_PATH 指定路径；" +
                    "默认在壳所在目录找 " + ExeName(BusRoles.A) + " 与 " + ExeName(BusRoles.I) + "。");
                return 2;
            }

            using var supervisor = new Supervisor();
            var infoI = supervisor.Ensure(BusRoles.I, iPath);
            var infoA = supervisor.Ensure(BusRoles.A, aPath);

            Console.WriteLine(infoA != null && infoI != null
                ? $"[OpenLoop] A {infoA.Bus} · I {infoI.Bus} · Ctrl+C 退出"
                : "[OpenLoop] 有进程未就绪，看上面的日志");

            var stop = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => supervisor.StopAll();

            stop.Wait();
            return 0;
        }

        private static string Resolve(string[] args, string argName, string variable, string role)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i + 1]);
            }

            string fromEnv = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv);

            string sibling = Path.Combine(AppContext.BaseDirectory, ExeName(role));
            return File.Exists(sibling) ? sibling : null;
        }

        private static string ExeName(string role)
        {
            string name = "OpenLoop." + role.ToUpperInvariant();
            return OperatingSystem.IsWindows() ? name + ".exe" : name;
        }
    }
}
