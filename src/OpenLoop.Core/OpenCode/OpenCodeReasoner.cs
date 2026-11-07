using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 借用 OpenCode 的模型通道：把 OpenLoop 的消息压成单个 prompt，走临时会话的单次调用。
    /// OpenLoop 自己持有全部 trace（历史、工具调用、结果、思维链）；OpenCode 只当透传壳。
    /// 会话工具权限设为 ask 并一律拒绝 —— 原生调用在那边不可能执行，工具只落到 OpenLoop。
    /// </summary>
    public sealed class OpenCodeReasoner : IReasoner, IStreamingReasoner
    {
        private readonly OpenCodeClient _client;
        private readonly OpenCodeModelRef _model;
        private readonly string _workspace;
        private readonly Func<OpenCodePermission, bool> _decide;

        public OpenCodeReasoner(OpenCodeClient client, OpenCodeModelRef model, string workspace = null,
            ApprovalHandler approve = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _model = model;
            _workspace = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace;
            _decide = OpenCodePermissions.Decider(_workspace, approve);
        }

        public Action<OpenAiCompatibleReasoner.ReasonerProgress> OnProgress { get; set; }

        // 协议块紧跟 system，保证每轮 prompt 前缀逐字一致（上游前缀缓存靠这个），也避免在尾部每次追加。
        private const string Protocol =
            "[protocol]\nNative tool calling is disabled in this session; the calling application provides the tools. " +
            "To use a tool, print one line: tool-call: {\"tool\":\"<name>\",\"args\":{...}} — the application executes that " +
            "line and sends you tool-result: {...} as the next message. Never emit native tool calls, never claim " +
            "that tools are unavailable, and never mention a rejected native call in your answer.\n";

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
            var turn = await _client.RunTurnAsync(prompt, _model, _workspace, onDelta, onReasoning, decide: _decide, ct: ct);
            if (string.IsNullOrWhiteSpace(turn.Text) && string.IsNullOrWhiteSpace(turn.Reasoning))
                turn = await _client.RunTurnAsync(prompt + RetryHint, _model, _workspace, onDelta, onReasoning, decide: _decide, ct: ct);

            return new CompletionResult
            {
                Content = turn.Text,
                Reasoning = turn.Reasoning,
                Cost = turn.Cost,
                TokensIn = turn.TokensIn,
                TokensOut = turn.TokensOut,
                FinishReason = "stop"
            };
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
