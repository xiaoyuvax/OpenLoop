using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 借用 OpenCode 的模型通道：把 OpenLoop 的消息压成单个 prompt，走临时会话的单次调用。
    /// OpenLoop 自己持有全部 trace（历史、工具调用、结果、思维链）；OpenCode 只当透传壳。
    /// 原生工具按宿主审批执行；调用与结果实时上报，作为 native 步骤写进 trace。
    /// </summary>
    public sealed class OpenCodeReasoner : IReasoner, IStreamingReasoner, INativeToolObserver, INoticeObserver
    {
        private OpenCodeClient _client;
        private OpenCodeModelRef _model;
        private readonly string _workspace;
        private readonly Func<OpenCodePermission, bool> _decide;
        private bool _keepTools;
        private readonly Func<CancellationToken, Task<(OpenCodeClient Client, OpenCodeModelRef Model)>> _resolve;
        private readonly Func<bool> _keepToolsNow;
        /// <summary>连续多久没有新输出就判挂住；同模型挂过一次后按学到的更短上限起步。</summary>
        private int _idleSeconds = 600;

        public OpenCodeReasoner(OpenCodeClient client, OpenCodeModelRef model, string workspace = null,
            ApprovalHandler approve = null, bool keepTools = false)
            : this(OpenCodeModelId.Format(model), workspace, approve, keepTools)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _model = model;
        }

        /// <summary>
        /// 目录还没加载好（A/I 刚起、opencode 刚重启）：首轮先等就绪再调用，不占线程也不算失败。
        /// 等待期间用 <see cref="OnNotice"/> 交代；<paramref name="keepTools"/> 在就绪后按模型是否免费现算。
        /// </summary>
        public OpenCodeReasoner(
            Func<CancellationToken, Task<(OpenCodeClient Client, OpenCodeModelRef Model)>> resolve,
            string modelId, string workspace = null, ApprovalHandler approve = null, Func<bool> keepTools = null)
            : this(modelId, workspace, approve, false)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _keepToolsNow = keepTools;
        }

        private OpenCodeReasoner(string modelId, string workspace, ApprovalHandler approve, bool keepTools)
        {
            _workspace = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace;
            _decide = OpenCodePermissions.Decider(_workspace, approve);
            _keepTools = keepTools;
            int learned = OpenCodeStallBudget.IdleSeconds(modelId);
            if (learned > 0) _idleSeconds = Math.Min(_idleSeconds, learned);
        }

        public Action<OpenAiCompatibleReasoner.ReasonerProgress> OnProgress { get; set; }

        /// <summary>原生工具调用实时上报（只用于 trace；调用结果由 OpenCode 侧决定）。</summary>
        public Action<NativeToolCall> OnNativeTool { get; set; }

        /// <summary>等待审批等即时状态（null = 清除）。界面据此显示「等待审批…」，不是空白。</summary>
        public Action<string> OnNotice { get; set; }

        /// <summary>宿主请求软停止：一次调用内部的多个原生工具之间也据此收尾。</summary>
        public Func<bool> ShouldStop { get; set; }

        /// <summary>清掉遗留的临时会话（上次进程超时/被杀没删掉的）；不 await，不影响本轮。</summary>
        public Task SweepStaleSessions()
            => _client?.SweepStaleSessionsAsync(CancellationToken.None) ?? Task.CompletedTask;

        // 协议块紧跟 system，保证每轮 prompt 前缀逐字一致（上游前缀缓存靠这个），也避免在尾部每次追加。
        private const string Protocol =
            "[protocol]\nNative tool calling is disabled in this session; the calling application provides the tools. " +
            "To use a tool, print one line: tool-call: {\"tool\":\"<name>\",\"args\":{...}} — the application executes that " +
            "line and sends you tool-result: {...} as the next message. Never emit native tool calls, never claim " +
            "that tools are unavailable, and never mention a rejected native call in your answer.\n" +
            "History may contain tool-call lines whose tool name starts with native:. They were already executed by the " +
            "runtime that wraps you; use them as facts and never emit a native: call yourself. " +
            "In particular, the native `execute` tool is a sandboxed Code Mode with NO filesystem, shell or file access — " +
            "its tool set (automation/browser/opencode session management) is not yours. If you need to read, write or " +
            "search files or run commands, those tools exist only through the text protocol line above; do not conclude " +
            "that tools are missing just because `execute` cannot see them.\n";

        // 只在上一轮完全没正文时追加在末尾（新开一次调用，不影响正常轮的前缀）。
        private const string RetryHint =
            "\n[system]\nYour previous reply contained no text. Send your tool call again as one line: " +
            "tool-call: {\"tool\":\"<name>\",\"args\":{...}}; otherwise answer in plain text.\n";

        // F-48a2b91a：模型滑进 native:execute（Code Mode）死胡同后误判「宿主没有文件工具」而拒绝任务。
        // 命中判定时追加这条纠正提示重发一次，把它从 Code Mode 拉回文本协议。
        public const string CodeModeHint =
            "\n[system]\nYour previous turn only used the native `execute` tool (sandboxed Code Mode). That sandbox " +
            "has NO filesystem, shell or file access — its tool set (automation/browser/opencode session management) " +
            "is not the host's tool set. The host's file/shell tools (read, write, list, grep, shell, ...) exist ONLY " +
            "through the text protocol: print exactly one line `tool-call: {\"tool\":\"<name>\",\"args\":{...}}` and " +
            "wait for tool-result. Do not report missing tools; continue the task via the text protocol.\n";

        /// <summary>
        /// F-48a2b91a：一轮里只调了 Code Mode 的 execute（没有任何正文协议调用、没有真实文件/命令类原生工具），
        /// 判为「滑进 Code Mode 死胡同」。text 含 tool-call 协议行就不算——说明它已经在用文本协议。
        /// </summary>
        internal static bool LooksLikeCodeModeLoop(IReadOnlyList<OpenCodeNativeTool> native, string text)
        {
            if (native is not { Count: > 0 }) return false;
            if (!string.IsNullOrEmpty(text) &&
                text.Contains("tool-call:", StringComparison.OrdinalIgnoreCase)) return false;
            bool anyRealTool = native.Any(t =>
                !string.Equals(t.Name, "execute", StringComparison.OrdinalIgnoreCase));
            return !anyRealTool;
        }

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => RunAsync(messages, null, null, ct);

        public Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta,
            CancellationToken ct = default, Action<string> onReasoning = null, Action onRestart = null)
            => RunAsync(messages, onDelta, onReasoning, ct, onRestart);

        private async Task<CompletionResult> RunAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta, Action<string> onReasoning, CancellationToken ct,
            Action onRestart = null)
        {
            OnProgress?.Invoke(new OpenAiCompatibleReasoner.ReasonerProgress("等待 OpenCode 响应", 0, TimeSpan.Zero));
            if (_client == null)
            {
                // 服务刚起/刚重启：等目录就绪再发请求，否则会被发到别的端点（模型名不认）而直接报错。
                OnNotice?.Invoke("等待 OpenCode 服务就绪…");
                var ready = await _resolve(ct);
                _client = ready.Client;
                _model = ready.Model;
                if (_keepToolsNow != null) _keepTools = _keepToolsNow();
                OnNotice?.Invoke(null);
            }
            string prompt = Flatten(messages);
            // 附件随当轮 prompt 的 files 发给上游：只认最后一条消息（刚产生的 tool-result），
            // 不从更早的历史里捞——否则同一张图会在后面每一轮重复上传。
            var attachments = messages?.LastOrDefault()?.Attachments;
            // 免费模型保留工具表只为过上游门禁；原生工具按宿主审批执行，调用与结果实时上报。
            var decide = _decide;
            var native = new List<OpenCodeNativeTool>();
            Action<OpenCodeNativeTool> nativeSink = tool =>
            {
                native.Add(tool);
                OnNativeTool?.Invoke(new NativeToolCall(tool.Name, tool.Input, tool.Ok, tool.Output, tool.Status, tool.DurationMs));
            };
            var turn = await Call(prompt);
            // 只调了原生工具、没有正文是免费模型的常态：有原生结果就不要再整轮重发一次。
            if (string.IsNullOrWhiteSpace(turn.Text) && string.IsNullOrWhiteSpace(turn.Reasoning)
                && native.Count == 0 && ShouldStop?.Invoke() != true)
                turn = await Call(prompt + RetryHint);
            // F-48a2b91a：只调了 Code Mode 的 execute、没有文本协议调用——补纠正提示重发一次。
            else if (_keepTools && ShouldStop?.Invoke() != true
                && LooksLikeCodeModeLoop(native, turn.Text))
                turn = await Call(prompt + CodeModeHint);

            async Task<OpenCodeTurn> Call(string text)
            {
                try
                {
                    return await RetryConnectAsync(
                        () => _client.RunTurnAsync(text, _model, _workspace, onDelta, onReasoning,
                            timeoutSeconds: _idleSeconds, decide: decide, keepTools: _keepTools, attachments: attachments,
                            onNativeTool: nativeSink, ct: ct, onNotice: OnNotice, shouldStop: ShouldStop),
                        ct, onRestart);
                }
                catch (OpenCodeStalledException)
                {
                    // 撞过一次「无输出」就把空闲上限收紧：同一轮里后面的压缩与重试不该再各等十分钟才收场。
                    _idleSeconds = Math.Min(_idleSeconds, OpenCodeStallBudget.StallIdleSeconds);
                    // 多半是 prompt 超过上游实际收得下的量（自报窗口不作数）：记下来，本进程内同模型提前压缩。
                    OpenCodeStallBudget.Learn(OpenCodeModelId.Format(_model), AgentContext.EstimateTokens(text));
                    throw;
                }
            }

            return new CompletionResult
            {
                Content = turn.Text,
                Reasoning = turn.Reasoning,
                Cost = turn.Cost,
                TokensIn = turn.TokensIn,
                TokensOut = turn.TokensOut,
                ExtraMessages = BuildExtraMessages(turn.Messages),
                // 上游本轮自己压过上下文：摘要回灌给宿主替换更早的历史，否则下一轮又带着同样的超长前缀重发。
                Compaction = OpenCodeClient.LastCompaction(turn.Messages),
                NativeToolSucceeded = native.Any(tool => tool.Ok),
                FinishReason = "stop"
            };
        }

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 连接层的失败（本地 opencode/代理没起来、DNS 没通、上游连接被重置）不算这一轮失败：退避重试到连上为止。
        /// 用户停止（ct）或软打断就按「已停止」收尾；HTTP 状态错误、额度与模型报错照旧直接抛。
        /// </summary>
        private async Task<OpenCodeTurn> RetryConnectAsync(Func<Task<OpenCodeTurn>> call, CancellationToken ct, Action onRestart)
        {
            var policy = FeatureCatalog.PolicyOf(_workspace);
            bool retried = false;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var turn = await call();
                    if (retried) OnNotice?.Invoke(null);
                    return turn;
                }
                catch (Exception ex) when (IsConnectFailure(ex))
                {
                    if (!policy.ConnectRetry) throw;
                    if (ct.IsCancellationRequested || ShouldStop?.Invoke() == true)
                        throw new OperationCanceledException(ct);

                    retried = true;
                    // 断在半截的正文要清掉，重发后才不会出现两遍
                    onRestart?.Invoke();
                    TimeSpan delay = RetryBackoff.Next(
                        attempt, policy.ConnectRetryBaseSeconds, policy.ConnectRetryMaxSeconds);
                    OnNotice?.Invoke($"连接失败，{delay.TotalSeconds:0} 秒后重试（第 {attempt + 1} 次）");
                    var deadline = DateTime.UtcNow + delay;
                    while (DateTime.UtcNow < deadline)
                    {
                        TimeSpan left = deadline - DateTime.UtcNow;
                        await Task.Delay(left < PollInterval ? left : PollInterval, ct);
                        if (ShouldStop?.Invoke() == true) throw new OperationCanceledException(ct);
                    }
                }
            }
        }

        // OpenCode 把上游网络故障包成一段文本（如「provider.transport：ECONNRESET: ...」），
        // 没有错误码可用，只能按信号判断；只收连接层的信号，429/额度/模型报错不在内。
        private static readonly string[] ConnectSignals =
        [
            "provider.transport", "ECONNRESET", "ECONNREFUSED", "ETIMEDOUT", "ENOTFOUND", "EAI_AGAIN", "EPIPE",
            "socket connection was closed", "socket hang up", "UND_ERR_SOCKET", "fetch failed"
        ];

        /// <summary>连接层面的失败（拒绝连接、DNS 解析失败、连接被重置）才重试；能连上但对方报错的不算。</summary>
        internal static bool IsConnectFailure(Exception ex) =>
            ex is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError }
            || ex is HttpRequestException { InnerException: SocketException }
            || ex is InvalidOperationException
               && ConnectSignals.Any(signal => ex.Message.Contains(signal, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 把一次调用里的原生工具与中间正文还原成历史消息：每次原生调用补一行 tool-call 和一行
        /// tool-result，最终正文留给 Content。推理通道（Reasoning）不进历史，它只用于展示。
        /// </summary>
        internal static IReadOnlyList<LlmMessage> BuildExtraMessages(IReadOnlyList<OpenCodeMessage> messages)
        {
            var extras = new List<LlmMessage>();
            if (messages == null || messages.Count == 0) return extras;

            int lastText = -1;
            for (int i = 0; i < messages.Count; i++)
                if (!string.IsNullOrEmpty(messages[i].Text)) lastText = i;

            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                // message 内部正文与工具的先后已在解析时丢失，近似按「先调用后正文」排。
                foreach (var tool in message.NativeTools)
                {
                    if (tool.Status is "running" or "streaming" or "pending") continue;
                    extras.Add(new LlmMessage("assistant", CallLine(tool)));
                    extras.Add(new LlmMessage("user", ResultLine(tool)));
                }
                if (i != lastText && !string.IsNullOrEmpty(message.Text))
                    extras.Add(new LlmMessage("assistant", message.Text));
            }
            return extras;
        }

        private static string CallLine(OpenCodeNativeTool tool)
        {
            string args = "{}";
            if (!string.IsNullOrWhiteSpace(tool.Input))
            {
                try
                {
                    using var doc = JsonDocument.Parse(tool.Input);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object) args = tool.Input;
                }
                catch (JsonException)
                {
                }
            }
            string name = JsonSerializer.Serialize("native:" + tool.Name, AgentLoop.ToolResultJson);
            return "tool-call: {\"tool\":" + name + ",\"args\":" + args + "}";
        }

        private static string ResultLine(OpenCodeNativeTool tool)
        {
            string data = string.IsNullOrEmpty(tool.Output) ? tool.Status : tool.Output;
            return "tool-result: " + JsonSerializer.Serialize(
                new { ok = tool.Ok, data = Tools.ToolText.Clip(data) },
                AgentLoop.ToolResultJson);
        }

        internal static string Flatten(IReadOnlyList<LlmMessage> messages)
        {
            // shortcut: 单次调用只收一个 prompt，角色以标签并入正文；system 段之后立刻插入固定协议块
            var text = new StringBuilder();
            bool protocolDone = false;
            foreach (var message in messages ?? [])
            {
                if (message == null || string.IsNullOrEmpty(message.Content)) continue;
                text.Append('[').Append(message.Role).Append("]\n").Append(message.Content).Append("\n\n");
                if (!protocolDone && message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    text.Append(Protocol);
                    protocolDone = true;
                }
            }
            if (!protocolDone) text.Insert(0, Protocol + "\n");
            return text.ToString();
        }
    }
}
