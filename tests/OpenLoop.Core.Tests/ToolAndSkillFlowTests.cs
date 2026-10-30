using System.Text;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>工具调用与技能调用的端到端流程：真工具 + 脚本化模型，验证结果确实回喂给模型。</summary>
public sealed class ToolAndSkillFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-flow-tests", Guid.NewGuid().ToString("N"));

    public ToolAndSkillFlowTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task Read_File_Through_Loop_Feeds_Result_Back_To_Model()
    {
        File.WriteAllText(Path.Combine(_root, "data.txt"), "magic-word-42", new UTF8Encoding(false));
        var reasoner = new ScriptedReasoner(
            Loops.Call("read_file", "{\"path\":\"data.txt\"}"),
            "内容是 magic-word-42");
        var loop = new AgentLoop(ToolRegistry.Workspace(new WorkspaceScope(_root)), reasoner);

        var result = await loop.RunAsync([], "读 data.txt");

        Assert.Equal("内容是 magic-word-42", result.FinalAnswer);
        var step = Assert.Single(result.Steps);
        Assert.Equal("read_file", step.Tool);
        Assert.True(step.Ok, step.Summary);

        //第二轮的上下文必须带上工具结果，否则模型只能瞎猜
        Assert.Equal(2, reasoner.Calls.Count);
        var feedback = Assert.Single(reasoner.Calls[1], m => m.Content.Contains("tool-result:"));
        Assert.Equal("user", feedback.Role);
        Assert.Contains("magic-word-42", feedback.Content);
    }

    [Fact]
    public async Task Write_Then_Read_Through_Loop_Touches_Disk()
    {
        var reasoner = new ScriptedReasoner(
            Loops.Call("write_file", "{\"path\":\"out.txt\",\"content\":\"hello-openloop\"}"),
            Loops.Call("read_file", "{\"path\":\"out.txt\"}"),
            "已写入并确认");
        var loop = new AgentLoop(ToolRegistry.Workspace(new WorkspaceScope(_root)), reasoner);

        var result = await loop.RunAsync([], "写文件再读回来");

        Assert.Equal("已写入并确认", result.FinalAnswer);
        Assert.Equal(["write_file", "read_file"], result.Steps.Select(s => s.Tool));
        Assert.All(result.Steps, s => Assert.True(s.Ok, s.Summary));
        Assert.Equal("hello-openloop", File.ReadAllText(Path.Combine(_root, "out.txt")));
    }

    [Fact]
    public async Task Skill_Is_Indexed_In_Prompt_And_Read_On_Demand()
    {
        WriteSkill("pdf-guide", "pdf-guide",
            "处理 PDF：读取、合并、拆分、填表单。用户提到 .pdf 时使用。",
            "# PDF Processing Guide\n\nPDF-BODY-MARKER\n\n```bash\nqpdf --empty --pages a.pdf b.pdf -- out.pdf\n```");
        string references = Path.Combine(_root, ".openloop", "skills", "pdf-guide", "references");
        Directory.CreateDirectory(references);
        File.WriteAllText(Path.Combine(references, "FORMS.md"), "表单填写步骤", new UTF8Encoding(false));

        var skills = SkillCatalog.Load(_root);
        var reasoner = new ScriptedReasoner(
            Loops.Call("read_skill", "{\"name\":\"pdf-guide\"}"),
            Loops.Call("read_skill", "{\"name\":\"pdf-guide\",\"file\":\"references/FORMS.md\"}"),
            "按技能步骤处理");
        var loop = new AgentLoop(
            ToolRegistry.Workspace(new WorkspaceScope(_root), null, skills), reasoner, skills: skills);

        //提示词只带索引（名字+描述），正文留给 read_skill 按需取
        string system = loop.BuildSystemPrompt();
        Assert.Contains("pdf-guide", system);
        Assert.Contains("处理 PDF", system);
        Assert.DoesNotContain("PDF-BODY-MARKER", system);

        var result = await loop.RunAsync([new LlmMessage("system", system)], "帮我合并两个 PDF");

        Assert.Equal("按技能步骤处理", result.FinalAnswer);
        Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.True(s.Ok, s.Summary));
        Assert.Contains("PDF-BODY-MARKER", reasoner.Calls[1].Last().Content);
        Assert.Contains("references/FORMS.md", reasoner.Calls[1].Last().Content);
        Assert.Contains("表单填写步骤", reasoner.Calls[2].Last().Content);
    }

    [Fact]
    public async Task Run_Skill_Script_Executes_After_Approval()
    {
        //脚本走 powershell，非 Windows 环境没有：这类平台跳过（CI 上的 Linux 只跑其它用例）
        if (!OperatingSystem.IsWindows()) return;

        WriteSkill("demo", "demo", "做演示", "正文");
        string scripts = Path.Combine(_root, ".openloop", "skills", "demo", "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "hello.ps1"),
            "Write-Output 'hi-from-skill'", new UTF8Encoding(false));

        var tool = new RunSkillScriptTool(_root, SkillCatalog.Load(_root),
            _ => ApprovalDecision.AllowOnce);
        var result = await tool.ExecuteAsync(
            new ToolArgs(new() { ["name"] = "demo", ["script"] = "hello.ps1" }), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("exit=0", result.Data);
        Assert.Contains("hi-from-skill", result.Data);
    }

    private void WriteSkill(string folder, string name, string description, string body)
    {
        string dir = Path.Combine(_root, ".openloop", "skills", folder);
        Directory.CreateDirectory(dir);
        string text = $"---\nname: {name}\ndescription: {description}\n---\n{body}";
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), text, new UTF8Encoding(false));
    }
}
