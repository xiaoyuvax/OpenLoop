using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Store;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class LedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-ledger-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private Ledger NewLedger()
    {
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();
        return ledger;
    }

    private static Dictionary<string, FeatureRecord> Map(Ledger ledger)
        => ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);

    [Fact]
    public void Seed_writes_features_and_v1()
    {
        var ledger = NewLedger();

        Assert.True(File.Exists(ledger.LedgerPath));
        Assert.Equal(12, ledger.Features().Count);

        var current = ledger.Current;
        Assert.NotNull(current);
        Assert.Equal(1, current.Version);
        Assert.Equal(12, current.Features.Count);

        var policy = FeatureCatalog.PolicyFrom(current, Map(ledger));
        Assert.True(policy.RepeatCallGuard);
        Assert.Equal(3, policy.MaxRepeatCalls);
        Assert.True(policy.MalformedRetry);
        Assert.Equal(100, policy.MaxSteps);
        Assert.Equal(6000, policy.ToolResultLimit);
        Assert.Equal(3, FeatureCatalog.RulesFrom(current, Map(ledger)).Count);
    }

    [Fact]
    public void AddRule_registers_dormant_feature_and_is_idempotent()
    {
        var ledger = NewLedger();

        var feature = ledger.AddRule("先读再写", "修改前先读文件。");
        Assert.StartsWith("rule.user_", feature.Id);
        Assert.Equal("rule", feature.Kind);
        Assert.Equal("修改前先读文件。", feature.Payload.GetString());
        Assert.DoesNotContain(ledger.Current.Features, r => r.Id == feature.Id);

        var again = ledger.AddRule("先读再写", "修改前先读文件。");
        Assert.Equal(feature.Id, again.Id);
        Assert.Single(ledger.Features(), f => f.Id == feature.Id);

        var edited = ledger.AddRule("先读再写", "修改前先读文件，再写。");
        Assert.NotEqual(feature.Id, edited.Id);
        Assert.Equal(14, ledger.Features().Count);
    }

    [Fact]
    public void Feature_id_cannot_be_reused()
    {
        var ledger = NewLedger();
        var duplicate = new FeatureRecord
        {
            Id = FeatureCatalog.Claim,
            Kind = "guard",
            Problem = "换个说法",
            Payload = JsonSerializer.SerializeToElement(new { enabled = false })
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ledger.AppendFeature(duplicate));
        Assert.Contains("不复用", ex.Message);
        Assert.Single(ledger.Features(), f => f.Id == FeatureCatalog.Claim);
    }

    [Fact]
    public void Activation_narrows_guards_and_keeps_old_version_bytes()
    {
        var ledger = NewLedger();
        string v1Path = Path.Combine(ledger.DefsDir, "v1.json");
        byte[] v1Before = File.ReadAllBytes(v1Path);

        var narrowed = ledger.Current.Features
            .Where(f => f.Id != FeatureCatalog.Claim)
            .ToList();
        var v2 = ledger.Activate(narrowed, "loop", "关掉 claim 护栏");

        Assert.Equal(2, v2.Version);
        var policy = FeatureCatalog.PolicyFrom(v2, Map(ledger));
        Assert.False(policy.ClaimGuard);
        Assert.True(policy.PromiseGuard);
        Assert.Equal(v1Before, File.ReadAllBytes(v1Path));
    }

    [Fact]
    public void Rollback_appends_new_version_and_records_verdict()
    {
        var ledger = NewLedger();
        var v1Ids = ledger.Current.Features.Select(f => f.Id).ToList();

        var narrowed = ledger.Current.Features
            .Where(f => f.Id != FeatureCatalog.Exploration)
            .ToList();
        ledger.Activate(narrowed, "loop", "试验性收窄");
        string v1Path = Path.Combine(ledger.DefsDir, "v1.json");
        string v2Path = Path.Combine(ledger.DefsDir, "v2.json");
        byte[] v1Before = File.ReadAllBytes(v1Path);
        byte[] v2Before = File.ReadAllBytes(v2Path);

        var restored = ledger.Rollback(1, "user", "收窄错了");

        Assert.Equal(3, restored.Version);
        Assert.Equal(v1Ids, restored.Features.Select(f => f.Id).ToList());
        Assert.Equal(v1Before, File.ReadAllBytes(v1Path));
        Assert.Equal(v2Before, File.ReadAllBytes(v2Path));
        Assert.Equal(3, ledger.Activations().Count);

        var verdict = Assert.Single(ledger.Verdicts());
        Assert.Equal("rolled-back", verdict.Decision);
        Assert.Equal(2, verdict.BaselineVersion);
        Assert.Equal(3, verdict.ResultVersion);
    }

    [Fact]
    public void Rule_order_follows_activation_order()
    {
        var ledger = NewLedger();
        var ruleIds = ledger.Current.Features
            .Where(f => f.Id.StartsWith("rule.", StringComparison.Ordinal))
            .Select(f => f.Id)
            .Reverse()
            .ToList();
        Assert.Equal(3, ruleIds.Count);

        var refs = ledger.Current.Features.Where(f => f.Id.StartsWith("rule.", StringComparison.Ordinal)).ToList();
        var reordered = new List<FeatureRef>();
        foreach (string id in ruleIds) reordered.Add(refs.First(r => r.Id == id));

        var activation = ledger.Activate(reordered, "loop", "只留规则并反序");
        var rules = FeatureCatalog.RulesFrom(activation, Map(ledger));

        Assert.Equal(3, rules.Count);
        string firstText = ledger.FindFeature(ruleIds[0]).Payload.GetString();
        Assert.Equal(firstText, rules[0]);
    }

    [Fact]
    public void Run_evidence_keeps_cost_and_pointer()
    {
        var ledger = NewLedger();
        ledger.AppendRun(new RunRecord
        {
            Id = Ledger.NewId("R"),
            ActivationVersion = 1,
            Task = "整理 notes",
            Outcome = "ok",
            Steps = 4,
            Cost = 0.0123m,
            TraceDir = ".openloop/traces/T-1",
            At = DateTimeOffset.UtcNow.ToString("O")
        });

        var run = Assert.Single(ledger.Runs());
        Assert.Equal("ok", run.Outcome);
        Assert.Equal(4, run.Steps);
        Assert.Equal(0.0123m, run.Cost);
        Assert.Equal(".openloop/traces/T-1", run.TraceDir);
    }

    [Fact]
    public void Prompt_contains_ledger_rules()
    {
        var ledger = NewLedger();
        var rules = FeatureCatalog.RulesFrom(ledger.Current, Map(ledger));

        string prompt = SystemPromptBuilder.Build([], 10, null, rules);

        Assert.Contains("# 经验规则", prompt);
        Assert.Contains(rules[0], prompt);
        Assert.True(prompt.IndexOf("# 经验规则", StringComparison.Ordinal)
                    < prompt.IndexOf(rules[0], StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_activation_reference_is_refused()
    {
        var ledger = NewLedger();
        var bogus = new List<FeatureRef> { new() { Id = "guard.not.exists" } };

        Assert.Throws<InvalidOperationException>(() => ledger.Activate(bogus, "loop", "x"));
        Assert.Equal(1, ledger.Current.Version);
    }
}
