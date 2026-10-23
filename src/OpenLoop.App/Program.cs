namespace OpenLoop.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();

            var startup = AppSettings.Load();
            Localization.Apply(startup.Language);
            UiStyle.Apply(UiStyle.Parse(startup.Theme));

            Application.ThreadException += (_, e) => AppLog.Error("未处理的界面异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException +=
                (_, e) => AppLog.Error("未处理的异常", e.ExceptionObject as Exception);

            AppLog.Info("OpenLoop 启动");
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
