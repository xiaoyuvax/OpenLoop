using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Storage;

namespace OpenLoop.Service.Worker
{
    public sealed class WorkerTurn
    {
        public string User { get; set; } = "";
        public string Assistant { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string ToolLines { get; set; } = "";
        public int Steps { get; set; }
        public long Ms { get; set; }
        public bool Streaming { get; set; }
        public bool Failed { get; set; }

        /// <summary>这一轮开始前的 git 快照 id，界面「撤回对话」用它恢复文件。</summary>
        public string Checkpoint { get; set; }

        /// <summary>模型结束原因（content_filter 等），界面据此解释空回复。</summary>
        public string FinishReason { get; set; }

        /// <summary>这一轮结束时的完整消息（含工具调用/结果），用于下一轮的上下文。</summary>
        public List<LlmMessage> Transcript { get; set; }
    }

    /// <summary>给界面/总线用的只读投影。</summary>
    public sealed class WorkerSessionSnapshot
    {
        public string Id { get; set; }
        public string Workspace { get; set; }
        public string Model { get; set; }
        public string Effort { get; set; }
        public string State { get; set; }
        public int Revision { get; set; }
        public bool Paused { get; set; }
        public string TraceDirectory { get; set; }
        public int ActivationVersion { get; set; }
        public int FeatureCount { get; set; }
        public int RuleCount { get; set; }

        /// <summary>最近一轮的结果：ok / budget / stopped / error。</summary>
        public string Outcome { get; set; }
        public decimal? Cost { get; set; }
        public int? ContextTokens { get; set; }
        public List<WorkerTurn> Turns { get; set; } = [];
        public List<string> Pending { get; set; } = [];
        public List<WorkerPromptView> Prompts { get; set; } = [];
    }

    /// <summary>等待界面回答的审批/确认。回答用 <c>POST /api/sessions/{id}/approval</c>。</summary>
    public sealed class WorkerPromptView
    {
        public string Id { get; set; }
        public string Kind { get; set; }

        /// <summary>审批类别：<c>outside</c>（工作区外）/ <c>sensitive</c>（风险操作）。</summary>
        public string Category { get; set; }
        public string Text { get; set; }
        public string Tool { get; set; }
        public string Detail { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Worker 会话宿主：拥有会话、队列与运行状态，不依赖界面。
    /// WinForms 与 Web 都只读取快照、调用方法；进程内用 <see cref="Changed"/>，跨进程用 revision 轮询。
    /// </summary>
    public sealed class WorkerSessionHost
    {
        public const string Idle = "idle";
        public const string Running = "running";
        public const string Done = "done";
        public const string Interrupted = "interrupted";

        private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<WorkerRunRequest, CancellationToken, Task<WorkerRunResult>> _runner;
        private readonly OpenLoop.Service.Web.LoopApiClient _loop;
        private readonly object _lock = new();

        public WorkerSessionHost(
            Func<WorkerRunRequest, CancellationToken, Task<WorkerRunResult>> runner = null,
            OpenLoop.Service.Web.LoopApiClient loop = null)
        {
            _runner = runner ?? ((request, ct) => new WorkerSessionService().RunAsync(request, ct));
            _loop = loop;
        }

        /// <summary>会话有任何变化时触发（参数是会话 id）。可能在线程池线程上触发。</summary>
        public event Action<string> Changed;

        /// <summary>有会话正在跑（/bus/restart 忙时拒绝）。</summary>
        public bool IsBusy
        {
            get
            {
                lock (_lock) return _sessions.Values.Any(s => s.State == Running);
            }
        }

        public WorkerSessionSnapshot Create(string workspace, string model, string effort, Func<AppSettings> settings,
            string id = null)
        {
            var session = new Session
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id,
                Workspace = workspace ?? "",
                Model = model ?? "",
                Effort = effort ?? "",
                Settings = settings ?? (() => new AppSettings())
            };

            foreach (var entry in ChatHistory.Read(session.Workspace, session.Id))
            {
                if (entry == null) continue;
                session.Turns.Add(new WorkerTurn
                {
                    User = entry.User ?? "",
                    Assistant = entry.Assistant ?? "",
                    Reasoning = entry.Reasoning ?? "",
                    ToolLines = entry.ToolLines ?? "",
                    Steps = entry.Steps,
                    Ms = entry.Ms,
                    Transcript = entry.Messages?
                        .Where(m => !string.IsNullOrEmpty(m?.Content))
                        .Select(m => new LlmMessage(m.Role ?? "user", m.Content))
                        .ToList()
                });
            }

            lock (_lock) _sessions[session.Id] = session;
            return snapshot(session);
        }

        public IReadOnlyList<WorkerSessionSnapshot> List(bool summary = false)
        {
            lock (_lock)
                return _sessions.Values.OrderByDescending(s => s.CreatedAt)
                    .Select(s => summary ? brief(s) : snapshot(s)).ToList();
        }

        /// <summary>列表轮询用的轻量投影：只带状态，不带轮次正文。</summary>
        private static WorkerSessionSnapshot brief(Session session) => new()
        {
            Id = session.Id,
            Workspace = session.Workspace,
            Model = session.Model,
            Effort = session.Effort,
            State = session.State,
            Revision = session.Revision,
            Paused = session.Paused,
            Outcome = session.Outcome,
            Cost = session.Cost,
            ContextTokens = session.ContextTokens
        };

        public WorkerSessionSnapshot Get(string id)
        {
            lock (_lock)
                return _sessions.TryGetValue(id ?? "", out var session) ? snapshot(session) : null;
        }

        public bool SetModel(string id, string model, string effort = null)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Model = model ?? session.Model;
                if (effort != null) session.Effort = effort;
                session.Revision++;
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>空闲时立即开跑；运行中则排队，等当前轮结束按顺序执行。</summary>
        public bool Send(string id, string tag, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session)) return false;
                if (session.State == Running)
                {
                    session.Pending.Add(text);
                    session.Revision++;
                }
                else
                {
                    Start(session, tag ?? "task", text);
                }
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>停止当前轮，并暂停自动续跑（队列保留）。</summary>
        public bool Stop(string id)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Paused = true;
                session.Cancel?.Cancel();
                session.Revision++;
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>恢复自动续跑：队列里有东西就立刻发下一条。</summary>
        public bool Resume(string id)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Paused = false;
                session.Revision++;

