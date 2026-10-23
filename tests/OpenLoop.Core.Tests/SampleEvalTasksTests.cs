using OpenLoop.Core.Store;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class SampleEvalTasksTests
{
    [Fact]
    public void Samples_Load_With_Unique_Id_And_Checks()
    {
        var tasks = TaskSet.Load(Path.Combine(RepoRoot(), "samples", "eval"));

        Assert.True(tasks.Tasks.Count >= 4);
        Assert.All(tasks.Tasks, t =>
        {
            Assert.NotEmpty(t.Id);
            Assert.NotEmpty(t.Prompt);
            Assert.NotEmpty(t.Checks);
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenLoop.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
