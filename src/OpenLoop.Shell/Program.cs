using OpenLoop.Service.Bus;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Shell;

namespace OpenLoop.Shell
{
    /// <summary>
    /// 启动壳（非 Windows 形态）：拉起 I 与 A，等退出信号，退出时收摊。
    /// Windows 形态由 OpenLoop.App 提供同一套 Supervisor，只是多了 WinForms 界面。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 更新过的版本记在 current.json：先看它，再看随包 worker/（与 Windows 壳一致）
            var store = new VersionStore();
            string aPath = Resolve(args, "--a", "OPENLOOP_A_PATH", BusRoles.A, store);
            string iPath = Resolve(args, "--i", "OPENLOOP_I_PATH", BusRoles.I, store);            if (aPath == null || iPath == null)
            {
                Console.Error.WriteLine(
                    "找不到 A/I 可执行文件。用 --a/--i 或 OPENLOOP_A_PATH/OPENLOOP_I_PATH 指定路径；" +
                    "默认在壳所在目录及其 worker/ 下找 " + ExeName(BusRoles.A) + " 与 " + ExeName(BusRoles.I) + "。");
                return 2;
            }

            using var supervisor = new Supervisor();
            var infoI = supervisor.Ensure(BusRoles.I, iPath);
            var infoA = supervisor.Ensure(BusRoles.A, aPath);

            if (infoA != null && infoI != null)
            {
                Console.WriteLine($"[OpenLoop] A {infoA.Bus} · I {infoI.Bus}");
                // 浏览器入口：?key= 换 Cookie（Web 界面与 bus 用同一把 key）。
                string key = KeyFile.ReadOrCreate(AppPaths.BusKeyPath);
                Console.WriteLine($"[OpenLoop] 界面 http://{infoA.Bus}/?key={key}");
                Console.WriteLine("[OpenLoop] Ctrl+C / SIGTERM 退出");
            }
            else
            {
                Console.WriteLine("[OpenLoop] 有进程未就绪，看上面的日志");
            }

            var stop = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => supervisor.StopAll();
            using var sigterm = OperatingSystem.IsWindows()
                ? null
                : System.Runtime.InteropServices.PosixSignalRegistration.Create(
                    System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
                    {
                        context.Cancel = true;
                        stop.Set();
                    });

            stop.Wait();
            return 0;
        }

        private static string Resolve(string[] args, string argName, string variable, string role, VersionStore store)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i + 1]);
            }

            string fromEnv = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv);

            string current = store?.Exe(role);
            if (!string.IsNullOrWhiteSpace(current)) return Path.GetFullPath(current);

            // 与 Windows 壳一致的布局：A/I 在 worker/ 子目录（各自带依赖），也可与壳并排。
            foreach (string dir in new[] { Path.Combine(AppContext.BaseDirectory, "worker"), AppContext.BaseDirectory })
            {
                string candidate = Path.Combine(dir, ExeName(role));
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string ExeName(string role)
        {
            string name = "OpenLoop." + role.ToUpperInvariant();
            return OperatingSystem.IsWindows() ? name + ".exe" : name;
        }
    }
}
