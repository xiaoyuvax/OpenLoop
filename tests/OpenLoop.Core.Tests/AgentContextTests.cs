using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using Xunit;

namespace OpenLoop.Core.Tests
{
    public class AgentContextTests
    {
        [Fact]
        public void EstimatesCjkAndAsciiAtSameScale()
        {
            Assert.Equal(2, AgentContext.EstimateTokens("汉字"));
            Assert.Equal(2, AgentContext.EstimateTokens("abcdefgh"));
            Assert.Equal(0, AgentContext.EstimateTokens(""));
        }

        [Fact]
        public void SplitKeepsSystemAndRecentMessages()
        {
            var messages = new List<LlmMessage>
            {
                new("system", "sys"),
                new("user", "a"),
                new("assistant", "tool-call: {}"),
                new("user", "tool-result: {}"),
                new("assistant", "tool-call: {}"),
                new("user", "tool-result: {}"),
            };

            var (head, middle, tail) = AgentContext.SplitForCompression(messages, 1);

            Assert.Single(head);
            Assert.Equal("system", head[0].Role);
            Assert.True(middle.Count > 0);
            Assert.False(AgentContext.IsToolResult(tail[0]));
            Assert.Contains("tool-result", tail[^1].Content);
        }

        [Fact]
        public void SplitDoesNothingWhenFewMessages()
        {
            var messages = new List<LlmMessage> { new("system", "s"), new("user", "a") };

            var (head, middle, tail) = AgentContext.SplitForCompression(messages, 6);

            Assert.Single(head);
            Assert.Empty(middle);
            Assert.Single(tail);
        }

        [Fact]
        public void SplitCapsTheTailByTokensNotJustMessageCount()
        {
            // 各 ~400 token 的长消息：按条数会留满 6 条，压缩就再也降不下去
            var messages = Enumerable.Range(0, 10)
                .Select(_ => new LlmMessage("user", new string('話', 400)))
                .ToList();

            var (_, middle, tail) = AgentContext.SplitForCompression(messages, 6, 600);
            Assert.Single(tail);
            Assert.Equal(9, middle.Count);

            var (_, _, looseTail) = AgentContext.SplitForCompression(messages, 6);
            Assert.Equal(6, looseTail.Count);
        }

        [Fact]
        public void SplitAlwaysKeepsOneMessageEvenIfItBlowsTheBudget()
        {
            var messages = Enumerable.Range(0, 7)
                .Select(_ => new LlmMessage("user", "a"))
                .Append(new LlmMessage("assistant", new string('話', 900)))
                .ToList();

            var (_, middle, tail) = AgentContext.SplitForCompression(messages, 6, 100);

            Assert.Single(tail);
            Assert.Equal(7, middle.Count);
        }

        [Theory]
        [InlineData("模型接口返回 400：This model's maximum context length is 131072 tokens")]
        [InlineData("provider.context_length_exceeded：Input is too long for requested model")]
        [InlineData("the request exceeds the available context size")]
        [InlineData("The input token count (1048577) exceeds the maximum number of tokens allowed (1048576)")]
        [InlineData("400：上下文长度超过模型上限")]
        public void DetectsContextOverflowErrors(string message)
            => Assert.True(AgentContext.IsContextOverflow(message));

        [Theory]
        [InlineData("模型接口返回 429 Too Many Requests：rate limit exceeded")]
        [InlineData("模型接口返回 401 Unauthorized：invalid api key")]
        [InlineData("insufficient quota")]
        [InlineData("The max_tokens parameter is invalid")]
        [InlineData("")]
        public void IgnoresNonOverflowErrors(string message)
            => Assert.False(AgentContext.IsContextOverflow(message));
    }
}
