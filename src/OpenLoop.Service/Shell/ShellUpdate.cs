using System.Diagnostics;
using System.Text.Json;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Source;

namespace OpenLoop.Service.Shell
{
    /// <summary>
    /// 壳自更新：新壳自己当 helper，不生成也不调用任何脚本。
    /// 旧壳启动「新壳 --apply-shell-update &lt;安装目录&gt; --apply-shell-pid &lt;pid&gt;」后退出；
    /// helper 等旧壳退出 → 备份将被覆盖的文件 → 复制自己这一份上去 → 启动 → 20 秒内没进界面就还原并起旧壳。
    /// 同版本重启走 <see cref="RestartFlag"/>：helper 是安装目录里的同一个 exe，只等旧壳退出再拉起，不复制文件。
    /// </summary>
    public static class ShellUpdate
    {
        public const string Flag = "--apply-shell-update";
        public const string RestartFlag = "--restart-shell";
        private const string PidFlag = "--apply-shell-pid";
        private const string WorkspaceFlag = "--apply-shell-workspace";
        private static readonly TimeSpan StartedTimeout = TimeSpan.FromSeconds(20);
        private const int RestartAttempts = 3;

        public static bool IsRequested(string[] args)
            => args != null && args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

        public static bool IsRestartRequested(string[] args)
            => args != null && args.Any(a => string.Equals(a, RestartFlag, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 同版本重启 helper：等旧壳退出后把新壳拉起来（不复制文件），进到界面才算成功；失败重试几次，全程写 restart 日志。
        /// 旧壳还活着就不启动第二个窗口。
        /// </summary>
        public static int Restart(string[] args)
        {
            string install = Arg(args, RestartFlag);
            if (string.IsNullOrWhiteSpace(install)) return 2;

            int pid = int.TryParse(Arg(args, PidFlag), out int value) ? value : 0;
            string workspace = Arg(args, WorkspaceFlag) ?? "";
            install = Path.GetFullPath(install).TrimEnd('\\', '/');

            if (!WaitExit(pid, TimeSpan.FromSeconds(300)))
            {
                Log("restart skipped: old shell still running " + DateTimeOffset.Now.ToString("O") + " pid=" + pid);
                return 4;
            }

            for (int attempt = 1; attempt <= RestartAttempts; attempt++)
            {
                TryDelete(IterationRunner.ShellStartedPath);
                try
                {
                    var started = Start(install, workspace, iteration: false);
                    if (WaitStarted(started, StartedTimeout))
                    {
                        Log("restart ok " + DateTimeOffset.Now.ToString("O") + " pid=" + started.Id + " attempt=" + attempt);
                        return 0;
                    }
                    Log("restart attempt failed " + DateTimeOffset.Now.ToString("O") + " pid=" + started.Id
                        + " code=" + ExitCode(started) + " attempt=" + attempt);
                }
                catch (Exception ex)
                {
                    Log("restart attempt failed " + DateTimeOffset.Now.ToString("O") + " " + ex.Message + " attempt=" + attempt);
                }
                Thread.Sleep(1000);
            }
            return 1;
        }

        /// <summary>同版本重启 helper 的启动参数：测试与 <see cref="IterationRunner.LaunchShellRestart"/> 共用这一份。</summary>
        internal static ProcessStartInfo RestartStart(string install, int pid, string workspace)
        {
            string exe = Path.Combine(install, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = install };
            start.ArgumentList.Add(RestartFlag);
            start.ArgumentList.Add(install);
            start.ArgumentList.Add(PidFlag);
            start.ArgumentList.Add(pid.ToString());
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                start.ArgumentList.Add(WorkspaceFlag);
                start.ArgumentList.Add(workspace.Trim());
            }
            return start;
        }

        /// <summary>helper 进程入口：处理完就退出；正常壳不会走到这里。</summary>
        public static int Run(string[] args)
        {
            string install = Arg(args, Flag);
            if (string.IsNullOrWhiteSpace(install)) return 2;

            int pid = int.TryParse(Arg(args, PidFlag), out int value) ? value : 0;
            string workspace = Arg(args, WorkspaceFlag) ?? "";
            string source = AppContext.BaseDirectory.TrimEnd('\\', '/');
            install = Path.GetFullPath(install).TrimEnd('\\', '/');
            string backup = Path.Combine(AppPaths.VersionsDir, "shell", "rollback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            if (!WaitExit(pid, TimeSpan.FromSeconds(300)))
            {
                // 旧壳还活着就覆盖安装目录只会撞文件锁，留下半新半旧的产物
                Log("old shell still running, skip update " + DateTimeOffset.Now.ToString("O") + " pid=" + pid);
                SkipUpdate(install, workspace, pid);
                return 4;
            }
            try
            {
                Copy(source, install, backup);
                TryDelete(IterationRunner.ShellStartedPath);
                var started = Start(install, workspace);
                if (WaitStarted(started, StartedTimeout))
                {
                    Log("restart ok " + DateTimeOffset.Now.ToString("O") + " pid=" + started.Id);
                    return 0;
                }

                Log("restart failed " + DateTimeOffset.Now.ToString("O") + " pid=" + started.Id + " code=" + ExitCode(started));
                Restore(backup, install);
                File.WriteAllText(IterationRunner.ShellFailedPath, "new shell failed to start");
                TryDelete(IterationRunner.ShellStartedPath);
                Start(install, workspace);
                return 1;
            }
            catch (Exception ex)
            {
                Log("restart failed " + DateTimeOffset.Now.ToString("O") + " " + ex.Message);
                if (!IsAlive(pid))
                {
                    // 复制只做了一半：先把备份还原回去，再拉起旧壳
                    try { Restore(backup, install); } catch { }
                    try { File.WriteAllText(IterationRunner.ShellFailedPath, "shell update failed: " + ex.Message); } catch { }
                    try { Start(install, workspace); } catch { }
                }
                return 3;
            }
        }

        /// <summary>
        /// 换壳被跳过（旧壳在等待窗口内没退出）：把已写盘的迭代报告标成失败、留 ShellFailedPath，
        /// 再等一小段——旧壳若随后退出就拉起安装目录里的旧版本壳兜底，避免「没人接手」。
        /// 0.1.55 事故：skip 分支静默返回，报告仍是 Ok=true，用户以为换版已生效（F-c8d5af6b）。
        /// </summary>
        private static void SkipUpdate(string install, string workspace, int pid)
        {
            try
            {
                var report = IterationRunner.TakeReport(TimeSpan.FromHours(1));
                if (report != null)
                {
                    report.Ok = false;
                    report.Steps.Add(new Iteration.Step("restart shell", false,
                        "旧壳（pid=" + pid + "）未在等待窗口内退出，换壳已跳过，仍是旧版本"));
                    IterationRunner.WriteReport(report);
                }
            }
            catch { }
            try { File.WriteAllText(IterationRunner.ShellFailedPath, "shell update skipped: old shell (pid=" + pid + ") still running"); } catch { }

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            while (IsAlive(pid) && DateTime.UtcNow < deadline) Thread.Sleep(1000);
            if (!IsAlive(pid))
            {
                try
                {
                    Log("old shell exited after skip, starting previous shell " + DateTimeOffset.Now.ToString("O"));
                    Start(install, workspace, iteration: false);
                }
                catch (Exception ex) { Log("start previous shell failed " + ex.Message); }
            }
        }

        /// <summary>把 source 里的文件覆盖到 install：被覆盖的旧文件先原样存进 backup（不镜像、不删除 install 里多余的文件）。</summary>
        internal static void Copy(string source, string install, string backup)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (Ignored(relative)) continue;

                string target = Path.Combine(install, relative);
                if (File.Exists(target))
                {
                    string saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    CopyWithRetry(target, saved);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyWithRetry(file, target);
            }
        }

        internal static void Restore(string backup, string install)
        {
            if (!Directory.Exists(backup)) return;
            foreach (string file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(install, Path.GetRelativePath(backup, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyWithRetry(file, target);
            }
        }

        /// <summary>旧壳退出时它的线程/句柄可能还没全释放，遇到文件锁就等一会儿再试。</summary>
        private static void CopyWithRetry(string from, string to)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                try
                {
                    File.Copy(from, to, true);
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(300);
                }
                catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(300);
                }
            }
        }

