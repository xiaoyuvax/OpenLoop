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
    }
}
