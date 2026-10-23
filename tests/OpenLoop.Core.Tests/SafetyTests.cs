using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class SafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-safety-tests", Guid.NewGuid().ToString("N"));

    public SafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("rm", "-rf", "/")]
    [InlineData("format", "C:")]
    [InlineData("shutdown", "/s")]
    public void Denied_Commands_Are_Blocked(params string[] argv)
        => Assert.Equal(CommandRisk.Denied, CommandPolicy.Classify(argv).Risk);

    [Theory]
    [InlineData("git", "reset", "--hard")]
    [InlineData("powershell", "-enc", "AAAA")]
    [InlineData("rm", "-rf", "build")]
    [InlineData("curl", "https://example.com")]
    public void Risky_Commands_Need_Approval(params string[] argv)
        => Assert.Equal(CommandRisk.Risky, CommandPolicy.Classify(argv).Risk);

    [Theory]
    [InlineData("dotnet", "build")]
    [InlineData("git", "status")]
    [InlineData("ls", "-la")]
    public void Ordinary_Commands_Are_Safe(params string[] argv)
        => Assert.Equal(CommandRisk.Safe, CommandPolicy.Classify(argv).Risk);

    [Fact]
    public void Scope_Rejects_Parent_Escape()
    {
        var scope = new WorkspaceScope(_root);
        Assert.False(scope.TryResolve("../outside.txt", out _, out string error));
        Assert.Contains("越界", error);
        Assert.True(scope.TryResolve("sub/new.txt", out string full, out _));
        Assert.StartsWith(scope.Root, full);
    }

    [Fact]
    public void Scope_Rejects_Symlink_Escape()
    {
        string outside = Path.Combine(Path.GetTempPath(), "openloop-safety-out", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            string link = Path.Combine(_root, "link");
            try { Directory.CreateSymbolicLink(link, outside); }
            catch { return; }

            var scope = new WorkspaceScope(_root);
            Assert.False(scope.TryResolve("link/evil.txt", out _, out _));
        }
        finally
        {
            try { Directory.Delete(outside, true); } catch { }
        }
    }

    [Fact]
    public void Child_Environment_Has_No_Secrets()
    {
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sk-test");
        try
        {
            var env = ChildEnvironment.Build();
            Assert.DoesNotContain(env.Keys, k => k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(env.Keys, ChildEnvironment.LooksSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        }
    }

    [Fact]
    public async Task RunCommand_Uses_Argv_And_Rejects_Risky_Without_Approver()
    {
        var tool = new RunCommandTool(new WorkspaceScope(_root));

        var blocked = await tool.ExecuteAsync(
            new ToolArgs(new() { ["argv"] = new[] { "git", "reset", "--hard" } }), CancellationToken.None);
        Assert.False(blocked.Ok);
        Assert.Contains("批准", blocked.Error);

        var legacy = await tool.ExecuteAsync(
            new ToolArgs(new() { ["command"] = "echo hi" }), CancellationToken.None);
        Assert.False(legacy.Ok);
        Assert.Contains("argv", legacy.Error);
    }

    [Fact]
    public async Task RunCommand_Executes_Safe_Argv()
    {
        var tool = new RunCommandTool(new WorkspaceScope(_root));
        var result = await tool.ExecuteAsync(
            new ToolArgs(new() { ["argv"] = new[] { "dotnet", "--version" } }), CancellationToken.None);
        if (!result.Ok && (result.Error ?? "").Contains("启动失败", StringComparison.Ordinal)) return;
        Assert.True(result.Ok, "ERR=" + result.Error + " OUT=" + result.Data);
        Assert.Matches(@"\d+\.\d+\.\d+", result.Data ?? "");
    }

    [Fact]
    public void Snapshot_Returns_Sha_For_Dirty_Repo()
    {
        if (!Git(_root, "init")) return;
        Git(_root, "config", "user.email", "test@example.com");
        Git(_root, "config", "user.name", "test");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "1");
        if (!Git(_root, "add", "a.txt") || !Git(_root, "commit", "-m", "init")) return;

        File.WriteAllText(Path.Combine(_root, "a.txt"), "2");
        string sha = Snapshots.Create(_root, "test");
        Assert.NotNull(sha);
        Assert.Matches("^[0-9a-f]{40}$", sha);
    }

    private static bool Git(string dir, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
