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
    public void Prompt_ContainsInstructionsSection()
    {
        string prompt = SystemPromptBuilder.Build([], 10, null, null, "规则A");
        Assert.Contains("项目约定（AGENTS.md）", prompt);
        Assert.Contains("规则A", prompt);
    }
}
