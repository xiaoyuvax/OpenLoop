using System.Diagnostics;
using OpenLoop.Core.Safety;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class SnapshotsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-snapshot-tests", Guid.NewGuid().ToString("N"));

    public SnapshotsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Restore_BringsBackTrackedAndDeletesNewButKeepsIgnored()
    {
        if (!GitAvailable()) return;

        string repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        Git(repo, "config", "user.email", "test@example.com");
        Git(repo, "config", "user.name", "test");

        string tracked = Path.Combine(repo, "a.txt");
        string deleted = Path.Combine(repo, "keep.txt");
        File.WriteAllText(tracked, "old");
        File.WriteAllText(deleted, "keep");
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "*.log");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-qm", "init");

        string untracked = Path.Combine(repo, "untracked.txt");
        File.WriteAllText(untracked, "pre");

        string snapshot = Snapshots.Create(repo, "test");
        Assert.False(string.IsNullOrEmpty(snapshot));

        File.WriteAllText(tracked, "new");
        File.Delete(deleted);
        File.WriteAllText(Path.Combine(repo, "created.txt"), "x");
        File.WriteAllText(Path.Combine(repo, "noise.log"), "ignored");

        Assert.True(Snapshots.Restore(repo, snapshot));

        Assert.Equal("old", File.ReadAllText(tracked));
        Assert.Equal("keep", File.ReadAllText(deleted));
        Assert.Equal("pre", File.ReadAllText(untracked));
        Assert.False(File.Exists(Path.Combine(repo, "created.txt")));
        Assert.True(File.Exists(Path.Combine(repo, "noise.log")));
        Assert.Equal("?? untracked.txt", GitOutput(repo, "status", "--porcelain").Trim());
    }

    [Fact]
    public void Tree_and_Pin_RecordTheBuiltWorktreeUnderACustomRef()
    {
        // 迭代用：构建前定格 tree，发布后把 refs/openloop/version/<角色>/<版本> 指回它
        if (!GitAvailable()) return;

        string repo = Path.Combine(_root, "repo-pin");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "one");

        string tree = Snapshots.Tree(repo);
        Assert.False(string.IsNullOrEmpty(tree));
        Assert.True(Snapshots.Pin(repo, "refs/openloop/version/a/0.1.1", tree));
        Assert.Equal(tree, GitOutput(repo, "rev-parse", "refs/openloop/version/a/0.1.1").Trim());

        // 同一个 tree 可以挂到多个角色的 ref 上（A 与壳常常同源）
        Assert.True(Snapshots.Pin(repo, "refs/openloop/version/shell/0.1.0", tree));
        Assert.Equal(tree, GitOutput(repo, "rev-parse", "refs/openloop/version/shell/0.1.0").Trim());
    }

    [Fact]
    public void CommitTree_CreatesDetachedCommitOnHeadAndKeepsWorktreeClean()
    {
        if (!GitAvailable()) return;

        string repo = Path.Combine(_root, "repo-commit");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        Git(repo, "config", "user.email", "test@example.com");
        Git(repo, "config", "user.name", "test");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "one");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-qm", "init");

        string tree = Snapshots.Tree(repo);
        var (obj, isCommit) = Snapshots.CommitTree(repo, tree, "iteration a 0.1.1");
        Assert.True(isCommit);
        Assert.True(Snapshots.Pin(repo, "refs/openloop/version/a/0.1.1", obj));

        Assert.Equal("iteration a 0.1.1", GitOutput(repo, "log", "-1", "--format=%s", "refs/openloop/version/a/0.1.1").Trim());
        Assert.Equal(GitOutput(repo, "rev-parse", "HEAD").Trim(),
            GitOutput(repo, "rev-parse", "refs/openloop/version/a/0.1.1^").Trim());
        Assert.Equal("", GitOutput(repo, "status", "--porcelain").Trim());   // 不碰索引/工作区
    }

    private static bool GitAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(5000);
            return process != null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Git(string workspace, params string[] args)
    {
        string error = GitOutput(workspace, out int exitCode, args);
        Assert.True(exitCode == 0, "git " + string.Join(" ", args) + "：" + error);
    }

    private static string GitOutput(string workspace, params string[] args)
        => GitOutput(workspace, out _, args);

    private static string GitOutput(string workspace, out int exitCode, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git 启动失败");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return exitCode == 0 ? output : error;
    }
}
