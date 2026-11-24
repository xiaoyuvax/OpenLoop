using OpenLoop.Core.Safety;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests;

/// <summary>
/// PATH 里的命令解析：npm 的 .cmd 垫片（ocr、npx 这类）CreateProcess 补不到，必须走 cmd.exe /c；
/// 参数含 shell 元字符时不能交给 cmd（会被二次解释）。
/// </summary>
public sealed class ExecutableResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "openloop-resolver-tests", Guid.NewGuid().ToString("N"));

    public ExecutableResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Exe_On_Path_Resolves_To_Its_Full_Path()
    {
        if (!OperatingSystem.IsWindows()) return;
        string exe = Path.Combine(_root, "demo-tool.exe");
        File.WriteAllText(exe, "");
        string old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", _root);
            var (fileName, prefix, error) = ExecutableResolver.Resolve("demo-tool", ["--version"]);
            Assert.Null(error);
            Assert.Empty(prefix);
            Assert.Equal(exe, fileName, ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    [Fact]
    public void Cmd_Shim_Goes_Through_Cmd_Exe()
    {
        if (!OperatingSystem.IsWindows()) return;
        string shim = Path.Combine(_root, "demo-shim.cmd");
        File.WriteAllText(shim, "@echo off\r\necho hi\r\n");
        string old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", _root);
            var (fileName, prefix, error) = ExecutableResolver.Resolve("demo-shim", ["--version"]);
            Assert.Null(error);
            Assert.Equal("cmd.exe", fileName);
            Assert.Equal(2, prefix.Count);
            Assert.Equal("/c", prefix[0]);
            // 扩展名按 PATHEXT 大写补，路径只在大小写上可能不同
            Assert.Equal(shim, prefix[1], ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    [Fact]
    public void Cmd_Shim_Refuses_Shell_Metacharacters()
    {
        if (!OperatingSystem.IsWindows()) return;
        string shim = Path.Combine(_root, "demo-shim.cmd");
        File.WriteAllText(shim, "@echo off\r\n");
        string old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", _root);
            var (_, prefix, error) = ExecutableResolver.Resolve("demo-shim", ["a & b"]);
            Assert.NotNull(error);
            Assert.Empty(prefix);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    [Fact]
    public void Unknown_Command_Is_Returned_As_Is()
    {
        var (fileName, prefix, error) = ExecutableResolver.Resolve(
            "openloop-not-a-real-command", []);
        Assert.Null(error);
        Assert.Empty(prefix);
        Assert.Equal("openloop-not-a-real-command", fileName);
    }

    /// <summary>
    /// 装在本机的 npm 全局垫片（ocr）既要能解析成 cmd.exe /c，也要能被 Shell 工具真跑起来；
    /// 没装 ocr 的机器直接跳过。
    /// </summary>
    [Fact]
    public async Task Shell_Runs_Npm_Shim_Command()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!OnPath("ocr.cmd") && !OnPath("ocr.exe")) return;

        var (fileName, prefix, error) = ExecutableResolver.Resolve("ocr", ["--version"]);
        Assert.Null(error);
        Assert.Equal("cmd.exe", fileName);
        Assert.NotEmpty(prefix);

        var tool = new ShellTool(new WorkspaceScope(_root));
        var result = await tool.ExecuteAsync(
            new ToolArgs(new() { ["argv"] = new[] { "ocr", "--version" } }), CancellationToken.None);
        Assert.True(result.Ok, "ERR=" + result.Error + " OUT=" + result.Data);
        Assert.Contains("open-code-review", result.Data ?? "");
    }

    private static bool OnPath(string fileName)
    {
        foreach (string raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            try
            {
                if (File.Exists(Path.Combine(dir, fileName))) return true;
            }
            catch
            {
                // 非法路径跳过
            }
        }
        return false;
    }
}
