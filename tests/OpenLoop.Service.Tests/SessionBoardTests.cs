using OpenLoop.Service.Web;
using Puppet.Core;
using Xunit;

namespace OpenLoop.Service.Tests
{
    [Collection("agent-book")]
    public class SessionBoardTests
    {
        [Fact]
        public void Peer_summary_carries_idle_time_and_step_timestamps()
        {
            var peer = new PeerInfo
            {
                Id = "A-same0001", Doing = "重构 ChatFlow 行渲染", IdleSeconds = 125,
                Steps = ["20:38:12 Edit[src/ChatFlow/Row.cs] → ok"]
            };

            string line = peer.Summary();

            Assert.Contains("A-same0001（空闲 2m；在做：重构 ChatFlow 行渲染；最近：20:38:12 Edit[src/ChatFlow/Row.cs] → ok）", line);
        }

        [Fact]
        public void Peers_keeps_same_workspace_only_and_reports_idle_time()
        {
            string self = "A-self" + Guid.NewGuid().ToString("N")[..8];
            string peer = "A-peer" + Guid.NewGuid().ToString("N")[..8];
            string other = "A-other" + Guid.NewGuid().ToString("N")[..8];
            const string ws = @"D:\PUB\OpenLoop";
            try
            {
                AgentBook.Heartbeat(self, "Worker 会话 · " + ws, "自己", 0, "openloop");
                AgentBook.Heartbeat(peer, "Worker 会话 · " + ws, "改 ChatFlow", 0, "openloop",
                    step: "Edit[src/ChatFlow/Row.cs] → ok");
                AgentBook.Heartbeat(other, @"Worker 会话 · D:\PUB\Elsewhere", "别处", 0, "openloop");

                var peers = SessionBoard.Peers(ws, self);

                var only = Assert.Single(peers);
                Assert.Equal(peer, only.Id);
                Assert.InRange(only.IdleSeconds, 0, 5);
                Assert.Contains("在做：改 ChatFlow", only.Summary());
                Assert.Contains("Row.cs", only.Summary());
            }
            finally
            {
                AgentBook.Release(self);
                AgentBook.Release(peer);
                AgentBook.Release(other);
            }
        }
    }
}
