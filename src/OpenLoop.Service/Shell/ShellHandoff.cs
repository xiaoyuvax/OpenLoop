using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenLoop.Service.Runtime;

namespace OpenLoop.Service.Shell
{
    /// <summary>
    /// 只换壳的交接：旧壳退出前写下交接标记，A/I 据此不随父进程退出，等新壳接手；
    /// 新壳把自己的 pid 写进 <c>runtime/shell.json</c>，A/I 认领后重新守护它。
    /// 标记过期（默认 3 分钟）还没人接手，A/I 照旧退出——不留无主的孤儿进程。
    /// </summary>
    public static class ShellHandoff
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

        private static string MarkerPath => Path.Combine(AppPaths.RuntimeDir, "shell-handoff.json");
        private static string ShellPath => Path.Combine(AppPaths.RuntimeDir, "shell.json");

        /// <summary>旧壳即将退出（helper 已起来）：宣布接下来由新壳接手 A/I。</summary>
        public static void Begin()
            => Write(MarkerPath, new Record { Pid = Environment.ProcessId, Expires = DateTimeOffset.Now + Window });

        /// <summary>壳启动：公开自己的 pid，供脱离旧壳的 A/I 认领。</summary>
        public static void PublishShell()
            => Write(ShellPath, new Record { Pid = Environment.ProcessId, Expires = DateTimeOffset.MinValue });

        /// <summary>本进程正是交接出去的壳：退出时不要把 A/I 一起收掉。</summary>
        public static bool IsOwnedByCurrent()
            => Load(MarkerPath) is { } marker
               && marker.Pid == Environment.ProcessId
               && marker.Expires > DateTimeOffset.Now;

        /// <summary>
        /// A/I 的父进程（旧壳）已退出时调用：是交接就等新壳报到并返回它的 pid（A/I 改守护它）；
        /// 不是交接、或等到标记过期都没新壳，返回 0（调用方照旧退出）。
        /// </summary>
        public static int NextShellPid(int previous)
        {
            var marker = Load(MarkerPath);
            if (marker == null || marker.Expires <= DateTimeOffset.Now) return 0;

            while (DateTimeOffset.Now < marker.Expires)
            {
                var shell = Load(ShellPath);
                if (shell != null && shell.Pid != previous && shell.Pid != Environment.ProcessId && IsAlive(shell.Pid))
                    return shell.Pid;
                Thread.Sleep(300);
            }
            return 0;
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
            catch { return false; }
        }

        private static void Write(string path, Record record)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.RuntimeDir);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record));
                File.Move(temp, path, true);
            }
            catch
            {
            }
        }

        private static Record Load(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<Record>(File.ReadAllBytes(path))
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class Record
        {
            public int Pid { get; set; }
            public DateTimeOffset Expires { get; set; }
        }
    }
}
