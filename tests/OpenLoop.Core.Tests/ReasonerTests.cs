using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using Xunit;

namespace OpenLoop.Core.Tests;

public sealed class ReasonerTests
{
    [Fact]
    public void Parse_reads_openrouter_cost()
    {
        const string body = """
        {
          "choices": [ { "message": { "content": "ok" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 2, "total_tokens": 12, "cost": 0.00123 }
        }
        """;

        var result = OpenAiCompatibleReasoner.Parse(body);

        Assert.Equal("ok", result.Content);
        Assert.Equal(0.00123m, result.Cost);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public void Parse_without_usage_leaves_cost_null()
    {
        const string body = """
        { "choices": [ { "message": { "content": "ok" } } ] }
        """;

        Assert.Null(OpenAiCompatibleReasoner.Parse(body).Cost);
    }

    [Fact]
    public async Task Stream_parser_accumulates_deltas_and_cost()
    {
        const string sse = """
        : OPENROUTER PROCESSING

        data: {"choices":[{"delta":{"content":"ok"},"finish_reason":null}]}

        data: {"choices":[{"delta":{"content":"!"},"finish_reason":"stop"}]}

        data: {"choices":[],"usage":{"prompt_tokens":3,"completion_tokens":2,"cost":0.0004}}

        data: [DONE]

        """;

        var deltas = new List<string>();
        var result = await OpenAiCompatibleReasoner.ParseStreamAsync(new StringReader(sse), deltas.Add);

        Assert.Equal("ok!", result.Content);
        Assert.Equal(0.0004m, result.Cost);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(new[] { "ok", "!" }, deltas);
    }

    [Fact]
    public async Task Process_reasoner_round_trips_stdin_to_stdout()
    {
        var reasoner = new ProcessReasoner("cmd", ["/c", "more"], TimeSpan.FromSeconds(30));

        var result = await reasoner.CompleteAsync(
            [new LlmMessage("system", "sys-line"), new LlmMessage("user", "hello-loop")]);

        Assert.Contains("hello-loop", result.Content);
        Assert.Contains("sys-line", result.Content);
    }

    [Fact]
    public async Task Process_reasoner_surfaces_nonzero_exit()
    {
        var reasoner = new ProcessReasoner("cmd", ["/c", "exit", "3"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reasoner.CompleteAsync([new LlmMessage("user", "x")]));

        Assert.Contains("退出码 3", ex.Message);
    }
}
