using OpenLoop.Core.Store;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class FindingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-finding-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("/report 改模型后按钮仍显示旧值", "unknown", "other", "改模型后按钮仍显示旧值")]
    [InlineData("harness 思考链闪现太快", "harness", "other", "思考链闪现太快")]
    [InlineData("prompt 输出规则太啰嗦", "harness", "prompt", "输出规则太啰嗦")]
    [InlineData("code ChatFlow 行高算错", "source", "code", "ChatFlow 行高算错")]
    [InlineData("harness", "unknown", "other", "harness")]
    public void Report_prefix_selects_target_and_object(
        string text, string target, string objectKind, string summary)
    {
        var parsed = FindingTriage.ParseReport(text);

        Assert.Equal(target, parsed.Target);
        Assert.Equal(objectKind, parsed.ObjectKind);
        Assert.Equal(summary, parsed.Summary);
    }

    [Fact]
    public void Append_reads_back_and_legacy_line_still_opens()
    {
        string id = FindingsLog.Append(_root, "新观察", source: "user",
            target: FindingTriage.Source, objectKind: "code", severity: "warn");
        string legacyId = "F-legacy";
        File.AppendAllText(FindingsLog.FilePath(_root),
            "{\"id\":\"F-legacy\",\"kind\":\"quality\",\"severity\":\"info\",\"summary\":\"旧行\"}\n");

        var all = FindingsLog.Read(_root);
        Assert.Equal(2, all.Count);
        Assert.Equal(id, all[0].Id);
        Assert.Equal("code", all[0].Object);
        Assert.Equal(legacyId, all[1].Id);
        Assert.Null(all[1].Target);

        Assert.Equal(2, FindingTriage.OpenFindings(_root).Count);
    }

    [Fact]
    public void Triage_status_is_append_only_and_removes_from_open()
    {
        string id = FindingsLog.Append(_root, "观察");

        FindingTriage.Append(_root, id, FindingTriage.Dispatched, "B-1");
        Assert.Equal(FindingTriage.Dispatched, FindingTriage.StatusOf(_root, id));
        Assert.Empty(FindingTriage.OpenFindings(_root));

        // 再追加一条 open = 重新打开，不覆盖历史
        FindingTriage.Append(_root, id, FindingTriage.Open);
        Assert.Single(FindingTriage.OpenFindings(_root));
        Assert.Equal(2, FindingTriage.Read(_root).Count);
    }

    [Fact]
    public void Report_lands_user_finding_without_side_effects()
    {
        var (ok, message) = FindingTriage.Report(_root, "/report prompt 输出规则太啰嗦");

        Assert.True(ok);
        Assert.Contains("F-", message);
        var finding = FindingsLog.Read(_root).Single();
        Assert.Equal("user", finding.Source);
        Assert.Equal(FindingTriage.Harness, finding.Target);
        Assert.Equal("prompt", finding.Object);
        Assert.Single(FindingTriage.OpenFindings(_root));
        // 只落账：报告入口不产生处置记录
        Assert.Empty(FindingTriage.Read(_root));

        Assert.False(FindingTriage.Report(_root, "/report").Ok);
    }

    [Fact]
    public void Dispatch_work_order_lists_source_findings()
    {
        string id = FindingsLog.Append(_root, "行高算错",
            target: FindingTriage.Source, objectKind: "code", suggestion: "重算");
        var finding = FindingsLog.Read(_root).Single(f => f.Id == id);

        string path = FindingTriage.WriteDispatch(_root, "B-1", [finding]);

        Assert.NotNull(path);
        string text = File.ReadAllText(path!);
        Assert.Contains("B-1", text);
        Assert.Contains("行高算错", text);
        Assert.Contains("重算", text);
    }
}
