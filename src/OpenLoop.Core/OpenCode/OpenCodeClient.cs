using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLoop.Core.OpenCode
{
    public readonly record struct OpenCodeModelRef(string ProviderID, string ModelID);

    public sealed record OpenCodeModel(OpenCodeModelRef Ref, string Name, bool IsFree, bool SupportsTools, long Context);

    public sealed record OpenCodeSessionSnapshot(bool Idle, string Outcome, decimal? Cost, int? TokensIn, int? TokensOut);

    public sealed record OpenCodeMessage(string Text, string Reasoning, IReadOnlyList<string> Tools);

    public sealed record OpenCodePermission(
        string ID, string Action, IReadOnlyList<string> Resources, IReadOnlyList<string> Save, string Message);

    public sealed record OpenCodeFileDiff(string File, int Additions, int Deletions, string Status);

    /// <summary>一次「模型调用」的结果：正文、思维链与用量。</summary>
    public sealed record OpenCodeTurn(string Text, string Reasoning, decimal? Cost, int? TokensIn, int? TokensOut);

    /// <summary>模型在下拉/设置里的稳定标识：opencode/&lt;modelID&gt;@&lt;providerID&gt;。</summary>
    public static class OpenCodeModelId
    {
        public const string Prefix = "opencode/";

        public static string Format(OpenCodeModelRef reference)
            => Prefix + reference.ModelID + "@" + reference.ProviderID;

        public static bool TryParse(string id, out OpenCodeModelRef reference)
        {
            reference = default;
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = id[Prefix.Length..];
            int at = rest.LastIndexOf('@');
            if (at <= 0 || at == rest.Length - 1) return false;

            reference = new OpenCodeModelRef(rest[(at + 1)..].Trim(), rest[..at].Trim());
            return reference.ProviderID.Length > 0 && reference.ModelID.Length > 0;
        }
    }

    /// <summary>OpenCode v2 HTTP 接口的传输层。只做请求与解析，不做重试策略。</summary>
    public sealed class OpenCodeClient : IDisposable
    {
        private static readonly HttpClient Shared = new(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        private readonly HttpClient _http;
        private readonly string _base;
        private readonly string _password;
        private readonly bool _owns;

        public OpenCodeClient(string url, string password) : this(url, password, null)
        {
        }

        internal OpenCodeClient(string url, string password, HttpMessageHandler handler)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("OpenCode 地址为空", nameof(url));
            _base = url.Trim().TrimEnd('/');
            _password = password ?? "";
            _owns = handler != null;
            _http = handler == null ? Shared : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public void Dispose()
        {
            if (_owns) _http.Dispose();
        }

        public async Task<IReadOnlyList<OpenCodeModel>> ListModelsAsync(CancellationToken ct = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/api/model", null, ct);
            var list = new List<OpenCodeModel>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in data.EnumerateArray())
            {
                string provider = OpenCodeEndpoint.Str(item, "providerID");
                string id = OpenCodeEndpoint.Str(item, "modelID");
                if (provider.Length == 0 || id.Length == 0) continue;

                string name = OpenCodeEndpoint.Str(item, "name");
                bool tools = item.TryGetProperty("capabilities", out var capabilities)
                    && capabilities.ValueKind == JsonValueKind.Object
                    && capabilities.TryGetProperty("tools", out var toolFlag)
                    && toolFlag.ValueKind == JsonValueKind.True;
                long context = item.TryGetProperty("limit", out var limit)
                    && limit.ValueKind == JsonValueKind.Object
                    && limit.TryGetProperty("context", out var contextEl)
                    && contextEl.TryGetInt64(out long parsed)
                        ? parsed
                        : 0;

                list.Add(new OpenCodeModel(
                    new OpenCodeModelRef(provider, id),
                    name.Length == 0 ? id : name,
                    PricedFree(item, id, name),
                    tools,
                    context));
            }
            return list;
        }

        /// <summary>
        /// 跑一次「模型调用」：临时会话 + 全量 prompt。工具权限全部设为 ask，收到请求一律拒绝 ——
        /// 这样原生工具在 OpenCode 侧不可能执行，又不会像 deny 那样把整轮跑挂；工具只落到 OpenLoop。
        /// 思维链与正文经事件流回调；会话用完即删，OpenCode 不保留任何历史。
        /// </summary>
        public async Task<OpenCodeTurn> RunTurnAsync(
            string prompt, OpenCodeModelRef? model, string workspace,
            Action<string> onDelta = null, Action<string> onReasoning = null,
            int timeoutSeconds = 600, CancellationToken ct = default)
        {
            string sessionID = await CreateSessionAsync("OpenLoop turn", workspace, model, null, "ask", ct);
            if (string.IsNullOrWhiteSpace(sessionID))
                throw new InvalidOperationException("OpenCode 没有返回会话 ID");

            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new StrongBox<string>();
            var answered = new HashSet<string>(StringComparer.Ordinal);
            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task stream = Task.Run(
                () => ReadEventsAsync(sessionID, onDelta, onReasoning, completed, failure, streamCts.Token),
                streamCts.Token);

            try
            {
                await PromptAsync(sessionID, prompt, ct);

                var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 30, 3600));
                OpenCodeSessionSnapshot snapshot = null;
                while (!completed.Task.IsCompleted)
                {
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException($"OpenCode 超过 {timeoutSeconds} 秒未完成（会话 {sessionID}）");

                    await Task.Delay(700, ct);
                    await RejectPermissionsAsync(sessionID, answered, ct);
                    snapshot = await GetSessionAsync(sessionID, ct);
                    if (snapshot.Outcome.Length > 0) break;
                }
                snapshot ??= await GetSessionAsync(sessionID, ct);

                //429/额度/网关错误都在这里：不能当成「模型没说话」，否则用户只看到一句「没有返回正文」
                if (!string.IsNullOrWhiteSpace(failure.Value))
                    throw new InvalidOperationException(failure.Value);
                if (string.Equals(snapshot.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"OpenCode 会话 {sessionID} 以 failed 结束，没有返回错误详情");

                var messages = await ReadMessagesAsync(sessionID, ct);
                return new OpenCodeTurn(
                    LastText(messages),
                    string.Concat(messages.Select(m => m.Reasoning)),
                    snapshot.Cost, snapshot.TokensIn, snapshot.TokensOut);
            }
            finally
            {
                streamCts.Cancel();
                await Task.WhenAny(stream, Task.Delay(TimeSpan.FromSeconds(2)));
                try { await DeleteSessionAsync(sessionID, CancellationToken.None); }
                catch { }
            }
        }

        /// <summary>
        /// 订阅全局事件流，只取本会话的正文/思维链增量；完成信号同时由 execution.* 事件给出。
        /// 流断开或被取消都不影响结果——最终文本始终再从会话消息里取权威值。
        /// </summary>
        private async Task ReadEventsAsync(
            string sessionID, Action<string> onDelta, Action<string> onReasoning,
            TaskCompletionSource<bool> completed, StrongBox<string> failure, CancellationToken ct)
        {
            const string types =
                "types=session.text.delta&types=session.reasoning.delta" +
                "&types=session.execution.succeeded&types=session.execution.failed&types=session.idle";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _base + "/api/event?" + types);
                request.Headers.Authorization = OpenCodeEndpoint.BasicAuth(_password);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return;

                await using var body = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(body, Encoding.UTF8);
                while (!ct.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync(ct);
                    if (line == null) return;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line[5..].Trim());
                        var root = doc.RootElement;
                        string type = OpenCodeEndpoint.Str(root, "type");
                        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                            continue;
                        if (OpenCodeEndpoint.Str(data, "sessionID") != sessionID) continue;

                        switch (type)
                        {
                            case "session.text.delta":
                                onDelta?.Invoke(OpenCodeEndpoint.Str(data, "delta"));
                                break;
                            case "session.reasoning.delta":
                                onReasoning?.Invoke(OpenCodeEndpoint.Str(data, "delta"));
                                break;
                            case "session.execution.failed":
                                failure.Value = DescribeFailure(data);
                                completed.TrySetResult(true);
                                break;
                            default:
                                if (type.StartsWith("session.execution", StringComparison.Ordinal) || type == "session.idle")
                                    completed.TrySetResult(true);
                                break;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // 事件流只是锦上添花：拿不到就用会话消息兜底
            }
        }

        private static string LastText(IReadOnlyList<OpenCodeMessage> messages)
        {
            string text = "";
            foreach (var message in messages)
                if (!string.IsNullOrEmpty(message.Text)) text = message.Text;
            return text;
        }

        /// <summary>把 OpenCode 的权限请求一律拒掉：工具只由 OpenLoop 执行，拒绝后模型会改走文本协议。</summary>
        private async Task RejectPermissionsAsync(string sessionID, HashSet<string> answered, CancellationToken ct)
        {
            try
            {
                foreach (var permission in await GetPermissionsAsync(sessionID, ct))
                {
                    if (!answered.Add(permission.ID)) continue;
                    await ReplyPermissionAsync(sessionID, permission.ID, "reject",
                        "工具由调用方执行，不要提及这次拒绝。" +
                        "改用文本协议：输出一行 tool-call: {\"tool\":\"<name>\",\"args\":{...}}。", ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                //权限轮询失败不影响本轮结果：真的执行了也会在会话里留下痕迹
            }
        }

        /// <summary>刚结束时消息可能还没落库：短暂重读几次，别把有内容的一轮当成空回复。</summary>
        private async Task<IReadOnlyList<OpenCodeMessage>> ReadMessagesAsync(string sessionID, CancellationToken ct)
        {
            IReadOnlyList<OpenCodeMessage> messages = [];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                messages = await GetMessagesAsync(sessionID, ct);
                if (messages.Any(m => m.Text.Length > 0 || m.Reasoning.Length > 0)) break;
                await Task.Delay(400, ct);
            }
            return messages;
        }

        private static string DescribeFailure(JsonElement data)
        {
            if (!data.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return "";
            string type = OpenCodeEndpoint.Str(error, "type");
            string message = OpenCodeEndpoint.Str(error, "message");
            if (message.Length == 0) message = OpenCodeEndpoint.Str(error, "detail");
            return type.Length == 0 ? message : (message.Length == 0 ? type : type + "：" + message);
        }

        public async Task DeleteSessionAsync(string sessionID, CancellationToken ct = default)
        {
            using var _ = await SendAsync(HttpMethod.Delete, $"/api/session/{sessionID}", null, ct);
        }

        public async Task<string> CreateSessionAsync(
            string title, string workspace, OpenCodeModelRef? model = null, string agent = null,
            string permissionEffect = null, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(title)) body["title"] = title;
            if (!string.IsNullOrWhiteSpace(workspace))
                body["location"] = new Dictionary<string, object> { ["directory"] = workspace };
            if (model is { } reference) body["model"] = RefBody(reference);
            if (!string.IsNullOrWhiteSpace(agent)) body["agent"] = agent;
            if (!string.IsNullOrWhiteSpace(permissionEffect))
                body["permissions"] = new[] { new Dictionary<string, object>
                {
                    ["action"] = "*",
                    ["resource"] = "*",
                    ["effect"] = permissionEffect
                } };

            using var doc = await SendAsync(HttpMethod.Post, "/api/session", body, ct);
            var info = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
            return OpenCodeEndpoint.Str(info, "id");
        }

        public async Task PromptAsync(string sessionID, string text, CancellationToken ct = default)
        {
            using var _ = await SendAsync(HttpMethod.Post, $"/api/session/{sessionID}/prompt",
                new Dictionary<string, object> { ["text"] = text ?? "" }, ct);
        }

        public async Task<OpenCodeSessionSnapshot> GetSessionAsync(string sessionID, CancellationToken ct = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/api/session/{sessionID}", null, ct);
            var info = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;

            bool idle = false;
            if (info.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Object
                && time.TryGetProperty("idle", out var idleEl) && idleEl.ValueKind == JsonValueKind.Number)
            {
                double idleAt = idleEl.GetDouble();
                double busyAt = time.TryGetProperty("busy", out var busyEl) && busyEl.ValueKind == JsonValueKind.Number
                    ? busyEl.GetDouble()
                    : 0;
                idle = busyAt == 0 || idleAt >= busyAt;
            }

            decimal? cost = info.TryGetProperty("cost", out var costEl)
                && costEl.ValueKind == JsonValueKind.Number
                && costEl.TryGetDecimal(out decimal parsedCost)
                    ? parsedCost
                    : null;

            int? input = null, output = null;
            if (info.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                if (tokens.TryGetProperty("input", out var inEl) && inEl.ValueKind == JsonValueKind.Number && inEl.TryGetInt32(out int i))
                    input = i;
                if (tokens.TryGetProperty("output", out var outEl) && outEl.ValueKind == JsonValueKind.Number && outEl.TryGetInt32(out int o))
                    output = o;
            }

            return new OpenCodeSessionSnapshot(idle, OpenCodeEndpoint.Str(info, "outcome"), cost, input, output);
        }

        public async Task<IReadOnlyList<OpenCodeMessage>> GetMessagesAsync(string sessionID, CancellationToken ct = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/api/session/{sessionID}/message", null, ct);
            var messages = new List<OpenCodeMessage>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return messages;

            foreach (var item in data.EnumerateArray())
            {
                if (OpenCodeEndpoint.Str(item, "type") != "assistant") continue;

                var text = new StringBuilder();
                var reasoning = new StringBuilder();
                var tools = new List<string>();
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        switch (OpenCodeEndpoint.Str(part, "type"))
                        {
                            case "text":
                                text.Append(OpenCodeEndpoint.Str(part, "text"));
                                break;
                            case "reasoning":
                                reasoning.Append(OpenCodeEndpoint.Str(part, "text"));
                                break;
                            case "tool":
                                tools.Add(OpenCodeEndpoint.Str(part, "name"));
                                break;
                        }
                    }
                }
                messages.Add(new OpenCodeMessage(text.ToString(), reasoning.ToString(), tools));
            }
            return messages;
        }

        public async Task<IReadOnlyList<OpenCodePermission>> GetPermissionsAsync(
            string sessionID, CancellationToken ct = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/api/session/{sessionID}/permission", null, ct);
            var permissions = new List<OpenCodePermission>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return permissions;

            foreach (var item in data.EnumerateArray())
            {
                string id = OpenCodeEndpoint.Str(item, "id");
                if (id.Length == 0) continue;
                permissions.Add(new OpenCodePermission(
                    id,
                    OpenCodeEndpoint.Str(item, "action"),
                    Strings(item, "resources"),
                    Strings(item, "save"),
                    OpenCodeEndpoint.Str(item, "message")));
            }
            return permissions;
        }

        public async Task ReplyPermissionAsync(
            string sessionID, string requestID, string decision, string message = null,
            CancellationToken ct = default)
        {
            var body = new Dictionary<string, object> { ["decision"] = decision };
            if (!string.IsNullOrWhiteSpace(message)) body["message"] = message;
            using var _ = await SendAsync(
                HttpMethod.Post, $"/api/session/{sessionID}/permission/{requestID}/reply", body, ct);
        }

        public async Task<IReadOnlyList<OpenCodeFileDiff>> GetDiffAsync(
            string sessionID, CancellationToken ct = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/api/session/{sessionID}/diff", null, ct);
            var diffs = new List<OpenCodeFileDiff>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return diffs;

            foreach (var item in data.EnumerateArray())
            {
                string file = OpenCodeEndpoint.Str(item, "file");
                if (file.Length == 0) continue;
                diffs.Add(new OpenCodeFileDiff(
                    file,
                    Int(item, "additions"),
                    Int(item, "deletions"),
                    OpenCodeEndpoint.Str(item, "status")));
            }
            return diffs;
        }

        public async Task InterruptAsync(string sessionID, CancellationToken ct = default)
        {
            using var _ = await SendAsync(HttpMethod.Post, $"/api/session/{sessionID}/interrupt",
                new Dictionary<string, object> { ["resume"] = false }, ct);
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
        {
            var request = new HttpRequestMessage(method, _base + path);
            request.Headers.Authorization = OpenCodeEndpoint.BasicAuth(_password);
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");

            using (request)
            using (var response = await _http.SendAsync(request, ct))
            {
                string text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"OpenCode 返回 {(int)response.StatusCode}：{Brief(text)}");
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            }
        }

        private static Dictionary<string, object> RefBody(OpenCodeModelRef reference)
            => new() { ["id"] = reference.ModelID, ["providerID"] = reference.ProviderID };

        private static IReadOnlyList<string> Strings(JsonElement node, string name)
        {
            if (!node.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
            return array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static int Int(JsonElement node, string name)
            => node.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out int parsed)
                ? parsed
                : 0;

        private static bool PricedFree(JsonElement model, string id, string name)
        {
            if (model.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Array && cost.GetArrayLength() > 0)
            {
                foreach (var tier in cost.EnumerateArray())
                {
                    if (tier.ValueKind != JsonValueKind.Object) continue;
                    if (!IsZero(tier, "input") || !IsZero(tier, "output")) return false;
                }
                return true;
            }
            return (id + " " + name).Contains("free", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsZero(JsonElement node, string name)
            => !node.TryGetProperty(name, out var value)
               || value.ValueKind != JsonValueKind.Number
               || !value.TryGetDecimal(out decimal price)
               || price == 0;

        private static string Brief(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString() is { Length: > 0 } text)
                    return text;
            }
            catch (JsonException)
            {
            }
            return body.Length <= 300 ? body : body[..300] + "…";
        }

        private static class Json
        {
            public static readonly JsonSerializerOptions Options = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }
    }
}
