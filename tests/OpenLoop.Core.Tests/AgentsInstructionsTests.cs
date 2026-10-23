using OpenLoop.Core.Agent;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class AgentsInstructionsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-agents-tests", Guid.NewGuid().ToString("N"));

    public AgentsInstructionsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Load_ReturnsFileContent_AndNullWhenMissing()
    {
        Assert.Null(AgentsInstructions.Load(_root));

        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "构建：dotnet build\n");
        Assert.Equal("构建：dotnet build", AgentsInstructions.Load(_root));
    }

    [Fact]
    public void Load_Merges_Global_Before_Project()
    {
        string global = Path.Combine(_root, "global-AGENTS.md");
        File.WriteAllText(global, "全局规则");
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "项目规则");

        string text = AgentsInstructions.Load(_root, global);
        Assert.Contains("全局规则", text);
        Assert.Contains("项目规则", text);
        Assert.True(text.IndexOf("全局规则", StringComparison.Ordinal)
                    < text.IndexOf("项目规则", StringComparison.Ordinal));

        string onlyGlobal = Path.Combine(_root, "empty");
        Directory.CreateDirectory(onlyGlobal);
        Assert.Equal("全局规则", AgentsInstructions.Load(onlyGlobal, global));
    }

    [Fact]
    public void Prompt_Fingerprint_Is_Stable_And_Sensitive()
    {
        string hash = SystemPromptBuilder.Fingerprint("abc");
        Assert.Equal(hash, SystemPromptBuilder.Fingerprint("abc"));
        Assert.NotEqual(hash, SystemPromptBuilder.Fingerprint("abd"));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Prompt_ContainsInstructionsSection()
    {
        string prompt = SystemPromptBuilder.Build([], 10, null, null, "规则A");
        Assert.Contains("约定（AGENTS.md）", prompt);
        Assert.Contains("规则A", prompt);
    }
}
