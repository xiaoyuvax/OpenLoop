using OpenLoop.Core.Agent;
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

        /// <summary>思维片段与工具调用按发生顺序穿插后的文本；旧记录为空，界面回退到两段式。</summary>
        public string Timeline { get; set; } = "";

        /// <summary>采集中的顺序块，只在本进程内用，投影出去的是 <see cref="Timeline"/>。</summary>
        internal RunTimeline TimelineRun { get; set; }
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

        /// <summary>会话标题（chats/index.json 里的自定义标题；空则界面回退到模型名）</summary>
        public string Title { get; set; }

        /// <summary>true=本进程内存里已有（可直接 GET）；false=只在 chats/ 索引里（要带 id POST 建会话）</summary>
        public bool Live { get; set; }

        /// <summary>最近一轮的结果：ok / budget / stopped / error。</summary>
        public string Outcome { get; set; }
        public decimal? Cost { get; set; }
        public int? ContextTokens { get; set; }

        /// <summary>当前模型的上下文窗口；和 ContextTokens 一起供界面算占比。</summary>
        public int ContextLimit { get; set; }

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

            var entries = ChatHistory.Read(session.Workspace, session.Id);
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                session.Turns.Add(new WorkerTurn
                {
                    User = entry.User ?? "",
                    Assistant = entry.Assistant ?? "",
                    Reasoning = entry.Reasoning ?? "",
                    ToolLines = entry.ToolLines ?? "",
                    Timeline = entry.Timeline ?? "",
                    Steps = entry.Steps,
                    Ms = entry.Ms,
                    Transcript = entry.Messages?
                        .Where(m => !string.IsNullOrEmpty(m?.Content))
                        .Select(m => new LlmMessage(m.Role ?? "user", m.Content))
                        .ToList()
                });
            }

            // 冷启动判定用：文件最后写入时间（缓存已冷）+ 是否换了模型（前缀对不上，缓存本来也没有）。
            string path = ChatHistory.PathFor(session.Workspace, session.Id);
            if (File.Exists(path)) session.LastSavedAt = File.GetLastWriteTimeUtc(path);

            // 网页/API 冷启动时没带模型：沿用这段历史最后用过的（WinForms 由界面传进来）
            if (string.IsNullOrWhiteSpace(session.Model) || string.IsNullOrWhiteSpace(session.Effort))
            {
                var last = entries.LastOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e?.Model) || !string.IsNullOrWhiteSpace(e?.Effort));
                if (string.IsNullOrWhiteSpace(session.Model)) session.Model = last?.Model ?? "";
                if (string.IsNullOrWhiteSpace(session.Effort)) session.Effort = last?.Effort ?? "";
            }

            session.ModelChanged = entries.Any(e => !string.IsNullOrWhiteSpace(e?.Model)
                && !string.Equals(e.Model, session.Model, StringComparison.OrdinalIgnoreCase));

            lock (_lock) _sessions[session.Id] = session;
            return snapshot(session);
        }

        public IReadOnlyList<WorkerSessionSnapshot> List(bool summary = false)
        {
            List<WorkerSessionSnapshot> rows;
            lock (_lock)
                rows = _sessions.Values.OrderByDescending(s => s.CreatedAt)
                    .Select(s => summary ? brief(s) : snapshot(s)).ToList();

            // 冷启动后 A 的内存里没有会话：按 chats/index.json 补上，网页/别的 agent 才看得见历史
            try
            {
                foreach (var entry in ChatHistory.Index())
                {
                    if (string.IsNullOrEmpty(entry?.Id)) continue;
                    var row = rows.FirstOrDefault(r => r.Id == entry.Id);
                    if (row != null)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Title)) row.Title = entry.Title;
                        continue;
                    }
                    if (!Directory.Exists(entry.Workspace)) continue;
                    if (!File.Exists(ChatHistory.PathFor(entry.Workspace, entry.Id))) continue;
                    rows.Add(new WorkerSessionSnapshot
                    {
                        Id = entry.Id,
                        Workspace = entry.Workspace,
                        State = Idle,
                        Title = entry.Title
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取会话索引失败：" + ex.Message);
            }
            return rows;
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
            ContextTokens = session.ContextTokens,
            Live = true
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

        /// <summary>
        /// 软停止：只标记，让当前轮在下一个 step 之前收尾（进行中的工具不打断），
        /// 用于「立即」插队 —— 轮次结束后由界面把排队文本当新的一轮发出去。
        /// </summary>
        public bool Interrupt(string id)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Interrupt = true;
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

        /// <summary>
        /// 把第 index 条排队改成「立即」：挪到队首；正在跑就软打断（本轮收尾后先发它），
        /// 空闲就直接发。对应 WinForms 排队项的「立即」。
        /// </summary>
        public bool SendNow(string id, int index)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                if (index < 0 || index >= session.Pending.Count) return false;

                string text = session.Pending[index];
                session.Pending.RemoveAt(index);
                session.Pending.Insert(0, text);
                session.Paused = false;
                session.Revision++;

                if (session.State == Running)
                {
                    session.Interrupt = true;
                    Changed?.Invoke(id);
                    return true;
                }

                session.Pending.RemoveAt(0);
                Start(session, "task", text);
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

        /// <summary>
        /// 冷启动整理：空闲超过一小时（服务端缓存必然过期）或换了模型时才做，
        /// 且每个会话只做一次——之后只追加，不再改写前缀。
        /// </summary>
        private static void MaybePruneHistory(Session session)
        {
            bool cold = session.ModelChanged
                || (session.LastSavedAt is { } saved && DateTime.UtcNow - saved > IdleBeforePrune);
            if (!cold) return;

            var turn = session.Turns.LastOrDefault(t => t.Transcript is { Count: > 0 });
            if (turn?.Transcript == null) return;

            int changed = HistoryPruner.Prune(turn.Transcript);
            if (changed > 0)
                AppLog.Info($"冷启动整理历史：删除/改写 {changed} 条（换模型={session.ModelChanged}）");
        }

        private static readonly TimeSpan IdleBeforePrune = TimeSpan.FromMinutes(60);

        /// <summary>从历史里拼下一轮要带的消息：以最近一份 transcript 为准。</summary>
        internal static List<LlmMessage> BuildHistory(IReadOnlyList<WorkerTurn> turns)
        {
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var stored = turns[i].Transcript;
                if (stored == null || stored.Count == 0) continue;

                var list = new List<LlmMessage>();

                // 调用方每次都会把当轮 system 提示词插到最前面；transcript 里存的那份是上一轮的，
                // 带进来就会一轮叠一份（实测第 8 轮已经有 9 份 system，ctx 白涨 5k token）。
                foreach (var message in stored)
                {
                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new LlmMessage(message.Role, message.Content));
                }

                for (int j = i + 1; j < turns.Count; j++) AppendText(list, turns[j]);
                return list;
            }

            var fallback = new List<LlmMessage>();
            foreach (var turn in turns) AppendText(fallback, turn);
            return fallback;
        }

        /// <summary>
        /// 没有 transcript 的轮次退回正文。失败轮的正文是接口报错（free-limit、超时等），
        /// 但它的 user 还得留下：连续几次失败后历史整段消失，下一个模型会以为在开新对话。
        /// </summary>
        private static void AppendText(List<LlmMessage> list, WorkerTurn turn)
        {
            if (!string.IsNullOrEmpty(turn?.User)) list.Add(new LlmMessage("user", turn.User));
            if (!string.IsNullOrEmpty(turn?.Assistant) && !ServerNotice.IsNotice(turn.Assistant))
                list.Add(new LlmMessage("assistant", turn.Assistant));
        }

        private void Start(Session session, string tag, string text)
        {
            // 调用方持有 _lock
            if (!session.HistoryPruned)
            {
                session.HistoryPruned = true;
                MaybePruneHistory(session);
            }
            List<LlmMessage> history = BuildHistory(session.Turns);
            var turn = new WorkerTurn { User = text, Streaming = true };
            session.Turns.Add(turn);
            session.State = Running;
            session.Revision++;

            var cts = new CancellationTokenSource();
            session.Cancel?.Dispose();
            session.Cancel = cts;
            session.Interrupt = false;

            string id = session.Id;
            // 流式正文只在确认不是工具调用协议时才写进 Assistant，与 LoopView/SideWorkView 的
            // streamBuffer + LooksLikeToolCallText 过滤保持一致；协议原文不能出现在正文里。
            string stream = "";
            // 上下文占比：本轮 prompt 估算 + 已生成内容（按增量累加，避免每次 delta 重扫整段）
            int promptTokens = 0, liveTokens = 0;
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
                ShouldStop = () => session.Interrupt,
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
                        stream += piece;
                        liveTokens += AgentContext.EstimateTokens(piece);
                        if (!AgentToolCallParser.LooksLikeToolCallText(stream)) turn.Assistant = stream;
                        session.ContextTokens = promptTokens + liveTokens;
                        session.Revision++;
                    }
                },
                Reasoning = piece =>
                {
                    lock (_lock)
                    {
                        turn.Reasoning += piece;
                        liveTokens += AgentContext.EstimateTokens(piece);
                        session.ContextTokens = promptTokens + liveTokens;
                        (turn.TimelineRun ??= new RunTimeline()).Thinking(piece);
                        session.Revision++;
                    }
                },
                ResetStream = () =>
                {
                    lock (_lock)
                    {
                        stream = "";
                        liveTokens = 0;
                        turn.Assistant = "";
                        turn.Reasoning = "";
                        turn.TimelineRun?.DropThinking();
                        session.Revision++;
                    }
                },
                ToolStarted = _ =>
                {
                    lock (_lock)
                    {
                        stream = "";
                        turn.Assistant = "";
                        session.Revision++;
                    }
                },
                ContextTokens = tokens =>
                {
                    lock (_lock)
                    {
                        promptTokens = tokens;
                        liveTokens = 0;
                        session.ContextTokens = tokens;
                        session.Revision++;
                    }
                },
                Step = step =>
                {
                    lock (_lock)
                    {
                        turn.Steps++;
                        turn.Ms += step.DurationMs;
                        string line = step.DisplayLine;
                        turn.ToolLines = string.IsNullOrEmpty(turn.ToolLines) ? line : turn.ToolLines + "\n" + line;
                        (turn.TimelineRun ??= new RunTimeline()).Tool(line);
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
                    result = new WorkerRunResult
                    {
                        Outcome = "stopped",
                        FinalAnswer = ServerNotice.Stopped
                    };
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
                        ? ServerNotice.FailedPrefix + result.ErrorMessage
                        : result.FinalAnswer ?? turn.Assistant;
                    turn.Failed = result.Outcome == "error";
                    turn.FinishReason = result.FinishReason;
                    turn.Transcript = result.Transcript as List<LlmMessage> ?? result.Transcript?.ToList();
                    turn.Timeline = turn.TimelineRun?.Render() ?? turn.Timeline;

                    session.Steps = result.Steps;
                    session.Outcome = result.Outcome;
                    session.Cost = result.Cost;
                    session.ContextTokens = result.ContextTokens ?? session.ContextTokens;
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
                    Timeline = t.TimelineRun?.Render() ?? t.Timeline,
                    Steps = t.Steps,
                    Ms = t.Ms,
                    Model = session.Model,
                    Effort = session.Effort,
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
            ContextLimit = OpenLoop.Service.Models.ModelCatalog.ContextLimit(session.Model, session.Settings()),
            ActivationVersion = session.ActivationVersion,
            FeatureCount = session.FeatureCount,
            RuleCount = session.RuleCount,
            Live = true,
            Turns = session.Turns.Select(t => new WorkerTurn
            {
                User = t.User,
                Assistant = t.Assistant,
                Reasoning = t.Reasoning,
                ToolLines = t.ToolLines,
                Timeline = t.TimelineRun?.Render() ?? t.Timeline,
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

            /// <summary>软停止：让当前轮在下一个 step 前收尾（进行中的工具不打断）。</summary>
            public bool Interrupt { get; set; }
            public int Steps { get; set; }
            public string TraceDirectory { get; set; }
            public string Outcome { get; set; }
            public decimal? Cost { get; set; }
            public int? ContextTokens { get; set; }

            /// <summary>这份会话最后一次落盘的时间（从文件 mtime 取）；null = 新会话。</summary>
            public DateTime? LastSavedAt { get; set; }

            /// <summary>历史是用别的模型写的（前缀缓存对不上）。</summary>
            public bool ModelChanged { get; set; }

            /// <summary>冷启动整理只做一次，之后本会话只追加、不再改写前缀。</summary>
            public bool HistoryPruned { get; set; }
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
