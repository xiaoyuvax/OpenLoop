using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// 用本地"黑洞"端点验证超时与重试预算：网关收下连接但永不回包（以及回了头却不吐正文）。
/// 只连 127.0.0.1，不产生任何真实模型调用或费用。
/// </summary>
public sealed class ReasonerTimeoutTests
{
    [Fact]
    public async Task Streaming_gives_up_after_retry_budget_instead_of_hanging_forever()
    {
        using var blackhole = BlackholeServer.Start();
        var progress = new List<OpenAiCompatibleReasoner.ReasonerProgress>();

        var reasoner = new OpenAiCompatibleReasoner($"http://127.0.0.1:{blackhole.Port}", "test-model", "key")
        {
            FirstResponseTimeout = TimeSpan.FromMilliseconds(600),
            RetryBudget = TimeSpan.FromSeconds(60),
            MaxTransientRetries = 1,
            StreamTimeout = TimeSpan.FromSeconds(10)
        };
        reasoner.OnProgress = p => { lock (progress) progress.Add(p); };

        // 兜底：即便实现回归成"无限等"，30 秒后用取消令牌把测试打断，而不是把测试挂死
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reasoner.StreamAsync([new LlmMessage("user", "hi")], _ => { }, guard.Token));
        watch.Stop();

        Assert.Contains("已停止重试", ex.Message);
        // 600ms 首包超时 + 1s 退避 + 600ms 第二次 ≈ 2.3s；留足余量断言确实"有界"
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"实际耗时 {watch.Elapsed}");

        lock (progress)
        {
            Assert.Equal("等待模型响应", progress[0].Phase);
            Assert.Contains(progress, p => p.Phase.StartsWith("第 1 次重试"));
            Assert.Equal("放弃重试", progress[^1].Phase);
        }
    }

    [Fact]
    public async Task Stalled_stream_body_times_out_with_readable_error()
    {
        using var server = StalledStreamServer.Start();
        var reasoner = new OpenAiCompatibleReasoner($"http://127.0.0.1:{server.Port}", "test-model", "key")
        {
            FirstResponseTimeout = TimeSpan.FromSeconds(10),
            StreamTimeout = TimeSpan.FromSeconds(1),
            RetryBudget = TimeSpan.FromSeconds(60)
        };

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();

        // 正文超时必须抛 InvalidOperationException（→ 宿主记 error），
        // 不能是 OperationCanceledException（→ 宿主会误判成用户点了停止）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reasoner.StreamAsync([new LlmMessage("user", "hi")], _ => { }, guard.Token));
        watch.Stop();

        Assert.Contains("流式输出超时", ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"实际耗时 {watch.Elapsed}");
    }

    /// <summary>接受 TCP 连接后永不回任何字节：模拟网关无响应。</summary>
    private sealed class BlackholeServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _held = [];

        private BlackholeServer(TcpListener listener) => _listener = listener;

        public int Port { get; private init; }

        public static BlackholeServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new BlackholeServer(listener)
            {
                Port = ((IPEndPoint)listener.LocalEndpoint).Port
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!server._cts.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(server._cts.Token);
                        lock (server._held) server._held.Add(client);
                    }
                }
                catch { /* 关闭时取消 */ }
            });
            return server;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_held)
            {
                foreach (var c in _held) c.Dispose();
            }
            _cts.Dispose();
        }
    }

    /// <summary>回 200 + SSE 头和一行正文后停住：模拟"开始了但再也不吐"。</summary>
    private sealed class StalledStreamServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _held = [];

        private StalledStreamServer(TcpListener listener) => _listener = listener;

        public int Port { get; private init; }

        public static StalledStreamServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new StalledStreamServer(listener)
            {
                Port = ((IPEndPoint)listener.LocalEndpoint).Port
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!server._cts.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(server._cts.Token);
                        lock (server._held) server._held.Add(client);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var _stream = client.GetStream();
                                byte[] head = Encoding.UTF8.GetBytes(
                                    "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: text/event-stream\r\n" +
                                    "Connection: close\r\n\r\n" +
                                    "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n");
                                await _stream.WriteAsync(head, server._cts.Token);
                                await _stream.FlushAsync(server._cts.Token);
                                // 之后不再写入也不关闭：读取端会一直等
                                await Task.Delay(Timeout.InfiniteTimeSpan, server._cts.Token);
                            }
                            catch { /* 客户端断开或服务器关闭 */ }
                        });
                    }
                }
                catch { /* 关闭时取消 */ }
            });
            return server;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_held)
            {
                foreach (var c in _held) c.Dispose();
            }
            _cts.Dispose();
        }
    }
}
