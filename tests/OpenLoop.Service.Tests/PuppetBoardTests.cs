using OpenLoop.Service.Web;
using Xunit;

namespace OpenLoop.Service.Tests
{
    public class PuppetBoardTests
    {
        [Fact]
        public void Board_row_picks_latest_message_and_latest_agent()
        {
            var now = new DateTime(2026, 11, 18, 0, 20, 0);
            var agents = new[]
            {
                new PuppetBoard.BookAgent { Id = "A-one", Operation = "改地图", LastActive = now.AddMinutes(-1) },
                new PuppetBoard.BookAgent { Id = "A-two", Operation = "旧活", LastActive = now.AddMinutes(-9) }
            };
            var messages = new[]
            {
                new PuppetMessage { Host = "@A", Seq = 1, AgentId = "A-one", Text = "旧留言", At = now.AddMinutes(-8) },
                new PuppetMessage { Host = "@A", Seq = 2, AgentId = "I-x", Text = "新留言", At = now.AddMinutes(-2) }
            };

            var row = PuppetBoard.Summarize("@A", agents, messages, now);

            Assert.Equal("@A", row.Host);
            Assert.Equal("新留言", row.LastText);
            Assert.Equal("改地图", row.Doing);
            Assert.True(row.Live);
            Assert.Equal(now.AddMinutes(-1), row.LastAt);
        }

        [Fact]
        public void Board_row_goes_idle_when_board_is_old()
        {
            var now = new DateTime(2026, 11, 18, 0, 20, 0);
            var agents = new[] { new PuppetBoard.BookAgent { Id = "A-one", LastActive = now.AddMinutes(-30) } };

            var row = PuppetBoard.Summarize("@A", agents, [], now);

            Assert.False(row.Live);
            Assert.Null(row.LastText);
            Assert.Equal(now.AddMinutes(-30), row.LastAt);
        }
    }
}
