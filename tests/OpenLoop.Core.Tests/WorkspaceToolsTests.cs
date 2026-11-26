using System.Text;
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
        public async Task WriteAppendBuildsFileInSegments()
        {
            var scope = new WorkspaceScope(_root);
            var write = new WriteFileTool(scope);

            var first = await write.ExecuteAsync(
                Args(("path", "big/out.txt"), ("content", "part1\n")), default);
            var second = await write.ExecuteAsync(
                Args(("path", "big/out.txt"), ("content", "part2\n"), ("mode", "append")), default);
            var third = await write.ExecuteAsync(
                Args(("path", "big/out.txt"), ("content", "part3"), ("mode", "APPEND")), default);

            Assert.True(first.Ok, first.Error);
            Assert.True(second.Ok, second.Error);
            Assert.True(third.Ok, third.Error);
            Assert.Equal("part1\npart2\npart3", await File.ReadAllTextAsync(Path.Combine(_root, "big/out.txt")));
            Assert.Contains("已追加", second.Data);
            Assert.Contains("已写入", first.Data);
        }

        [Fact]
        public async Task WriteAppendToExistingFileKeepsOldContent()
        {
            var scope = new WorkspaceScope(_root);
            string path = Path.Combine(_root, "log.txt");
            await File.WriteAllTextAsync(path, "old\n");

            var result = await new WriteFileTool(scope).ExecuteAsync(
                Args(("path", "log.txt"), ("content", "new"), ("mode", "append")), default);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("old\nnew", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteUnknownModeFailsWithHint()
        {
            var scope = new WorkspaceScope(_root);

            var result = await new WriteFileTool(scope).ExecuteAsync(
                Args(("path", "a.txt"), ("content", "x"), ("mode", "truncate")), default);

            Assert.False(result.Ok);
            Assert.Contains("mode", result.Error);
        }

        [Fact]
        public async Task EditFileReplacesUniqueAnchorAndKeepsRest()
        {
            var scope = new WorkspaceScope(_root);
            Directory.CreateDirectory(Path.Combine(_root, "src"));
            string path = Path.Combine(_root, "src/a.cs");
            await File.WriteAllTextAsync(path, "class A\r\n{\r\n    int x = 1;\r\n}\r\n");

            var result = await new EditFileTool(scope).ExecuteAsync(
                Args(("path", "src/a.cs"), ("old", "    int x = 1;\n"), ("new", "    int x = 2;\n")), default);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("第 3 行", result.Data);
            Assert.Equal("class A\r\n{\r\n    int x = 2;\r\n}\r\n", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task EditFileRefusesAmbiguousAnchorWithoutWriting()
        {
            var scope = new WorkspaceScope(_root);
            string path = Path.Combine(_root, "dup.txt");
            await File.WriteAllTextAsync(path, "x = 1;\nx = 1;\n");

            var result = await new EditFileTool(scope).ExecuteAsync(
                Args(("path", "dup.txt"), ("old", "x = 1;"), ("new", "x = 2;")), default);

            Assert.False(result.Ok);
            Assert.Contains("出现多次", result.Error);
            Assert.Equal("x = 1;\nx = 1;\n", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task EditFileKeepsUtf8BomAndCrlf()
        {
            var scope = new WorkspaceScope(_root);
            string path = Path.Combine(_root, "bom.cs");
            await File.WriteAllBytesAsync(path,
                [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("// 中文\r\nint a = 1;\r\n")]);

            var result = await new EditFileTool(scope).ExecuteAsync(
                Args(("path", "bom.cs"), ("old", "int a = 1;"), ("new", "int a = 2;")), default);

            Assert.True(result.Ok, result.Error);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Equal("// 中文\r\nint a = 2;\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }

        [Fact]
        public async Task ConcurrentEditsOfDifferentRegionsBothApply()
        {
            var scope = new WorkspaceScope(_root);
            string path = Path.Combine(_root, "shared.cs");
            await File.WriteAllTextAsync(path, "int a = 1;\nint b = 1;\n");
            var edit = new EditFileTool(scope);

            // 两个会话同时改同一个文件的不同位置：不串行化时后写者会用旧内容整文件回写，吃掉前一个的改动
            var first = Task.Run(() => edit.ExecuteAsync(
                Args(("path", "shared.cs"), ("old", "int a = 1;"), ("new", "int a = 2;")), default));
            var second = Task.Run(() => edit.ExecuteAsync(
                Args(("path", "shared.cs"), ("old", "int b = 1;"), ("new", "int b = 2;")), default));
            var results = await Task.WhenAll(first, second);

            Assert.All(results, r => Assert.True(r.Ok, r.Error));
            Assert.Equal("int a = 2;\nint b = 2;\n", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteFileAddsBomForPowerShellScript()
        {
            var scope = new WorkspaceScope(_root);

            var result = await new WriteFileTool(scope).ExecuteAsync(
                Args(("path", "t.ps1"), ("content", "Write-Output '中文'")), default);

            Assert.True(result.Ok, result.Error);
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "t.ps1"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }

        [Fact]
        public async Task WriteFileRefusesSuspiciousShrinkUnlessForced()
        {
            var scope = new WorkspaceScope(_root);
            string path = Path.Combine(_root, "big.cs");
            await File.WriteAllTextAsync(path, new string('a', 8000));

            var blocked = await new WriteFileTool(scope).ExecuteAsync(
                Args(("path", "big.cs"), ("content", "placeholder")), default);

            Assert.False(blocked.Ok);
            Assert.Contains("EditFile", blocked.Error);
            Assert.Equal(8000, new FileInfo(path).Length);

            // 走真实的协议参数形态（布尔是 JsonElement）
            using var json = System.Text.Json.JsonDocument.Parse("true");
            var forced = await new WriteFileTool(scope).ExecuteAsync(
                Args(("path", "big.cs"), ("content", "placeholder"), ("force", json.RootElement.Clone())), default);

            Assert.True(forced.Ok, forced.Error);
            Assert.Equal("placeholder", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task ReadReportsActuallyReturnedLinesWhenCapped()
        {
            var scope = new WorkspaceScope(_root);
            await File.WriteAllLinesAsync(Path.Combine(_root, "big.txt"),
                Enumerable.Range(1, 1200).Select(i => $"line {i} {new string('x', 40)}"));
            var read = new ReadFileTool(scope);

            var result = await read.ExecuteAsync(Args(("path", "big.txt"), ("limit", "400")), default);

            Assert.True(result.Ok);
            Assert.Contains("行未显示", result.Data);
            string header = result.Data.Split('\n')[0];
            var match = System.Text.RegularExpressions.Regex.Match(header, @"第 1-(\d+) 行，共 1200 行");
            Assert.True(match.Success, header);
            int shown = int.Parse(match.Groups[1].Value);
            Assert.InRange(shown, 1, 399);
            int bodyLines = result.Data.Split('\n')
                .Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+: "));
            Assert.Equal(shown, bodyLines);
        }

        [Fact]
        public async Task ReadLineLimitTellsNextOffsetAndResumes()
        {
            // 900 行短行 > 默认 limit 400，但字符数不超 6000：此前只报行数、不给续读 offset
            var scope = new WorkspaceScope(_root);
            await File.WriteAllLinesAsync(Path.Combine(_root, "many.txt"),
                Enumerable.Range(1, 900).Select(i => $"line {i}"));
            var read = new ReadFileTool(scope);

            var result = await read.ExecuteAsync(Args(("path", "many.txt")), default);

            Assert.True(result.Ok);
            Assert.Contains("第 1-400 行，共 900 行", result.Data);
            Assert.Contains("offset=401", result.Data);

            var rest = await read.ExecuteAsync(Args(("path", "many.txt"), ("offset", "401")), default);
            Assert.True(rest.Ok, rest.Error);
            Assert.Contains("line 401", rest.Data);
            // 默认 limit 400：401-800，续读 offset 必须接着给
            Assert.Contains("offset=801", rest.Data);

            var tail = await read.ExecuteAsync(Args(("path", "many.txt"), ("offset", "801")), default);
            Assert.True(tail.Ok, tail.Error);
            Assert.Contains("line 900", tail.Data);
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

            var result = await new ShellTool(scope).ExecuteAsync(
                Args(("argv", new[] { "dotnet", "--version" }), ("timeoutSeconds", 30)), default);

            Assert.True(result.Ok);
            Assert.Contains("exit=0", result.Data);
        }
    }
}
