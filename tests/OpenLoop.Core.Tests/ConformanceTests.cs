using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// docs/CONFORMANCE.md 第 6 节的可执行向量（参考实现的跑法）。
/// 编号与文档一致；失败即不符合规范。
/// </summary>
public sealed class ConformanceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-conformance", Guid.NewGuid().ToString("N"));

    public ConformanceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private WorkspaceScope Scope => new(_root);

    private static ToolArgs Args(params (string, object)[] pairs)
        => new(pairs.ToDictionary(p => p.Item1, p => p.Item2));

    [Fact]
    public async Task T1_Tool_Execution_Writes_File_And_Records_Step()
    {
        var tools = ToolRegistry.Workspace(Scope);
        var reasoner = new ScriptedReasoner(
            "tool-call: {\"tool\":\"write_file\",\"args\":{\"path\":\"hello.txt\",\"content\":\"ok\"}}",
            "done");
        var loop = new AgentLoop(tools, reasoner);

        var result = await loop.RunAsync([], "create hello.txt");

        Assert.Equal("ok", File.ReadAllText(Path.Combine(_root, "hello.txt")));
        Assert.Single(result.Steps);
        Assert.True(result.Steps[0].Ok);
    }

    [Fact]
    public async Task T2_Path_Escape_Is_Rejected()
    {
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, "secret-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "top-secret");
        try
        {
            var tool = new ReadFileTool(Scope);
            var result = await tool.ExecuteAsync(Args(("path", "../" + Path.GetFileName(outside))), default);
            Assert.False(result.Ok);
            Assert.DoesNotContain("top-secret", result.Data + result.Error);
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    [Fact]
    public async Task T3_Command_Does_Not_Go_Through_Shell()
    {
        var tool = new RunCommandTool(Scope);
        var result = await tool.ExecuteAsync(
            Args(("argv", new[] { "git", "status", "--porcelain; echo pwned" })), default);

        if (!result.Ok && (result.Error ?? "").Contains("启动失败", StringComparison.Ordinal))
            return; // 环境没有 git：该向量在本机不可判定
        Assert.DoesNotContain("pwned", result.Data + result.Error);
    }

    [Fact]
    public async Task T4_Child_Environment_Has_No_Secrets()
    {
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sk-conformance");
        try
        {
            var env = ChildEnvironment.Build();
            Assert.DoesNotContain(env.Keys, k => k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        }
    }

    [Fact]
    public async Task T5_Dangerous_Command_Is_Rejected()
    {
        var tool = new RunCommandTool(Scope);
        var result = await tool.ExecuteAsync(Args(("argv", new[] { "rm", "-rf", "/" })), default);

        Assert.False(result.Ok);
        Assert.Contains("拒绝", result.Error);
        Assert.Equal(CommandRisk.Denied, CommandPolicy.Classify(["rm", "-rf", "/"]).Risk);
    }

    [Fact]
    public async Task T6_Gate_Adopts_Better_Candidate()
    {
        var (ledger, tasks, baseline, candidate) = PrepareGate("a", "b", "c");
        var runner = new PassRunner((task, version) => version == 1 ? task.Id == "a" : true);
        var gate = new Gate(ledger, tasks, ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal), runner);

        var report = await gate.EvaluateAsync(baseline, candidate);

        Assert.True(report.Adopted);
        Assert.True(report.CandidatePassed > report.BaselinePassed);
    }

    [Fact]
    public async Task T7_Gate_Keeps_Incumbent_On_Tie()
    {
        var (ledger, tasks, baseline, candidate) = PrepareGate("a", "b", "c");
        var runner = new PassRunner((task, version) => task.Id is "a" or "b");
        var gate = new Gate(ledger, tasks, ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal), runner);

        var report = await gate.EvaluateAsync(baseline, candidate);

        Assert.False(report.Adopted);
        Assert.Equal(ledger.Current.Version, baseline.Version);
    }

    [Fact(Skip = "versions/vN 与成对替换尚未实现（docs/CONFORMANCE.md T8）")]
    public void T8_Version_Rollback() { }

    [Fact]
    public async Task T9_Survey_Quota_Is_Three()
    {
        var tool = new AskUserTool(_root, () => ("s-t9", 1));
        for (int i = 0; i < 3; i++)
            Assert.True((await tool.ExecuteAsync(Args(("title", "q" + i)), default)).Ok);

        var fourth = await tool.ExecuteAsync(Args(("title", "overflow")), default);
        Assert.False(fourth.Ok);
        Assert.Equal(3, SurveyStore.Load(_root, "s-t9").Count(s => s.State == "pending"));
    }

    [Fact]
    public async Task T10_Loop_Cannot_Read_Eval_Tasks()
    {
        string dir = Path.Combine(_root, ".openloop", "eval", "tasks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "secret.json"), "{\"id\":\"hidden\"}");

        var tool = new ReadEvidenceTool(_root);
        var list = await tool.ExecuteAsync(Args(("path", "")), default);
        Assert.DoesNotContain("secret.json", list.Data + list.Error);

        var direct = await tool.ExecuteAsync(Args(("path", "eval/tasks/secret.json")), default);
        Assert.False(direct.Ok);
        Assert.DoesNotContain("hidden", direct.Data + direct.Error);
    }

    private (Ledger Ledger, TaskSet Tasks, ActivationRecord Baseline, ActivationRecord Candidate) PrepareGate(params string[] ids)
    {
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();
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

        string dir = Path.Combine(_root, ".openloop", "eval", "tasks");
        Directory.CreateDirectory(dir);
        foreach (string id in ids)
            File.WriteAllText(Path.Combine(dir, id + ".json"),
                $$"""{"id":"{{id}}","prompt":"do {{id}}","checks":[{"type":"file_exists","path":"{{id}}.txt"}]}""",
                new System.Text.UTF8Encoding(false));

        var baseline = ledger.Current;
        var candidate = new ActivationRecord
        {
            Version = baseline.Version + 1,
            At = DateTimeOffset.UtcNow.ToString("O"),
            By = "loop",
            Note = "conformance",
            Features = baseline.Features.Where(f => f.Id != FeatureCatalog.Claim).ToList()
        };
        return (ledger, TaskSet.Load(Path.Combine(_root, ".openloop", "eval")), baseline, candidate);
    }

    private sealed class PassRunner : ITaskRunner
    {
        private readonly Func<EvalTask, int, bool> _pass;
        public PassRunner(Func<EvalTask, int, bool> pass) => _pass = pass;

        public Task<TaskOutcome> RunAsync(EvalTask task, ActivationRecord activation, CancellationToken ct)
        {
            bool passed = _pass(task, activation.Version);
            return Task.FromResult(new TaskOutcome
            {
                Passed = passed,
                Detail = passed ? "ok" : "no",
                Cost = 0.01m,
                Steps = 1,
                EvidenceDir = $".openloop/eval/runs/{task.Id}-v{activation.Version}"
            });
        }
    }
}
