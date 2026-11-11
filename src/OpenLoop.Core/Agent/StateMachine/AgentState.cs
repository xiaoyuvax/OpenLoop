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

        /// <summary>MaxSteps ≤ 0 表示不设上限；到限判断只走这一处。</summary>
        public bool StepLimitReached => MaxSteps > 0 && Step >= MaxSteps;

        public string LastSignature { get; set; }

        /// <summary>上一批工具结果的内容签名：只有「同一调用 + 结果没变」才算空转（轮询不会被误判）。</summary>
        public string LastResultSignature { get; set; }

        public int RepeatStreak { get; set; }
        public int RepeatNudges { get; set; }

        /// <summary>已提醒过一次的重复调用签名：下一批同样的调用直接拒绝执行。</summary>
        public HashSet<string> BannedSignatures { get; } = new(StringComparer.Ordinal);

        /// <summary>轮询额度：按调用签名计（换 id 不增加额度）。</summary>
        public Dictionary<string, AgentPollGrant> PollGrants { get; } = new(StringComparer.Ordinal);

        /// <summary>上一批里是否有调用因重复被临时禁用（用于给模型一次改做法的机会）。</summary>
        public bool LastBatchBanned { get; set; }

        /// <summary>全任务已放行的轮询次数（所有签名合计）。</summary>
        public int PollTotal { get; set; }

        public int ExplorationStreak { get; set; }
        public int ExplorationNudges { get; set; }
        public int MalformedRetries { get; set; }
        public int PromiseNudges { get; set; }
        public int ClaimNudges { get; set; }
        public bool AnyToolSucceeded { get; set; }
        public int EmptyRetries { get; set; }

        /// <summary>步数上限已自动追加的次数。</summary>
        public int BudgetExtensions { get; set; }

        /// <summary>本轮自动修复了坏 JSON 后回给模型的一句说明（ToolDispatch 消费）。</summary>
        public string PendingRepairNote { get; set; }

        /// <summary>服务端上下文超限后已强制压缩的次数（压缩无效时防止无限重试）。</summary>
        public int ContextOverflows { get; set; }

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

    /// <summary>一次轮询声明在宿主侧的额度账。</summary>
    public sealed class AgentPollGrant
    {
        public string Id { get; set; }
        public int Allowed { get; set; }
        public int Used { get; set; }
    }
}
