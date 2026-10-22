using OpenLoop.Core.Store;

namespace OpenLoop.Core.Loop
{
    public sealed class TaskOutcome
    {
        public bool Passed { get; set; }
        public string Detail { get; set; }
        public decimal? Cost { get; set; }
        public int Steps { get; set; }
        public string EvidenceDir { get; set; }
    }

    public interface ITaskRunner
    {
        Task<TaskOutcome> RunAsync(EvalTask task, ActivationRecord activation, CancellationToken ct);
    }

    public sealed class TaskComparison
    {
        public string TaskId { get; set; }
        public string Fingerprint { get; set; }
        public bool BaselinePassed { get; set; }
        public bool CandidatePassed { get; set; }
        public decimal? BaselineCost { get; set; }
        public decimal? CandidateCost { get; set; }
        public string BaselineDetail { get; set; }
        public string CandidateDetail { get; set; }
        public string BaselineEvidence { get; set; }
        public string CandidateEvidence { get; set; }
    }

    public sealed class GateCheck
    {
        public bool Ok { get; set; }
        public string Detail { get; set; }
    }

    public sealed class GateReport
    {
        public int BaselineVersion { get; set; }
        public int? CandidateVersion { get; set; }
        public bool Adopted { get; set; }
        public string Reason { get; set; }
        public GateCheck Build { get; set; }
        public IReadOnlyList<ConformanceItem> Conformance { get; set; } = [];
        public List<TaskComparison> Tasks { get; } = [];
        public int BaselinePassed { get; set; }
        public int CandidatePassed { get; set; }
        public decimal? Cost { get; set; }
    }

    /// <summary>
    /// 三层门禁：构建/单测 → 符合性 → 任务集 A/B。
    /// 只有第三层严格更好才更替；平局判旧版。无论结果都写裁决记录。
    /// </summary>
    public sealed class Gate
    {
        private readonly Ledger _ledger;
        private readonly TaskSet _tasks;
        private readonly IReadOnlyDictionary<string, FeatureRecord> _features;
        private readonly ITaskRunner _runner;
        private readonly Func<CancellationToken, Task<GateCheck>> _buildCheck;

        public Gate(
            Ledger ledger, TaskSet tasks, IReadOnlyDictionary<string, FeatureRecord> features,
            ITaskRunner runner, Func<CancellationToken, Task<GateCheck>> buildCheck = null)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _features = features ?? throw new ArgumentNullException(nameof(features));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _buildCheck = buildCheck;
        }

        public async Task<GateReport> EvaluateAsync(
            ActivationRecord baseline, ActivationRecord candidate, CancellationToken ct = default)
        {
            var report = new GateReport { BaselineVersion = baseline.Version };

            if (_buildCheck != null)
            {
                report.Build = await _buildCheck(ct);
                if (!report.Build.Ok)
                    return Fail(report, "构建/单元测试未通过：" + report.Build.Detail);
            }

            var conformance = Conformance.Check(_ledger, _tasks);
            report.Conformance = conformance;
            var broken = conformance.Where(c => !c.Ok).ToList();
            if (broken.Count > 0)
                return Fail(report, "符合性未通过：" + string.Join("；", broken.Select(b => $"{b.Name}（{b.Detail}）")));

            var problems = CandidateValidator.Validate(candidate, _features);
            if (problems.Count > 0)
                return Fail(report, "候选不合法：" + string.Join("；", problems));

            decimal? cost = 0;
            var evidence = new List<string>();

            foreach (var task in _tasks.Tasks)
            {
                var baselineRun = await _runner.RunAsync(task, baseline, ct);
                var candidateRun = await _runner.RunAsync(task, candidate, ct);
                cost += (baselineRun.Cost ?? 0) + (candidateRun.Cost ?? 0);
                evidence.Add(baselineRun.EvidenceDir);
                evidence.Add(candidateRun.EvidenceDir);

                report.Tasks.Add(new TaskComparison
                {
                    TaskId = task.Id,
                    Fingerprint = _tasks.Fingerprint(task),
                    BaselinePassed = baselineRun.Passed,
                    CandidatePassed = candidateRun.Passed,
                    BaselineCost = baselineRun.Cost,
                    CandidateCost = candidateRun.Cost,
                    BaselineDetail = baselineRun.Detail,
                    CandidateDetail = candidateRun.Detail,
                    BaselineEvidence = baselineRun.EvidenceDir,
                    CandidateEvidence = candidateRun.EvidenceDir
                });
            }

            int baselinePassed = report.Tasks.Count(t => t.BaselinePassed);
            int candidatePassed = report.Tasks.Count(t => t.CandidatePassed);
            report.BaselinePassed = baselinePassed;
            report.CandidatePassed = candidatePassed;
            report.Cost = cost;

            string candidatePointer = _ledger.SaveCandidate(candidate);
            evidence.Add(candidatePointer);

            if (candidatePassed > baselinePassed)
            {
                var adopted = _ledger.Activate(candidate.Features, "loop", candidate.Note, evidence);
                _ledger.AppendVerdict(new VerdictRecord
                {
                    Id = Ledger.NewId("V"),
                    Decision = "adopted",
                    BaselineVersion = baseline.Version,
                    ResultVersion = adopted.Version,
                    Judge = "task-set",
                    Note = candidate.Note,
                    Cost = cost,
                    Evidence = evidence,
                    At = DateTimeOffset.UtcNow.ToString("O")
                });
                report.Adopted = true;
                report.CandidateVersion = adopted.Version;
                report.Reason = $"候选 {candidatePassed}/{report.Tasks.Count} > 基线 {baselinePassed}/{report.Tasks.Count}";
            }
            else
            {
                _ledger.AppendVerdict(new VerdictRecord
                {
                    Id = Ledger.NewId("V"),
                    Decision = "rejected",
                    BaselineVersion = baseline.Version,
                    ResultVersion = baseline.Version,
                    Judge = "task-set",
                    Note = candidatePassed == baselinePassed ? "平局判旧版" : "候选更差",
                    Cost = cost,
                    Evidence = evidence,
                    At = DateTimeOffset.UtcNow.ToString("O")
                });
                report.Reason = candidatePassed == baselinePassed
                    ? $"平局 {candidatePassed}/{report.Tasks.Count}，保留旧版"
                    : $"候选 {candidatePassed}/{report.Tasks.Count} < 基线 {baselinePassed}/{report.Tasks.Count}，保留旧版";
            }

            return report;
        }

        private static GateReport Fail(GateReport report, string reason)
        {
            report.Reason = reason;
            return report;
        }
    }
}
