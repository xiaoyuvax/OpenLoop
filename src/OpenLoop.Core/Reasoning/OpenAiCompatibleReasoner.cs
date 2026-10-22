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
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

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
                Reasoning = _effort == null ? null : new ReasoningOption { Effort = _effort }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, PayloadJson), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                string brief = body.Length <= 500 ? body : body[..500] + "…";
                throw new InvalidOperationException(
                    $"模型接口返回 {(int)response.StatusCode} {response.ReasonPhrase}：{brief}");
            }

            return Parse(body);
        }

        public async Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta, CancellationToken ct = default)
        {
            var payload = new RequestPayload
            {
                Model = _model,
                Stream = true,
                Messages = (messages ?? []).Select(m => new MessagePayload { Role = m.Role, Content = m.Content }).ToArray(),
                Usage = _includeUsage ? new UsageOption() : null,
                StreamOptions = _includeUsage ? new StreamOption { IncludeUsage = true } : null,
                Reasoning = _effort == null ? null : new ReasoningOption { Effort = _effort }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, PayloadJson), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                string brief = body.Length <= 500 ? body : body[..500] + "…";
                throw new InvalidOperationException(
                    $"模型接口返回 {(int)response.StatusCode} {response.ReasonPhrase}：{brief}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await ParseStreamAsync(reader, onDelta, ct);
        }

        internal static async Task<CompletionResult> ParseStreamAsync(
            TextReader reader, Action<string> onDelta, CancellationToken ct = default)
        {
            var content = new StringBuilder();
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
                if (choice.TryGetProperty("delta", out var delta)
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
            if (choice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentEl)
                && contentEl.ValueKind == JsonValueKind.String)
            {
                content = contentEl.GetString() ?? "";
            }

            string finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;

            var parsedUsage = ReadUsage(root);
            return new CompletionResult
            {
                Content = content,
                Cost = ReadCost(root),
                FinishReason = finish,
                TokensIn = parsedUsage.In,
                TokensOut = parsedUsage.Out
            };
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
