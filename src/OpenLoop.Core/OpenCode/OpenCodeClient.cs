using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLoop.Core.OpenCode
{
    public readonly record struct OpenCodeModelRef(string ProviderID, string ModelID);

    public sealed record OpenCodeModel(OpenCodeModelRef Ref, string Name, bool IsFree, bool SupportsTools, long Context);

    public sealed record OpenCodeSessionSnapshot(bool Idle, string Outcome, decimal? Cost, int? TokensIn, int? TokensOut);

    public sealed record OpenCodeMessage(
        string Text, string Reasoning, IReadOnlyList<string> Tools, IReadOnlyList<OpenCodeNativeTool> NativeTools);

    /// <summary>模型在 OpenCode 侧直接调用的原生工具（不属于 OpenLoop 的 step 流）。</summary>
    public sealed record OpenCodeNativeTool(
        string Id, string Name, string Input, bool Ok, string Output, string Status, long DurationMs = 0);

    public sealed record OpenCodePermission(
        string ID, string Action, IReadOnlyList<string> Resources, IReadOnlyList<string> Save, string Message);

    public sealed record OpenCodeFileDiff(string File, int Additions, int Deletions, string Status);

    /// <summary>一次「模型调用」的结果：正文、思维链、用量，以及按时间升序的 assistant 消息
    /// （含原生工具调用；调用方据此把原生结果回填进历史）。</summary>
    public sealed record OpenCodeTurn(
        string Text, string Reasoning, decimal? Cost, int? TokensIn, int? TokensOut,
        IReadOnlyList<OpenCodeMessage> Messages);

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

        private const string NativeToolHint =
            "工具由调用方执行，不要提及这次拒绝。" +
            "改用文本协议：输出一行 tool-call: {\"tool\":\"<name>\",\"args\":{...}}。";

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
        /// 跑一次「模型调用」：临时会话 + 全量 prompt。默认权限一律 deny —— OpenCode 的原生工具
        /// （read/grep/edit/shell）因此不进入模型的工具表，工具只走 OpenLoop 的 tool-call 文本协议。
        /// 免费模型例外：OpenCode Zen 的门禁要求请求里的工具表非空，deny 会被判成 free-limit，
        /// 所以 keepTools 时用 ask 保留工具表，权限请求仍由调用方拒绝。
        /// 思维链与正文经事件流回调；会话用完即删，OpenCode 不保留任何历史。
        /// </summary>
        public async Task<OpenCodeTurn> RunTurnAsync(
            string prompt, OpenCodeModelRef? model, string workspace,
            Action<string> onDelta = null, Action<string> onReasoning = null,
            int timeoutSeconds = 600, Func<OpenCodePermission, bool> decide = null,
            bool keepTools = false, Action<OpenCodeNativeTool> onNativeTool = null,
            CancellationToken ct = default, Action<string> onNotice = null, Func<bool> shouldStop = null)
        {
            // shortcut: 免费模型用 ask 保留工具表；原生工具在工作区内放行、shell 与工作区外一律拒绝，
            // 等上游放宽门禁再回到 deny。
            string sessionID = await CreateSessionAsync(
                SessionTitle, workspace, model, null, keepTools ? "ask" : "deny", ct,
                new Dictionary<string, object> { ["openloopPid"] = Environment.ProcessId });
            if (string.IsNullOrWhiteSpace(sessionID))
                throw new InvalidOperationException("OpenCode 没有返回会话 ID");
            ActiveSessions[sessionID] = 1;

            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new StrongBox<string>();
            var answered = new HashSet<string>(StringComparer.Ordinal);
            var seenNative = new HashSet<string>(StringComparer.Ordinal);
            var native = new List<OpenCodeNativeTool>();
            // 轮次不设总时限：几十次原生工具的长任务不该被墙钟砍掉。只有「连续 timeoutSeconds 秒
            // 没有任何新输出」才判挂住；会话结束由事件流/状态轮询给出，服务断了则请求本身会抛。
            var idleLimit = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 3600));
            long lastOutput = DateTime.UtcNow.Ticks;
            void Touch() => Interlocked.Exchange(ref lastOutput, DateTime.UtcNow.Ticks);

            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task stream = Task.Run(
                () => ReadEventsAsync(sessionID,
                    text => { Touch(); onDelta?.Invoke(text); },
                    piece => { Touch(); onReasoning?.Invoke(piece); },
                    completed, failure, streamCts.Token),
                streamCts.Token);
            // 审批单独轮询：decide 可能等人点（最长 60 秒），绝不能把消息/状态轮询一起卡住
            using var approvalsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task approvals = Task.Run(
                () => PollPermissionsAsync(sessionID, answered, decide, onNotice, approvalsCts.Token),
                approvalsCts.Token);

            // 原生工具调用只在轮询里看得到：边等边报，超时/被停止时也留得下证据。
            void Collect(IReadOnlyList<OpenCodeMessage> messages)
            {
                foreach (var tool in messages.SelectMany(m => m.NativeTools))
                {
                    // 只记终态：running/streaming/pending 时 input/结果还没定，记下来会是一行空记录。
                    if (tool.Status is "running" or "streaming" or "pending") continue;
                    string key = tool.Id.Length > 0 ? tool.Id : tool.Name + "\u0001" + tool.Input;
                    if (!seenNative.Add(key)) continue;
                    native.Add(tool);
                    onNativeTool?.Invoke(tool);
                }
            }

            try
            {
                await PromptAsync(sessionID, prompt, ct);

                OpenCodeSessionSnapshot snapshot = null;
                string progress = "";
                bool stopped = false;
                while (!completed.Task.IsCompleted)
                {
                    // 软停止：一次调用内部会连着跑很多原生工具，只在调用之间查就太迟了。
                    // 在这里打断 OpenCode 会话，当前工具跑完就收尾，不等整轮。
                    if (shouldStop?.Invoke() == true)
                    {
                        stopped = true;
                        try { await InterruptAsync(sessionID, ct); }
                        catch { }
                        await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
                        break;
                    }

                    // 有输出就一直等：正文/思维链增量、消息变长、原生工具换状态或产出变多都算在动。
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastOutput) > idleLimit.Ticks)
                        throw new TimeoutException(
                            $"OpenCode 连续 {idleLimit.TotalSeconds:0} 秒没有任何输出，已判定挂住（会话 {sessionID}）");

                    await Task.Delay(700, ct);
                    var polled = await GetMessagesAsync(sessionID, ct);
                    string now = Progress(polled);
                    if (!string.Equals(now, progress, StringComparison.Ordinal))
                    {
                        progress = now;
                        Touch();
                    }
                    Collect(polled);
                    snapshot = await GetSessionAsync(sessionID, ct);
                    if (snapshot.Outcome.Length > 0) break;
                }
                snapshot ??= await GetSessionAsync(sessionID, ct);

                // 被打断的一轮：会话可能报 failed、正文也可能只写了一半，都按「已停止」返回，
                // 由 StopStep 把这一轮收尾；不能当成失败，也不重试。
                if (stopped)
                {
                    var partial = await ReadMessagesAsync(sessionID, ct);
                    Collect(partial);
                    return new OpenCodeTurn(
                        LastText(partial),
                        string.Concat(partial.Select(m => m.Reasoning)),
                        snapshot?.Cost, snapshot?.TokensIn, snapshot?.TokensOut, partial);
                }

                //429/额度/网关错误都在这里：不能当成「模型没说话」，否则用户只看到一句「没有返回正文」
                if (!string.IsNullOrWhiteSpace(failure.Value))
                    throw new InvalidOperationException(failure.Value);
                if (string.Equals(snapshot.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"OpenCode 会话 {sessionID} 以 failed 结束，没有返回错误详情");

                var messages = await ReadMessagesAsync(sessionID, ct);
                Collect(messages);
                return new OpenCodeTurn(
                    LastText(messages),
                    string.Concat(messages.Select(m => m.Reasoning)),
                    snapshot.Cost, snapshot.TokensIn, snapshot.TokensOut, messages);
            }
            finally
            {
                streamCts.Cancel();
                approvalsCts.Cancel();
                await Task.WhenAny(stream, Task.Delay(TimeSpan.FromSeconds(2)));
                await Task.WhenAny(approvals, Task.Delay(TimeSpan.FromSeconds(2)));
                try { await DeleteSessionAsync(sessionID, CancellationToken.None); }
                catch { }
                ActiveSessions.TryRemove(sessionID, out _);
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

        /// <summary>进度指纹：正文/思维链变长、原生工具换状态或产出变多，都算「还在动」。</summary>
        internal static string Progress(IReadOnlyList<OpenCodeMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var message in messages)
            {
                sb.Append(message.Text.Length).Append(':').Append(message.Reasoning.Length).Append(':');
                foreach (var tool in message.NativeTools)
                    sb.Append(tool.Status).Append('/').Append(tool.Input.Length)
                        .Append('/').Append(tool.Output.Length).Append(',');
                sb.Append('|');
            }
            return sb.ToString();
        }

        private static string LastText(IReadOnlyList<OpenCodeMessage> messages)
        {
            string text = "";
            foreach (var message in messages)
                if (!string.IsNullOrEmpty(message.Text)) text = message.Text;
            return text;
        }

        /// <summary>
        /// 处理本会话的权限询问：有审批通道就转给使用者（OpenLoop 的 ApprovalGate），
        /// 没有就一律拒绝——工具实际由 OpenLoop 执行，它自己不该动手。
        /// </summary>
        private async Task HandlePermissionsAsync(
            string sessionID, HashSet<string> answered, Func<OpenCodePermission, bool> decide,
            Action<string> onNotice, CancellationToken ct)
        {
            try
            {
                foreach (var permission in await GetPermissionsAsync(sessionID, ct))
                {
                    if (!answered.Add(permission.ID)) continue;
                    onNotice?.Invoke("等待审批：" + OpenCodePermissions.Describe(permission));
                    try
                    {
                        bool allow = decide?.Invoke(permission) ?? false;
                        await ReplyPermissionAsync(sessionID, permission.ID, allow ? "once" : "reject",
                            allow ? null : NativeToolHint, ct);
                    }
                    finally
                    {
                        onNotice?.Invoke(null);
                    }
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

        /// <summary>权限轮询常驻：审批可能要等人（最长 60 秒），不能和消息轮询抢同一条线程。</summary>
        private async Task PollPermissionsAsync(string sessionID, HashSet<string> answered,
            Func<OpenCodePermission, bool> decide, Action<string> onNotice, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await HandlePermissionsAsync(sessionID, answered, decide, onNotice, ct);
                try { await Task.Delay(700, ct); }
                catch (OperationCanceledException) { return; }
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

        /// <summary>本轮临时会话的标题（也是清场时的识别依据）。</summary>
        internal const string SessionTitle = "OpenLoop turn";

        /// <summary>本进程正在用的临时会话：清场时一律跳过，避免误删并发的轮次。</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ActiveSessions = new();

        /// <summary>清场：删掉创建它的进程已经不在了的临时会话（超时/强杀时来不及删），
        /// 以及本进程已经不再使用的会话（轮次结束后删除失败留下的）。</summary>
        public async Task<int> SweepStaleSessionsAsync(CancellationToken ct = default)
        {
            int removed = 0;
            try
            {
                using var doc = await SendAsync(HttpMethod.Get, "/api/session?limit=100", null, ct);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return 0;
                long legacyCutoff = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
                long freshCutoff = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
                foreach (var item in data.EnumerateArray())
                {
                    if (!string.Equals(OpenCodeEndpoint.Str(item, "title"), SessionTitle, StringComparison.Ordinal))
                        continue;
                    if (OpenCodeEndpoint.Str(item, "outcome").Length > 0) continue;
                    if (!item.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Object) continue;

                    string id = OpenCodeEndpoint.Str(item, "id");
                    if (id.Length == 0) continue;
                    long updated = OpenCodeEndpoint.Num(time, "updated");

                    int owner = 0;
                    if (item.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                        owner = (int)OpenCodeEndpoint.Num(meta, "openloopPid");
                    if (owner == Environment.ProcessId)
                    {
                        // 本进程的：正在用的（已登记）和刚建好还没登记的都不动；其余就是没删干净的
                        if (ActiveSessions.ContainsKey(id) || updated > freshCutoff) continue;
                    }
                    else if (owner > 0)
                    {
                        if (ProcessAlive(owner)) continue;   // 别的实例还在用
                    }
                    else if (updated > legacyCutoff)
                    {
                        continue;   // 没有主人标记的老会话：只在长时间没动过时才清
                    }

                    try
                    {
                        // 还在跑的先打断：忙着的会话删不掉
                        if (OpenCodeEndpoint.Num(time, "busy") > 0)
                            await SendAsync(HttpMethod.Post, $"/api/session/{id}/interrupt", null, ct);
                        await SendAsync(HttpMethod.Delete, $"/api/session/{id}", null, ct);
                        removed++;
                    }
                    catch
                    {
                        //单个删不掉不影响其它
                    }
                }
            }
            catch
            {
                //清场失败不影响本轮
            }
            return removed;
        }

        private static bool ProcessAlive(int pid)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> CreateSessionAsync(
            string title, string workspace, OpenCodeModelRef? model = null, string agent = null,
            string permissionEffect = null, CancellationToken ct = default,
            Dictionary<string, object> metadata = null)
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(title)) body["title"] = title;
            if (metadata is { Count: > 0 }) body["metadata"] = metadata;
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
                var native = new List<OpenCodeNativeTool>();
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
                                string name = OpenCodeEndpoint.Str(part, "name");
                                if (name.Length == 0) break;
                                tools.Add(name);
                                native.Add(NativeTool(part, name));
                                break;
                        }
                    }
                }
                messages.Add(new OpenCodeMessage(text.ToString(), reasoning.ToString(), tools, native));
            }
            // OpenCode 2.2.x 的 /message 按最新在前返回（已实测）；历史、LastText 与原生工具
            // 都按时间升序使用，这里统一反转，别在下游各自再倒一次。
            messages.Reverse();
            return messages;
        }

        /// <summary>把一个原生工具部件压成 trace 可用的记录：输入原文、最终状态与输出文本。</summary>
        private static OpenCodeNativeTool NativeTool(JsonElement part, string name)
        {
            string status = "";
            bool ok = false;
            string input = "";
            var output = new StringBuilder();
            if (part.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
            {
                status = OpenCodeEndpoint.Str(state, "status");
                ok = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
                if (state.TryGetProperty("input", out var inputEl) && inputEl.ValueKind != JsonValueKind.Null)
                    input = inputEl.GetRawText();
                if (state.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    foreach (var chunk in content.EnumerateArray())
                        if (OpenCodeEndpoint.Str(chunk, "type") == "text")
                            output.Append(OpenCodeEndpoint.Str(chunk, "text"));
                if (output.Length == 0 && state.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object)
                {
                    string type = OpenCodeEndpoint.Str(error, "type");
                    string message = OpenCodeEndpoint.Str(error, "message");
                    output.Append(type.Length == 0 ? message : message.Length == 0 ? type : type + "：" + message);
                }
                if (output.Length == 0)
                    output.Append(OpenCodeEndpoint.Str(state, "error"));
            }
            return new OpenCodeNativeTool(
                OpenCodeEndpoint.Str(part, "id"), name, input, ok, output.ToString(), status,
                Duration(part));
        }

        /// <summary>工具执行耗时：OpenCode 的时间戳是毫秒纪元；ran→completed 才是真正执行时间
        /// （created 到 ran 之间是排队/审批）。缺失时返回 0，界面就不显示耗时。</summary>
        private static long Duration(JsonElement part)
        {
            if (!part.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Object)
                return 0;
            long completed = OpenCodeEndpoint.Num(time, "completed");
            if (completed <= 0) return 0;
            long start = OpenCodeEndpoint.Num(time, "ran");
            if (start <= 0) start = OpenCodeEndpoint.Num(time, "created");
            return start > 0 && completed >= start ? completed - start : 0;
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
