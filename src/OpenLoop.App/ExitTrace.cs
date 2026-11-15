using OpenLoop.Service.Runtime;

namespace OpenLoop.App
{
    /// <summary>
    /// 同步写盘的「最后一口气」。WimaLogger 是异步的，壳一退出缓冲区就没了——
    /// 「壳无声无息不见了」只能靠这条独立通道分清是「正常退出」还是「被外部杀掉」。
    /// </summary>
    internal static class ExitTrace
    {
        private static readonly object Sync = new();

        internal static string FilePath => Path.Combine(AppPaths.DataRoot, "logs", "exit.log");

        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath,
                        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}",
                        System.Text.Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
