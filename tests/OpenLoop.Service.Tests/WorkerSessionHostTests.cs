using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Service.Config;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;
using Xunit;

namespace OpenLoop.Service.Tests
{
    [Collection("chat-index")]
    public class WorkerSessionHostTests : IDisposable
    {
        /// <summary>每个测试用独立目录，别把会话写进真实的 %APPDATA%\OpenLoop\chats。</summary>
        private readonly string _chats = Path.Combine(Path.GetTempPath(), "openloop-chat-" + Guid.NewGuid().ToString("N"));
        private readonly string _previousRoot;

        public WorkerSessionHostTests()
        {
            _previousRoot = ChatHistory.Root;
            ChatHistory.Root = _chats;
        }

        public void Dispose()
        {
            ChatHistory.Root = _previousRoot;
            try { Directory.Delete(_chats, true); } catch { }
        }

        private static WorkerSessionHost NewHost(
            Func<WorkerRunRequest, CancellationToken, Task<WorkerRunResult>> runner)
            => new(runner);

        private static WorkerSessionSnapshot NewSession(WorkerSessionHost host)
            => host.Create("C:/work", "openrouter/free", "", () => new AppSettings());

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
        public void List_includes_saved_sessions_after_restart()
        {
            ChatHistory.Write(_chats, "webonly",
                [new ChatEntry { User = "hi", Assistant = "yo", Model = "m1", Effort = "low" }]);
            ChatHistory.Touch(_chats, "webonly", "标题");

            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult()));
            var row = Assert.Single(host.List());

