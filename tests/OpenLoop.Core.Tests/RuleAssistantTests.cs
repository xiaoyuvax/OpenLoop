using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class RuleAssistantTests
{
    [Fact]
    public async Task Uses_own_prompt_and_normalizes_reply()
    {
        var reasoner = new ScriptedReasoner("```\n- 修改文件前先用 read_file 确认内容。\n```");

        string rule = await RuleAssistant.ImproveAsync(reasoner, "先读再写", "改之前读一下");

        Assert.Equal("修改文件前先用 read_file 确认内容。", rule);
        Assert.Single(reasoner.Calls);
        Assert.Equal("system", reasoner.Calls[0][0].Role);
        Assert.Contains("规则编辑助手", reasoner.Calls[0][0].Content);
        Assert.DoesNotContain("tool-call:", reasoner.Calls[0][0].Content);
        Assert.Contains("改之前读一下", reasoner.Calls[0][1].Content);
    }

    [Fact]
    public async Task Strips_quotes_and_newlines()
    {
        var reasoner = new ScriptedReasoner("\n“先读\n再写文件。”\n");

        string rule = await RuleAssistant.ImproveAsync(reasoner, null, "原文");

        Assert.Equal("先读 再写文件。", rule);
    }
}
