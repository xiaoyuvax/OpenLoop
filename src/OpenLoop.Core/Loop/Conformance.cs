using OpenLoop.Core.Store;

namespace OpenLoop.Core.Loop
{
    public sealed class ConformanceItem
    {
        public string Name { get; init; }
        public bool Ok { get; init; }
        public string Detail { get; init; }
    }

    /// <summary>
    /// 门禁第二层：环还守不守规矩。五项对应规范 §8 的五个可观测行为。
    /// 只查结构与来源，不查"好不好"。
    /// </summary>
    public static class Conformance
    {
        public static IReadOnlyList<ConformanceItem> Check(Ledger ledger, TaskSet tasks)
        {
            return
            [
                Recallable(ledger),
                Restorable(ledger),
                Judgeable(ledger),
                ExternallyJudged(ledger, tasks),
                Rollbackable(ledger)
            ];
        }

        private static ConformanceItem Recallable(Ledger ledger)
        {
            var runs = ledger.Runs();
            if (runs.Count == 0)
                return Item("可回指", false, "还没有任何运行证据");

            var missing = runs
                .Where(r => string.IsNullOrWhiteSpace(r.TraceDir) || !Exists(ledger, r.TraceDir))
                .ToList();
            return missing.Count == 0
                ? Item("可回指", true, $"{runs.Count} 条运行都有 trace")
                : Item("可回指", false, $"{missing.Count} 条运行找不到 trace");
        }

        private static ConformanceItem Restorable(Ledger ledger)
        {
            var activations = ledger.Activations();
            if (activations.Count == 0)
                return Item("可还原", false, "没有激活记录");

            for (int i = 1; i < activations.Count; i++)
                if (activations[i].Version <= activations[i - 1].Version)
                    return Item("可还原", false, "版本号没有递增");

            var features = ledger.Features().Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var activation in activations)
            {
                foreach (var reference in activation.Features)
                    if (reference?.Id == null || !features.Contains(reference.Id))
                        return Item("可还原", false, $"v{activation.Version} 引用了不存在的特征：{reference?.Id}");
                if (!File.Exists(Path.Combine(ledger.DefsDir, $"v{activation.Version}.json")))
                    return Item("可还原", false, $"v{activation.Version} 缺少版本文件");
            }
            return Item("可还原", true, $"{activations.Count} 个版本可重放");
        }

        private static ConformanceItem Judgeable(Ledger ledger)
        {
            var verdicts = ledger.Verdicts();
            if (verdicts.Count == 0)
                return Item("可裁决", true, "尚无变更记录");

            foreach (var verdict in verdicts)
            {
                if (string.IsNullOrWhiteSpace(verdict.Judge))
                    return Item("可裁决", false, $"{verdict.Id} 缺少裁决方");
                if (verdict.ResultVersion < verdict.BaselineVersion)
                    return Item("可裁决", false, $"{verdict.Id} 的结果版本早于基线");
                if ((verdict.Evidence is not { Count: > 0 }) && string.IsNullOrWhiteSpace(verdict.Note))
                    return Item("可裁决", false, $"{verdict.Id} 既无证据也无说明");
            }
            return Item("可裁决", true, $"{verdicts.Count} 条裁决都有对照与证据");
        }

        private static ConformanceItem ExternallyJudged(Ledger ledger, TaskSet tasks)
        {
            if (tasks.Tasks.Count == 0)
                return Item("外部裁决", false, "没有考核任务，判据不成立");

            var internalOnes = tasks.Tasks
                .Where(t => t.Source is not ("user" or "world"))
                .Select(t => t.Id)
                .ToList();
            return internalOnes.Count == 0
                ? Item("外部裁决", true, $"{tasks.Tasks.Count} 道题均由 user/world 提供")
                : Item("外部裁决", false, $"这些任务不是外部给的：{string.Join(", ", internalOnes)}");
        }

        private static ConformanceItem Rollbackable(Ledger ledger)
        {
            var activations = ledger.Activations();
            if (activations.Count == 0)
                return Item("可回滚", false, "没有激活记录");

            var rollbacks = ledger.Verdicts().Where(v => v.Decision == "rolled-back").ToList();
            foreach (var rollback in rollbacks)
                if (!File.Exists(Path.Combine(ledger.DefsDir, $"v{rollback.ResultVersion}.json")))
                    return Item("可回滚", false, $"回滚目标 v{rollback.ResultVersion} 的文件不存在");

            return Item("可回滚", true, activations.Count > 1
                ? $"{activations.Count} 个版本保留，可随时退回"
                : "只有一个版本，尚无可回滚对象");
        }

        private static bool Exists(Ledger ledger, string path)
        {
            string full = Path.IsPathRooted(path) ? path : Path.Combine(ledger.Workspace, path);
            return Directory.Exists(full) || File.Exists(full);
        }

        private static ConformanceItem Item(string name, bool ok, string detail)
            => new() { Name = name, Ok = ok, Detail = detail };
    }
}