            Assert.Equal("webonly", row.Id);
            Assert.Equal(WorkerSessionHost.Idle, row.State);
            Assert.Equal(_chats, row.Workspace);
        }

        [Fact]
        public void Create_restores_last_model_when_caller_does_not_pass_one()
        {
            ChatHistory.Write(_chats, "s1",
                [new ChatEntry { User = "a", Model = "openrouter/free", Effort = "low" }]);
            ChatHistory.Touch(_chats, "s1");
            Assert.Equal("openrouter/free", Assert.Single(ChatHistory.Read(_chats, "s1")).Model);

            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult()));
            var session = host.Create(_chats, "", "", () => new AppSettings(), "s1");

            Assert.Equal("openrouter/free", session.Model);
            Assert.Equal("low", session.Effort);
        }

        [Fact]
        public async Task Send_runs_and_stores_the_turn()
        {
            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult
            {
                Outcome = "ok",
                FinalAnswer = "done",
                Steps = 2
            }));
            var session = NewSession(host);

            Assert.True(host.Send(session.Id, "task", "hi"));
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

            var snapshot = host.Get(session.Id);
            Assert.Single(snapshot.Turns);
            Assert.Equal("hi", snapshot.Turns[0].User);
            Assert.Equal("done", snapshot.Turns[0].Assistant);
            Assert.Equal(2, snapshot.Turns[0].Steps);
            Assert.False(snapshot.Turns[0].Streaming);
        }

        [Fact]
        public async Task Second_message_queues_then_runs_after_the_first()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var host = NewHost(async (request, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    await gate.Task.WaitAsync(ct);
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = request.Text };
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "first");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Running);

            host.Send(session.Id, "task", "second");
            Assert.Single(host.Get(session.Id).Pending);

            gate.SetResult();
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 2);

            var snapshot = host.Get(session.Id);
            Assert.Equal(2, snapshot.Turns.Count);
            Assert.Empty(snapshot.Pending);
            Assert.Equal("second", snapshot.Turns[1].Assistant);
        }

        [Fact]
        public async Task Stop_cancels_the_run_and_keeps_the_queue()
        {
            int calls = 0;
            var host = NewHost(async (request, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    await Task.Delay(Timeout.Infinite, ct);
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = "resumed" };
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "first");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Running);

            host.Send(session.Id, "task", "second");
            host.Stop(session.Id);

            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Interrupted);
            Assert.Single(host.Get(session.Id).Pending);
            Assert.True(host.Get(session.Id).Paused);

            host.Resume(session.Id);
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 2);

            var snapshot = host.Get(session.Id);
            Assert.Empty(snapshot.Pending);
            Assert.Equal("resumed", snapshot.Turns[1].Assistant);
        }

        [Fact]
        public async Task Approval_prompt_round_trips_through_the_snapshot()
        {
            var decisions = new List<ApprovalDecision>();
            var host = NewHost((request, ct) =>
            {
                var decision = request.Approve(new ApprovalRequest(
                    ApprovalKind.SensitiveAction, "Shell", "rm -rf build", "删除目录"));
                lock (decisions) decisions.Add(decision);
                return Task.FromResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).Prompts.Count == 1);

            var prompt = host.Get(session.Id).Prompts[0];
            Assert.Equal("approval", prompt.Kind);
            Assert.Equal("sensitive", prompt.Category);
            Assert.Contains("rm -rf build", prompt.Detail);
            Assert.True(host.ResolvePrompt(session.Id, prompt.Id, 2));

            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
            lock (decisions) Assert.Equal(ApprovalDecision.AllowAlways, Assert.Single(decisions));
        }

        [Fact]
        public async Task AutoApprove_skips_the_prompt()
        {
            var decisions = new List<ApprovalDecision>();
            var host = NewHost((request, ct) =>
            {
                lock (decisions) decisions.Add(request.Approve(new ApprovalRequest(
                    ApprovalKind.OutsideWorkspace, "ReadFile", "C:/other/a.txt", "")));
                return Task.FromResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            });
            var settings = new AppSettings { AutoApprove = true };
            var session = host.Create("C:/work", "m", "", () => settings);

            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

            lock (decisions) Assert.Equal(ApprovalDecision.AllowOnce, Assert.Single(decisions));
        }

        [Fact]
        public async Task Turns_are_persisted_and_can_be_reopened_by_id()
        {
            string dir = Path.Combine(Path.GetTempPath(), "openloop-chat-" + Guid.NewGuid().ToString("N"));
            string previous = ChatHistory.Root;
            ChatHistory.Root = dir;
            try
            {
                var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult
                {
                    Outcome = "ok",
                    FinalAnswer = "persisted",
                    Steps = 1,
                    Transcript = [new LlmMessage("user", "hi"), new LlmMessage("assistant", "persisted")]
                }));

                var session = host.Create("C:/work", "m", "", () => new AppSettings(), "sess01");
                host.Send(session.Id, "task", "hi");
                await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

                Assert.True(File.Exists(ChatHistory.PathFor("C:/work", "sess01")));

                var reopened = NewHost((request, ct) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }))
                    .Create("C:/work", "m", "", () => new AppSettings(), "sess01");
                Assert.Single(reopened.Turns);
                Assert.Equal("persisted", reopened.Turns[0].Assistant);
            }
            finally
            {
                ChatHistory.Root = previous;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public async Task Persisting_a_turn_indexes_the_session_so_the_shell_can_list_it()
        {
            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult
            {
                Outcome = "ok",
                FinalAnswer = "done"
            }));
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "sess02");

            Assert.Empty(ChatHistory.Index());   // 没跑过就没有索引项
            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

            var entry = Assert.Single(ChatHistory.Index());
            Assert.Equal("sess02", entry.Id);
            Assert.Equal(Path.GetFullPath("C:/work"), entry.Workspace);
        }

        [Fact]
        public async Task List_summary_keeps_state_but_drops_turn_content()
        {
            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult
            {
                Outcome = "ok",
                FinalAnswer = "answer"
            }));
            var session = NewSession(host);
            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

            var brief = Assert.Single(host.List(summary: true));
            Assert.Equal(WorkerSessionHost.Done, brief.State);
            Assert.Equal(session.Workspace, brief.Workspace);
            Assert.Empty(brief.Turns);

            Assert.Single(Assert.Single(host.List()).Turns);
        }

        [Fact]
        public void History_does_not_reuse_the_system_prompt_from_the_transcript()
        {
            var turns = new List<WorkerTurn>
            {
                new()
                {
                    User = "one",
                    Assistant = "answer-1",
                    Transcript =
                    [
                        new LlmMessage("system", "S"),
                        new LlmMessage("user", "one"),
                        new LlmMessage("assistant", "answer-1")
                    ]
                },
                new()
                {
                    User = "two",
                    Assistant = "answer-2",
                    Transcript =
                    [
                        new LlmMessage("system", "S"),
                        new LlmMessage("user", "one"),
                        new LlmMessage("assistant", "answer-1"),
                        new LlmMessage("user", "two"),
                        new LlmMessage("assistant", "answer-2")
                    ]
                }
            };

            var history = WorkerSessionHost.BuildHistory(turns);

            Assert.DoesNotContain(history, m => m.Role == "system");
            Assert.Equal(["one", "answer-1", "two", "answer-2"], history.Select(m => m.Content));
        }

        [Fact]
        public void History_keeps_the_user_text_of_a_failed_turn_but_not_the_error()
        {
            var turns = new List<WorkerTurn>
            {
                new() { User = "改成 Markdown", Assistant = "执行失败：模型接口返回 429 Too Many Requests" },
                new() { User = "继续", Assistant = ServerNotice.FailedPrefix + "provider.free-limit", Failed = true }
            };

            var history = WorkerSessionHost.BuildHistory(turns);

            Assert.Equal(["改成 Markdown", "继续"], history.Select(m => m.Content));
            Assert.All(history, m => Assert.Equal("user", m.Role));
        }

        [Fact]
        public void Write_keeps_the_transcript_a_writer_did_not_have()
        {
            var entry = new ChatEntry
            {
                User = "hi",
                Assistant = "回答",
                Messages = [new ChatMessageEntry { Role = "user", Content = "hi" }]
            };
            ChatHistory.Write("C:/work", "merge1", [entry]);

            // 壳手里的轮次没有 transcript，整表覆盖不能把它抹掉
            ChatHistory.Write("C:/work", "merge1", [new ChatEntry { User = "hi", Assistant = "回答" }]);
            var stored = Assert.Single(ChatHistory.Read("C:/work", "merge1"));
            Assert.Equal("hi", Assert.Single(stored.Messages).Content);

            // 换了轮次（user 对不上）就不该继承别人的 transcript
            ChatHistory.Write("C:/work", "merge1", [new ChatEntry { User = "别的", Assistant = "回答" }]);
            Assert.Null(Assert.Single(ChatHistory.Read("C:/work", "merge1")).Messages);
        }

        [Fact]
        public async Task History_carries_previous_turns()        {
            var histories = new List<List<LlmMessage>>();
            var host = NewHost((request, ct) =>
            {
                lock (histories) histories.Add(request.History.ToList());
                return Task.FromResult(new WorkerRunResult
                {
                    Outcome = "ok",
                    FinalAnswer = "answer-" + histories.Count
                });
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "one");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
            host.Send(session.Id, "task", "two");
            await WaitAsync(() => host.Get(session.Id).Turns.Count == 2);

            lock (histories)
            {
                Assert.Equal(2, histories.Count);
                Assert.Empty(histories[0]);
                Assert.Contains(histories[1], m => m.Role == "user" && m.Content == "one");
                Assert.Contains(histories[1], m => m.Role == "assistant" && m.Content == "answer-1");
            }
        }

        [Fact]
        public async Task Context_tokens_follow_the_stream_and_should_not_wait_for_the_run_to_finish()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost(async (request, ct) =>
            {
                request.ContextTokens?.Invoke(1000);
                request.Delta?.Invoke("abcd");
                await gate.Task.WaitAsync(ct);
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" };
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).ContextTokens == 1001);
            Assert.Equal(WorkerSessionHost.Running, host.Get(session.Id).State);

            gate.SetResult();
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
            Assert.Equal(1001, host.Get(session.Id).ContextTokens);
        }
    }
}
