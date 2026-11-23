using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Service.Config;
using OpenLoop.Service.Web;
using OpenLoop.Service.Worker;
using Puppet.Core;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 留言板私信（say 带 to）：只对发送方与目标可见，并作为「协作提示」主动投递到目标会话队列。
    /// 这里只测不依赖模型的纯逻辑：可见性过滤、提示文本、投递分流与入队语义。
    /// </summary>
    [Collection("peer-delivery")]
    public class PeerDeliveryTests
    {
        private const string Board = """
            {
              "messages": [
                { "seq": 1, "agentId": "I-aaa", "text": "广播", "to": "" },
                { "seq": 2, "agentId": "I-aaa", "text": "给 bbb 的私信", "to": "I-bbb" },
                { "seq": 3, "agentId": "I-ccc", "text": "给 aaa 的私信", "to": "I-aaa" },
                { "seq": 4, "agentId": "I-aaa", "text": "给 ccc 的私信", "to": "I-ccc" }
              ],
              "peers": [
                { "id": "I-ccc", "lastMessage": "给 aaa 的私信", "lastMessageAt": "2026-11-23T12:00:00", "messageTo": "I-aaa",
                  "summary": "I-ccc is on instance unknown; doing 改界面; last said: 给 aaa 的私信" }
              ],
              "chat": { "seq": 4, "agentId": "I-aaa", "text": "给 ccc 的私信", "to": "I-ccc" }
            }
            """;

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        private static List<string> Messages(JsonElement board)
            => board.GetProperty("messages").EnumerateArray()
                .Select(m => m.GetProperty("text").GetString()).ToList();

        [Fact]
        public void Private_messages_are_visible_only_to_sender_and_target()
        {
            // 发送方自己在板上还看得见（它是自己发的）
            Assert.Equal(["广播", "给 bbb 的私信", "给 aaa 的私信", "给 ccc 的私信"],
                Messages(Parse(BoardPrivacy.Filter(Board, "I-aaa"))));

            // 目标看得见发给它的那条
            Assert.Equal(["广播", "给 bbb 的私信"],
                Messages(Parse(BoardPrivacy.Filter(Board, "I-bbb"))));

            // 第三者只看得到广播：别人的私信一条都不露
            Assert.Equal(["广播"], Messages(Parse(BoardPrivacy.Filter(Board, "I-ddd"))));
        }

        [Fact]
        public void Peer_summary_drops_someone_else_s_last_message()
        {
            var mine = Parse(BoardPrivacy.Filter(Board, "I-aaa"));
            Assert.Equal("给 aaa 的私信", mine.GetProperty("peers")[0].GetProperty("lastMessage").GetString());
            Assert.True(mine.TryGetProperty("chat", out _));

            var other = Parse(BoardPrivacy.Filter(Board, "I-ddd"));
            var hidden = other.GetProperty("peers")[0];
            Assert.False(hidden.TryGetProperty("lastMessage", out _));
            Assert.DoesNotContain("last said", hidden.GetProperty("summary").GetString());
            // 在场与在做什么仍可用，只是不露私信内容
            Assert.Contains("改界面", hidden.GetProperty("summary").GetString());
            // 「chat（最近一条）」同样是别人的私信：整条摘掉
            Assert.False(other.TryGetProperty("chat", out _));
        }

        [Fact]
        public void Filter_keeps_unreadable_payload_untouched()
        {
            Assert.Equal("not json", BoardPrivacy.Filter("not json", "I-aaa"));
            Assert.Equal("[]", BoardPrivacy.Filter("[]", "I-aaa"));
            // 没有观看者（用户自己看板）不过滤
            Assert.Equal(Board, BoardPrivacy.Filter(Board, null));
        }

        [Fact]
        public void Private_notice_text_names_the_sender_and_how_to_reply()
        {
            string notice = PeerNoticeText.ForMessage("I-ccc", "请先别动 ChatFlowControl.cs");

            Assert.Contains("I-ccc", notice);
            Assert.Contains("私信", notice);
            Assert.Contains("只有你与它可见", notice);
            Assert.Contains("请先别动 ChatFlowControl.cs", notice);
            Assert.Contains("to=\"I-ccc\"", notice);

            // 首行当折叠头（界面按协作提示折成一行），正文另起一段
            var lines = notice.Replace("\r\n", "\n").Split('\n');
            Assert.StartsWith("【协作提示】", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.Equal("请先别动 ChatFlowControl.cs", lines[2]);
        }

        [Fact]
        public async Task Delivery_reaches_the_queue_through_the_auto_channel()
        {
            var gate = new TaskCompletionSource<WorkerRunResult>();
            var host = new WorkerSessionHost((_, _) => gate.Task);
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "peer0001");

            host.Send(session.Id, "task", "先跑一段");
            Assert.Equal(WorkerSessionHost.Running, host.Get(session.Id).State);

            // 私信以「协作提示」进队：与用户的排队消息同一条队列，但标 Auto + Immediate
            Assert.True(host.EnqueueAuto(session.Id, PeerNoticeText.ForMessage("I-ccc", "同步一下边界"), "collab"));

            var snapshot = host.Get(session.Id);
            int at = snapshot.Pending.FindIndex(p => p.Contains("同步一下边界"));
            Assert.True(at >= 0, "私信应进队列，实际：" + string.Join(" | ", snapshot.Pending));
            Assert.True(snapshot.PendingAuto[at]);

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Delivery_prefers_the_in_process_hook()
        {
            string previousRole = PuppetBridge.SelfRole;
            var previousLocal = PeerDelivery.Local;
            var got = new List<(string Id, string Text)>();
            PuppetBridge.SelfRole = "a";
            PeerDelivery.Local = (id, text, tag) =>
            {
                lock (got) got.Add((id, text));
                Assert.Equal("mention", tag);
                return true;
            };
            try
            {
                // A 里的 Worker 会话：A- 前缀直投本进程，不经端口；投的是「协作提示」形状的正文
                Assert.Null(await PeerDelivery.DeliverAsync("I-ccc", "A-abc123", "私信原文"));
                lock (got)
                {
                    var one = Assert.Single(got);
                    Assert.Equal("abc123", one.Id);
                    Assert.Contains("I-ccc", one.Text);
                    Assert.Contains("私信原文", one.Text);
                }

                // 私信给自己不投递
                Assert.NotNull(await PeerDelivery.DeliverAsync("I-ccc", "I-ccc", "给自己"));
            }
            finally
            {
                PeerDelivery.Local = previousLocal;
                PuppetBridge.SelfRole = previousRole;
            }
        }

        [Fact]
        public void Resolve_accepts_only_same_workspace_peers_and_unique_prefixes()
        {
            string ws = Path.Combine(Path.GetTempPath(), "ol-peerres-" + Guid.NewGuid().ToString("N"));
            string self = "A-self" + Guid.NewGuid().ToString("N")[..8];
            string peer = "A-abc123" + Guid.NewGuid().ToString("N")[..4];
            try
            {
                AgentBook.Heartbeat(peer, "Worker 会话 · " + ws, "改界面", 0, "openloop");
                AgentBook.Heartbeat("A-elsewhere", @"Worker 会话 · D:\PUB\Elsewhere", "别处", 0, "openloop");
                using var identity = AgentIdentity.Begin(self, "self", ws);

                // 全 id 命中：照投
                var exact = PeerDelivery.Resolve(peer);
                Assert.Equal(peer, exact.Id);
                Assert.Null(exact.Refusal);
                // 写前缀，唯一命中就补成完整 id
                var prefixed = PeerDelivery.Resolve(peer[..5]);
                Assert.Equal(peer, prefixed.Id);
                Assert.Null(prefixed.Refusal);
                // 别的会话/别的工作区：不投，并说明原因
                Assert.Contains("本工作区", PeerDelivery.Resolve("A-elsewhere").Refusal);
            }
            finally
            {
                AgentBook.Release(peer);
                AgentBook.Release("A-elsewhere");
            }
        }
    }
}

