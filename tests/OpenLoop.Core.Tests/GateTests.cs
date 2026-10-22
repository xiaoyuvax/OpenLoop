using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class GateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-gate-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private Ledger NewLedger(bool withRun = true)
    {
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();
        if (withRun)
        {
            string trace = Path.Combine(_root, ".openloop", "traces", "T-1");
            Directory.CreateDirectory(trace);
            ledger.AppendRun(new RunRecord
            {
                Id = Ledger.NewId("R"),
                ActivationVersion = 1,
                Task = "seed",
                Outcome = "ok",
                Steps = 1,
                Cost = 0.001m,
                TraceDir = ".openloop/traces/T-1",
                At = DateTimeOffset.UtcNow.ToString("O")
            });
        }
        return ledger;
    }

    private TaskSet AddTasks(params string[] ids)
    {
        string dir = Path.Combine(_root, ".openloop", "eval", "tasks");
        Directory.CreateDirectory(dir);
        foreach (string id in ids)
        {
            File.WriteAllText(Path.Combine(dir, id + ".json"), $$"""
            {
              "id": "{{id}}",
              "prompt": "完成 {{id}}",
              "checks": [ { "type": "file_exists", "path": "{{id}}.txt" } ]
            }
            """, new System.Text.UTF8Encoding(false));
        }
        return TaskSet.Load(Path.Combine(_root, ".openloop", "eval"));
    }

    private static Dictionary<string, FeatureRecord> Map(Ledger ledger)
        => ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);

    private static ActivationRecord Narrow(ActivationRecord baseline, params string[] removed)
    {
        var drop = removed.ToHashSet(StringComparer.Ordinal);
        return new ActivationRecord
        {
            Version = baseline.Version + 1,
            At = DateTimeOffset.UtcNow.ToString("O"),
            By = "loop",
            Note = "test",
            Features = baseline.Features.Where(f => !drop.Contains(f.Id)).ToList()
        };
    }

    private sealed class FakeRunner : ITaskRunner
    {
        private readonly Func<EvalTask, int, bool> _pass;
        public FakeRunner(Func<EvalTask, int, bool> pass) => _pass = pass;
        public int Calls { get; private set; }
        public decimal Cost { get; init; } = 0.01m;

        public Task<TaskOutcome> RunAsync(EvalTask task, ActivationRecord activation, CancellationToken ct)
        {
            Calls++;
            bool passed = _pass(task, activation.Version);
            return Task.FromResult(new TaskOutcome
            {
                Passed = passed,
                Detail = passed ? "ok" : "no",
                Cost = Cost,
                Steps = 1,
                EvidenceDir = $".openloop/eval/runs/{task.Id}-v{activation.Version}"
            });
        }
    }

    [Fact]
    public async Task Gate_adopts_strictly_better_candidate()
    {
        var ledger = NewLedger();
        var tasks = AddTasks("a", "b", "c");
        var baseline = ledger.Current;
        var candidate = Narrow(baseline, FeatureCatalog.Claim);

        var runner = new FakeRunner((task, version) =>
            version == 1 ? task.Id == "a" : task.Id is "a" or "b");
        var gate = new Gate(ledger, tasks, Map(ledger), runner);

        var report = await gate.EvaluateAsync(baseline, candidate);

        Assert.True(report.Adopted);
        Assert.Equal(2, report.CandidatePassed);
        Assert.Equal(1, report.BaselinePassed);
        Assert.Equal(2, ledger.Current.Version);
        Assert.Equal(candidate.Features.Select(f => f.Id), ledger.Current.Features.Select(f => f.Id));
        Assert.Equal(0.06m, report.Cost);

        var verdict = Assert.Single(ledger.Verdicts());
        Assert.Equal("adopted", verdict.Decision);
        Assert.Equal(1, verdict.BaselineVersion);
        Assert.Equal(2, verdict.ResultVersion);
        Assert.Contains(verdict.Evidence, e => e.Contains("candidates/"));
    }

    [Fact]
    public async Task Gate_keeps_incumbent_on_tie()
    {
        var ledger = NewLedger();
        var tasks = AddTasks("a", "b");
        var baseline = ledger.Current;
        var candidate = Narrow(baseline, FeatureCatalog.Claim);

        var runner = new FakeRunner((task, _) => task.Id == "a");
        var gate = new Gate(ledger, tasks, Map(ledger), runner);

        var report = await gate.EvaluateAsync(baseline, candidate);

        Assert.False(report.Adopted);
        Assert.Equal(1, ledger.Current.Version);
        var verdict = Assert.Single(ledger.Verdicts());
        Assert.Equal("rejected", verdict.Decision);
        Assert.Equal("平局判旧版", verdict.Note);
        Assert.Equal(1, verdict.ResultVersion);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_root, ".openloop", "eval", "candidates")));
    }

    [Fact]
    public async Task Gate_stops_when_build_fails()
    {
        var ledger = NewLedger();
        var tasks = AddTasks("a");
        var runner = new FakeRunner((_, _) => true);
        var gate = new Gate(ledger, tasks, Map(ledger), runner,
            _ => Task.FromResult(new GateCheck { Ok = false, Detail = "编译失败" }));

        var report = await gate.EvaluateAsync(ledger.Current, Narrow(ledger.Current, FeatureCatalog.Claim));

        Assert.False(report.Adopted);
        Assert.Contains("构建", report.Reason);
        Assert.Equal(0, runner.Calls);
        Assert.Empty(ledger.Verdicts());
    }

    [Fact]
    public async Task Gate_stops_when_conformance_fails()
    {
        var ledger = NewLedger(withRun: false);
        var tasks = AddTasks("a");
        var runner = new FakeRunner((_, _) => true);
        var gate = new Gate(ledger, tasks, Map(ledger), runner);

        var report = await gate.EvaluateAsync(ledger.Current, Narrow(ledger.Current, FeatureCatalog.Claim));

        Assert.False(report.Adopted);
        Assert.Contains("符合性", report.Reason);
        Assert.Contains(report.Conformance, c => c.Name == "可回指" && !c.Ok);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void Validator_rejects_unknown_features_and_bad_params()
    {
        var ledger = NewLedger();
        var features = Map(ledger);
        var bogus = new ActivationRecord
        {
            Features = [new FeatureRef { Id = "guard.invented" }]
        };
        Assert.Contains(CandidateValidator.Validate(bogus, features), p => p.Contains("不得凭空命名"));

        var badRange = new ActivationRecord
        {
            Features =
            [
                new FeatureRef
                {
                    Id = FeatureCatalog.RepeatCall,
                    Params = new() { ["max"] = JsonSerializer.SerializeToElement(99) }
                }
            ]
        };
        Assert.Contains(CandidateValidator.Validate(badRange, features), p => p.Contains("超出允许范围"));

        var badKey = new ActivationRecord
        {
            Features =
            [
                new FeatureRef
                {
                    Id = FeatureCatalog.Claim,
                    Params = new() { ["max"] = JsonSerializer.SerializeToElement(3) }
                }
            ]
        };
        Assert.Contains(CandidateValidator.Validate(badKey, features), p => p.Contains("不认识参数"));

        var ok = new ActivationRecord
        {
            Features = [new FeatureRef { Id = FeatureCatalog.Claim, Params = new() { ["enabled"] = JsonSerializer.SerializeToElement(false) } }]
        };
        Assert.Empty(CandidateValidator.Validate(ok, features));
    }

    [Fact]
    public void TaskSet_fingerprint_tracks_seed_content()
    {
        string dir = Path.Combine(_root, ".openloop", "eval", "tasks");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "fp.json");
        File.WriteAllText(path, """
        { "id":"fp", "prompt":"x", "seed":{"a.txt":"1"},
          "checks":[{"type":"file_exists","path":"a.txt"}] }
        """, new System.Text.UTF8Encoding(false));

        var first = TaskSet.Load(Path.Combine(_root, ".openloop", "eval"));
        string fp1 = first.Fingerprint(first.Tasks[0]);

        File.WriteAllText(path, """
        { "id":"fp", "prompt":"x", "seed":{"a.txt":"2"},
          "checks":[{"type":"file_exists","path":"a.txt"}] }
        """, new System.Text.UTF8Encoding(false));
        var second = TaskSet.Load(Path.Combine(_root, ".openloop", "eval"));
        string fp2 = second.Fingerprint(second.Tasks[0]);

        Assert.NotEqual(fp1, fp2);
        Assert.Equal("user", second.Tasks[0].Source);
    }

    [Fact]
    public void TaskJudge_checks_files_and_exit_codes()
    {
        string dir = Path.Combine(_root, "judge");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello ok\r\n");

        var pass = TaskJudge.Evaluate(dir,
        [
            new TaskCheck { Type = "file_exists", Path = "a.txt" },
            new TaskCheck { Type = "file_contains", Path = "a.txt", Text = "ok" },
            new TaskCheck { Type = "file_equals", Path = "a.txt", Text = "hello ok" },
            new TaskCheck { Type = "file_missing", Path = "b.txt" },
            new TaskCheck { Type = "command", Args = ["cmd", "/c", "exit", "3"], ExpectExit = 3 }
        ]);
        Assert.True(pass.Passed, pass.Detail);

        var fail = TaskJudge.Evaluate(dir,
        [
            new TaskCheck { Type = "file_contains", Path = "a.txt", Text = "missing-marker" }
        ]);
        Assert.False(fail.Passed);
        Assert.Contains("不含", fail.Detail);
    }

    [Fact]
    public async Task LoopAgent_parses_json_and_validates()
    {
        var ledger = NewLedger();
        var features = Map(ledger);
        string packet = "packet";

        var good = new ScriptedReasoner("""
        {"note":"关掉 claim 护栏","features":[{"id":"guard.claim","params":{"enabled":false}}]}
        """);
        var proposal = await new LoopAgent(good).ProposeAsync(packet, ledger.Current, features);
        Assert.Null(proposal.Error);
        Assert.Single(proposal.Candidate.Features);
        Assert.Equal("guard.claim", proposal.Candidate.Features[0].Id);

        var bad = new ScriptedReasoner("""
        {"note":"x","features":[{"id":"guard.invented"}]}
        """);
        var rejected = await new LoopAgent(bad).ProposeAsync(packet, ledger.Current, features);
        Assert.NotNull(rejected.Error);
        Assert.Contains(rejected.Problems, p => p.Contains("不得凭空命名"));

        var garbage = new ScriptedReasoner("没有 JSON");
        var none = await new LoopAgent(garbage).ProposeAsync(packet, ledger.Current, features);
        Assert.Contains("没有 JSON", none.Error);
    }

    [Fact]
    public async Task AgentTaskRunner_runs_isolated_workspace_and_judges()
    {
        var ledger = NewLedger();
        var features = Map(ledger);
        var reasoner = new ScriptedReasoner(
            Loops.Call("write_file", "{\"path\":\"hello.txt\",\"content\":\"ok\"}"),
            "完成");

        var runner = new AgentTaskRunner(_ => reasoner, features, _root,
            Path.Combine(_root, ".openloop", "eval"));
        var task = new EvalTask
        {
            Id = "write-hello",
            Prompt = "在当前目录创建 hello.txt，内容写 ok。",
            Checks =
            [
                new TaskCheck { Type = "file_exists", Path = "hello.txt" },
                new TaskCheck { Type = "file_contains", Path = "hello.txt", Text = "ok" }
            ]
        };

        var outcome = await runner.RunAsync(task, ledger.Current, CancellationToken.None);

        Assert.True(outcome.Passed, outcome.Detail);
        Assert.Equal(1, outcome.Steps);
        string dir = Path.Combine(_root, outcome.EvidenceDir);
        Assert.True(Directory.Exists(dir));
        Assert.Equal("ok", File.ReadAllText(Path.Combine(dir, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "final.md")));
        Assert.True(File.Exists(Path.Combine(dir, "run.json")));
    }
}
