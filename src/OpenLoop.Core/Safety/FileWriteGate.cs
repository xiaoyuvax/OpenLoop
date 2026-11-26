using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OpenLoop.Core.Safety
{
    /// <summary>
    /// 文件写入闸门：按 realpath 取锁，同进程的多会话与跨进程（A / I / 壳）都串行。
    /// Windows 用命名 Mutex；其它平台 .NET 不支持命名 Mutex，退回进程内 SemaphoreSlim（跨进程不设防）。
    /// 只挡走这里的写入；进程外编辑器与 Shell 命令不认这把锁，EditFile 写前的内容校验兜底。
    /// </summary>
    public static class FileWriteGate
    {
        private const int TimeoutMs = 10_000;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Local =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 成功时返回的 lease 必须在同一线程释放（Mutex 归线程所有），持有期间不要 await。
        /// </summary>
        public static bool TryEnter(string fullPath, out IDisposable lease, out string error)
        {
            lease = null;
            error = null;

            //名字用路径哈希：命名 Mutex 不能带路径分隔符，也避免把用户目录写进内核对象名
            string key = fullPath.ToLowerInvariant();
            string name = "OpenLoop.FileWrite." + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8);

            //shortcut: 非 Windows 只有进程内串行（命名 Mutex 在 Unix 抛 PlatformNotSupportedException）；
            //真出现跨平台部署再补文件锁
            if (!OperatingSystem.IsWindows())
            {
                SemaphoreSlim gate = Local.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
                if (!gate.Wait(TimeoutMs))
                {
                    error = BusyMessage(fullPath);
                    return false;
                }
                lease = new SemaphoreLease(gate);
                return true;
            }

            Mutex mutex;
            try
            {
                mutex = new Mutex(false, name);
            }
            catch (Exception ex)
            {
                error = $"无法创建写入锁：{ex.Message}";
                return false;
            }

            bool owned;
            try
            {
                owned = mutex.WaitOne(TimeoutMs);
            }
            catch (AbandonedMutexException)
            {
                owned = true; //上一个持有者进程直接退出，锁归我们
            }
            catch (Exception ex)
            {
                mutex.Dispose();
                error = $"等待写入锁失败：{ex.Message}";
                return false;
            }

            if (!owned)
            {
                mutex.Dispose();
                error = BusyMessage(fullPath);
                return false;
            }

            lease = new MutexLease(mutex);
            return true;
        }

        private static string BusyMessage(string fullPath)
            => $"文件正被其他会话写入（等待 {TimeoutMs / 1000} 秒未获得锁，未改动）：{fullPath}";

        private sealed class MutexLease(Mutex mutex) : IDisposable
        {
            public void Dispose()
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
                mutex.Dispose();
            }
        }

        private sealed class SemaphoreLease(SemaphoreSlim gate) : IDisposable
        {
            public void Dispose() => gate.Release();
        }
    }
}
