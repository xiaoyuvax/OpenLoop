using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Tools;

namespace OpenLoop.Core.Tests
{
    internal sealed class ScriptedReasoner : IReasoner
    {
        private readonly Queue<string> _script;

        public ScriptedReasoner(params string[] script) => _script = new Queue<string>(script);

        public List<List<LlmMessage>> Calls { get; } = [];

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(messages.Select(m => new LlmMessage(m.Role, m.Content)).ToList());
            string next = _script.Count > 0 ? _script.Dequeue() : "完成";
            return Task.FromResult(new CompletionResult { Content = next });
        }
    }

    /// <summary>只回推理文本、正文为空的推理模型（OpenRouter 推理模型的常见形态）。</summary>
    internal sealed class ReasoningOnlyReasoner : IReasoner
    {
        private readonly string _reasoning;

        public ReasoningOnlyReasoner(string reasoning) => _reasoning = reasoning;

        public int Calls { get; private set; }

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new CompletionResult { Content = "", Reasoning = _reasoning });
        }
    }

    /// <summary>首次补全抛「上下文超限」，之后正常返回（验证按报错压缩后重试）。</summary>
    internal sealed class OverflowOnceReasoner : IReasoner
    {
        public List<List<LlmMessage>> Calls { get; } = [];

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(messages.Select(m => new LlmMessage(m.Role, m.Content)).ToList());
            if (Calls.Count == 1)
                throw new InvalidOperationException(
                    "模型接口返回 400：This model's maximum context length is 1000 tokens");
            return Task.FromResult(new CompletionResult { Content = "完成" });
        }
    }

    /// <summary>压缩摘要固定返回给定文本，其它调用返回最终答复；用于验证压缩不会无限重试。</summary>
    internal sealed class SummarizeReasoner(string summary) : IReasoner
    {
        public int Summaries { get; private set; }

        public Task<CompletionResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            bool summarizing = messages.Count == 1
                               && (messages[0].Content ?? "").StartsWith(AgentContext.SummaryInstruction,
                                   StringComparison.Ordinal);
            if (summarizing) Summaries++;
            return Task.FromResult(new CompletionResult { Content = summarizing ? summary : "已完成" });
        }
    }

    internal sealed class FakeTool : ITool
    {        public Func<ToolArgs, AgentToolResult> Handler { get; init; } = _ => AgentToolResult.Success("ok");

        public string Name { get; init; } = "echo";
        public string Description => "回显";
        public string ParamsSchema => "x: string";
        public bool IsReadOnly { get; init; }
        public int Calls { get; private set; }

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Handler(args));
        }
    }

    internal static class Loops
    {
        public static AgentLoop Build(ITool tool, IReasoner reasoner, AgentPolicy policy = null,
            Action<AgentStepRecord> onStep = null)
        {
            return new AgentLoop(new ToolRegistry([tool]), reasoner, policy ?? new AgentPolicy(), onStep);
        }

        public static string Call(string tool, string argsJson = "{}")
            => $"tool-call: {{\"tool\":\"{tool}\",\"args\":{argsJson}}}";
    }
}
