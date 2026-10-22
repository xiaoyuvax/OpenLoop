using System.Globalization;

namespace OpenLoop.Core.Agent
{
    /// <summary>一次工具执行记录（界面步骤流 + trace 用）。</summary>
    public sealed class AgentStepRecord
    {
        public int Index { get; init; }
        public string Tool { get; init; }
        public string ArgsSummary { get; init; }
        public string ArgsFull { get; init; }
        public bool Ok { get; init; }
        public string Summary { get; init; }
        public string FullResult { get; init; }
        public string ResultFull { get; init; }
        public bool ArgsRepaired { get; init; }
        public long DurationMs { get; init; }

        public string DurationText => DurationMs < 1000
            ? $"{DurationMs}ms"
            : (DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
    }
}
