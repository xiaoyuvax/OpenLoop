using System.Diagnostics;
using OpenLoop.Service.Runtime;
using OpenLoop.Service.Source;

namespace OpenLoop.Service.Shell
{
    /// <summary>
    /// 壳自更新：新壳自己当 helper，不生成也不调用任何脚本。
    /// 旧壳启动「新壳 --apply-shell-update &lt;安装目录&gt; --apply-shell-pid &lt;pid&gt;」后退出；
    /// helper 等旧壳退出 → 备份将被覆盖的文件 → 复制自己这一份上去 → 启动 → 8 秒内没进界面就还原并起旧壳。
    /// </summary>
    public static class ShellUpdate
    {
        public const string Flag = "--apply-shell-update";
        private const string PidFlag = "--apply-shell-pid";
        private const string WorkspaceFlag = "--apply-shell-workspace";

        public static bool IsRequested(string[] args)
            => args != null && args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

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

            WaitExit(pid, TimeSpan.FromSeconds(60));
            try
            {
                Copy(source, install, backup);
                TryDelete(IterationRunner.ShellStartedPath);
                var started = Start(install, workspace);
                if (WaitStarted(started, TimeSpan.FromSeconds(8)))
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
                return 3;
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

        private static Process Start(string install, string workspace)
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
                start.ArgumentList.Add("--iteration");
            }
            start.Environment["OPENLOOP_SOURCE"] = SourceSync.SourceRoot;
            return Process.Start(start);
        }

        /// <summary>新壳进到界面会写 started 标记；只看进程存活会把「起来后又崩」当成成功。</summary>
        private static bool WaitStarted(Process process, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
                if (File.Exists(IterationRunner.ShellStartedPath)) return true;
                try { if (process.HasExited) return false; } catch { return false; }
            }
            return File.Exists(IterationRunner.ShellStartedPath);
        }

        private static void WaitExit(int pid, TimeSpan timeout)
        {
            if (pid <= 0) return;
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (Process.GetProcessById(pid) == null) return;
                }
                catch
                {
                    return;
                }
                Thread.Sleep(300);
            }
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