                if (session.State != Running && session.Pending.Count > 0)
                {
                    string next = session.Pending[0];
                    session.Pending.RemoveAt(0);
                    Start(session, "task", next);
                }
            }

            Changed?.Invoke(id);
            return true;
        }

        public bool RemovePending(string id, int index)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                if (index < 0 || index >= session.Pending.Count) return false;
                session.Pending.RemoveAt(index);
                session.Revision++;
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>丢掉内存里的会话（正在跑的先停）；对话文件由调用方删。</summary>
        public bool Remove(string id)
        {
            Session session;
            lock (_lock)
            {
                if (!_sessions.Remove(id ?? "", out session)) return false;
                session.Cancel?.Cancel();
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>回答一个等待中的审批/确认：0 = 拒绝，1 = 允许一次，2 = 同类后续都允许。</summary>
        public bool ResolvePrompt(string id, string promptId, int decision)
        {
            Prompt prompt;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                prompt = session.Prompts.FirstOrDefault(p => p.Id == promptId);
            }

            if (prompt == null) return false;
            prompt.Decision.TrySetResult(decision);
            return true;
        }

        /// <summary>从历史里拼下一轮要带的消息：以最近一份 transcript 为准。</summary>
        internal static List<LlmMessage> BuildHistory(IReadOnlyList<WorkerTurn> turns)
        {
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var stored = turns[i].Transcript;
                if (stored == null || stored.Count == 0) continue;

                var list = new List<LlmMessage>(stored);
                for (int j = i + 1; j < turns.Count; j++)
                {
                    if (turns[j].Failed) continue;
                    if (!string.IsNullOrEmpty(turns[j].User)) list.Add(new LlmMessage("user", turns[j].User));
                    if (!string.IsNullOrEmpty(turns[j].Assistant)) list.Add(new LlmMessage("assistant", turns[j].Assistant));
                }
                return list;
            }

            var fallback = new List<LlmMessage>();
            foreach (var turn in turns)
            {
                if (turn.Failed) continue;
                if (!string.IsNullOrEmpty(turn.User)) fallback.Add(new LlmMessage("user", turn.User));
                if (!string.IsNullOrEmpty(turn.Assistant)) fallback.Add(new LlmMessage("assistant", turn.Assistant));
            }
            return fallback;
        }

