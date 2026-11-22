using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>AnnouncePeers 的行为验证（诊断用，跑完即删）。</summary>
    public sealed class PeerNoticeProbe : IDisposable
    {
        private readonly string _chats = Path.Combine(Path.GetTempPath(), "ol-peerprobe-" + Guid.NewGuid().ToString("N"));
        private readonly string _prev;

        public PeerNoticeProbe()
        {
            _prev = OpenLoop.Service.Storage.ChatHistory.Root;
            OpenLoop.Service.Storage.ChatHistory.Root = _chats;
        }

        public void Dispose()
        {
            OpenLoop.Service.Storage.ChatHistory.Root = _prev;
            try { Directory.Delete(_chats, true); } catch { }
        }

        [Fact]
        public async Task Notice_lands_in_pending_on_first_send()
        {
            string peer = "A-peer" + Guid.NewGuid().ToString("N")[..8];
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Puppet.Core.AgentBook.Heartbeat(peer, "Worker 会话 · " + _chats, "干活", 0, "openloop");

                var host = new WorkerSessionHost((request, ct) => gate.Task);
                var session = host.Create(_chats, "openrouter/free", "", () => new Config.AppSettings());

                host.Send(session.Id, "task", "start");
                var pending = host.Get(session.Id).Pending;
                var notice = Assert.Single(pending, t => t.Contains(peer));
                Assert.Contains("book", notice);

                gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            }
            finally
            {
                Puppet.Core.AgentBook.Release(peer);
            }
        }
    }
}
