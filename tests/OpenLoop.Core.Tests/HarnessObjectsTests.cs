using System.Text.Json;
using OpenLoop.Core.Store;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// 工作区 Harness 配置的纯逻辑：列对象、解析参数、按类替换激活集成员、出厂默认。
/// 这些是配置窗体的判据，不能靠界面手点验证。
/// </summary>
public sealed class HarnessObjectsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-harness-objects", Guid.NewGuid().ToString("N"));

    public HarnessObjectsTests()
    {
        Directory.CreateDirectory(_root);
        Ledger.Open(_root).EnsureInitialized();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void List_ReportsActiveStateAndValues_ForEachKind()
    {
        var ledger = Ledger.Open(_root);

        var limits = HarnessObjects.List(ledger, HarnessObjects.Limit);
        Assert.NotEmpty(limits);
        Assert.All(limits, item => Assert.Equal(HarnessObjects.Limit, item.Kind));
        var toolResult = limits.First(item => item.Id == FeatureCatalog.ToolResultLimit);
        Assert.True(toolResult.Active);
        Assert.Equal("51200", toolResult.Values["value"]);
        Assert.Equal("51200", toolResult.Defaults["value"]);
        Assert.Contains("value=51200", toolResult.ParamsText);

        var puppetResult = limits.First(item => item.Id == FeatureCatalog.PuppetResultLimit);
        Assert.True(puppetResult.Active);
        Assert.Equal("51200", puppetResult.Defaults["value"]);

        // 规则：正文与参数分开，规则没有参数
        var rules = HarnessObjects.List(ledger, HarnessObjects.Rule);
        Assert.NotEmpty(rules);
        Assert.All(rules, item => Assert.Empty(item.Values));
        Assert.All(rules, item => Assert.False(string.IsNullOrWhiteSpace(item.Text)));
        Assert.Contains(rules, item => item.Active);

        // 护栏里背负开关的项：默认值与生效值一致
        var claim = HarnessObjects.List(ledger, HarnessObjects.Guard)
            .First(item => item.Id == FeatureCatalog.Claim);
        Assert.True(claim.Active);
        Assert.Equal("true", claim.Values["enabled"]);
    }

    [Fact]
    public void ParseParams_UsesDefaultValueKind_AndRejectsUnknownKeys()
    {
        var feature = Ledger.Open(_root).FindFeature(FeatureCatalog.RepeatCall);

        var ok = HarnessObjects.ParseParams(feature, "enabled=false, max=5", out string error);
        Assert.NotNull(ok);
        Assert.Null(error);
        Assert.False(ok["enabled"].GetBoolean());
        Assert.Equal(5, ok["max"].GetInt32());

        Assert.Null(HarnessObjects.ParseParams(feature, "max=很多", out string badNumber));
        Assert.Contains("max", badNumber);

        Assert.Null(HarnessObjects.ParseParams(feature, "enabled=yes", out string badFlag));
        Assert.Contains("enabled", badFlag);

        Assert.Null(HarnessObjects.ParseParams(feature, "nope=1", out string unknown));
        Assert.Contains("nope", unknown);

        Assert.Null(HarnessObjects.ParseParams(feature, "max", out string malformed));
        Assert.False(string.IsNullOrWhiteSpace(malformed));

        // 小数（compress.at 这类 0.8 的阈值）按默认值的数字类型收
        var compressAt = Ledger.Open(_root).FindFeature(FeatureCatalog.CompressAt);
        var real = HarnessObjects.ParseParams(compressAt, "value=0.5", out string realError);
        Assert.NotNull(real);
        Assert.Null(realError);
        Assert.Equal(0.5, real["value"].GetDouble(), 3);
    }

    [Fact]
    public void Replace_SwapsOnlyOneKind_AndKeepsTheRestInOrder()
    {
        var ledger = Ledger.Open(_root);
        var current = ledger.Current;
        var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);

        var replacement = new[]
        {
            new FeatureRef { Id = FeatureCatalog.ToolResultLimit, Params = Params(("value", 9000)) }
        };
        var result = HarnessObjects.Replace(current, features, HarnessObjects.Limit, replacement);

        Assert.Equal(current.Features.Count - LimitCount(current, features) + 1, result.Count);
        Assert.Contains(result, r => r.Id == FeatureCatalog.ToolResultLimit);
        Assert.DoesNotContain(result, r => r.Id == FeatureCatalog.MaxSteps);      // 被替换掉的同类
        Assert.Contains(result, r => r.Id == FeatureCatalog.Claim);               // 别的类原样在
        // 顺序：别的类仍在前面，替换项接在最后
        Assert.Equal(FeatureCatalog.ToolResultLimit, result[^1].Id);
    }

    [Fact]
    public void Defaults_ComeFromSeedActivation_AndSkillsHaveNone()
    {
        Assert.Empty(HarnessObjects.Defaults(HarnessObjects.Skill));

        var guards = HarnessObjects.Defaults(HarnessObjects.Guard);
        Assert.Contains(guards, r => r.Id == FeatureCatalog.Claim);
        Assert.All(guards, r => Assert.Equal(HarnessObjects.Guard, HarnessObjects.KindOfSeedId(r.Id)));

        var limits = HarnessObjects.Defaults(HarnessObjects.Limit);
        Assert.Contains(limits, r => r.Id == FeatureCatalog.MaxSteps);
        Assert.Equal(limits.Count, limits.Select(r => r.Id).Distinct().Count());

        Assert.Equal(HarnessObjects.Rule, HarnessObjects.KindOfSeedId("rule.answer_first"));
        Assert.Equal("", HarnessObjects.KindOfSeedId("something.else"));
    }

    private static Dictionary<string, JsonElement> Params(params (string Name, object Value)[] pairs)
        => pairs.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value));

    private static int LimitCount(
        ActivationRecord current, IReadOnlyDictionary<string, FeatureRecord> features)
        => current.Features.Count(r => r?.Id != null && features.TryGetValue(r.Id, out var f)
            && f.Kind == HarnessObjects.Limit);
}