        private static bool Ignored(string relative)
            => string.Equals(relative, VersionManifest.FileName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(relative, "source-ref.txt", StringComparison.OrdinalIgnoreCase);

        private static Process Start(string install, string workspace, bool iteration = true)
        {
            string exe = Path.Combine(install, OperatingSystem.IsWindows() ? "OpenLoop.exe" : "OpenLoop");
            var start = new ProcessStartInfo(exe)
            {
                WorkingDirectory = install,
                UseShellExecute = false
            };
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                start.ArgumentList.Add("--loop");
                start.ArgumentList.Add(workspace.Trim());
                if (iteration) start.ArgumentList.Add("--iteration");
            }
            start.Environment["OPENLOOP_SOURCE"] = SourceSync.SourceRoot;
            return Process.Start(start);
        }

        /// <summary>新壳进到界面会写 started 标记；只看进程存活会把「起来后又崩」当成成功，标记里的 pid 也必须对上。</summary>
        private static bool WaitStarted(Process process, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
                if (StartedBy(process)) return true;
                try { if (process.HasExited) return false; } catch { return false; }
            }
            return StartedBy(process);
        }

        private static bool StartedBy(Process process)
        {
            try
            {
                if (!File.Exists(IterationRunner.ShellStartedPath)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllBytes(IterationRunner.ShellStartedPath));
                return doc.RootElement.TryGetProperty("pid", out var pid) && pid.GetInt32() == process.Id;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>等旧壳退出；超时还活着返回 false（调用方不能在这种情况下覆盖安装目录）。</summary>
        private static bool WaitExit(int pid, TimeSpan timeout)
        {
            if (pid <= 0) return true;
            var deadline = DateTime.UtcNow + timeout;
            while (IsAlive(pid))
            {
                if (DateTime.UtcNow >= deadline) return false;
                Thread.Sleep(300);
            }
            return true;
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try { return Process.GetProcessById(pid) != null; }
            catch { return false; }
        }

        private static int ExitCode(Process process)
        {
            try { return process.HasExited ? process.ExitCode : -1; } catch { return -1; }
        }

        private static void Log(string line)
        {
            try { File.AppendAllText(IterationRunner.RestartLogPath, line + Environment.NewLine); } catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < (args?.Length ?? 0) - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
