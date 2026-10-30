using OpenLoop.Core.Safety;
using OpenLoop.Core.Store;
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
    public void Scope_Protects_OpenLoop_Runtime()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".openloop", "traces"));
        File.WriteAllText(Path.Combine(_root, ".openloop", "ledger.jsonl"), "{}\n");

        var scope = new WorkspaceScope(_root);
        Assert.False(scope.TryResolve(".openloop/ledger.jsonl", out _, out string error));
        Assert.Contains("受保护", error);
        Assert.True(scope.IsDenied(Path.Combine(_root, ".openloop", "traces")));
        Assert.False(scope.IsDenied(Path.Combine(_root, ".openloopx", "a.txt")));
    }

    [Fact]
    public async Task List_And_Grep_Hide_Protected_OpenLoop()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".openloop", "traces"));
        File.WriteAllText(Path.Combine(_root, ".openloop", "traces", "a.txt"), "secret");
        File.WriteAllText(Path.Combine(_root, "visible.txt"), "secret");

        var scope = new WorkspaceScope(_root);
        var list = await new ListDirTool(scope).ExecuteAsync(
            new ToolArgs(new() { ["recursive"] = true }), CancellationToken.None);
        Assert.True(list.Ok);
        Assert.Contains("visible.txt", list.Data);
        Assert.DoesNotContain(".openloop", list.Data);

        var grep = await new GrepTool(scope).ExecuteAsync(
            new ToolArgs(new() { ["pattern"] = "secret" }), CancellationToken.None);
        Assert.True(grep.Ok);
        Assert.Contains("visible.txt", grep.Data);
        Assert.DoesNotContain(".openloop", grep.Data);
    }

    [Fact]
    public async Task EvidenceTool_Reads_Evidence_But_Not_Eval()
    {
        string dir = Path.Combine(_root, ".openloop");
        Directory.CreateDirectory(Path.Combine(dir, "eval", "tasks"));
        File.WriteAllText(Path.Combine(dir, "ledger.jsonl"), "{\"id\":\"L1\"}\n");
        File.WriteAllText(Path.Combine(dir, "eval", "tasks", "t.json"), "{\"id\":\"T1\"}");

        var tool = new ReadEvidenceTool(_root);
        var ledger = await tool.ExecuteAsync(
            new ToolArgs(new() { ["path"] = "ledger.jsonl" }), CancellationToken.None);
        Assert.True(ledger.Ok, ledger.Error);
        Assert.Contains("L1", ledger.Data);

        var eval = await tool.ExecuteAsync(
            new ToolArgs(new() { ["path"] = "eval/tasks/t.json" }), CancellationToken.None);
        Assert.False(eval.Ok);
        Assert.Contains("受保护", eval.Error);
    }

    [Fact]
    public async Task RecordFinding_Appends_To_Journal()
    {
        var tool = new RecordFindingTool(_root);
        var result = await tool.ExecuteAsync(new ToolArgs(new()
        {
            ["kind"] = "access",
            ["severity"] = "warn",
            ["summary"] = "worker 试图写 .openloop",
            ["evidence"] = "trace:T-1"
        }), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        string path = FindingsLog.FilePath(_root);
        Assert.True(File.Exists(path));
        Assert.Contains("worker 试图写 .openloop", File.ReadAllText(path));
    }

    [Fact]
    public void Commands_Touching_OpenLoop_Need_Approval()
        => Assert.Equal(CommandRisk.Risky,
            CommandPolicy.Classify(["git", "status", ".openloop"]).Risk);

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
    public void Scope_Outside_Needs_Approval()
    {
        var scope = new WorkspaceScope(_root);
        Assert.False(scope.TryResolve("../outside.txt", out _, out _));

        var asked = new List<ApprovalRequest>();
        scope.Approve = request => { asked.Add(request); return ApprovalDecision.AllowOnce; };
        Assert.True(scope.TryResolve("../outside.txt", out string full, out _));
        Assert.Equal(ApprovalKind.OutsideWorkspace, Assert.Single(asked).Kind);
        Assert.Contains("outside.txt", full);

        scope.Approve = _ => ApprovalDecision.Deny;
        Assert.False(scope.TryResolve("../outside.txt", out _, out string error));
        Assert.Contains("未获批准", error);
    }

    [Fact]
    public void Scope_Inside_Never_Asks()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".openloop"));
        var scope = new WorkspaceScope(_root);
        int calls = 0;
        scope.Approve = _ => { calls++; return ApprovalDecision.AllowOnce; };

        Assert.True(scope.TryResolve("sub/a.txt", out _, out _));
        //受保护目录仍然硬拒：不因为「后续都许可」而放行
        Assert.False(scope.TryResolve(".openloop/x.jsonl", out _, out _));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RunCommand_Risky_Asks_And_Honors_Decision()
    {
        var scope = new WorkspaceScope(_root);
        var asked = new List<ApprovalKind>();
        var tool = new RunCommandTool(scope, request =>
        {
            asked.Add(request.Kind);
            return ApprovalDecision.Deny;
        });

        var denied = await tool.ExecuteAsync(
            new ToolArgs(new() { ["argv"] = new[] { "git", "reset", "--hard" } }), CancellationToken.None);
        Assert.False(denied.Ok);
        Assert.Contains("拒绝", denied.Error);
        Assert.Equal(ApprovalKind.SensitiveAction, Assert.Single(asked));
    }

    [Fact]
    public async Task RunCommand_Safe_Does_Not_Ask()
    {
        var scope = new WorkspaceScope(_root);
        int calls = 0;
        var tool = new RunCommandTool(scope, _ => { calls++; return ApprovalDecision.Deny; });

        var result = await tool.ExecuteAsync(
            new ToolArgs(new() { ["argv"] = new[] { "dotnet", "--version" } }), CancellationToken.None);
        if (!result.Ok && (result.Error ?? "").Contains("启动失败", StringComparison.Ordinal)) return;
        Assert.True(result.Ok, "ERR=" + result.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Read_Inside_Workspace_Does_Not_Ask()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        var scope = new WorkspaceScope(_root);
        int calls = 0;
        scope.Approve = _ => { calls++; return ApprovalDecision.Deny; };

        var result = await new ReadFileTool(scope).ExecuteAsync(
            new ToolArgs(new() { ["path"] = "a.txt" }), CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(0, calls);
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
