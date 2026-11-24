using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;

namespace OpenLoop.App
{
    internal static class Program
    {
        /// <summary>壳异常退出前的兜底保存；MainForm 建好后挂上来（没挂就是空操作）。</summary>
        internal static Action EmergencySave { get; set; }

        [STAThread]
        private static void Main(string[] args)
        {
            // 壳自更新 helper：新壳把自己这份覆盖到安装目录，成功/失败都不用脚本
            if (ShellUpdate.IsRequested(args))
            {
                ShellUpdate.Run(args);
                return;
            }
            // 同版本重启 helper：等旧壳退出再把安装目录里的壳拉起来
            if (ShellUpdate.IsRestartRequested(args))
            {
                ShellUpdate.Restart(args);
                return;
            }

            ApplicationConfiguration.Initialize();
            Startup.Parse(args);

            var startup = AppSettings.Load();
            Localization.Apply(startup.Language);
            UiStyle.Apply(UiStyle.Parse(startup.Theme));

            Application.ThreadException += (_, e) =>
            {
                // 先把界面里跑着的会话落一次盘：A 只保 Worker 的历史，Loop 的正文没有别人替它保存
                try { EmergencySave?.Invoke(); } catch { }
                AppLog.Error("未处理的界面异常", e.Exception);
                ExitTrace.Write("未处理的界面异常：" + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                AppLog.Error("未处理的异常", e.ExceptionObject as Exception);
                ExitTrace.Write("未处理的异常（进程即将退出）：" + e.ExceptionObject);
            };
            // 正常退出（关窗 / Application.Exit）与「被外部杀掉」在这里分开记：后者不会留下任何一行
            Application.ApplicationExit += (_, _) => ExitTrace.Write("退出：ApplicationExit（正常退出流程）");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ExitTrace.Write("退出：ProcessExit");

            AppLog.Info("OpenLoop 启动");
            ExitTrace.Write("启动：" + Environment.ProcessPath + " " + string.Join(" ", args));

            // 壳只负责拉起 A（Worker）。界面是它的客户端，不再在自己的进程里跑 Agent。
            var store = new VersionStore();
            // 只换壳的交接：公开本进程 pid，留在旧壳下的 A/I 认领后改守护这里，重启前后的会话不断
            ShellHandoff.PublishShell();
            using var supervisor = new Supervisor(message => AppLog.Info(message), store);
            supervisor.PruneArchives();
            try
            {
                string exe = Environment.GetEnvironmentVariable("OPENLOOP_A_PATH");
                if (string.IsNullOrWhiteSpace(exe)) exe = InstalledExe(store, BusRoles.A);
                if (supervisor.Ensure(BusRoles.A, exe) == null)
                    AppLog.Error("Worker 未就绪，任务将无法执行：" + exe);

                string loopExe = Environment.GetEnvironmentVariable("OPENLOOP_I_PATH");
                if (string.IsNullOrWhiteSpace(loopExe)) loopExe = InstalledExe(store, BusRoles.I);
                if (supervisor.Ensure(BusRoles.I, loopExe) == null)
                    AppLog.Error("Loop 未就绪，演进/回滚将不可用：" + loopExe);
            }
            catch (Exception ex)
            {
                AppLog.Error("启动 Worker 失败", ex);
            }

            using var form = new MainForm();
            Application.Run(form);
            ExitTrace.Write("退出：Application.Run 返回");
            AppLog.Info("OpenLoop 退出");
        }

        /// <summary>
        /// A/I 一律从安装目录的 <c>backends\</c> 启动（与壳同处安装目录）；<c>versions\</c> 只是版本存档与回退源。
        /// 只有 backends 缺文件（旧布局或裁剪安装）时才退回登记版本，免得整机起不来。
        /// </summary>
        private static string InstalledExe(VersionStore store, string role)
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "backends", VersionStore.ExeName(role));
            return File.Exists(exe) ? exe : store.Exe(role) ?? exe;
        }
        private static void Dump(string kind, Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenLoop");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:O}] {kind}\n{ex}\n\n");
            }
            catch { }
        }    }
}
