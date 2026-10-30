using System.Text;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Core.OpenCode
{
    /// <summary>
    /// 借用 OpenCode 的模型通道：把 OpenLoop 的消息压成单个 prompt，走无状态 generate/text。
    /// 代价：没有 system 角色、没有流式、没有工具；OpenLoop 自己的循环与护栏仍照常工作。
    /// </summary>
    public sealed class OpenCodeReasoner : IReasoner, IStreamingReasoner
    {
        private readonly OpenCodeClient _client;
        private readonly OpenCodeModelRef _model;
        private readonly string _workspace;

        public OpenCodeReasoner(OpenCodeClient client, OpenCodeModelRef model, string workspace = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _model = model;
            _workspace = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace;
        }

        public Action<OpenAiCompatibleReasoner.ReasonerProgress> OnProgress { get; set; }

        // 会话自带原生工具时，模型会改用原生调用，generate 只回文本 → 空正文。
        // 先温和提醒「原生调用不可用、工具由调用方执行」；仍为空再用强措辞兜底（强措辞会让模型自称没有工具，影响可读性）。
        private const string NoNativeTools =
            "[system]\nNative tool calling is disabled in this session. To use a tool, print the tool call as text " +
            "in the documented format; the calling application executes it and sends you the result as the next message. " +
            "Never emit native tool calls, and never claim that tools are unavailable.\n";

        private const string ForceText =
            "[system]\nYou have NO tools in this session. Never call native tools. " +
            "Reply with plain text only; when a tool is required, print the tool call as text in the documented format.\n";

        public async Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            OnProgress?.Invoke(new OpenAiCompatibleReasoner.ReasonerProgress("等待 OpenCode 响应", 0, TimeSpan.Zero));
            string prompt = Flatten(messages);
            string text = await _client.GenerateTextAsync(prompt + NoNativeTools, _model, _workspace, ct);
            if (string.IsNullOrWhiteSpace(text))
                text = await _client.GenerateTextAsync(prompt + ForceText, _model, _workspace, ct);
            return new CompletionResult { Content = text, FinishReason = "stop" };
        }

        public async Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta,
            CancellationToken ct = default, Action<string> onReasoning = null, Action onRestart = null)
        {
            var result = await CompleteAsync(messages, ct);
            if (!string.IsNullOrEmpty(result.Content)) onDelta?.Invoke(result.Content);
            return result;
        }

        internal static string Flatten(IReadOnlyList<LlmMessage> messages)
        {
            // shortcut: generate/text 只收单个 prompt 且没有 system 角色；角色以标签并入正文，结构化上下文会退化
            var text = new StringBuilder();
            foreach (var message in messages ?? [])
            {
                if (message == null || string.IsNullOrEmpty(message.Content)) continue;
                text.Append('[').Append(message.Role).Append("]\n").Append(message.Content).Append("\n\n");
            }
            return text.ToString();
        }
    }
}
