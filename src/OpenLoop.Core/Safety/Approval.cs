namespace OpenLoop.Core.Safety
{
    /// <summary>审批类别：同类决定可以用「后续都许可」记住，下次不再询问。</summary>
    public enum ApprovalKind
    {
        /// <summary>访问工作目录之外的路径。</summary>
        OutsideWorkspace,

        /// <summary>有风险的操作（危险命令、任意代码脚本等）。</summary>
        SensitiveAction
    }

    public enum ApprovalDecision
    {
        Deny,
        AllowOnce,
        AllowAlways
    }

    /// <summary>一次审批请求。Text 是给用户看的完整说明。</summary>
    public sealed class ApprovalRequest
    {
        public ApprovalRequest(ApprovalKind kind, string tool, string detail, string reason, string source = null)
        {
            Kind = kind;
            Tool = tool ?? "";
            Detail = detail ?? "";
            Reason = reason ?? "";
            Source = source;
        }

        public ApprovalKind Kind { get; }

        /// <summary>发起请求的工具名。</summary>
        public string Tool { get; }

        /// <summary>具体内容：命令、路径等。</summary>
        public string Detail { get; }

        public string Reason { get; }

        /// <summary>发起这次审批的会话标题（可空）：多个会话同时跑时，弹框必须能看出是谁在问。</summary>
        public string Source { get; set; }

        /// <summary>展示文本（含类别说明与会话来源）。</summary>
        public string Text
        {
            get
            {
                string body = Kind == ApprovalKind.OutsideWorkspace
                    ? $"访问工作目录之外的路径：\n{Detail}"
                    : $"风险操作（{Reason}）：\n{Detail}";
                return string.IsNullOrEmpty(Source) ? body : $"会话「{Source}」：\n{body}";
            }
        }
    }

    /// <summary>审批回调。返回 Deny 表示拒绝执行。</summary>
    public delegate ApprovalDecision ApprovalHandler(ApprovalRequest request);
}
