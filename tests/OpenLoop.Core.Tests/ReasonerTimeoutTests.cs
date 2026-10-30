using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// 用本地"黑洞"/截断端点验证连接类故障的重试策略：一直指数退避重试，只有用户取消能中断。
/// 只连 127.0.0.1，不产生任何真实模型调用或费用。
/// </summary>
public sealed class ReasonerTimeoutTests
{
    [Fact]
    public async Task Unresponsive_gateway_retries_until_user_cancels()
    {
        using var blackhole = BlackholeServer.Start();
        var progress = new List<OpenAiCompatibleReasoner.ReasonerProgress>();

        var reasoner = new OpenAiCompatibleReasoner($"http://127.0.0.1:{blackhole.Port}", "test-model", "key")
        {
            FirstResponseTimeout = TimeSpan.FromMilliseconds(300),
            StreamTimeout = TimeSpan.FromSeconds(10)
        };
        reasoner.OnProgress = p => { lock (progress) progress.Add(p); };

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ex = await Record.ExceptionAsync(
            () => reasoner.StreamAsync([new LlmMessage("user", "hi")], _ => { }, guard.Token));

        // 用户取消必须原样抛出（宿主据此记 stopped，而不是 error）
        Assert.IsAssignableFrom<OperationCanceledException>(ex);

        lock (progress)
        {
            Assert.Equal("等待模型响应", progress[0].Phase);
            Assert.True(progress.Count(p => p.Phase.StartsWith("第 ") && p.Phase.Contains("次重试")) >= 2,
                "应至少重试两次而不是首轮失败即放弃");
            Assert.DoesNotContain(progress, p => p.Phase == "放弃重试");
        }
    }

    [Fact]
    public async Task Stalled_stream_restarts_instead_of_failing()
    {
        using var server = StalledStreamServer.Start();
        int restarts = 0;
        var deltas = new List<string>();

        var reasoner = new OpenAiCompatibleReasoner($"http://127.0.0.1:{server.Port}", "test-model", "key")
        {
            FirstResponseTimeout = TimeSpan.FromSeconds(10),
            StreamTimeout = TimeSpan.FromMilliseconds(300)
        };

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ex = await Record.ExceptionAsync(() => reasoner.StreamAsync(
            [new LlmMessage("user", "hi")],
            piece => { lock (deltas) deltas.Add(piece); },
            guard.Token,
            onRestart: () => Interlocked.Increment(ref restarts)));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(Volatile.Read(ref restarts) >= 1, "正文卡住应整轮重发，并通知宿主清屏");
    }

    [Fact]
    public async Task Truncated_stream_restarts_and_completes()
    {
        using var server = TruncatingServer.Start();
        int restarts = 0;
        var deltas = new List<string>();

        var reasoner = new OpenAiCompatibleReasoner($"http://127.0.0.1:{server.Port}", "test-model", "key")
        {
            FirstResponseTimeout = TimeSpan.FromSeconds(5),
            StreamTimeout = TimeSpan.FromSeconds(5)
        };

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await reasoner.StreamAsync(
            [new LlmMessage("user", "hi")],
            piece => { lock (deltas) deltas.Add(piece); },
            guard.Token,
            onRestart: () => Interlocked.Increment(ref restarts));

        Assert.Equal(1, Volatile.Read(ref restarts));
        Assert.Equal("hello", result.Content);
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
                                using var stream = client.GetStream();
                                byte[] head = Encoding.UTF8.GetBytes(
                                    "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: text/event-stream\r\n" +
                                    "Connection: close\r\n\r\n" +
                                    "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n");
                                await stream.WriteAsync(head, server._cts.Token);
                                await stream.FlushAsync(server._cts.Token);
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

    /// <summary>第一次连接声明 Content-Length 后提前断开（ResponseEnded），第二次返回完整 SSE。</summary>
    private sealed class TruncatingServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _held = [];
        private int _connections;

        private TruncatingServer(TcpListener listener) => _listener = listener;

        public int Port { get; private init; }

        public static TruncatingServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new TruncatingServer(listener)
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
                        int n = Interlocked.Increment(ref server._connections);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var stream = client.GetStream();
                                string payload = n == 1
                                    ? "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: 4096\r\n\r\n" +
                                      "data: {\"choices\":[{\"delta\":{\"content\":\"hel\"}}]}\n\n"
                                    : "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" +
                                      "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
                                      "data: [DONE]\n\n";
                                await stream.WriteAsync(Encoding.UTF8.GetBytes(payload), server._cts.Token);
                                await stream.FlushAsync(server._cts.Token);
                                // 第一条故意不写满声明的 4096 字节就关连接
                                client.Client.Shutdown(SocketShutdown.Send);
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
