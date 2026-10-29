using OpenLoop.Core.Messages;

namespace OpenLoop.Core.Agent
{
    /// <summary>
    /// 状态机在步骤之间共享的状态。消息是主状态；其余字段只保存跨步骤需要的
    /// 计数与待处理项。当前为进程内状态，宿主可自行把 Messages 与 Pending* 落盘。
    /// </summary>
    public sealed class AgentState
    {
        public AgentState(IEnumerable<LlmMessage> history, string userMessage)
        {
            Messages = new List<LlmMessage>(history ?? []);
            if (!string.IsNullOrEmpty(userMessage))
                Messages.Add(new LlmMessage("user", userMessage));
        }

        public List<LlmMessage> Messages { get; }

        public AgentRunResult Result { get; } = new();

        /// <summary>当前是第几轮模型调用（从 0 开始，InferenceStep 进入时 +1）。</summary>
        public int Step { get; set; }

        public int MaxSteps { get; set; } = new AgentPolicy().MaxSteps;

        public string LastSignature { get; set; }
        public int RepeatStreak { get; set; }
        public int ExplorationStreak { get; set; }
        public int MalformedRetries { get; set; }
        public int PromiseNudges { get; set; }
        public bool ClaimNudged { get; set; }
        public bool AnyToolSucceeded { get; set; }
        public bool EmptyRetried { get; set; }

        public string PendingContent { get; set; }
        public string PendingReasoning { get; set; }
        public string PendingFinishReason { get; set; }
        public List<AgentToolCall> PendingCalls { get; set; } = [];
        public bool PendingFinal { get; set; }

        public bool LastActionWasTools { get; set; }
        public bool ProgressChecked { get; set; }
        public string LastBatchTool { get; set; }
        public int LastBatchCount { get; set; }

        /// <summary>等待宿主确认的文本；非空时状态机停在 Yielded。</summary>
        public string PendingApproval { get; set; }

        /// <summary>宿主对 PendingApproval 的答复，由下一次 ProgressGuardStep 消费。</summary>
        public bool? ApprovalGranted { get; set; }

        public bool NeedsInference
            => Messages.Count > 0
               && string.Equals(Messages[^1].Role, "user", StringComparison.OrdinalIgnoreCase);
    }
}
