using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class WorkspaceToolsTests : IDisposable
    {
        private readonly string _root;

        public WorkspaceToolsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "openloop-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static ToolArgs Args(params (string Key, object Value)[] pairs)
        {
            var dict = new Dictionary<string, object>();
            foreach (var (key, value) in pairs) dict[key] = value;
            return new ToolArgs(dict);
        }

        [Fact]
        public void ScopeRejectsEscapingPath()
        {
            var scope = new WorkspaceScope(_root);

            Assert.True(scope.TryResolve("sub/a.txt", out _, out _));
            Assert.False(scope.TryResolve("..\\outside.txt", out _, out string error));
            Assert.Contains("越界", error);
        }

        [Fact]
        public async Task WriteThenReadRoundtrip()
        {
            var scope = new WorkspaceScope(_root);
            var write = new WriteFileTool(scope);
            var read = new ReadFileTool(scope);

            var written = await write.ExecuteAsync(
                Args(("path", "notes/a.txt"), ("content", "第一行\n第二行")), default);
            var readBack = await read.ExecuteAsync(Args(("path", "notes/a.txt")), default);

            Assert.True(written.Ok);
            Assert.True(readBack.Ok);
            Assert.Contains("第一行", readBack.Data);
            Assert.Contains("notes/a.txt", readBack.Data);
        }

        [Fact]
        public async Task ReadRejectsMissingFile()
        {
            var read = new ReadFileTool(new WorkspaceScope(_root));

            var result = await read.ExecuteAsync(Args(("path", "nope.txt")), default);

            Assert.False(result.Ok);
            Assert.Contains("不存在", result.Error);
        }

        [Fact]
        public async Task GrepFindsMatchesWithLineNumbers()
        {
            var scope = new WorkspaceScope(_root);
            await File.WriteAllTextAsync(Path.Combine(_root, "a.cs"), "class A\n// TODO: fix\n");
            await File.WriteAllTextAsync(Path.Combine(_root, "b.txt"), "TODO: later\n");

            var result = await new GrepTool(scope).ExecuteAsync(
                Args(("pattern", "TODO"), ("glob", "*.cs")), default);

            Assert.True(result.Ok);
            Assert.Contains("a.cs:2:", result.Data);
            Assert.DoesNotContain("b.txt", result.Data);
        }

        [Fact]
        public async Task RunCommandReturnsExitCodeAndOutput()
        {
            var scope = new WorkspaceScope(_root);

            var result = await new RunCommandTool(scope).ExecuteAsync(
                Args(("command", "echo hello"), ("timeoutSeconds", 30)), default);

            Assert.True(result.Ok);
            Assert.Contains("hello", result.Data);
            Assert.Contains("exit=0", result.Data);
        }
    }
}
