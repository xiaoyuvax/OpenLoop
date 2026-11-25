namespace OpenLoop.App
{
    /// <summary>
    /// UI 线程重活计时：超过阈值写一条日志，卡顿时能直接看出是哪一个操作。
    /// 心跳只证明「UI 停过」，证明不了停在哪。
    /// </summary>
    internal static class UiTiming
    {
        private const long WarnMs = 1000;

        public static void Measure(string what, Action work)
        {
            long start = Environment.TickCount64;
            try
            {
                work();
            }
            finally
            {
                Report(what, start);
            }
        }

        public static T Measure<T>(string what, Func<T> work)
        {
            long start = Environment.TickCount64;
            try
            {
                return work();
            }
            finally
            {
                Report(what, start);
            }
        }

        private static void Report(string what, long start)
        {
            long ms = Environment.TickCount64 - start;
            if (ms < WarnMs) return;
            try { OpenLoop.Service.Logging.AppLog.Warn($"[UI 卡顿] {what} 耗时 {ms} ms"); }
            catch
            {
            }
        }
    }
}
