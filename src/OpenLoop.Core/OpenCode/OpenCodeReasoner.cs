using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 借用 OpenCode 的模型通道：把 OpenLoop 的消息压成单个 prompt，走临时会话的单次调用。
    /// OpenLoop 自己持有全部 trace（历史、工具调用、结果、思维链）；OpenCode 只当透传壳。
    /// 原生工具按宿主审批执行；调用与结果实时上报，作为 native 步骤写进 trace。
    /// </summary>
    public sealed class OpenCodeReasoner : IReasoner, IStreamingReasoner, INativeToolObserver, INoticeObserver
    {
        private readonly OpenCodeClient _client;
        private readonly OpenCodeModelRef _model;
        private readonly string _workspace;
        private readonly Func<OpenCodePermission, bool> _decide;
        private readonly bool _keepTools;

        public OpenCodeReasoner(OpenCodeClient client, OpenCodeModelRef model, string workspace = null,
            ApprovalHandler approve = null, bool keepTools = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _model = model;
            _workspace = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace;
            _decide = OpenCodePermissions.Decider(_workspace, approve);
            _keepTools = keepTools;
        }

        public Action<OpenAiCompatibleReasoner.ReasonerProgress> OnProgress { get; set; }

        /// <summary>原生工具调用实时上报（只用于 trace；调用结果由 OpenCode 侧决定）。</summary>
        public Action<NativeToolCall> OnNativeTool { get; set; }

        /// <summary>等待审批等即时状态（null = 清除）。界面据此显示「等待审批…」，不是空白。</summary>
        public Action<string> OnNotice { get; set; }

        /// <summary>宿主请求软停止：一次调用内部的多个原生工具之间也据此收尾。</summary>
        public Func<bool> ShouldStop { get; set; }

        /// <summary>清掉遗留的临时会话（上次进程超时/被杀没删掉的）；不 await，不影响本轮。</summary>
        public Task SweepStaleSessions() => _client.SweepStaleSessionsAsync(CancellationToken.None);

        // 协议块紧跟 system，保证每轮 prompt 前缀逐字一致（上游前缀缓存靠这个），也避免在尾部每次追加。
        private const string Protocol =
            "[protocol]\nNative tool calling is disabled in this session; the calling application provides the tools. " +
            "To use a tool, print one line: tool-call: {\"tool\":\"<name>\",\"args\":{...}} — the application executes that " +
            "line and sends you tool-result: {...} as the next message. Never emit native tool calls, never claim " +
            "that tools are unavailable, and never mention a rejected native call in your answer.\n" +
            "History may contain tool-call lines whose tool name starts with native:. They were already executed by the " +
            "runtime that wraps you; use them as facts and never emit a native: call yourself.\n";

        // 只在上一轮完全没正文时追加在末尾（新开一次调用，不影响正常轮的前缀）。
        private const string RetryHint =
            "\n[system]\nYour previous reply contained no text. Send your tool call again as one line: " +
            "tool-call: {\"tool\":\"<name>\",\"args\":{...}}; otherwise answer in plain text.\n";

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => RunAsync(messages, null, null, ct);

        public Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta,
            CancellationToken ct = default, Action<string> onReasoning = null, Action onRestart = null)
            => RunAsync(messages, onDelta, onReasoning, ct);

        private async Task<CompletionResult> RunAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta, Action<string> onReasoning, CancellationToken ct)
        {
            OnProgress?.Invoke(new OpenAiCompatibleReasoner.ReasonerProgress("等待 OpenCode 响应", 0, TimeSpan.Zero));
            string prompt = Flatten(messages);
            // 免费模型保留工具表只为过上游门禁；原生工具按宿主审批执行，调用与结果实时上报。
            var decide = _decide;
            var native = new List<OpenCodeNativeTool>();
            Action<OpenCodeNativeTool> nativeSink = tool =>
            {
                native.Add(tool);
                OnNativeTool?.Invoke(new NativeToolCall(tool.Name, tool.Input, tool.Ok, tool.Output, tool.Status, tool.DurationMs));
            };
            var turn = await RetryConnectAsync(() => _client.RunTurnAsync(prompt, _model, _workspace, onDelta, onReasoning,
                decide: decide, keepTools: _keepTools, onNativeTool: nativeSink, ct: ct, onNotice: OnNotice,
                shouldStop: ShouldStop), ct);
            // 只调了原生工具、没有正文是免费模型的常态：有原生结果就不要再整轮重发一次。
            if (string.IsNullOrWhiteSpace(turn.Text) && string.IsNullOrWhiteSpace(turn.Reasoning)
                && native.Count == 0 && ShouldStop?.Invoke() != true)
                turn = await RetryConnectAsync(() => _client.RunTurnAsync(prompt + RetryHint, _model, _workspace, onDelta, onReasoning,
                    decide: decide, keepTools: _keepTools, onNativeTool: nativeSink, ct: ct, onNotice: OnNotice,
                    shouldStop: ShouldStop), ct);

            return new CompletionResult
            {
                Content = turn.Text,
                Reasoning = turn.Reasoning,
                Cost = turn.Cost,
                TokensIn = turn.TokensIn,
                TokensOut = turn.TokensOut,
                ExtraMessages = BuildExtraMessages(turn.Messages),
                NativeToolSucceeded = native.Any(tool => tool.Ok),
                FinishReason = "stop"
            };
        }

        // shortcut: 原生结果不跟随 AgentPolicy.ToolResultLimit（默认 6000），按 4 倍放宽；
        // 免费模型窗口或上下文压力真的撞上时再改成可配置。
        internal const int NativeResultLimit = 24000;

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 连不上本地 opencode/代理（服务重启、代理没起来、DNS 还没通）不算这一轮失败：退避重试到连上为止。
        /// 用户停止（ct）或软打断就按「已停止」收尾；HTTP 状态错误、上游报的失败照旧直接抛。
        /// </summary>
        private async Task<OpenCodeTurn> RetryConnectAsync(Func<Task<OpenCodeTurn>> call, CancellationToken ct)
        {
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
                    if (ct.IsCancellationRequested || ShouldStop?.Invoke() == true)
                        throw new OperationCanceledException(ct);

                    retried = true;
                    TimeSpan delay = RetryBackoff.Next(attempt);
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

        /// <summary>连接层面的失败（拒绝连接、DNS 解析失败）才重试；能连上但对方报错的不算。</summary>
        internal static bool IsConnectFailure(Exception ex) =>
            ex is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError }
            || ex is HttpRequestException { InnerException: SocketException };

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
                new { ok = tool.Ok, data = AgentLoop.Truncate(data, NativeResultLimit) },
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
