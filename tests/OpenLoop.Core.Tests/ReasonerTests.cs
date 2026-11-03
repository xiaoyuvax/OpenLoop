using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class ReasonerTests
{
    [Fact]
    public void Parse_reads_openrouter_cost()
    {
        const string body = """
        {
          "choices": [ { "message": { "content": "ok" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 2, "total_tokens": 12, "cost": 0.00123 }
        }
        """;

        var result = OpenAiCompatibleReasoner.Parse(body);

        Assert.Equal("ok", result.Content);
        Assert.Equal(0.00123m, result.Cost);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public void Parse_without_usage_leaves_cost_null()
    {
        const string body = """
        { "choices": [ { "message": { "content": "ok" } } ] }
        """;

        Assert.Null(OpenAiCompatibleReasoner.Parse(body).Cost);
    }

    [Fact]
    public async Task Stream_parser_accumulates_deltas_and_cost()
    {
        const string sse = """
        : OPENROUTER PROCESSING

        data: {"choices":[{"delta":{"content":"ok"},"finish_reason":null}]}

        data: {"choices":[{"delta":{"content":"!"},"finish_reason":"stop"}]}

        data: {"choices":[],"usage":{"prompt_tokens":3,"completion_tokens":2,"cost":0.0004}}

        data: [DONE]

        """;

        var deltas = new List<string>();
        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(new StringReader(sse), deltas.Add);

        Assert.Equal("ok!", result.Content);
        Assert.Equal(0.0004m, result.Cost);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(new[] { "ok", "!" }, deltas);
    }

    [Fact]
    public async Task Stream_parser_forwards_reasoning_deltas()
    {
        const string sse = """
        data: {"choices":[{"delta":{"reasoning":"先看目录。"},"finish_reason":null}]}

        data: {"choices":[{"delta":{"reasoning":"再读源码。"},"finish_reason":"length"}]}

        data: [DONE]

        """;

        var reasoning = new List<string>();
        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(
            new StringReader(sse), _ => { }, default, reasoning.Add);

        Assert.Equal("", result.Content);
        Assert.Equal("先看目录。再读源码。", result.Reasoning);
        Assert.Equal(new[] { "先看目录。", "再读源码。" }, reasoning);
        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public async Task Stream_parser_reads_reasoning_content_variant()
    {
        const string sse = """
        data: {"choices":[{"delta":{"reasoning_content":"思考"},"finish_reason":"stop"}]}

        data: [DONE]

        """;

        var reasoning = new List<string>();
        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(
            new StringReader(sse), _ => { }, default, reasoning.Add);

        Assert.Equal("思考", result.Reasoning);
        Assert.Equal(new[] { "思考" }, reasoning);
    }

    [Fact]
    public async Task Stream_parser_reads_reasoning_details_blocks()
    {
        const string sse = """
        data: {"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.text","text":"第一,"}]},"finish_reason":null}]}

        data: {"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.summary","summary":"第二。"}]},"finish_reason":"stop"}]}

        data: [DONE]

        """;

        var reasoning = new List<string>();
        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(
            new StringReader(sse), _ => { }, default, reasoning.Add);

        Assert.Equal("第一,第二。", result.Reasoning);
        Assert.Equal(new[] { "第一,", "第二。" }, reasoning);
    }

    [Fact]
    public async Task Stream_error_frame_in_200_is_reported_not_swallowed()
    {
        //dsh 网关把上游 429 塞进 200 的 SSE 帧里（choices 为空 + error），过去会被当成「回答是空的」
        const string sse = """
        data: {"choices":[],"error":{"message":"Free usage limit reached","code":"RATE_LIMIT","status":429}}

        data: [DONE]

        """;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenAiCompatibleReasoner.ParseStreamAsync(new StringReader(sse), _ => { }));

        Assert.Contains("Free usage limit reached", ex.Message);
    }

    [Fact]
    public async Task Stream_that_stalls_midway_fails_after_the_idle_timeout()
    {
        //长会话实测：网关回了响应头，正文一行不动，过去要挂满 StreamTimeout（10 分钟）
        var reader = new StallingReader("""data: {"choices":[{"delta":{"content":"半句"}}]}""");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenAiCompatibleReasoner.ParseStreamAsync(
                reader, _ => { }, default, null, TimeSpan.FromMilliseconds(200)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"空闲超时没有生效：{watch.Elapsed}");
    }

    [Fact]
    public async Task Stream_that_keeps_stalling_gives_up_instead_of_retrying_forever()
    {
        // 实测：网关回 200 后一个字节不吐，无限重试会让界面永远「运行中」
        using var server = new StallingSseServer();
        var reasoner = new OpenAiCompatibleReasoner(server.BaseUrl, "m", "")
        {
            StreamIdleTimeout = TimeSpan.FromMilliseconds(200),
            StreamStallLimit = 2,
            FirstResponseTimeout = TimeSpan.FromSeconds(5)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reasoner.StreamAsync([new LlmMessage("user", "hi")], _ => { }, cts.Token));

        Assert.Contains("连续 2 次", ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"放弃得太慢：{watch.Elapsed}");
    }

    [Fact]
    public async Task Stream_without_response_headers_also_counts_as_a_stall()
    {
        // 网关接了连接却不回响应头：首包超时以前在 SendWithRetryAsync 里无限重发，停滞上限管不到
        using var server = new StallingSseServer(sendHeaders: false);
        var reasoner = new OpenAiCompatibleReasoner(server.BaseUrl, "m", "")
        {
            FirstResponseTimeout = TimeSpan.FromMilliseconds(200),
            StreamIdleTimeout = TimeSpan.FromMilliseconds(200),
            StreamStallLimit = 2
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reasoner.StreamAsync([new LlmMessage("user", "hi")], _ => { }, cts.Token));

        Assert.Contains("连续 2 次", ex.Message);
        Assert.Contains("首包", ex.Message);
    }

    /// <summary>回 200 + 一帧 SSE 后再也不发数据（也不断开），模拟网关挂住；sendHeaders=false 时连响应头都不给。</summary>
    private sealed class StallingSseServer : IDisposable
    {
        private readonly bool _sendHeaders;
        private readonly System.Net.Sockets.TcpListener _listener =
            new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();

        public StallingSseServer(bool sendHeaders = true)
        {
            _sendHeaders = sendHeaders;
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((System.Net.IPEndPoint)_listener.LocalEndpoint).Port}/v1";
            _ = Task.Run(AcceptAsync);
        }

        public string BaseUrl { get; }

        private async Task AcceptAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(System.Net.Sockets.TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 4096, leaveOpen: true);
                int length = 0;
                string line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line[(line.IndexOf(':') + 1)..].Trim(), out length);
                }
                if (length > 0)
                {
                    var body = new char[length];
                    int read = 0;
                    while (read < length)
                    {
                        int chunk = await reader.ReadAsync(body.AsMemory(read, length - read));
                        if (chunk == 0) break;
                        read += chunk;
                    }
                }

                if (_sendHeaders)
                {
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(
                        "data: {\"choices\":[{\"delta\":{\"content\":\"半句\"}}]}\n\n"));
                    await stream.FlushAsync();
                }

                try { await Task.Delay(Timeout.Infinite, _cts.Token); } catch { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    /// <summary>吐一行后不再返回，直到读取被取消（模拟网关半途挂住）。</summary>
    private sealed class StallingReader(string first) : TextReader
    {
        private bool _sent;

        public override async ValueTask<string> ReadLineAsync(CancellationToken ct)
        {
            if (!_sent)
            {
                _sent = true;
                return first;
            }

            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
    }

    [Fact]
    public void Status_Classification_Matches_Retry_Policy()
    {
        // 429 限流与 401/402/403 鉴权额度：不重试
        Assert.Equal(OpenAiCompatibleReasoner.StatusClass.NoRetry,
            OpenAiCompatibleReasoner.ClassifyStatus(429));
        Assert.Equal(OpenAiCompatibleReasoner.StatusClass.NoRetry,
            OpenAiCompatibleReasoner.ClassifyStatus(402));
        // 参数/请求构造类 4xx：同请求重试 1 次
        Assert.Equal(OpenAiCompatibleReasoner.StatusClass.ParamRetry,
            OpenAiCompatibleReasoner.ClassifyStatus(400));
        // 5xx / 网关错误：瞬时，指数退避重试
        Assert.Equal(OpenAiCompatibleReasoner.StatusClass.Transient,
            OpenAiCompatibleReasoner.ClassifyStatus(502));
    }

    [Fact]
    public void Api_error_brief_prefers_error_message()
    {
        //OpenRouter 的错误体里 message 才是人话，其余字段（metadata/raw）对用户没用
        string body = """{"error":{"message":"model is temporarily rate-limited upstream","code":429}}""";
        Assert.Equal("model is temporarily rate-limited upstream",
            OpenAiCompatibleReasoner.ErrorBrief(body));

        //非 JSON 错误体：原样保留，长则截断
        Assert.Equal("gateway exploded", OpenAiCompatibleReasoner.ErrorBrief("gateway exploded"));
        Assert.Empty(OpenAiCompatibleReasoner.ErrorBrief(""));
        Assert.Equal(501, OpenAiCompatibleReasoner.ErrorBrief(new string('x', 900)).Length);
    }

    [Fact]
    public void Backoff_Doubles_And_Caps_At_30s()
    {
        Assert.Equal(1000, OpenAiCompatibleReasoner.BackoffDelayMs(1));
        Assert.Equal(2000, OpenAiCompatibleReasoner.BackoffDelayMs(2));
        Assert.Equal(4000, OpenAiCompatibleReasoner.BackoffDelayMs(3));
        Assert.Equal(30000, OpenAiCompatibleReasoner.BackoffDelayMs(10));
    }

    [Fact]
    public void Transient_classification_covers_timeout_and_network()
    {
        // 注意：用户主动取消在 SendWithRetryAsync 里被前置 catch 过滤，这里只表达“属于瞬时类”
        Assert.True(OpenAiCompatibleReasoner.IsTransient(new OperationCanceledException()));
        Assert.True(OpenAiCompatibleReasoner.IsTransient(new System.Net.Http.HttpRequestException("boom")));
        Assert.True(OpenAiCompatibleReasoner.IsTransient(new System.Net.Sockets.SocketException(10054)));
        // 网关半途掐断连接抛的是 HttpIOException : IOException（"The response ended prematurely"）
        Assert.True(OpenAiCompatibleReasoner.IsTransient(new System.IO.IOException("The response ended prematurely.")));
        Assert.False(OpenAiCompatibleReasoner.IsTransient(new InvalidOperationException("nope")));

        Assert.Equal("超时无响应", OpenAiCompatibleReasoner.TransientReason(new TaskCanceledException()));
        Assert.Equal("网络中断", OpenAiCompatibleReasoner.TransientReason(new System.Net.Sockets.SocketException(10054)));
        Assert.Equal("连接中断", OpenAiCompatibleReasoner.TransientReason(new System.IO.IOException("x")));
        Assert.Equal("网络/网关错误", OpenAiCompatibleReasoner.TransientReason(new System.Net.Http.HttpRequestException("x")));
    }

    [Fact]
    public void Parse_reads_reasoning_from_message()
    {
        const string body = """
        { "choices": [ { "message": { "content": "ok", "reasoning": "想一想" } } ] }
        """;

        var result = OpenAiCompatibleReasoner.Parse(body);

        Assert.Equal("ok", result.Content);
        Assert.Equal("想一想", result.Reasoning);
    }

    [Fact]
    public async Task Process_reasoner_round_trips_stdin_to_stdout()
    {
        var reasoner = new ProcessReasoner("cmd", ["/c", "more"], TimeSpan.FromSeconds(30));

        var result = await reasoner.CompleteAsync(
            [new LlmMessage("system", "sys-line"), new LlmMessage("user", "hello-loop")]);

        Assert.Contains("hello-loop", result.Content);
        Assert.Contains("sys-line", result.Content);
    }

    [Fact]
    public async Task Process_reasoner_surfaces_nonzero_exit()
    {
        var reasoner = new ProcessReasoner("cmd", ["/c", "exit", "3"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reasoner.CompleteAsync([new LlmMessage("user", "x")]));

        Assert.Contains("退出码 3", ex.Message);
    }
}
