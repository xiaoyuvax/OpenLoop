using System.Diagnostics;
using OpenLoop.Service.Storage;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 会话索引是「读-改-写」整张表，壳和 A 两个进程都会写。
    /// 这里守两件事：并发写不丢条目、锁真的跨进程（不是 static lock）。
    /// </summary>
    [Collection("chat-index")]
    public class ChatIndexTests : IDisposable
    {
        private readonly string _previous;
        private readonly string _dir;

        public ChatIndexTests()
        {
            _previous = ChatHistory.Root;
            _dir = Path.Combine(Path.GetTempPath(), "openloop-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ChatHistory.Root = _dir;
        }

        public void Dispose()
        {
            ChatHistory.Root = _previous;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public async Task Concurrent_touches_keep_every_entry()
        {
            var ids = Enumerable.Range(0, 40).Select(i => "s" + i).ToList();
            await Task.WhenAll(ids.Select(id => Task.Run(() => ChatHistory.Touch(@"C:\work", id))));

            // 只要求「一条都不丢」：别的测试的异步收尾偶尔会顺手补登一条，不该算失败
            var stored = ChatHistory.Index().Select(e => e.Id).ToList();
            Assert.Empty(ids.Except(stored));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Index_lock_blocks_another_process()
        {
            if (!OperatingSystem.IsWindows()) return;

            string marker = Path.Combine(_dir, "held");
            string script =
                "$m=[Threading.Mutex]::new($false,'OpenLoop.chats.index');" +
                "$m.WaitOne() | Out-Null;" +
                $"[IO.File]::WriteAllText('{marker}','held');" +
                "Start-Sleep -Milliseconds 1200;" +
                "$m.ReleaseMutex()";

            using var child = Process.Start(new ProcessStartInfo("powershell",
                "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            Assert.NotNull(child);

            var wait = Stopwatch.StartNew();
            while (!File.Exists(marker) && wait.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(25);
            if (!File.Exists(marker))
            {
                child.WaitForExit(2000);
                Assert.Fail("子进程没拿到索引锁：" +
                            (child.HasExited ? child.StandardError.ReadToEnd() : "(还在跑)"));
            }

            var watch = Stopwatch.StartNew();
            ChatHistory.Touch(@"C:\work", "blocked");
            watch.Stop();

            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(500),
                $"索引锁没有跨进程：只等了 {watch.ElapsedMilliseconds}ms");
        }
    }
}
