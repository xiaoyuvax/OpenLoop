using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;

namespace OpenLoop.Core.Tools.Builtin
{
    /// <summary>Loop 专用：把审计发现追加写入 .openloop/loop/findings.jsonl。</summary>
    public sealed class RecordFindingTool : ITool
    {
        private static readonly string[] Kinds = ["access", "claim", "quality", "other"];
        private static readonly string[] Severities = ["info", "warn", "high"];
        private static readonly string[] Targets = [FindingTriage.Harness, FindingTriage.Source, FindingTriage.Unknown];
        private static readonly string[] Objects = ["prompt", "skill", "rule", "limit", "guard", "code", "other"];

        private readonly string _workspace;

        public RecordFindingTool(string workspace) => _workspace = workspace;

        public string Name => "RecordFinding";
        public string Description =>
            "把一条尚未处置的观察追加写入 .openloop/loop/findings.jsonl（只追加，不改账本与 trace）。" +
            "只记能分派到 Prompt/Skill/Rule/Limit/Guard/源码的问题，一条只记一个对象；" +
            "当场已修好的、环境故障、一次性事故不写。";
        public string ParamsSchema =>
            "summary: string（一句话结论）; target: string（harness|source|unknown，默认 unknown）; " +
            "object: string（prompt|skill|rule|limit|guard|code|other，默认 other）; " +
            "kind: string（access|claim|quality|other）; severity: string（info|warn|high）; " +
            "evidence: string 可选（trace/账本路径或工具名）; suggestion: string 可选（建议改法）";
        public bool IsReadOnly => false;

        public Task<AgentToolResult> ExecuteAsync(ToolArgs args, CancellationToken ct)
        {
            string summary = args.Str("summary");
            if (string.IsNullOrWhiteSpace(summary))
                return Task.FromResult(AgentToolResult.Failure("缺少参数 summary"));

            try
            {
                string id = FindingsLog.Append(
                    _workspace, summary,
                    source: "loop",
                    target: Pick(args.Str("target"), FindingTriage.Unknown, Targets),
                    objectKind: Pick(args.Str("object"), "other", Objects),
                    kind: Pick(args.Str("kind"), "other", Kinds),
                    severity: Pick(args.Str("severity"), "info", Severities),
                    evidence: args.Str("evidence"), suggestion: args.Str("suggestion"));
                return Task.FromResult(AgentToolResult.Success($"已记录发现 {id}（演进时分诊）"));
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
