namespace OpenLoop.App
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Windows.Forms;

    /// <summary>
    /// UI 线程心跳留痕：每 5 秒同步写一行时间戳到 logs/ui-heartbeat.log。
    /// 壳「未响应」（AppHang，UI 线程卡死）不会留下任何异常记录——启动时对比心跳就能圈出卡死区间。
    /// 心跳必须挂在 UI 线程计时器上：UI 卡死它就停，后台线程写的心跳没有这个性质。
    /// 文件体量控制：每天滚动一份，只留最近 7 天（hang 排查的价值只在最近几次会话）。
    /// </summary>
    internal static class UiHeartbeat
    {
        private const int IntervalSeconds = 5;
        private const int KeepDays = 7;
        /// <summary>超过这个间隔没心跳就视为上次会话异常结束（留出几倍的抖动余量）。</summary>
        private static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(IntervalSeconds * 6);

        private static readonly object Sync = new();
        private static Timer _timer;
        private static string _date = "";

        private static string Dir => Path.Combine(OpenLoop.Service.Runtime.AppPaths.DataRoot, "logs");
        private static string CurrentPath => Path.Combine(Dir, $"ui-heartbeat-{DateTime.Now:yyyyMMdd}.log");

        /// <summary>主窗体建好后启动；顺带把上一次会话末尾的卡死区间写进 exit.log，并清掉过期文件。</summary>
        internal static void Start()
        {
            ReportPreviousGap();
            PruneOld();
            _timer = new Timer { Interval = IntervalSeconds * 1000 };
            _timer.Tick += (_, _) => Beat();
            _timer.Start();
        }

        /// <summary>正常退出也补一行心跳，别把「关得干脆」误判成卡死。</summary>
        internal static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            Beat();
        }

        private static void Beat()
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(CurrentPath,
                        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {Environment.ProcessId}]{Environment.NewLine}");
                }
            }
            catch
            {
                // 心跳失败不影响运行
            }
        }

        /// <summary>上一条心跳距今超过阈值：上次会话末尾有一段 UI 卡死，把区间写进 exit.log。</summary>
        private static void ReportPreviousGap()
        {
            try
            {
                var today = CurrentPath;
                if (!File.Exists(today)) return;
                string last = File.ReadLines(today).LastOrDefault();
                if (last == null || !DateTimeOffset.TryParse(
                        last.AsSpan(0, Math.Min(23, last.Length)).TrimEnd(), out var at)) return;
                var gap = DateTimeOffset.Now - at;
                // 跨天启动时最后一条与现在差了一天以上：正常退出会补一行心跳，停一天以上就是没跑
                if (gap < HangThreshold) return;
                ExitTrace.Write($"上次会话 UI 心跳停在 {at:HH:mm:ss}（距本次启动 {gap.TotalMinutes:0} 分钟）：期间发生过界面卡死（未响应）");
            }
            catch
            {
                // 留痕失败不影响启动
            }
        }

        /// <summary>只留最近 KeepDays 天的心跳文件：留痕的价值只在最近几次会话，别让 logs 无限长。</summary>
        private static void PruneOld()
        {
            try
            {
                var cutoff = DateTime.Today.AddDays(-(KeepDays - 1));
                foreach (string file in Directory.EnumerateFiles(Dir, "ui-heartbeat-*.log"))
                {
                    // 文件名即日期：解析不出按删除处理（不该出现的名字留着也没用）
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!DateTime.TryParseExact(
                            name.Length > 12 ? name.Substring(12, 8) : name, "yyyyMMdd",
                            null, System.Globalization.DateTimeStyles.None, out var day)
                        || day < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
                // 清理失败不影响运行
            }
        }
    }
}
