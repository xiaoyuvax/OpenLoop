using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Service.Config;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;
using Puppet.Core;
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

        /// <summary>鎺掗槦椤归噷鐢ㄦ埛鏁茬殑閭ｉ儴鍒嗭紙鎺掗櫎鍗忎綔鎻愮ず绛?Auto 鏉＄洰锛夈€?/summary>
        private static IReadOnlyList<string> UserPending(WorkerSessionHost host, string id)
        {
            var snapshot = host.Get(id);
            return snapshot.Pending
                .Zip(snapshot.PendingAuto, (text, auto) => (text, auto))
                .Where(p => !p.auto)
                .Select(p => p.text).ToList();
        }

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
        public async Task New_peer_in_the_same_workspace_queues_one_notice()
        {
            string peer = "A-peer" + Guid.NewGuid().ToString("N")[..8];
            AgentBook.Heartbeat(peer, "Worker 会话 · " + _chats, "改 ChatFlow", 0, "openloop");
            var gate = new TaskCompletionSource<WorkerRunResult>();
            try
            {
                var host = NewHost((request, ct) => gate.Task);
                var session = host.Create(_chats, "openrouter/free", "", () => new AppSettings());

                host.Send(session.Id, "task", "先做我的");
                host.Send(session.Id, "task", "再说一句");

                var snapshot = host.Get(session.Id);
                var notice = Assert.Single(snapshot.Pending, t => t.Contains(peer));
                Assert.Contains("action=\"book\"", notice);

                gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
                await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Idle);
            }
            finally
            {
                AgentBook.Release(peer);
            }
        }

        [Fact]
        public void List_includes_saved_sessions_after_restart()
        {
            ChatHistory.Write(_chats, "webonly",
                [new ChatEntry { User = "hi", Assistant = "yo", Model = "m1", Effort = "low", Steps = 4 }]);
            ChatHistory.Touch(_chats, "webonly", "标题");

            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult()));
            var row = Assert.Single(host.List());

            Assert.Equal("webonly", row.Id);
            Assert.Equal(WorkerSessionHost.Idle, row.State);
            Assert.Equal(_chats, row.Workspace);
            // 旧索引条目没有步数：回填后列表（摘要投影）也要带上
            Assert.Equal(4, ChatHistory.BackfillSteps()["webonly"]);
            Assert.Equal(4, Assert.Single(host.List(summary: true)).Steps);
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
            Assert.Single(UserPending(host, session.Id));

            gate.SetResult();
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 2);

            var snapshot = host.Get(session.Id);
            Assert.Equal(2, snapshot.Turns.Count);
            Assert.Empty(UserPending(host, snapshot.Id));
            Assert.Equal("second", snapshot.Turns[1].Assistant);
        }

        [Fact]
        public async Task Error_turn_holds_the_queue_until_new_input()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new List<string>();
            var host = NewHost(async (request, ct) =>
            {
                lock (calls) calls.Add(request.Text);
                if (calls.Count == 1)
                {
                    await gate.Task.WaitAsync(ct);
                    return new WorkerRunResult { Outcome = "error", ErrorMessage = "boom", FinalAnswer = "boom" };
                }
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = request.Text };
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "first");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Running);
            host.Send(session.Id, "task", "queued");

            gate.SetResult();
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Interrupted);
            // 失败轮不续发队列：队列留着，也没有自己开下一轮
            Assert.Single(UserPending(host, session.Id));
            await Task.Delay(100);
            Assert.Equal(new[] { "first" }, calls.ToArray());

            // 新输入激活会话：它自己开一轮，队列等它收尾后再按规则发
            host.Send(session.Id, "task", "retry");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls.Count >= 3);

            var snapshot = host.Get(session.Id);
            Assert.Equal(new[] { "first", "retry", "queued" }, calls.ToArray());
            Assert.Empty(UserPending(host, snapshot.Id));
        }

        [Fact]
        public async Task Immediate_message_jumps_the_queue()
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

            host.Send(session.Id, "task", "later");
            host.Send(session.Id, "task", "now", immediate: true);
            Assert.Equal(new[] { "now", "later" }, host.Get(session.Id).Pending);

            gate.SetResult();
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 3);

            var snapshot = host.Get(session.Id);
            Assert.Equal(3, snapshot.Turns.Count);
            Assert.Equal("now", snapshot.Turns[1].Assistant);
            Assert.Equal("later", snapshot.Turns[2].Assistant);
        }

        [Fact]
        public async Task Removing_the_last_immediate_pending_item_retracts_the_soft_stop()
        {
            WorkerRunRequest seen = null;
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost((request, ct) => { seen = request; return gate.Task; });
            var session = NewSession(host);

            host.Send(session.Id, "task", "first");
            await WaitAsync(() => seen != null);

            host.Send(session.Id, "task", "now", immediate: true);
            Assert.True(seen.ShouldStop());

            Assert.True(host.RemovePending(session.Id, 0));
            Assert.False(seen.ShouldStop());

            host.Interrupt(session.Id);
            Assert.True(seen.ShouldStop());
            host.Interrupt(session.Id, on: false);
            Assert.False(seen.ShouldStop());

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
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
            Assert.Single(UserPending(host, session.Id));
            Assert.True(host.Get(session.Id).Paused);

            host.Resume(session.Id);
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 2);

            var snapshot = host.Get(session.Id);
            Assert.Empty(UserPending(host, snapshot.Id));
            Assert.Equal("resumed", snapshot.Turns[1].Assistant);
        }

        [Fact]
        public async Task New_input_after_stop_resumes_the_queue()
        {
            int calls = 0;
            var host = NewHost(async (request, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    await Task.Delay(Timeout.Infinite, ct);
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = request.Text };
            });
            var session = NewSession(host);

            host.Send(session.Id, "task", "first");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Running);
            host.Send(session.Id, "task", "queued");
            host.Stop(session.Id);
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Interrupted);
            Assert.True(host.Get(session.Id).Paused);

            // 用户又说了一句（不是点「立即」）：暂停到此为止，这一轮正常收尾就把队列发出去
            host.Send(session.Id, "task", "again");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done && calls >= 3);

            var snapshot = host.Get(session.Id);
            Assert.Empty(UserPending(host, snapshot.Id));
            Assert.False(snapshot.Paused);
            Assert.Equal("queued", snapshot.Turns[^1].Assistant);
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
        public async Task A_running_turn_is_on_disk_before_it_finishes()
        {
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost((_, _) => gate.Task);
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "sess-run");

            host.Send(session.Id, "task", "半路崩掉也不能丢");
            // 开轮即落盘：还没跑完，盘上就该有这一轮的用户请求（壳崩了 A 只活 1 秒）
            await WaitAsync(() => ChatHistory.Read("C:/work", "sess-run").Any(e => e.User == "半路崩掉也不能丢"));
            Assert.True(ChatHistory.Read("C:/work", "sess-run").Single().Streaming);

            gate.SetResult(new WorkerRunResult { Outcome = "ok" });
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
        }

        [Fact]
        public async Task Truncating_every_turn_removes_the_saved_session()
        {
            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult
            {
                Outcome = "ok",
                FinalAnswer = "done"
            }));
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "sess03");
            host.Send(session.Id, "task", "hi");
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
            Assert.Single(ChatHistory.Index());

            Assert.True(host.Truncate(session.Id, 0));

            Assert.Empty(ChatHistory.Index());
            Assert.False(File.Exists(ChatHistory.PathFor("C:/work", "sess03")));
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
            // Turns 在 Send 时就多一条用户轮，等它到 2 会在第二轮模型调用之前就返回（曾因此偶发只看到 1 次历史）
            await WaitAsync(() => { lock (histories) return histories.Count == 2; });
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);

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
        [Fact]
        public async Task The_turn_is_persisted_before_it_finishes()
        {
            // 开轮即落盘（与 Loop 侧同一口径）：进程中途崩了也要在历史里留下这条需求
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost((request, ct) => gate.Task);
            var session = NewSession(host);

            host.Send(session.Id, "task", "把这轮需求留下来");
            await WaitAsync(() => File.Exists(ChatHistory.PathFor(session.Workspace, session.Id)));

            var entry = Assert.Single(ChatHistory.Read(session.Workspace, session.Id));
            Assert.Equal("把这轮需求留下来", entry.User);
            Assert.True(entry.Streaming);   // 收尾前这一轮还是「在跑」，读回来据此补说明

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
        }

        [Fact]
        public async Task Mid_run_steps_are_checkpointed()
        {
            // 流式期间按账本间隔落盘：这里把间隔设成 0，验证每一步都会写出去
            var previous = ChatHistory.DefaultCheckpointInterval;
            ChatHistory.DefaultCheckpointInterval = TimeSpan.Zero;
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost(async (request, ct) =>
            {
                request.Delta?.Invoke("写到一半");
                request.Step?.Invoke(new OpenLoop.Core.Agent.AgentStepRecord
                {
                    Index = 1, Tool = "ReadFile", ArgsSummary = "path=a.cs", Ok = true, DurationMs = 5
                });
                await gate.Task.WaitAsync(ct);
                return new WorkerRunResult { Outcome = "ok", FinalAnswer = "done", Steps = 1 };
            });
            try
            {
                var session = NewSession(host);
                host.Send(session.Id, "task", "hi");

                // 还没收尾，磁盘上就已经有这一步的工具行与正文
                await WaitAsync(() =>
                {
                    var stored = ChatHistory.Read(session.Workspace, session.Id);
                    return stored.Count == 1 && stored[0].Steps >= 1;
                });
                var entry = Assert.Single(ChatHistory.Read(session.Workspace, session.Id));
                Assert.Contains("ReadFile", entry.ToolLines);
                Assert.Equal("写到一半", entry.Assistant);
                Assert.True(entry.Streaming);
                Assert.Equal(WorkerSessionHost.Running, host.Get(session.Id).State);

                gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done", Steps = 1 });
                await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
                Assert.False(Assert.Single(ChatHistory.Read(session.Workspace, session.Id)).Streaming);
            }
            finally
            {
                ChatHistory.DefaultCheckpointInterval = previous;
            }
        }

        [Fact]
        public void A_crashed_blank_turn_reads_back_with_an_actionable_notice()
        {
            // 中途快照留下的「还在跑 + 正文为空」，重开会话时要能看出这一轮断在哪，而不是一条静默空白
            ChatHistory.Write("C:/work", "crash1",
                [new ChatEntry { User = "做一半", Assistant = "", Steps = 12, Streaming = true }]);
            ChatHistory.Touch("C:/work", "crash1");

            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "crash1");

            var turn = Assert.Single(session.Turns);
            Assert.StartsWith(ServerNotice.Interrupted, turn.Assistant);
            Assert.Contains("12", turn.Assistant);
            Assert.False(turn.Streaming);
        }

        [Fact]
        public void A_crashed_turn_with_partial_text_keeps_the_text_under_the_notice()
        {
            // 崩溃前已经产出了一部分正文：读回来要保留内容，但顶上标明「没跑完」，
            // 这句以 ServerNotice.Interrupted 开头，拼上下文时会被剔掉（不当成模型说过的话）
            ChatHistory.Write("C:/work", "crash2",
                [new ChatEntry { User = "做一半", Assistant = "已改到一半", Steps = 7, Streaming = true }]);
            ChatHistory.Touch("C:/work", "crash2");

            var host = NewHost((request, ct) => Task.FromResult(new WorkerRunResult { Outcome = "ok" }));
            var session = host.Create("C:/work", "m", "", () => new AppSettings(), "crash2");

            var turn = Assert.Single(session.Turns);
            Assert.StartsWith(ServerNotice.Interrupted, turn.Assistant);
            Assert.Contains("已改到一半", turn.Assistant);
            Assert.True(ServerNotice.IsNotice(turn.Assistant));
        }

        [Fact]
        public async Task Send_carries_attachments_and_image_model()
        {
            WorkerRunRequest seen = null;
            var gate = new TaskCompletionSource<WorkerRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = NewHost((request, ct) => { seen = request; return gate.Task; });
            var session = NewSession(host);

            host.Send(session.Id, "task", "画一张", false, "gemini-3-pro-image", ["C:/work/a.png"]);
            await WaitAsync(() => seen != null);

            Assert.Equal("gemini-3-pro-image", seen.ImageModel);
            Assert.Equal("C:/work/a.png", Assert.Single(seen.Attachments));
            Assert.Contains("附件", seen.Text);

            gate.SetResult(new WorkerRunResult { Outcome = "ok", FinalAnswer = "done" });
            await WaitAsync(() => host.Get(session.Id).State == WorkerSessionHost.Done);
        }
    }
}
