using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Reasoning
{
    /// <summary>
    /// 推理边界：给一串消息，返回下一轮模型输出。
    /// 直连模型与外部进程（把任务交给别的可执行程序）都实现这一个接口。
    /// </summary>
    public interface IReasoner
    {
        Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default);
    }

    /// <summary>一次补全的结果。成本为 null 表示该通道不报价。</summary>
    public sealed class CompletionResult
    {
        public string Content { get; init; }

        /// <summary>推理通道文本（上游的 reasoning / reasoning_content 增量合并）。
        /// 有的推理模型把思考都放在这里、正文为空，调用方可用它兜底。</summary>
        public string Reasoning { get; init; }

        public decimal? Cost { get; init; }
        public string FinishReason { get; init; }

        /// <summary>本次请求的输入 token 数（usage.prompt_tokens），未报价时为 null。</summary>
        public int? TokensIn { get; init; }

        /// <summary>本次请求的输出 token 数（usage.completion_tokens），未报价时为 null。</summary>
        public int? TokensOut { get; init; }
    }

    /// <summary>可选的流式通道：每来一段就回调，返回最终完整结果。</summary>
    public interface IStreamingReasoner
    {
        /// <param name="onDelta">正文增量。</param>
        /// <param name="onReasoning">推理通道增量（上游不提供推理时永远不回调）。</param>
        Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta,
            CancellationToken ct = default, Action<string> onReasoning = null);
    }
}
