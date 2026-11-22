using OpenLoop.Core.Agent;
using Xunit;
using Xunit.Abstractions;

namespace OpenLoop.Core.Tests;

/// <summary>临时探针：真实会话正文经过流式显示过滤后剩多少。用完删除。</summary>
public class ZzCutProbe
{
    private readonly ITestOutputHelper _out;
    public ZzCutProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void CutRealAnswers()
    {
        foreach (string file in Directory.GetFiles(@"D:\PUB\OpenLoop\src\openloop\.tmp", "trunc-*.md").OrderBy(f => f))
        {
            string body = File.ReadAllText(file);
            string cut = AgentToolCallParser.CutToolCallText(body);
            _out.WriteLine($"{Path.GetFileName(file)}: full={body.Length} cut={cut.Length} lost={body.Length - cut.Length}");
            if (cut.Length < body.Length)
                _out.WriteLine("   cut tail: ..." + body.Substring(Math.Max(0, cut.Length - 40), Math.Min(120, body.Length - cut.Length + 40)).Replace("\n", "\\n"));
        }
    }
}
