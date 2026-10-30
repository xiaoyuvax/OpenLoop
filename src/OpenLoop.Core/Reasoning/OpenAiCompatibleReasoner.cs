using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Reasoning
{
    /// <summary>
    /// OpenAI 兼容的 chat/completions 通道。不流式（v0 取舍：一次补全等完整返回）。
    /// 代价：长任务期间界面只能靠工具状态提示，不能用逐 token 输出。
    /// OpenRouter 会自动请求 usage.include，以便把真实成本记进账本。
    /// </summary>
    public sealed class OpenAiCompatibleReasoner : IReasoner, IStreamingReasoner
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        /// <summary>流式首包超时：网关多久不回包就判超时；<=0 表示沿用 HttpClient.Timeout（10 分钟）。</summary>
        public TimeSpan FirstResponseTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>单次流式正文的静默上限：这么久没吐完就判这次尝试失败并重发；<=0 表示不限。</summary>
        public TimeSpan StreamTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>进度回调（等待模型响应 / 第 N 次重试 / 放弃），供宿主给视觉反馈。</summary>
        public Action<ReasonerProgress> OnProgress { get; set; }

        /// <summary>一次进度通知：阶段（自然语言）、第几次尝试、已耗时。</summary>
        public sealed record ReasonerProgress(string Phase, int Attempt, TimeSpan Elapsed);

        private static readonly JsonSerializerOptions PayloadJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _endpoint;
        private readonly string _model;
        private readonly string _apiKey;
        private readonly bool _includeUsage;
        private readonly string _effort;

        public OpenAiCompatibleReasoner(string baseUrl, string model, string apiKey, string effort = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl 不能为空", nameof(baseUrl));
            _endpoint = ResolveEndpoint(baseUrl);
            _model = model ?? "";
            _apiKey = apiKey ?? "";
            _effort = string.IsNullOrWhiteSpace(effort) ? null : effort.Trim();
            _includeUsage = new Uri(_endpoint).Host.EndsWith("openrouter.ai", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveEndpoint(string baseUrl)
        {
            string trimmed = baseUrl.Trim().TrimEnd('/');
            return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + "/chat/completions";
        }

        public async Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var payload = new RequestPayload
            {
                Model = _model,
                Stream = false,
                Messages = (messages ?? []).Select(m => new MessagePayload { Role = m.Role, Content = m.Content }).ToArray(),
                Usage = _includeUsage ? new UsageOption() : null,
                Reasoning = _effort == null ? null : new ReasoningOption { Effort = _effort },
                IncludeReasoning = true
            };
            string json = JsonSerializer.Serialize(payload, PayloadJson);

            using var response = await SendWithRetryAsync(() => BuildRequest(json), ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            return Parse(body);
        }

        public async Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta, CancellationToken ct = default,
            Action<string> onReasoning = null, Action onRestart = null)
        {
            var payload = new RequestPayload
            {
                Model = _model,
                Stream = true,
                Messages = (messages ?? []).Select(m => new MessagePayload { Role = m.Role, Content = m.Content }).ToArray(),
                Usage = _includeUsage ? new UsageOption() : null,
                StreamOptions = _includeUsage ? new StreamOption { IncludeUsage = true } : null,
                Reasoning = _effort == null ? null : new ReasoningOption { Effort = _effort },
                IncludeReasoning = true
            };
            string json = JsonSerializer.Serialize(payload, PayloadJson);

            int attempt = 0;
            while (true)
            {
                string failure;
                try
                {
                    using var response = await SendWithRetryAsync(
                        () => BuildRequest(json), ct, HttpCompletionOption.ResponseHeadersRead);

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    // 单次尝试的静默上限：否则网关开始输出后卡住，界面会永远停在“运行中”。
                    using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    if (StreamTimeout > TimeSpan.Zero) bodyCts.CancelAfter(StreamTimeout);
                    return await ParseStreamAsync(reader, onDelta, bodyCts.Token, onReasoning);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failure = $"正文 {StreamTimeout.TotalSeconds:0} 秒未吐完";
                }
                catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    failure = TransientReason(ex);
                }

                // 正文已经开始输出也可能被网关掐断（ResponseEnded/连接被关）：整轮丢掉重发。
                // 重发前通知宿主清掉半截输出，所以不会出现正文重复。
                attempt++;
                Report($"第 {attempt} 次重试：{failure}", attempt, TimeSpan.Zero);
                onRestart?.Invoke();
                await Task.Delay(BackoffDelayMs(attempt), ct);
            }
        }

        private HttpRequestMessage BuildRequest(string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        /// <summary>
        /// 发送与失败重试策略（用户约定，重试到用户中断为止）：
        /// 429 限流、401/402/403 鉴权/额度 → 立即失败，不重试；
        /// 其余 4xx（参数/请求构造类）→ 同一请求最多重试 1 次（循环里内容变了计数自然刷新）；
        /// 5xx / 网络异常 / 连接中断 / 超时 → 指数退避（1s 起步、封顶 30s）一直重试，只有取消令牌能中断。
        /// 正文阶段的断流由 <see cref="StreamAsync"/> 自己整轮重发（含 onRestart 通知宿主清屏）。
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<HttpRequestMessage> factory, CancellationToken ct,
            HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
        {
            int transientAttempt = 0;
            int paramRetried = 0;
            Report("等待模型响应", 0, TimeSpan.Zero);
            while (true)
            {
                HttpResponseMessage response;
                try
                {
                    response = await SendOnceAsync(factory, option, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    transientAttempt++;
                    await RetryLaterAsync(transientAttempt, TransientReason(ex), ct);
                    continue;
                }

                if (response.IsSuccessStatusCode) return response;

                string body = await response.Content.ReadAsStringAsync(ct);
                int code = (int)response.StatusCode;
                string phrase = response.ReasonPhrase;
                response.Dispose();
                switch (ClassifyStatus(code))
                {
                    case StatusClass.NoRetry:
                        throw ApiError(code, phrase, body);
                    case StatusClass.ParamRetry when paramRetried++ > 0:
                        throw ApiError(code, phrase, body);
                }
                transientAttempt++;
                await RetryLaterAsync(transientAttempt, $"HTTP {code}", ct);
            }
        }

        /// <summary>单次发送：流式加首包超时，避免网关不回包时干等 HttpClient.Timeout 的 10 分钟。</summary>
        private async Task<HttpResponseMessage> SendOnceAsync(
            Func<HttpRequestMessage> factory, HttpCompletionOption option, CancellationToken ct)
        {
            if (option == HttpCompletionOption.ResponseHeadersRead && FirstResponseTimeout > TimeSpan.Zero)
            {
                using var first = CancellationTokenSource.CreateLinkedTokenSource(ct);
                first.CancelAfter(FirstResponseTimeout);
                return await Http.SendAsync(factory(), option, first.Token);
            }
            return await Http.SendAsync(factory(), option, ct);
        }

        /// <summary>重试前先报进度，再按指数退避等待；只有取消令牌能中断。</summary>
        private async Task RetryLaterAsync(int attempt, string reason, CancellationToken ct)
        {
            Report($"第 {attempt} 次重试：{reason}", attempt, TimeSpan.Zero);
            await Task.Delay(BackoffDelayMs(attempt), ct);
        }

        /// <summary>瞬时错误的判定：网络异常、连接中断（含流被网关掐断的 ResponseEnded）、Socket 断开、超时（非用户取消）。</summary>
        internal static bool IsTransient(Exception ex)
            => ex is HttpRequestException || ex is IOException || ex is System.Net.Sockets.SocketException
                || ex is OperationCanceledException;

        /// <summary>把瞬时错误翻成人话，供进度回调与错误消息显示。</summary>
        internal static string TransientReason(Exception ex) => ex switch
        {
            OperationCanceledException => "超时无响应",
            System.Net.Sockets.SocketException => "网络中断",
            IOException => "连接中断",
            HttpRequestException => "网络/网关错误",
            _ => ex.GetType().Name
        };

        /// <summary>进度回调：反馈代码不允许影响主流程。</summary>
        private void Report(string phase, int attempt, TimeSpan elapsed)
        {
            try { OnProgress?.Invoke(new ReasonerProgress(phase, attempt, elapsed)); }
            catch { }
        }

        internal enum StatusClass { Transient, NoRetry, ParamRetry }

        /// <summary>按状态码分类重试策略：5xx=瞬时（退避重试）；429/401/402/403=不重试；其余 4xx=同请求重试 1 次。</summary>
        internal static StatusClass ClassifyStatus(int code)
        {
            if (code >= 500) return StatusClass.Transient;
            if (code is 429 or 401 or 402 or 403) return StatusClass.NoRetry;
            return StatusClass.ParamRetry;
        }

        /// <summary>指数退避：1s、2s、4s…封顶 30s。</summary>
        internal static int BackoffDelayMs(int attempt)
            => (int)Math.Min(30_000, 1000d * Math.Pow(2, Math.Clamp(attempt - 1, 0, 5)));

        private static InvalidOperationException ApiError(int code, string phrase, string body)
        {
            string brief = string.IsNullOrEmpty(body) ? "" : body.Length <= 500 ? body : body[..500] + "…";
            return new InvalidOperationException($"模型接口返回 {code} {phrase}：{brief}");
        }

        internal static async Task<CompletionResult> ParseStreamAsync(
            TextReader reader, Action<string> onDelta, CancellationToken ct = default,
            Action<string> onReasoning = null)
        {
            var content = new StringBuilder();
            var reasoning = new StringBuilder();
            string finish = null;
            decimal? cost = null;
            int? tokensIn = null;
            int? tokensOut = null;

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line[5..].Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]") break;

                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                decimal? chunkCost = ReadCost(root);
                if (chunkCost.HasValue) cost = chunkCost;
                var usage = ReadUsage(root);
                if (usage.In.HasValue) tokensIn = usage.In;
                if (usage.Out.HasValue) tokensOut = usage.Out;

                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta))
                {
                    //推理模型把「思考」放在 reasoning（OpenRouter）或 reasoning_content（其他网关）里
                    foreach (string piece in ReadReasoningPieces(delta))
                    {
                        reasoning.Append(piece);
                        onReasoning?.Invoke(piece);
                    }
                }
                if (choice.TryGetProperty("delta", out delta)
                    && delta.TryGetProperty("content", out var pieceEl)
                    && pieceEl.ValueKind == JsonValueKind.String)
                {
                    string piece = pieceEl.GetString();
                    if (!string.IsNullOrEmpty(piece))
                    {
                        content.Append(piece);
                        onDelta?.Invoke(piece);
                    }
                }
                if (choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String)
                    finish = f.GetString();
            }

            return new CompletionResult
            {
                Content = content.ToString(),
                Reasoning = reasoning.ToString(),
                Cost = cost,
                FinishReason = finish,
                TokensIn = tokensIn,
                TokensOut = tokensOut
            };
        }

        internal static CompletionResult Parse(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("模型接口返回中没有 choices");
            }

            var choice = choices[0];
            string content = "";
            string reasoning = "";
            if (choice.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var contentEl)
                    && contentEl.ValueKind == JsonValueKind.String)
                {
                    content = contentEl.GetString() ?? "";
                }
                reasoning = string.Concat(ReadReasoningPieces(message));
            }

            string finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;

            var parsedUsage = ReadUsage(root);
            return new CompletionResult
            {
                Content = content,
                Reasoning = reasoning,
                Cost = ReadCost(root),
                FinishReason = finish,
                TokensIn = parsedUsage.In,
                TokensOut = parsedUsage.Out
            };
        }

        /// <summary>取出一个 delta/message 里的推理片段：优先 reasoning/reasoning_content 字符串，
        /// 没有时再读 reasoning_details（Anthropic/Gemini/GLM 等在 OpenRouter 上的标准形态）。</summary>
        private static IEnumerable<string> ReadReasoningPieces(JsonElement node)
        {
            bool found = false;
            foreach (string name in (string[])["reasoning", "reasoning_content"])
            {
                if (node.TryGetProperty(name, out var el)
                    && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } piece)
                {
                    found = true;
                    yield return piece;
                }
            }
            if (found) yield break;

            if (!node.TryGetProperty("reasoning_details", out var details)
                || details.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var item in details.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                foreach (string field in (string[])["text", "summary"])
                {
                    if (item.TryGetProperty(field, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && value.GetString() is { Length: > 0 } piece)
                    {
                        yield return piece;
                        break;
                    }
                }
            }
        }

        private static (int? In, int? Out) ReadUsage(JsonElement root)
        {
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return (null, null);

            int? input = null;
            int? output = null;
            if (usage.TryGetProperty("prompt_tokens", out var inp)
                && inp.ValueKind == JsonValueKind.Number && inp.TryGetInt32(out int i))
                input = i;
            if (usage.TryGetProperty("completion_tokens", out var outp)
                && outp.ValueKind == JsonValueKind.Number && outp.TryGetInt32(out int o))
                output = o;
            return (input, output);
        }

        private static decimal? ReadCost(JsonElement root)
        {
            if (root.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("cost", out var cost)
                && cost.ValueKind == JsonValueKind.Number
                && cost.TryGetDecimal(out decimal value))
                return value;
            return null;
        }

        private sealed class RequestPayload
        {
            public string Model { get; init; }
            public bool Stream { get; init; }
            public MessagePayload[] Messages { get; init; }
            public UsageOption Usage { get; init; }
            public StreamOption StreamOptions { get; init; }
            public ReasoningOption Reasoning { get; init; }

            /// <summary>OpenRouter 扩展：要求上游把推理过程一并返回（reasoning 增量）。</summary>
            [JsonPropertyName("include_reasoning")]
            public bool? IncludeReasoning { get; init; }
        }

        private sealed class MessagePayload
        {
            public string Role { get; init; }
            public string Content { get; init; }
        }

        private sealed class UsageOption
        {
            public bool Include { get; init; } = true;
        }

        private sealed class StreamOption
        {
            public bool IncludeUsage { get; init; }
        }

        private sealed class ReasoningOption
        {
            public string Effort { get; init; }
        }
    }
}
