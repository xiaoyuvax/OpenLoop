using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;

namespace OpenLoop.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // 壳自更新 helper：新壳把自己这份覆盖到安装目录，成功/失败都不用脚本
            if (ShellUpdate.IsRequested(args))
            {
                ShellUpdate.Run(args);
                return;
            }

            ApplicationConfiguration.Initialize();
            Startup.Parse(args);

            var startup = AppSettings.Load();
            Localization.Apply(startup.Language);
            UiStyle.Apply(UiStyle.Parse(startup.Theme));

            Application.ThreadException += (_, e) => AppLog.Error("未处理的界面异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException +=
                (_, e) => AppLog.Error("未处理的异常", e.ExceptionObject as Exception);

            AppLog.Info("OpenLoop 启动");

            // 壳只负责拉起 A（Worker）。界面是它的客户端，不再在自己的进程里跑 Agent。
            var store = new VersionStore();
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
