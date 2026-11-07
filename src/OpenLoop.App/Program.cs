using OpenLoop.Service.Bus;
using OpenLoop.Service.Shell;

namespace OpenLoop.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
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
            try
            {
                string exe = Environment.GetEnvironmentVariable("OPENLOOP_A_PATH");
                if (string.IsNullOrWhiteSpace(exe)) exe = store.Exe(BusRoles.A);
                if (string.IsNullOrWhiteSpace(exe))
                    exe = Path.Combine(AppContext.BaseDirectory, "worker", VersionStore.ExeName(BusRoles.A));
                if (supervisor.Ensure(BusRoles.A, exe) == null)
                    AppLog.Error("Worker 未就绪，任务将无法执行：" + exe);

                string loopExe = Environment.GetEnvironmentVariable("OPENLOOP_I_PATH");
                if (string.IsNullOrWhiteSpace(loopExe)) loopExe = store.Exe(BusRoles.I);
                if (string.IsNullOrWhiteSpace(loopExe))
                    loopExe = Path.Combine(AppContext.BaseDirectory, "worker", VersionStore.ExeName(BusRoles.I));
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
