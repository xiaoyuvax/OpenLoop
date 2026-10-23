using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class FileUndoLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-undo-tests", Guid.NewGuid().ToString("N"));

    public FileUndoLogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task WriteFile_Undo_RestoresOldContentAndDeletesNewFile()
    {
        string existing = Path.Combine(_root, "a.txt");
        File.WriteAllText(existing, "old");

        var log = new FileUndoLog();
        var scope = new WorkspaceScope(_root) { Undo = log };
        var tool = new WriteFileTool(scope);

        var overwrite = await tool.ExecuteAsync(
            new ToolArgs(new() { ["path"] = "a.txt", ["content"] = "new" }), default);
        var create = await tool.ExecuteAsync(
            new ToolArgs(new() { ["path"] = "b.txt", ["content"] = "created" }), default);

        Assert.True(overwrite.Ok);
        Assert.True(create.Ok);
        Assert.Equal("new", File.ReadAllText(existing));

        var (restored, errors) = log.UndoAll();

        Assert.Empty(errors);
        Assert.Equal(2, restored);
        Assert.Equal("old", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(_root, "b.txt")));
    }
}
