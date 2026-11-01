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
