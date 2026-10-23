using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class AskUserToolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-ask-tests", Guid.NewGuid().ToString("N"));

    public AskUserToolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static ToolArgs Args(params (string, object)[] pairs)
        => new(pairs.ToDictionary(p => p.Item1, p => p.Item2));

    [Fact]
    public async Task Inserts_Card_At_Current_Turn_And_Caps_Pending()
    {
        var tool = new AskUserTool(_root, () => ("s1", 4));

        var first = await tool.ExecuteAsync(
            Args(("title", "合理吗"), ("dimension", "tool_usage")), default);
        Assert.True(first.Ok, first.Error);

        var cards = SurveyStore.Load(_root, "s1");
        Assert.Single(cards);
        Assert.Equal(4, cards[0].AnchorTurn);
        Assert.Equal("#FA8C16", cards[0].Accent);

        for (int i = 0; i < 2; i++)
            Assert.True((await tool.ExecuteAsync(Args(("title", "再来一次")), default)).Ok);

        var fourth = await tool.ExecuteAsync(Args(("title", "超额")), default);
        Assert.False(fourth.Ok);
        Assert.Contains("上限", fourth.Error);
    }

    [Fact]
    public async Task Requires_An_Active_Session()
    {
        var tool = new AskUserTool(_root, () => ("", -1));
        var result = await tool.ExecuteAsync(Args(("title", "合理吗")), default);
        Assert.False(result.Ok);
        Assert.Contains("会话", result.Error);
    }
}
