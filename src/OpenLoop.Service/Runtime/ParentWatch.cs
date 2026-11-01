using System.Diagnostics;

namespace OpenLoop.Service.Runtime
{
    /// <summary>
    /// 子进程的父进程守护：父进程消失后回调退出。
    /// shortcut: 轮询（1s）而不是 Windows Job Object，跨平台一致；需要更强保证时再加 Job Object。
    /// </summary>
    public static class ParentWatch
    {
        public static IDisposable Start(int parentPid, Action onParentGone)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (!IsAlive(parentPid))
                    {
                        onParentGone();
                        return;
                    }
                }
            });
            return new Handle(cts);
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private sealed class Handle : IDisposable
        {
            private readonly CancellationTokenSource _cts;
            public Handle(CancellationTokenSource cts) => _cts = cts;
            public void Dispose() => _cts.Cancel();
        }
    }
}
