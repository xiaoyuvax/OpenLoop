using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Agent
{
    /// <summary>要落到状态上的结果。事件（流式增量、工具开始）仍由步骤直接回调。</summary>
    public interface IAgentEffect
    {
        void Apply(AgentState state, AgentRunContext context);
    }

    public sealed class AppendMessageEffect : IAgentEffect
    {
        private readonly LlmMessage _message;

        public AppendMessageEffect(string role, string content)
            => _message = new LlmMessage(role, content);

        public void Apply(AgentState state, AgentRunContext context)
            => state.Messages.Add(_message);
    }

    public sealed class ReplaceMessagesEffect : IAgentEffect
    {
        private readonly IReadOnlyList<LlmMessage> _messages;

        public ReplaceMessagesEffect(IReadOnlyList<LlmMessage> messages)
            => _messages = messages;

        public void Apply(AgentState state, AgentRunContext context)
        {
            state.Messages.Clear();
            state.Messages.AddRange(_messages);
        }
    }

    public sealed class RecordUsageEffect : IAgentEffect
    {
        private readonly decimal? _cost;
        private readonly int? _contextTokens;

        public RecordUsageEffect(decimal? cost, int? contextTokens)
        {
            _cost = cost;
            _contextTokens = contextTokens;
        }

        public void Apply(AgentState state, AgentRunContext context)
        {
            if (_cost is decimal cost) state.Result.ChatCost = (state.Result.ChatCost ?? 0m) + cost;
            if (_contextTokens is int tokens) state.Result.ContextTokens = tokens;
        }
    }

    public sealed class RecordStepEffect : IAgentEffect
    {
        private readonly AgentStepRecord _record;

        public RecordStepEffect(AgentStepRecord record) => _record = record;

        public void Apply(AgentState state, AgentRunContext context)
        {
            state.Result.Steps.Add(_record);
            context.OnStep?.Invoke(_record);
        }
    }

    public sealed class SetFinalAnswerEffect : IAgentEffect
    {
        private readonly string _answer;
        private readonly string _finishReason;

        public SetFinalAnswerEffect(string answer, string finishReason = null)
        {
            _answer = answer;
            _finishReason = finishReason;
        }

        public void Apply(AgentState state, AgentRunContext context)
        {
            state.Result.FinalAnswer = _answer;
            state.Result.FinishReason = _finishReason;
        }
    }

    public sealed class SetBudgetExhaustedEffect : IAgentEffect
    {
        public void Apply(AgentState state, AgentRunContext context)
            => state.Result.BudgetExhausted = true;
    }

    public sealed class SetStoppedByUserEffect : IAgentEffect
    {
        public void Apply(AgentState state, AgentRunContext context)
            => state.Result.StoppedByUser = true;
    }

    public sealed class SetGuardedStopEffect : IAgentEffect
    {
        public void Apply(AgentState state, AgentRunContext context)
            => state.Result.StoppedByGuard = true;
    }
}
