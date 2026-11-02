using System.Text.Json;
using OpenLoop.Service.Plugins;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>stdio 宿主：成功/失败/崩溃隔离/只重启一次。</summary>
    public class PluginHostTests
    {
        private static PluginManifest Manifest(int timeoutMs = 1000) => new()
        {
            Id = "test",
            Entry = new PluginEntry { Command = "noop", TimeoutMs = timeoutMs }
        };

        private static Dictionary<string, object> Args() => new() { ["text"] = "hi" };

        private static string Reply(string data = "hi", bool ok = true, string error = null)
            => JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new { ok, data, error }
            });

        private sealed class FakeTransport : IPluginTransport
        {
            private readonly Func<string> _reply;

            public FakeTransport(Func<string> reply) => _reply = reply;

            public Task<string> RequestAsync(string json, TimeSpan timeout, CancellationToken ct)
            {
                try
                {
                    return Task.FromResult(_reply());
                }
                catch (Exception ex)
                {
                    return Task.FromException<string>(ex);
                }
            }

            public void Dispose()
            {
            }
        }

        private static PluginHost Host(Queue<Func<string>> replies, out Func<int> calls)
        {
            int count = 0;
            var host = new PluginHost(Manifest(), () =>
            {
                Interlocked.Increment(ref count);
                return new FakeTransport(replies.Dequeue());
            });
            calls = () => count;
            return host;
        }

        [Fact]
        public async Task Successful_call_returns_the_data()
        {
            using var host = Host(new Queue<Func<string>>([() => Reply("hi")]), out _);

            var result = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);

            Assert.True(result.Ok);
            Assert.Equal("hi", result.Data);
        }

        [Fact]
        public async Task Error_replies_become_tool_failures()
        {
            using var host = Host(
                new Queue<Func<string>>([() => Reply(ok: false, error: "缺少参数")]), out _);

            var result = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("缺少参数", result.Error);
        }

        [Fact]
        public async Task Malformed_reply_is_a_failure_not_an_exception()
        {
            using var host = Host(new Queue<Func<string>>([() => "not json"]), out _);

            var result = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("JSON", result.Error);
        }

        [Fact]
        public async Task A_crash_is_not_replayed_and_the_next_call_gets_a_fresh_process()
        {
            using var host = Host(
                new Queue<Func<string>>([() => throw new IOException("boom"), () => Reply("back")]),
                out var calls);

            var crashed = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);
            var after = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);

            Assert.False(crashed.Ok);
            Assert.True(after.Ok, after.Error);
            Assert.Equal("back", after.Data);
            Assert.Equal(2, calls());
        }

        [Fact]
        public async Task Two_failures_mark_the_plugin_dead_for_the_run()
        {
            using var host = Host(
                new Queue<Func<string>>([() => throw new IOException("first"), () => throw new IOException("second")]),
                out var calls);

            var first = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);
            var second = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);
            var third = await host.CallToolAsync("Echo", Args(), "/ws", CancellationToken.None);

            Assert.False(first.Ok);
            Assert.False(second.Ok);
            Assert.False(third.Ok);
            Assert.Contains("已停止", third.Error);
            Assert.Equal(2, calls());
        }

        [Fact]
        public async Task User_cancellation_is_not_swallowed_as_a_plugin_failure()
        {
            using var host = Host(new Queue<Func<string>>([() => throw new OperationCanceledException()]), out var calls);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                host.CallToolAsync("Echo", Args(), "/ws", cts.Token));
            Assert.Equal(0, calls());
        }
    }
}
