using OpenLoop.Core.Tools;
using OpenLoop.Core.Tools.Builtin;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class ReadEvidenceToolTests : IDisposable
    {
        private readonly string _root;

        public ReadEvidenceToolTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "openloop-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, ".openloop", "loop"));
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

        private void WriteFindings(int count, int padPerLine)
        {
            string path = Path.Combine(_root, ".openloop", "loop", "findings.jsonl");
            var lines = Enumerable.Range(1, count)
                .Select(i => $"{{\"id\":\"F-{i:d4}\",\"summary\":\"{new string('x', padPerLine)}\"}}");
            File.WriteAllLines(path, lines);
        }

        // 正文行形如 "12: {json}"
        private static readonly System.Text.RegularExpressions.Regex BodyLine =
            new(@"^\d+: ", System.Text.RegularExpressions.RegexOptions.Compiled);

        [Fact]
        public async Task SmallFileReadsFully()
        {
            WriteFindings(3, 20);
            var tool = new ReadEvidenceTool(_root);

            var result = await tool.ExecuteAsync(Args(("path", "loop/findings.jsonl")), default);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("F-0001", result.Data);
            Assert.Contains("F-0003", result.Data);
            Assert.DoesNotContain("未显示", result.Data);
        }

        [Fact]
        public async Task LineLimitTruncationTellsNextOffset()
        {
            // 400 行 > 默认 limit 200：必须提示还有剩余并给出 offset=201
            WriteFindings(400, 10);
            var tool = new ReadEvidenceTool(_root);

            var result = await tool.ExecuteAsync(Args(("path", "loop/findings.jsonl")), default);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("第 1-200 行，共 400 行", result.Data);
            Assert.Contains("offset=201", result.Data);

            var rest = await tool.ExecuteAsync(
                Args(("path", "loop/findings.jsonl"), ("offset", "201")), default);
            Assert.True(rest.Ok, rest.Error);
            Assert.Contains("F-0201", rest.Data);
            Assert.Contains("F-0400", rest.Data);
        }

        [Fact]
        public async Task CharCapTruncatesOnLineBoundaryAndTellsOffset()
        {
            // 每行约 130 字符 × 200 行 ≈ 26k 字符，必超 16000 字符上限
            WriteFindings(200, 110);
            var tool = new ReadEvidenceTool(_root);

            var result = await tool.ExecuteAsync(Args(("path", "loop/findings.jsonl")), default);

            Assert.True(result.Ok, result.Error);
            Assert.Contains("单次上限", result.Data);
            Assert.Contains("offset=", result.Data);

            // 截断必须落在行边界：正文里每条 JSON 都完整（没有半条）
            var bodyLines = result.Data.Split('\n').Where(l => BodyLine.IsMatch(l)).ToList();
            Assert.NotEmpty(bodyLines);
            Assert.All(bodyLines, l => Assert.EndsWith("}", l.TrimEnd()));

            // 首行声明的续读 offset 与显示的最后一行衔接
            var header = result.Data.Split('\n')[0];
            var m = System.Text.RegularExpressions.Regex.Match(header, @"第 1-(\d+) 行");
            Assert.True(m.Success, header);
            int shown = int.Parse(m.Groups[1].Value);
            Assert.Equal(bodyLines.Count, shown);
            Assert.Contains($"offset={shown + 1}", result.Data);

            var rest = await tool.ExecuteAsync(
                Args(("path", "loop/findings.jsonl"), ("offset", (shown + 1).ToString())), default);
            Assert.True(rest.Ok, rest.Error);
            Assert.Contains($"F-{shown + 1:d4}", rest.Data);
        }

        [Fact]
        public async Task EvalRemainsDenied()
        {
            Directory.CreateDirectory(Path.Combine(_root, ".openloop", "eval", "tasks"));
            File.WriteAllText(
                Path.Combine(_root, ".openloop", "eval", "tasks", "secret.json"), "{\"id\":\"hidden\"}");
            var tool = new ReadEvidenceTool(_root);

            var direct = await tool.ExecuteAsync(Args(("path", "eval/tasks/secret.json")), default);
            Assert.False(direct.Ok);
            Assert.DoesNotContain("hidden", direct.Data + direct.Error);
        }
    }
}
