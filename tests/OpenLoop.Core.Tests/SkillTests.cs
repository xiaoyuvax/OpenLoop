using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class SkillTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-skill-tests", Guid.NewGuid().ToString("N"));

    public SkillTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Load_Parses_FrontMatter_And_Hashes_File()
    {
        WriteSkill("demo", "demo-skill", "做演示", "# 正文\n步骤一");

        var skill = Assert.Single(SkillCatalog.Load(_root));
        Assert.Equal("demo-skill", skill.Name);
        Assert.Equal("做演示", skill.Description);
        Assert.Contains("步骤一", skill.Body);
        Assert.DoesNotContain("name:", skill.Body);
        Assert.Matches("^[0-9a-f]{64}$", skill.Sha256);
        Assert.Equal(".openloop/skills/demo/SKILL.md", skill.Source);
    }

    [Fact]
    public async Task ReadSkill_Returns_Body_And_Rejects_Unknown()
    {
        WriteSkill("demo", "demo-skill", "做演示", "# 正文\n步骤一");
        var tool = new ReadSkillTool(SkillCatalog.Load(_root));

        var ok = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill" }), CancellationToken.None);
        Assert.True(ok.Ok, ok.Error);
        Assert.Contains("步骤一", ok.Data);

        var bad = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "nope" }), CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Contains("未知技能", bad.Error);
    }

    [Fact]
    public void Prompt_Indexes_Skills_Without_Body()
    {
        var skills = new[]
        {
            new SkillInfo { Name = "demo", Description = "做演示", Body = "秘密步骤", Sha256 = "x", Source = "s" }
        };

        string prompt = SystemPromptBuilder.Build([], 10, skills: skills);

        Assert.Contains("# 可用技能", prompt);
        Assert.Contains("demo", prompt);
        Assert.Contains("做演示", prompt);
        Assert.DoesNotContain("秘密步骤", prompt);
    }

    [Fact]
    public void Registry_Adds_ReadSkill_Only_When_Skills_Present()
    {
        var scope = new WorkspaceScope(_root);
        var skills = new[]
        {
            new SkillInfo { Name = "demo", Description = "做演示", Body = "正文", Sha256 = "x", Source = "s" }
        };

        Assert.Null(ToolRegistry.Workspace(scope).Find("read_skill"));
        Assert.NotNull(ToolRegistry.Workspace(scope, null, skills).Find("read_skill"));
    }

    [Fact]
    public void Activation_Enables_Pinned_Skill_And_Rejects_Changed_Content()
    {
        WriteSkill("demo", "demo-skill", "做演示", "# 正文\n步骤一");
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();
        var catalog = SkillCatalog.Load(_root);
        SkillCatalog.RegisterFeatures(ledger, catalog);

        var skillFeature = Assert.Single(ledger.Features(), f => f.Kind == "skill");
        Assert.StartsWith("skill.demo-skill@", skillFeature.Id);

        var refs = ledger.Current.Features
            .Append(new FeatureRef { Id = skillFeature.Id })
            .ToList();
        var activation = ledger.Activate(refs, "test", "enable skill");
        var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);

        var active = SkillCatalog.Active(activation, features, catalog, out var problems);
        Assert.Single(active);
        Assert.Empty(problems);
        Assert.Empty(CandidateValidator.Validate(
            new ActivationRecord { Features = [new FeatureRef { Id = skillFeature.Id }] }, features));

        File.AppendAllText(SkillPath("demo"), "\nchanged", new UTF8Encoding(false));
        var changed = SkillCatalog.Load(_root);
        var after = SkillCatalog.Active(activation, features, changed, out var changedProblems);
        Assert.Empty(after);
        Assert.Contains(changedProblems, p => p.Contains("内容已变更"));
    }

    [Fact]
    public async Task ReadSkill_Reads_Reference_Files_Inside_Skill_Only()
    {
        WriteSkill("demo", "demo-skill", "做演示", "# 正文\n步骤一");
        string refs = Path.Combine(_root, ".openloop", "skills", "demo", "references");
        Directory.CreateDirectory(refs);
        File.WriteAllText(Path.Combine(refs, "ref.md"), "参考资料", new UTF8Encoding(false));

        var tool = new ReadSkillTool(SkillCatalog.Load(_root));
        var ok = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill", ["file"] = "references/ref.md" }),
            CancellationToken.None);
        Assert.True(ok.Ok, ok.Error);
        Assert.Contains("参考资料", ok.Data);

        var body = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill" }), CancellationToken.None);
        Assert.True(body.Ok);
        Assert.Contains("references/ref.md", body.Data);

        var outside = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill", ["file"] = "../demo/SKILL.md" }),
            CancellationToken.None);
        Assert.False(outside.Ok);

        var rootFile = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill", ["file"] = "SKILL.md" }),
            CancellationToken.None);
        Assert.False(rootFile.Ok);
        Assert.Contains("只允许", rootFile.Error);
    }

    [Fact]
    public async Task RunSkillScript_Needs_Approval_And_Stays_In_Scripts()
    {
        WriteSkill("demo", "demo-skill", "做演示", "正文");
        string scripts = Path.Combine(_root, ".openloop", "skills", "demo", "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "hello.ps1"), "Write-Output hi", new UTF8Encoding(false));

        var tool = new RunSkillScriptTool(_root, SkillCatalog.Load(_root), null);
        var noApproval = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill", ["script"] = "hello.ps1" }),
            CancellationToken.None);
        Assert.False(noApproval.Ok);
        Assert.Contains("批准", noApproval.Error);

        var escape = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo-skill", ["script"] = "..\\SKILL.md" }),
            CancellationToken.None);
        Assert.False(escape.Ok);
    }

    private string SkillPath(string folder)
        => Path.Combine(_root, ".openloop", "skills", folder, "SKILL.md");

    private void WriteSkill(string folder, string name, string description, string body)
    {
        string dir = Path.Combine(_root, ".openloop", "skills", folder);
        Directory.CreateDirectory(dir);
        string text = $"---\nname: {name}\ndescription: {description}\n---\n{body}";
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), text, new UTF8Encoding(false));
    }
}
