using OpenLoop.Service.Config;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    /// <summary>
    /// 协作：发起方分解 → 起平级会话 → 监测汇报以「自动」消息进队列。
    /// 这里只测不依赖模型的部分：登记落盘、状态行、自动入队的标记与时机。
    /// </summary>
    [Collection("chat-index")]
    public class CollabTests : IDisposable
    {
        private readonly string _chats = Path.Combine(Path.GetTempPath(), "openloop-collab-" + Guid.NewGuid().ToString("N"));
        private readonly string _previousRoot;

        public CollabTests()
        {
            _previousRoot = ChatHistory.Root;
            ChatHistory.Root = _chats;
        }

        public void Dispose()
        {
            ChatHistory.Root = _previousRoot;
            try { Directory.Delete(_chats, true); } catch { }
        }

        private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(20);
            }
            Assert.True(condition(), "等待超时");
        }

        [Fact]
        public void Watch_round_trips_through_sidecar()
        {
            var watch = new CollabWatch
            {
                Initiator = "init1", Workspace = "C:/work", Sessions = ["a", "b"], Signature = "sig"
            };
            CollabSidecar.Save(watch);

            var loaded = CollabSidecar.Load("C:/work", "init1");
            Assert.NotNull(loaded);
            Assert.Equal(["a", "b"], loaded.Sessions);
            Assert.Equal("sig", loaded.Signature);
            Assert.Contains(CollabSidecar.List("C:/work"), w => w.Initiator == "init1");

            CollabSidecar.Delete("C:/work", "init1");
            Assert.Null(CollabSidecar.Load("C:/work", "init1"));
        }

        [Fact]
        public void Signature_changes_with_state()
        {
            var running = new WorkerSessionSnapshot { Id = "a", State = WorkerSessionHost.Running, Steps = 1, Revision = 1 };
            var done = new WorkerSessionSnapshot { Id = "a", State = WorkerSessionHost.Done, Steps = 1, Revision = 2 };
            Assert.NotEqual(CollabStatus.Signature([running]), CollabStatus.Signature([done]));
        }

        [Fact]
        public void Failed_turn_reports_the_error_text()
        {
            var snapshot = new WorkerSessionSnapshot
            {
                Id = "a",
                State = WorkerSessionHost.Interrupted,
                Steps = 2,
                Turns = [new WorkerTurn { Failed = true, Assistant = "执行失败：boom" }]
            };
            string line = CollabStatus.Line(snapshot);
            Assert.Contains("已停止/异常结束", line);
            Assert.Contains("boom", line);
        }

        [Fact]
        public async Task Diagnose_index_after_run()
        {
            var host = new WorkerSessionHost((r, ct) => Task.FromResult(new WorkerRunResult
            {
                Outcome = "ok",
                FinalAnswer = "done"
            }));
            var s = host.Create("C:/work", "m", "", () => new AppSettings(), "diag1");
            host.Send(s.Id, "task", "hi");
            await Task.Delay(500);
            var index = ChatHistory.Index();
            Assert.True(index.Any(e => e.Id == "diag1"),
                "count=" + index.Count + " state=" + host.Get(s.Id)?.State + " file=" +
                File.Exists(ChatHistory.PathFor("C:/work", "diag1")));
        }

        [Fact]
        public async Task Auto_message_waits_in_queue_and_is_flagged()
        {
            var gate = new TaskCompletionSource<WorkerRunResult>();
            var host = new WorkerSessionHost((request, ct) => gate.Task);
            var session = host.Create("C:/work", "openrouter/free", "", () => new AppSettings());

            host.Send(session.Id, "task", "先跑一段");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Running);

            Assert.True(host.EnqueueAuto(session.Id, "[协作监测] 有会话仍在进行"));
            var snapshot = host.Get(session.Id);
            Assert.Equal(["[协作监测] 有会话仍在进行"], snapshot.Pending);
            Assert.Equal([true], snapshot.PendingAuto);

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await WaitAsync(() => host.Get(session.Id).Pending.Count == 0);
        }
    }
}
