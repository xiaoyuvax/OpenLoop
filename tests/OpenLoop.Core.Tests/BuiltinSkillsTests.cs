using System.Globalization;
using OpenLoop.Core.Skills;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class BuiltinSkillsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-builtin-skills", Guid.NewGuid().ToString("N"));

    public BuiltinSkillsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void ZCurl_Only_On_Chinese_Windows_With_It_On_Path()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_root, "zcurl.exe"), "");

        var zh = CultureInfo.GetCultureInfo("zh-CN");
        var en = CultureInfo.GetCultureInfo("en-US");

        var skill = Assert.Single(BuiltinSkills.For(_root, zh));
        Assert.Equal("zcurl", skill.Name);
        Assert.Contains("https://github.com/xiaoyuvax/zcurl", skill.Body);
        Assert.Empty(BuiltinSkills.For(_root, en));
        Assert.Empty(BuiltinSkills.For(Path.Combine(_root, "missing"), zh));
    }

    [Fact]
    public void Merge_Keeps_Users_Own_ZCurl_Skill()
    {
        var own = new[]
        {
            new SkillInfo { Name = "ZCurl", Description = "用户版", Body = "正文" }
        };

        var merged = BuiltinSkills.Merge(own);

        Assert.Equal("正文", Assert.Single(merged).Body);
    }

    [Fact]
    public void Peer_board_skill_only_when_the_workspace_has_peers()
    {
        using var identity = OpenLoop.Core.Agent.AgentIdentity.Begin("A-self", "self");

        Assert.DoesNotContain(BuiltinSkills.Merge([]), s => s.Name == BuiltinSkills.PeerBoardName);

        var merged = BuiltinSkills.Merge([], ["A-other（在做：改 ChatFlow；最近：Edit[x] → ok）"]);

        var skill = Assert.Single(merged, s => s.Name == BuiltinSkills.PeerBoardName);
        Assert.Contains("A-other", skill.Description);
        Assert.Contains("Edit[x] → ok", skill.Body);
    }
}
