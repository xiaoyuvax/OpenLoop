using OpenLoop.Core.Acp;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.Core.Acp
{
    /// <summary>
    /// 把 dsh 的 ACP 会话当成一个模型来用：OpenLoop 把这次要说的话压成一条 prompt 交给 dsh，
    /// dsh 自己的 agent 负责调用模型、使用它的工具，结果按 <c>session/update</c> 回来。
    /// 所以流式粒度是「整段」（见 PLAN-DUAL-PROCESS §15）；工具是 dsh 的，不是 OpenLoop 的。
    /// </summary>
    public sealed class DshAcpReasoner(DshAcpHost host, string model, string workspace) : IReasoner, IStreamingReasoner
    {
        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => RunAsync(messages, null, null, ct);

        public Task<CompletionResult> StreamAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta,
            CancellationToken ct = default, Action<string> onReasoning = null, Action onRestart = null)
            => RunAsync(messages, onDelta, onReasoning, ct);

        private async Task<CompletionResult> RunAsync(
            IReadOnlyList<LlmMessage> messages, Action<string> onDelta, Action<string> onReasoning,
            CancellationToken ct)
        {
            await host.InitializeAsync(ct).ConfigureAwait(false);
            return await host.RunAsync(workspace, model, messages, onDelta, onReasoning, ct).ConfigureAwait(false);
        }
    }
}
