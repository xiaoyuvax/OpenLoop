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

    [Fact]
    public void Load_Parses_Real_World_Skill_Shape()
    {
        //形如 GitHub 公开技能包（anthropics/skills 那类）的 SKILL.md：长描述 + license 行 + 正文引用 references/
        string dir = Path.Combine(_root, ".openloop", "skills", "pdf");
        Directory.CreateDirectory(dir);
        string description = "Use this skill whenever the user wants to do anything with PDF files. "
            + "This includes reading or extracting text or tables from PDFs, merging several PDFs into one, "
            + "splitting PDFs apart, rotating pages, adding watermarks, creating new PDFs and filling PDF forms. "
            + "If the user mentions a .pdf file or asks to produce one, use this skill.";
        string text = $"""
            ---
            name: pdf
            description: {description}
            license: Proprietary. LICENSE.txt has complete terms
            ---

            # PDF Processing Guide

            ## Overview

            处理 PDF 的常用做法，细节见 REFERENCE.md。

            | 任务 | 工具 |
            | --- | --- |
            | 合并 | pypdf |
            """;
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), text, new UTF8Encoding(false));

        var skill = Assert.Single(SkillCatalog.Load(_root));

        Assert.Equal("pdf", skill.Name);
        Assert.EndsWith("…", skill.Description);   //长描述压成一行并截断
        Assert.Equal(201, skill.Description.Length);
        Assert.Contains("# PDF Processing Guide", skill.Body);
        Assert.Contains("| 合并 | pypdf |", skill.Body);
        Assert.DoesNotContain("license:", skill.Body);
        Assert.Equal(".openloop/skills/pdf/SKILL.md", skill.Source);
    }

    [Fact]
    public void Sync_Enables_New_Skills_And_Respects_Disable()
    {
        WriteSkill("demo", "demo-skill", "做演示", "正文");
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();

        //新装的技能：第一次 Sync 就启用
        var first = SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out var problems);
        Assert.Single(first);
        Assert.Empty(problems);

        //用户停用后不会被自动重开
        SkillCatalog.SetEnabled(ledger, SkillCatalog.Load(_root), "demo-skill", false);
        Assert.Empty(SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out _));

        //手动再启用就回来
        SkillCatalog.SetEnabled(ledger, SkillCatalog.Load(_root), "demo-skill", true);
        Assert.Single(SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out _));
    }

    [Fact]
    public void Sync_Upgrades_Changed_Content_And_Keeps_Disabled_Off()
    {
        WriteSkill("demo", "demo-skill", "做演示", "正文");
        var ledger = Ledger.Open(_root);
        ledger.EnsureInitialized();
        Assert.Single(SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out _));

        //内容改了：新哈希自动顶替旧引用，不再报「内容已变更」
        File.AppendAllText(SkillPath("demo"), "\nchanged", new UTF8Encoding(false));
        var upgraded = SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out var problems);
        Assert.Single(upgraded);
        Assert.Empty(problems);

        //停用后再改内容：保持停用
        SkillCatalog.SetEnabled(ledger, SkillCatalog.Load(_root), "demo-skill", false);
        File.AppendAllText(SkillPath("demo"), "\nchanged-again", new UTF8Encoding(false));
        Assert.Empty(SkillCatalog.Sync(ledger, SkillCatalog.Load(_root), out _));
    }

    [Fact]
    public void Load_Merges_Global_Skills_With_Workspace_Precedence()
    {
        string global = Path.Combine(_root, "global-skills");
        WriteSkillAt(Path.Combine(global, "shared"), "shared", "全局版本", "全局正文");
        WriteSkillAt(Path.Combine(global, "only-global"), "only-global", "只在全局", "全局正文");
        WriteSkill("demo", "shared", "项目版本", "项目正文");

        var catalog = SkillCatalog.Load(_root, global);

        //同名：项目那份赢
        var shared = catalog.Single(s => s.Name == "shared");
        Assert.False(shared.Global);
        Assert.Contains("项目正文", shared.Body);
        Assert.Equal(".openloop/skills/demo/SKILL.md", shared.Source);

        var only = catalog.Single(s => s.Name == "only-global");
        Assert.True(only.Global);
        Assert.Equal("skills/only-global/SKILL.md", only.Source);
    }

    private string SkillPath(string folder)
        => Path.Combine(_root, ".openloop", "skills", folder, "SKILL.md");

    private void WriteSkill(string folder, string name, string description, string body)
        => WriteSkillAt(Path.Combine(_root, ".openloop", "skills", folder), name, description, body);

    private static void WriteSkillAt(string dir, string name, string description, string body)
    {
        Directory.CreateDirectory(dir);
        string text = $"---\nname: {name}\ndescription: {description}\n---\n{body}";
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), text, new UTF8Encoding(false));
    }
}
