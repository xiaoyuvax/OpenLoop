using OpenLoop.Core.Agent;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class SpawnAgentTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-spawn", Guid.NewGuid().ToString("N"));

    public SpawnAgentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static ToolArgs Args(params (string, object)[] pairs)
        => new(pairs.ToDictionary(p => p.Item1, p => p.Item2));

    private ToolRegistry Registry(IReadOnlyList<string> names)
    {
        var scope = new WorkspaceScope(_root);
        var list = new List<ITool>();
        foreach (string name in names)
        {
            switch (name)
            {
                case "read_file": list.Add(new ReadFileTool(scope)); break;
                case "write_file": list.Add(new WriteFileTool(scope)); break;
                case "list_dir": list.Add(new ListDirTool(scope)); break;
                case "grep": list.Add(new GrepTool(scope)); break;
            }
        }
        return new ToolRegistry(list);
    }

    [Fact]
    public async Task Child_Runs_Isolated_And_Returns_Summary()
    {
        var tool = new SpawnAgentTool(
            () => new ScriptedReasoner(
                "tool-call: {\"tool\":\"write_file\",\"args\":{\"path\":\"child.txt\",\"content\":\"done\"}}",
                "child finished"),
            Registry);

        var result = await tool.ExecuteAsync(Args(("task", "create child.txt with done")), default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("done", File.ReadAllText(Path.Combine(_root, "child.txt")));
        Assert.Contains("child finished", result.Data);
        Assert.Contains("steps=1", result.Data);
    }

    [Fact]
    public void Child_Tools_Exclude_Command_And_Recursion()
    {
        Assert.DoesNotContain("run_command", SpawnAgentTool.AllowedChildTools);
        Assert.DoesNotContain("spawn_agent", SpawnAgentTool.AllowedChildTools);
    }

    [Fact]
    public async Task Rejects_Only_Disallowed_Tools()
    {
        var tool = new SpawnAgentTool(() => new ScriptedReasoner("x"), Registry);
        var result = await tool.ExecuteAsync(
            Args(("task", "do it"), ("tools", new[] { "run_command", "spawn_agent" })), default);

        Assert.False(result.Ok);
        Assert.Contains("允许", result.Error);
    }
}
