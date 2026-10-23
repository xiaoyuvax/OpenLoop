using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>Loop 专用：把审计发现追加写入 .openloop/loop/findings.jsonl。</summary>
    public sealed class RecordFindingTool : ITool
    {
        private static readonly string[] Kinds = ["access", "claim", "quality", "other"];
        private static readonly string[] Severities = ["info", "warn", "high"];

        private readonly string _workspace;

        public RecordFindingTool(string workspace) => _workspace = workspace;

        public string Name => "record_finding";
        public string Description => "把审计发现追加写入 .openloop/loop/findings.jsonl（只追加，不改账本与 trace）。发现值得记录的问题时用它留痕，不要只写在回答里。";
        public string ParamsSchema => "kind: string（access|claim|quality|other）; severity: string（info|warn|high）; summary: string（一句话结论）; evidence: string 可选（trace/账本路径或工具名）; suggestion: string 可选（建议改法）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string summary = args.Str("summary");
            if (string.IsNullOrWhiteSpace(summary))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 summary"));

            string kind = Pick(args.Str("kind"), "other", Kinds);
            string severity = Pick(args.Str("severity"), "info", Severities);

            try
            {
                string id = FindingsLog.Append(
                    _workspace, kind, severity, summary,
                    args.Str("evidence"), args.Str("suggestion"));
                return Task.FromResult(AgentToolResult.Success($"已记录发现 {id}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AgentToolResult.Failure($"记录失败：{ex.Message}"));
            }
        }

        private static string Pick(string value, string fallback, string[] allowed)
        {
            string text = value?.Trim().ToLowerInvariant();
            return allowed.Contains(text) ? text : fallback;
        }
    }
}
