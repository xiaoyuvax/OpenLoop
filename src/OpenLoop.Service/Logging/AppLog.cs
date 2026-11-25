using Wima.Log;

namespace OpenLoop.Service.Logging
{
    /// <summary>
    /// 全局日志：直接用 Puppet 自带的 WimaLogger。
    /// WimaLogger 会登记进 LogBook，因此 /agent/logs 也能读到同一份日志。
    /// </summary>
    public static class AppLog
    {
        public static readonly WimaLogger Log = new("OpenLoop", logMode: LogMode.Native | LogMode.Console);

        public static void Debug(string message) => Log.Debug(message);
        public static void Info(string message) => Log.Info(message);
        public static void Warn(string message) => Log.Warn(message);

        public static void Error(string message, Exception exception = null)
        {
            if (exception == null) Log.Error(message);
            else Log.Error(message, exception);
        }
    }

    /// <summary>
    /// 按角色分日志目录：壳、A、I 同处安装根、都用 OpenLoop 这一个日志名，同一小时会抢同一个文件，
    /// 后起的进程只能写内存（Wima 报 Console Mode only），事后查不到。必须在第一次用 AppLog 之前调用。
    /// </summary>
    public static class LogRole
    {
        public static void Use(string role)
            => WimaLogger.LogRoot =
                Path.Combine(Environment.CurrentDirectory, "logs", role) + Path.DirectorySeparatorChar;
    }
}
