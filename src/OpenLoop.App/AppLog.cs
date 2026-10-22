using Wima.Log;

namespace OpenLoop.App
{
    /// <summary>
    /// 全局日志：直接用 Puppet 自带的 WimaLogger。
    /// WimaLogger 会登记进 LogBook，因此 /agent/logs 也能读到同一份日志。
    /// </summary>
    internal static class AppLog
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
}