        private void Start(Session session, string tag, string text)
        {
            // 调用方持有 _lock
            List<LlmMessage> history = BuildHistory(session.Turns);
            var turn = new WorkerTurn { User = text, Streaming = true };
            session.Turns.Add(turn);
            session.State = Running;
            session.Revision++;

            var cts = new CancellationTokenSource();
            session.Cancel?.Dispose();
            session.Cancel = cts;

            string id = session.Id;
            var request = new WorkerRunRequest
            {
                Settings = session.Settings(),
                Workspace = session.Workspace,
                Model = session.Model,
                Effort = session.Effort,
                Tag = tag,
                Text = text,
                History = history,
                Approve = approval => Approve(session, approval, cts.Token),
                ConfirmContinue = message => Ask(session, "continue", "", message, "", "", "", cts.Token) > 0,
                Started = ctx =>
                {
                    lock (_lock)
                    {
                        session.TraceDirectory = ctx.TraceDirectory;
                        turn.Checkpoint = ctx.Checkpoint;
                        session.ActivationVersion = ctx.ActivationVersion;
                        session.FeatureCount = ctx.FeatureCount;
                        session.RuleCount = ctx.RuleCount;
                        session.Revision++;
                    }
                },
                Delta = piece =>
                {
                    lock (_lock)
                    {
                        turn.Assistant += piece;
                        session.Revision++;
                    }
                },
                Reasoning = piece =>
                {
                    lock (_lock)
                    {
                        turn.Reasoning += piece;
                        session.Revision++;
                    }
                },
                ResetStream = () =>
                {
                    lock (_lock)
                    {
                        turn.Assistant = "";
                        turn.Reasoning = "";
                        session.Revision++;
                    }
                },
                ToolStarted = _ =>
                {
                    lock (_lock)
                    {
                        turn.Assistant = "";
                        session.Revision++;
                    }
                },
                Step = step =>
                {
                    lock (_lock)
                    {
                        turn.Steps++;
                        turn.Ms += step.DurationMs;
                        string line = $"{step.Tool}({step.ArgsSummary}) · {step.DurationText}{(step.Ok ? "" : " · 失败")}";
                        turn.ToolLines = string.IsNullOrEmpty(turn.ToolLines) ? line : turn.ToolLines + "\n" + line;
                        session.Revision++;
                    }
                }
            };

            _ = Task.Run(async () =>
            {
                WorkerRunResult result;
                try
                {
                    if (_loop != null)
                    {
                        try
                        {
                            request.Activation = await _loop.GetActivationAsync(request.Workspace).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn("读取激活集失败：" + ex.Message);
                        }
                    }

                    result = await _runner(request, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = new WorkerRunResult { Outcome = "stopped", FinalAnswer = "（已停止）" };
                }
                catch (Exception ex)
                {
                    result = new WorkerRunResult { Outcome = "error", ErrorMessage = ex.Message, FinalAnswer = ex.Message };
                }

                if (_loop != null)
                {
                    try
                    {
                        await _loop.DrainSpoolAsync(request.Workspace).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("运行证据入账失败：" + ex.Message);
                    }
                }

                lock (_lock)
                {
                    turn.Streaming = false;
                    turn.Steps = result.Steps > turn.Steps ? result.Steps : turn.Steps;
                    turn.Assistant = result.Outcome == "error"
                        ? "执行失败：" + result.ErrorMessage
                        : result.FinalAnswer ?? turn.Assistant;
                    turn.Failed = result.Outcome == "error";
                    turn.FinishReason = result.FinishReason;
                    turn.Transcript = result.Transcript as List<LlmMessage> ?? result.Transcript?.ToList();

                    session.Steps = result.Steps;
                    session.Outcome = result.Outcome;
                    session.Cost = result.Cost;
                    session.ContextTokens = result.ContextTokens;
                    session.State = result.Outcome is "ok" or "budget" ? Done : Interrupted;
                    session.Revision++;

                    if (!session.Paused && session.Pending.Count > 0)
                    {
                        string next = session.Pending[0];
                        session.Pending.RemoveAt(0);
                        Start(session, "task", next);
                    }
                }

                Changed?.Invoke(id);
                Persist(session);
            });
        }

        /// <summary>把会话的轮次写到 chats/（与旧版 App 同一个文件格式）。</summary>
        private void Persist(Session session)
        {
            List<ChatEntry> entries;
            string workspace;
            string id;

            lock (_lock)
            {
                workspace = session.Workspace;
                id = session.Id;
                entries = session.Turns.Select(t => new ChatEntry
                {
                    User = t.User,
                    Assistant = t.Assistant,
                    Reasoning = t.Reasoning,
                    ToolLines = t.ToolLines,
                    Steps = t.Steps,
                    Ms = t.Ms,
                    Messages = t.Transcript?
                        .Select(m => new ChatMessageEntry { Role = m.Role, Content = m.Content })
                        .ToList()
                }).ToList();
            }

            ChatHistory.Write(workspace, id, entries);
            ChatHistory.Touch(workspace, id);
        }

        private ApprovalDecision Approve(Session session, ApprovalRequest request, CancellationToken ct)
        {
            var settings = session.Settings();
            if (settings.AutoApprove) return ApprovalDecision.AllowOnce;
            if (request.Kind == ApprovalKind.OutsideWorkspace && settings.AllowOutsideWorkspace)
                return ApprovalDecision.AllowOnce;
            if (request.Kind == ApprovalKind.SensitiveAction && settings.AllowSensitiveActions)
                return ApprovalDecision.AllowOnce;

            return Ask(session, "approval", request.Kind == ApprovalKind.OutsideWorkspace ? "outside" : "sensitive",
                request.Text, request.Tool, request.Detail, request.Reason, ct) switch
            {
                2 => ApprovalDecision.AllowAlways,
                1 => ApprovalDecision.AllowOnce,
                _ => ApprovalDecision.Deny
            };
        }

        /// <summary>把问题挂进会话快照，等界面调用 <see cref="ResolvePrompt"/>；60 秒没人答就按拒绝。</summary>
        private int Ask(Session session, string kind, string category, string text, string tool, string detail,
            string reason, CancellationToken ct)
        {
            var prompt = new Prompt
            {
                Id = Guid.NewGuid().ToString("N")[..6],
                Kind = kind,
                Category = category,
                Text = text,
                Tool = tool,
                Detail = detail,
                Reason = reason
            };

            lock (_lock)
            {
                session.Prompts.Add(prompt);
                session.Revision++;
            }
            Changed?.Invoke(session.Id);

            try
            {
                return prompt.Decision.Task.Wait(TimeSpan.FromSeconds(60), ct)
                    ? prompt.Decision.Task.Result
                    : 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            finally
            {
                lock (_lock)
                {
                    session.Prompts.Remove(prompt);
                    session.Revision++;
                }
                Changed?.Invoke(session.Id);
            }
        }

        private static WorkerSessionSnapshot snapshot(Session session) => new()
        {
            Id = session.Id,
            Workspace = session.Workspace,
            Model = session.Model,
            Effort = session.Effort,
            State = session.State,
            Revision = session.Revision,
            Paused = session.Paused,
            TraceDirectory = session.TraceDirectory,
            Outcome = session.Outcome,
            Cost = session.Cost,
            ContextTokens = session.ContextTokens,
            ActivationVersion = session.ActivationVersion,
            FeatureCount = session.FeatureCount,
            RuleCount = session.RuleCount,
            Turns = session.Turns.Select(t => new WorkerTurn
            {
                User = t.User,
                Assistant = t.Assistant,
                Reasoning = t.Reasoning,
                ToolLines = t.ToolLines,
                Steps = t.Steps,
                Ms = t.Ms,
                Streaming = t.Streaming,
                Failed = t.Failed,
                Checkpoint = t.Checkpoint,
                FinishReason = t.FinishReason
            }).ToList(),
            Pending = session.Pending.ToList(),
            Prompts = session.Prompts.Select(p => new WorkerPromptView
            {
                Id = p.Id,
                Kind = p.Kind,
                Category = p.Category,
                Text = p.Text,
                Tool = p.Tool,
                Detail = p.Detail,
                Reason = p.Reason
            }).ToList()
        };

        private sealed class Session
        {
            public string Id { get; init; }
            public string Workspace { get; init; }
            public string Model { get; set; }
            public string Effort { get; set; }
            public Func<AppSettings> Settings { get; init; }
            public DateTime CreatedAt { get; } = DateTime.UtcNow;
            public List<WorkerTurn> Turns { get; } = [];
            public List<string> Pending { get; } = [];
            public List<Prompt> Prompts { get; } = [];
            public string State { get; set; } = Idle;
            public int Revision { get; set; }
            public bool Paused { get; set; }
            public int Steps { get; set; }
            public string TraceDirectory { get; set; }
            public string Outcome { get; set; }
            public decimal? Cost { get; set; }
            public int? ContextTokens { get; set; }
            public int ActivationVersion { get; set; }
            public int FeatureCount { get; set; }
            public int RuleCount { get; set; }
            public CancellationTokenSource Cancel { get; set; }
        }

        private sealed class Prompt
        {
            public string Id { get; init; }
            public string Kind { get; init; }
            public string Category { get; init; }
            public string Text { get; init; }
            public string Tool { get; init; }
            public string Detail { get; init; }
            public string Reason { get; init; }
            public TaskCompletionSource<int> Decision { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
