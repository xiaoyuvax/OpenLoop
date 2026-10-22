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

    internal sealed class FakeTool : ITool
    {
        public Func<ToolArgs, AgentToolResult> Handler { get; init; } = _ => AgentToolResult.Success("ok");

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
