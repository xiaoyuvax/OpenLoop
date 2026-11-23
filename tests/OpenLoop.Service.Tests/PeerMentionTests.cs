using OpenLoop.Core.Agent;
using OpenLoop.Service.Web;
using Puppet.Core;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 正文里 @ 一个同工作区会话：不写 to 也算定向留言，投递到对方队列（广播要对方自己来读）。
    /// 只认留言板上同工作区的会话，@ 到别人/别的项目不当作定向。
    /// </summary>
    [Collection("chat-index")]
    public class PeerMentionTests
    {
        [Fact]
        public void Mention_in_the_text_resolves_to_a_same_workspace_peer()
        {
            string ws = Path.Combine(Path.GetTempPath(), "ol-mention-" + Guid.NewGuid().ToString("N"));
            string self = "I-self" + Guid.NewGuid().ToString("N")[..8];
            string peer = "I-abc123" + Guid.NewGuid().ToString("N")[..4];
            try
            {
                AgentBook.Heartbeat(peer, "Loop 会话 · " + ws, "改界面", 0, "openloop");
                using var identity = AgentIdentity.Begin(self, "a", ws);

                // 正文里 @ 了同工作区会话：定向投递给它（不用再写 to）
                Assert.Equal(peer, PeerDelivery.MentionOf("@" + peer + " 我们撞文件了，先对下边界"));
                // 前缀也认
                Assert.Equal(peer, PeerDelivery.MentionOf("@" + peer[..7] + " 在吗"));
                // 显式 to 优先
                Assert.Equal("I-other", PeerDelivery.MentionOf("@" + peer + " 在吗", "I-other"));
                // 没有 @、也没有 to：广播
                Assert.Null(PeerDelivery.MentionOf("进度汇报：改完了"));
                // @ 的不是本工作区会话：不当作定向，只当普通文本
                Assert.Null(PeerDelivery.MentionOf("@A-nobody 在吗"));
            }
            finally
            {
                AgentBook.Release(peer);
            }
        }
    }
}
