using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Store;
using OpenLoop.Service.Config;
using OpenLoop.Service.Logging;
using OpenLoop.Service.Media;
using OpenLoop.Service.Models;
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

        /// <summary>分段投影：正文出现前的思维/工具一段，正文之后又出现思维另起一段（空 = 旧行为）。</summary>
        public List<WorkerPhase> Phases { get; set; } = [];
        public int Steps { get; set; }
        public long Ms { get; set; }
        public bool Streaming { get; set; }
        public bool Failed { get; set; }

        /// <summary>即时状态（例如「等待审批：shell …」）：运行中显示在对话尾部，结束时清空。</summary>
        public string Status { get; set; } = "";

        /// <summary>这一轮开始前的 git 快照 id，界面「撤回对话」用它恢复文件。</summary>
        public string Checkpoint { get; set; }

        /// <summary>模型结束原因（content_filter 等），界面据此解释空回复。</summary>
        public string FinishReason { get; set; }

        /// <summary>agent 自动插入的协作提示：界面折叠成一行标题、淡黄底，与用户敲的区分。</summary>
        public bool Notice { get; set; }

        /// <summary>自动提示的来源：collab（协作）/ mention（私信）/ puppet（Puppet 动作），界面据此标折叠头。</summary>
        public string NoticeKind { get; set; } = string.Empty;

        /// <summary>这一轮结束时的完整消息（含工具调用/结果），用于下一轮的上下文。</summary>
        public List<LlmMessage> Transcript { get; set; }
    }

    /// <summary>一轮里的一段：（本段思维与工具过程，本段正文）。</summary>
    public sealed class WorkerPhase
    {
        public string Thinking { get; set; }
        public string Answer { get; set; }
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

        /// <summary>工具栏选中的生图模型；空表示用可用清单第一个。</summary>
        public string ImageModel { get; set; }

        /// <summary>true=本进程内存里已有（可直接 GET）；false=只在 chats/ 索引里（要带 id POST 建会话）</summary>
        public bool Live { get; set; }

        /// <summary>最近一轮的结果：ok / budget / stopped / error。</summary>
        public string Outcome { get; set; }
        public decimal? Cost { get; set; }
        public int? ContextTokens { get; set; }

        /// <summary>当前模型的上下文窗口；和 ContextTokens 一起供界面算占比。</summary>
        public int ContextLimit { get; set; }

        /// <summary>最后一轮的步数：会话列表靠它显示步数，不必拉全文。</summary>
        public int Steps { get; set; }

        public List<WorkerTurn> Turns { get; set; } = [];

        /// <summary>排队中的文本；与 <see cref="PendingAuto"/> 一一对应。</summary>
        public List<string> Pending { get; set; } = [];

        /// <summary>true=这条是 agent 自动插入的（协作监测汇报），界面要用特殊底色与用户敲的区分。</summary>
        public List<bool> PendingAuto { get; set; } = [];

        /// <summary>发起方会话 id（协作会话才有）：它不隶属发起方，只记着是谁派的活。</summary>
        public string Parent { get; set; }

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

    /// <summary>手动压缩（/compact）的输入：压缩只需模型与会话信息，不跑工具。</summary>
    public sealed class WorkerCompactRequest
    {
        public AppSettings Settings { get; init; }
        public string Workspace { get; init; }
        public string Model { get; init; }
        public string Effort { get; init; }
        public IReadOnlyList<LlmMessage> History { get; init; }
        public int TailTokens { get; init; }
        public int KeepMessages { get; init; }
    }

    /// <summary>手动压缩的结果：Messages 是压缩后的历史，界面拿它同步本地副本。</summary>
    public sealed class WorkerCompactResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
        public List<LlmMessage> Messages { get; set; }
    }

    /// <summary>
    /// Worker 会话宿主：拥有会话、队列与运行状态，不依赖界面。
    /// WinForms 与 Web 都只读取快照、调用方法；进程内用 <see cref="Changed"/>，跨进程用 revision 轮询。
    /// </summary>
    public sealed partial class WorkerSessionHost
    {
        public const string Idle = "idle";
        public const string Running = "running";
        public const string Done = "done";
        public const string Interrupted = "interrupted";

        private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<WorkerRunRequest, CancellationToken, Task<WorkerRunResult>> _runner;
        private readonly Func<WorkerCompactRequest, CancellationToken, Task<List<LlmMessage>>> _compactor;
        private readonly OpenLoop.Service.Web.LoopApiClient _loop;
        private readonly object _lock = new();
        /// <summary>落盘的串行锁：checkpoint、收尾、退出兜底可能同时写同一个文件（共用 .tmp 名，撞了会丢内容）。</summary>
        private readonly object _persistGate = new();

        public WorkerSessionHost(
            Func<WorkerRunRequest, CancellationToken, Task<WorkerRunResult>> runner = null,
            OpenLoop.Service.Web.LoopApiClient loop = null,
            Func<WorkerCompactRequest, CancellationToken, Task<List<LlmMessage>>> compactor = null)
        {
            _runner = runner ?? ((request, ct) => new WorkerSessionService().RunAsync(request, ct));
            _compactor = compactor ?? CompactWithModel;
            _loop = loop;
        }

        private static async Task<List<LlmMessage>> CompactWithModel(WorkerCompactRequest request, CancellationToken ct)
        {
            var reasoner = Reasoners.Create(request.Settings, request.Model, request.Effort, request.Workspace,
                _ => ApprovalDecision.Deny);
            return await CompressionStep.CompressAsync(request.History, reasoner, ct, request.TailTokens,
                request.KeepMessages);
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
                    // 中途崩掉的轮次（落盘时还在跑、正文为空）读回来补一句可操作的说明：
                    // 补法在 ChatHistory.AssistantFor，与壳侧（ChatStore.Load）同一口径。
                    Assistant = ChatHistory.AssistantFor(entry.Assistant ?? "", entry.Steps, entry.Streaming),
                    Reasoning = entry.Reasoning ?? "",
                    ToolLines = entry.ToolLines ?? "",
                    Timeline = entry.Timeline ?? "",
                    Phases = entry.Phases?.Select(p => new WorkerPhase
                    {
                        Thinking = p?.Thinking ?? "",
                        Answer = p?.Answer ?? ""
                    }).ToList() ?? [],
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
            session.Steps = entries.LastOrDefault()?.Steps ?? 0;

            // 网页/API 冷启动时没带模型：沿用这段历史最后用过的（WinForms 由界面传进来）
            if (string.IsNullOrWhiteSpace(session.Model) || string.IsNullOrWhiteSpace(session.Effort))
            {
                var last = entries.LastOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e?.Model) || !string.IsNullOrWhiteSpace(e?.Effort));
                if (string.IsNullOrWhiteSpace(session.Model)) session.Model = last?.Model ?? "";
                if (string.IsNullOrWhiteSpace(session.Effort)) session.Effort = last?.Effort ?? "";
            }

            // 网页/API 新建时没带模型：最近用过的第一条，与下拉置顶一致
            if (string.IsNullOrWhiteSpace(session.Model))
                session.Model = OpenLoop.Service.Models.ModelCatalog.DefaultModel(session.Settings());

            session.ModelChanged = entries.Any(e => !string.IsNullOrWhiteSpace(e?.Model)
                && !string.Equals(e.Model, session.Model, StringComparison.OrdinalIgnoreCase));

            lock (_lock) _sessions[session.Id] = session;
            // A 重启后会话是懒建的：上次登记的协作监测在这里接着跑
            ResumeCollab(session.Id, session.Workspace);
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
                        Title = entry.Title,
                        Steps = entry.Steps
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
            ImageModel = session.ImageModel,
            State = session.State,
            Revision = session.Revision,
            Paused = session.Paused,
            Outcome = session.Outcome,
            Cost = session.Cost,
            ContextTokens = session.ContextTokens,
            Steps = session.Steps,
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
                session.UserEpoch++;
                session.Revision++;
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>空闲时立即开跑；运行中则排队，等当前轮结束按顺序执行。immediate=运行中插到队首并软打断本轮。</summary>
        public bool Send(string id, string tag, string text, bool immediate = false,
            string imageModel = null, IReadOnlyList<string> attachments = null, string context = null)
        {
            if (string.IsNullOrWhiteSpace(text) && (attachments == null || attachments.Count == 0)) return false;

            string described = Attachments.Describe(attachments);
            if (described.Length > 0)
                text = string.IsNullOrWhiteSpace(text) ? described : text.TrimEnd() + "\n\n" + described;

            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session)) return false;
                session.UserEpoch++;
                // 用户又说话了：上一轮「停止」留下的暂停到此为止，轮后队列照常续发
                session.Paused = false;
                if (!string.IsNullOrWhiteSpace(imageModel)) session.ImageModel = imageModel;
                if (session.State == Running)
                {
                    if (immediate)
                    {
                        session.Pending.Insert(0, new PendingSend
                        {
                            Text = text, Tag = tag ?? "task", Immediate = true,
                            ImageModel = imageModel, Attachments = attachments, Context = context
                        });
                    }
                    else session.Pending.Add(new PendingSend
                    {
                        Text = text, Tag = tag ?? "task",
                        ImageModel = imageModel, Attachments = attachments, Context = context
                    });
                    session.Revision++;
                }
                else
                {
                    Start(session, tag ?? "task", text, imageModel, attachments, context);
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
                session.UserEpoch++;
                session.Revision++;
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>当前轮该在下一个 step 前收尾吗：显式软停止，或队列里还有「立即」项。</summary>
        private bool ShouldStopRun(Session session)
        {
            lock (_lock) return session.Interrupt || session.Pending.Exists(p => p.Immediate);
        }

        /// <summary>
        /// 软停止：只标记，让当前轮在下一个 step 之前收尾（进行中的工具不打断），
        /// 用于「立即」插队 —— 轮次结束后由界面把排队文本当新的一轮发出去。
        /// on=false 收回标记：排队项改回「等本轮」时这一轮不该再被截断。
        /// </summary>
        public bool Interrupt(string id, bool on = true)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                session.Interrupt = on;
                session.UserEpoch++;
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
                session.UserEpoch++;
                session.Revision++;

                // 只在空闲时补发；判空后再取 batch[0]（TakeBatch 可能返回空）
                if (session.State != Running)
                {
                    var batch = TakeBatch(session.Pending);
                    if (batch.Count > 0) Start(session, batch[0].Tag, Merge(batch), batch[0].ImageModel, MergeAttachments(batch), MergeContexts(batch), batch[0].Auto ? batch[0].Tag ?? "collab" : null);
                }
            }

            Changed?.Invoke(id);
            return true;
        }

        /// <summary>丢掉第 keepTurns 轮之后的历史（界面「撤回对话」）。服务端不删，下一次发送会把撤回的轮次带回来。</summary>
        public bool Truncate(string id, int keepTurns)
        {
            Session session;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session)) return false;
                if (session.State == Running) return false;
                int keep = Math.Clamp(keepTurns, 0, session.Turns.Count);
                if (keep == session.Turns.Count) return true;
                session.Turns.RemoveRange(keep, session.Turns.Count - keep);
                session.UserEpoch++;
                session.Revision++;
            }

            Changed?.Invoke(id);
            Persist(session);
            return true;
        }

        /// <summary>
        /// 手动压缩上下文（/compact）：把较早的消息换成模型摘要，写进最近一轮的 transcript，
        /// 下一轮起沿用。运行中拒绝；压缩期间置 Compacting 状态（防止并发触发与误判空闲），
        /// 压缩期间用户又动了会话就丢弃这次结果。摘要调用失败时把真实原因返回给界面。
        /// </summary>
        public async Task<WorkerCompactResult> Compact(string id)
        {
            Session session;
            List<LlmMessage> history;
            int epoch;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out session)) return null;
                if (session.State == Running) return new() { Error = "当前轮还在跑，先停止再压缩。" };
                if (session.Compacting) return new() { Error = "正在压缩中，请稍候。" };
                history = BuildHistory(session.Turns);
                epoch = session.UserEpoch;
                session.Compacting = true;
                session.Revision++;
            }
            try
            {
                if (history.Count < 2) return new() { Error = "上下文太短，没有可压缩的部分。" };

                int before = AgentContext.EstimateTokens(history);
                var settings = session.Settings();
                var policy = FeatureCatalog.PolicyOf(session.Workspace);
                List<LlmMessage> compacted;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    compacted = await _compactor(new WorkerCompactRequest
                    {
                        Settings = settings,
                        Workspace = session.Workspace,
                        Model = session.Model,
                        Effort = session.Effort,
                        History = history,
                        KeepMessages = policy.CompressKeepMessages,
                        TailTokens = AgentContext.TailTokenBudget(
                            ModelCatalog.ContextLimit(session.Model, settings), policy.CompressKeepTokens)
                    }, timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    AppLog.Warn("手动压缩超时（120 秒）");
                    return new() { Error = "压缩超时（120 秒），模型没响应；稍后可重试。" };
                }
                catch (Exception ex)
                {
                    AppLog.Warn("手动压缩失败：" + ex.Message);
                    return new() { Error = "压缩调用失败：" + ex.Message };
                }

                if (compacted == null || compacted.Count == 0)
                    return new() { Error = "上下文太短，没有可压缩的中间部分。" };

                int after;
                lock (_lock)
                {
                    if (session.State == Running || session.UserEpoch != epoch)
                        return new() { Error = "会话有新动作，本次压缩已丢弃。" };
                    after = ApplyCompacted(session.Turns, compacted);
                    if (after < 0) return new() { Error = "没有可压缩的上下文。" };
                    session.ContextTokens = after;
                    session.UserEpoch++;
                    session.Revision++;
                }

                Changed?.Invoke(id);
                Persist(session);
                return new() { Ok = true, Before = before, After = after, Messages = compacted };
            }
            finally
            {
                lock (_lock)
                {
                    session.Compacting = false;
                    session.Revision++;
                }
                Changed?.Invoke(id);
            }
        }

        /// <summary>把压缩结果挂到最近一轮，下一轮的 BuildHistory 就以它为准；返回压缩后的估算 token。</summary>
        internal static int ApplyCompacted(List<WorkerTurn> turns, IReadOnlyList<LlmMessage> compacted)
        {
            if (turns == null || turns.Count == 0 || compacted == null) return -1;
            var list = compacted
                .Where(m => m != null && !string.IsNullOrEmpty(m.Content)
                    && !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                .Select(m => new LlmMessage(m.Role, m.Content))
                .ToList();
            if (list.Count == 0) return -1;
            turns[^1].Transcript = list;
            return AgentContext.EstimateTokens(list);
        }

        public bool RemovePending(string id, int index)
        {
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id ?? "", out var session)) return false;
                if (index < 0 || index >= session.Pending.Count) return false;
                session.Pending.RemoveAt(index);
                session.UserEpoch++;
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

                var item = session.Pending[index];
                session.Pending.RemoveAt(index);
                session.Pending.Insert(0, new PendingSend
                {
                    Text = item.Text, Tag = item.Tag, Immediate = true, Context = item.Context
                });
                session.Paused = false;
                session.UserEpoch++;
                session.Revision++;

                if (session.State == Running)
                {
                    // 打断由 Pending 里的即时项驱动（见 ShouldStop），撤回该项后自然不再截断
                    Changed?.Invoke(id);
                    return true;
                }

                var batch = TakeBatch(session.Pending);
                if (batch.Count > 0) Start(session, batch[0].Tag, Merge(batch), batch[0].ImageModel, MergeAttachments(batch), MergeContexts(batch), batch[0].Auto ? batch[0].Tag ?? "collab" : null);
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
            OpenLoop.Service.Web.SessionBoard.Leaving(
                AgentIdentity.IdFor("A", session.Id), LastWords(session));
            return true;
        }

        /// <summary>鍚屽伐浣滃尯鏈夋柊浼氳瘽鏃跺線闃熷垪閲屾斁涓€鏉″崗浣滄彁绀猴紙涓庣敤鎴疯緭鍏ュ悓涓€闃熷垪锛屽彲鎾ゅ洖锛夈€?/summary>
        private void AnnouncePeers(Session session)
        {
            // 璋冪敤鏂规寔鏈?_lock
            var peers = OpenLoop.Service.Web.SessionBoard.Peers(
                session.Workspace, AgentIdentity.IdFor("A", session.Id));
            var fresh = peers.Where(p => session.PeersAnnounced.Add(p.Id)).ToList();
            if (fresh.Count == 0) return;
            // 协作提示与监测汇报同一时机：插到队首、标 Immediate，当前轮在下一个 step 前收尾后先发
            session.Pending.Insert(0, new PendingSend
            {
                Text = OpenLoop.Service.Web.SessionBoard.PeerNotice(fresh.Select(p => p.Summary()).ToList()),
                Tag = "collab",
                Auto = true,
                Immediate = true
            });
            session.Revision++;
        }

        /// <summary>会话关闭时留给留言板的最后一句：优先最后一条回答，未完成的任务说清楚。</summary>
        private static string LastWords(Session session)
        {
            var last = session.Turns.LastOrDefault();
            if (last?.Streaming == true) return "（会话被关闭，任务未完成）";
            return string.IsNullOrWhiteSpace(last?.Assistant) ? "（会话结束）" : last.Assistant;
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

        /// <summary>正文区的执行中提示：并行批次逐行累积（同一行不重复叠加），Step 收尾时整批清空。</summary>
        private static string AppendToolLine(string status, string line)
            => string.IsNullOrEmpty(line) ? status
            : string.IsNullOrEmpty(status) ? line
            : status.Contains(line) ? status
            : status + "\n" + line;

        private void Start(Session session, string tag, string text,
            string imageModel = null, IReadOnlyList<string> attachments = null, string context = null,
            string noticeKind = null)
        {
            // 调用方持有 _lock
            if (!session.HistoryPruned)
            {
                session.HistoryPruned = true;
                MaybePruneHistory(session);
            }
            if (!string.IsNullOrWhiteSpace(imageModel)) session.ImageModel = imageModel;
            List<LlmMessage> history = BuildHistory(session.Turns);
            // 协作提示（agent 自己塞进来的仿用户消息）单独标记：界面折叠成一行、淡黄底，不冒充用户输入
            var turn = new WorkerTurn
            {
                User = text, Streaming = true,
                Notice = !string.IsNullOrWhiteSpace(noticeKind),
                NoticeKind = noticeKind ?? ""
            };
            session.Turns.Add(turn);
            session.State = Running;
            session.Revision++;
            // 中途快照的节流间隔：开轮时从账本读一次（limit.checkpoint_interval），别每步都去读文件
            session.CheckpointEvery = ChatHistory.CheckpointIntervalOf(session.Workspace);

            var cts = new CancellationTokenSource();
            session.Cancel?.Dispose();
            session.Cancel = cts;
            session.Interrupt = false;

            // 留言板报到：首轮附自我介绍（身份+任务），之后每次开轮只更新「在做的事」。
            OpenLoop.Service.Web.SessionBoard.Opening(
                AgentIdentity.IdFor("A", session.Id), "Worker", session.Workspace, session.Model, text);
            AnnouncePeers(session);

            string id = session.Id;
            // 流式正文只写截掉协议片段后的部分（CutToolCallText），与 LoopView/SideWorkView 的
            // streamBuffer 过滤保持一致；协议原文不能出现在正文里。
            string stream = "";
            // 正文喂进时间线的进度：正文出现即分段，正文之后的思维另起一段折叠区
            int answerLen = 0;
            // 上下文占比：本轮 prompt 估算 + 已生成内容（按增量累加，避免每次 delta 重扫整段）
            int promptTokens = 0, liveTokens = 0;
            var request = new WorkerRunRequest
            {
                Settings = session.Settings(),
                Workspace = session.Workspace,
                Model = session.Model,
                Effort = session.Effort,
                SessionId = session.Id,
                // 协作工具只在设置里打开时挂给会话：关掉后连工具名都看不见
                Collab = session.Settings().EnableCollab ? this : null,
                Tag = tag,
                Text = text,
                Context = context,
                ImageModel = session.ImageModel,
                Attachments = attachments ?? [],
                History = history,
                Approve = approval => Approve(session, approval, cts.Token),
                ConfirmContinue = message => Ask(session, "continue", "", message, "", "", "", cts.Token) > 0,
                ShouldStop = () => ShouldStopRun(session),
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
                        // 正文只放截掉协议片段后的部分：模型边写正文边吐调用原文时，别让协议原文先闪现
                        turn.Assistant = AgentToolCallParser.CutToolCallText(stream);
                        if (turn.Assistant.Length > answerLen)
                        {
                            (turn.TimelineRun ??= new RunTimeline()).Answer(turn.Assistant[answerLen..]);
                            answerLen = turn.Assistant.Length;
                        }
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
                        answerLen = 0;
                        turn.TimelineRun?.DropThinking();
                        session.Revision++;
                    }
                },
                ToolStarted = line =>
                {
                    lock (_lock)
                    {
                        stream = "";
                        turn.Assistant = "";
                        answerLen = 0;
                        turn.Status = AppendToolLine(turn.Status, line);
                        session.Revision++;
                    }
                },
                Notice = text =>
                {
                    lock (_lock)
                    {
                        turn.Status = text ?? "";
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
                        // 一批工具收尾：正文区的执行中提示整批清除（并行批次也是一起清）
                        turn.Status = "";
                        session.Revision++;
                    }
                    // 中途快照：整轮可能跑十几二十分钟，只在收尾落盘的话进程一崩就整段丢失。
                    // 按账本间隔节流（对话文件可能几十 MB，每步都写会拖慢运行）。
                    Checkpoint(session);

                    // 留言板进度：同工作区的其他会话据此看这一步改了什么、成没成
                    string note = step.DisplayLine.Replace(" >> ", "");
                    if (!string.IsNullOrEmpty(step.Summary)) note += " → " + step.Summary;
                    OpenLoop.Service.Web.SessionBoard.Working(AgentIdentity.IdFor("A", session.Id), note);
                }
            };

            _ = Task.Run(async () =>
            {
                // 这一轮先落盘再开跑（与 Loop 侧「发消息即 SaveLoopChat」同一口径）：
                // 用户请求与「运行中」状态都在历史里，进程中途崩了至少不丢这条需求。
                // 放在后台任务里是因为 Start 的调用方持有 _lock，写几十 MB 的文件不该压在里面。
                Persist(session);

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

                string failedModel = null;
                lock (_lock)
                {
                    turn.Streaming = false;
                    turn.Status = "";
                    turn.Steps = result.Steps > turn.Steps ? result.Steps : turn.Steps;
                    turn.Assistant = result.Outcome switch
                    {
                        "error" => FailedText(result.ErrorMessage),
                        // 被打断的轮次保留已经流出来的正文，否则用户前面看到的输出会凭空消失
                        "stopped" when string.IsNullOrWhiteSpace(result.FinalAnswer) || ServerNotice.IsNotice(result.FinalAnswer)
                            => string.IsNullOrWhiteSpace(turn.Assistant) ? ServerNotice.Stopped : turn.Assistant,
                        _ => result.FinalAnswer ?? turn.Assistant
                    };
                    turn.Failed = result.Outcome == "error";
                    turn.FinishReason = result.FinishReason;
                    turn.Transcript = result.Transcript as List<LlmMessage> ?? result.Transcript?.ToList();
                    turn.Timeline = turn.TimelineRun?.Render() ?? turn.Timeline;
                    if (turn.TimelineRun != null) turn.TimelineRun.FeedAnswerTail(turn.Assistant);
                    turn.Phases = PhasesOf(turn);

                    session.Steps = result.Steps;
                    session.Outcome = result.Outcome;
                    session.Cost = result.Cost;
                    session.ContextTokens = result.ContextTokens ?? session.ContextTokens;
                    session.State = result.Outcome is "ok" or "budget" ? Done : Interrupted;
                    session.Revision++;
                    failedModel = session.Model;

                    // 失败轮（接口报错/额度耗尽）不收尾排队：等有新输入激活会话后队列才按规则继续
                    if (result.Outcome != "error" && !session.Paused && session.Pending.Count > 0)
                    {
                        var batch = TakeBatch(session.Pending);
                        if (batch.Count > 0) Start(session, batch[0].Tag, Merge(batch), batch[0].ImageModel, MergeAttachments(batch), MergeContexts(batch), batch[0].Auto ? batch[0].Tag ?? "collab" : null);
                    }
                }

                Changed?.Invoke(id);
                Persist(session);

                // 免费额度耗尽：让 ConnMan 换节点，就绪后由它回调这里给原会话发「继续」
                if (result.Outcome == "error"
                    && ServerNotice.IsQuotaFailure(ServerNotice.FailedPrefix + result.ErrorMessage)
                    && FeatureCatalog.PolicyOf(session.Workspace).FreeLimitRecovery)
                {
                    FreeLimitRecovery.TryStart(id, failedModel, () => UserEpochOf(id), text => Send(id, "task", text));
                }
            });
        }

        /// <summary>失败轮的正文：只写失败原因，跑了几步看思维链区（与 Loop 侧同一文案）。</summary>
        private static string FailedText(string error)
            => ServerNotice.FailedPrefix + error;

        /// <summary>会话的用户动作计数；会话已不存在返回 null（免费额度自愈据此收工）。</summary>
        private int? UserEpochOf(string id)
        {
            lock (_lock) return _sessions.TryGetValue(id ?? "", out var session) ? session.UserEpoch : null;
        }

        /// <summary>按时间线切段投影；没有采集数据（旧会话）时沿用已有分段，别把落盘的分段抹掉。</summary>
        private static List<WorkerPhase> PhasesOf(WorkerTurn turn)
        {
            var phases = turn.TimelineRun?.RenderPhases()
                .Select(p => new WorkerPhase { Thinking = p.Thinking, Answer = p.Answer }).ToList()
                ?? turn.Phases ?? [];
            // 失败/停止轮的正文是接口提示而不是模型说过的话：挂到末段，否则被分段的正文顶掉、用户看不到报错
            if (phases.Count > 0 && !turn.Streaming && ServerNotice.IsNotice(turn.Assistant))
                phases[^1].Answer = turn.Assistant;
            return phases;
        }

        /// <summary>
        /// 流式期间的中途快照：整轮可能跑十几二十分钟，只在收尾落盘的话进程中途崩了就整段丢失。
        /// 按 <see cref="Session.CheckpointEvery"/> 节流（账本 limit.checkpoint_interval），与 Loop 侧同一口径。
        /// </summary>
        private void Checkpoint(Session session)
        {
            if (session.Turns.Count == 0) return;
            if (DateTime.UtcNow - session.LastCheckpointAt < session.CheckpointEvery) return;
            Persist(session);
        }

        /// <summary>
        /// 进程要退出时的兜底：把还在跑的轮次立刻落一次（已收尾的轮次早就写过了）。
        /// 父进程消失后 A 只活 1 秒，别指望下一次中途快照。
        /// </summary>
        public void PersistRunning()
        {
            List<Session> running;
            lock (_lock) running = _sessions.Values.Where(s => s.State == Running).ToList();
            foreach (var session in running)
            {
                try
                {
                    Persist(session);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("退出前保存会话失败（" + session.Id + "）：" + ex.Message);
                }
            }
        }

        /// <summary>把会话的轮次写到 chats/（与旧版 App 同一个文件格式）。</summary>
        private void Persist(Session session)
        {
            List<ChatEntry> entries;
            string workspace;
            string id;
            int steps;

            lock (_lock)
            {
                workspace = session.Workspace;
                id = session.Id;
                // 步数取「已完成轮」与「正在跑的这轮」的较大者：中途快照也让列表显示真实进度（与 Loop 同口径）
                steps = Math.Max(session.Steps, session.Turns.LastOrDefault()?.Steps ?? 0);
                session.LastCheckpointAt = DateTime.UtcNow;
                entries = session.Turns.Select(t => new ChatEntry
                {
                    User = t.User,
                    Assistant = t.Assistant,
                    Reasoning = t.Reasoning,
                    ToolLines = t.ToolLines,
                    Timeline = t.TimelineRun?.Render() ?? t.Timeline,
                    Phases = PhasesOf(t)
                        .Select(p => new ChatPhaseEntry { Thinking = p.Thinking, Answer = p.Answer }).ToList(),
                    Steps = t.Steps,
                    Ms = t.Ms,
                    Notice = t.Notice,
                    NoticeKind = t.NoticeKind,
                    Model = session.Model,
                    Effort = session.Effort,
                    // 中途快照：这一轮还在跑，读回来据此区分「跑到一半崩了」与「真的什么都没产出」
                    Streaming = t.Streaming,
                    Messages = t.Transcript?
                        .Select(m => new ChatMessageEntry { Role = m.Role, Content = m.Content })
                        .ToList()
                }).ToList();
            }

            if (entries.Count == 0)
            {
                // 撤回全部轮次后这条会话就没有对话了：别留文件，也别再登进索引
                lock (_persistGate) ChatHistory.Delete(workspace, id);
                return;
            }

            lock (_persistGate)
            {
                ChatHistory.Write(workspace, id, entries);
                ChatHistory.Touch(workspace, id, null, steps);
            }
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

        private static List<PendingSend> TakeBatch(List<PendingSend> pending)
        {
            if (pending.Count == 0) return new List<PendingSend>();
            bool immediate = pending[0].Immediate;
            var batch = pending.Where(p => p.Immediate == immediate).ToList();
            foreach (var item in batch) pending.Remove(item);
            return batch;
        }

        private static string Merge(List<PendingSend> batch) =>
            batch.Count == 1 ? batch[0].Text
                : string.Join("\n\n", batch.Select((b, i) => $"{i + 1}. {b.Text}"));

        private static List<string> MergeAttachments(List<PendingSend> batch)
            => batch.SelectMany(b => b.Attachments ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static string MergeContexts(List<PendingSend> batch)
            => string.Join("\n\n", batch.Select(b => b.Context)
                .Where(c => !string.IsNullOrWhiteSpace(c)));
        private WorkerSessionSnapshot snapshot(Session session) => new()
        {
            Id = session.Id,
            Workspace = session.Workspace,
            Model = session.Model,
            Effort = session.Effort,
            ImageModel = session.ImageModel,
            State = session.State,
            Revision = session.Revision,
            Paused = session.Paused,
            TraceDirectory = session.TraceDirectory,
            Outcome = session.Outcome,
            Cost = session.Cost,
            ContextTokens = session.ContextTokens,
            ContextLimit = OpenLoop.Service.Models.ModelCatalog.ContextLimit(session.Model, session.Settings()),
            Steps = session.Steps,
            ActivationVersion = session.ActivationVersion,
            FeatureCount = session.FeatureCount,
            RuleCount = session.RuleCount,
            Live = true,
            Turns = session.Turns.Select(t => new WorkerTurn
            {
                User = t.User,
                // 与壳侧（ChatStore.Save）同一口径：空白正文补一句可操作的说明，正在跑的轮次留原样
                Assistant = t.Streaming ? t.Assistant : ChatHistory.AssistantFor(t.Assistant, t.Steps),
                Reasoning = t.Reasoning,
                ToolLines = t.ToolLines,
                Timeline = t.TimelineRun?.Render() ?? t.Timeline,
                Phases = PhasesOf(t),
                Steps = t.Steps,
                Ms = t.Ms,
                Streaming = t.Streaming,
                Failed = t.Failed,
                Status = t.Status,
                Checkpoint = t.Checkpoint,
                FinishReason = t.FinishReason,
                Notice = t.Notice,
                NoticeKind = t.NoticeKind
            }).ToList(),
            Pending = session.Pending.Select(p => p.Text).ToList(),
            PendingAuto = session.Pending.Select(p => p.Auto).ToList(),
            Parent = ParentOf(session.Id),
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

        private sealed class PendingSend
        {
            public string Text { get; init; }
            public string Tag { get; init; }
            public bool Immediate { get; set; }

            /// <summary>只给模型看的补充上下文（@ 引用的其他会话），不写进界面上的用户消息。</summary>
            public string Context { get; init; }

            /// <summary>agent 自动插入（协作监测汇报等），不是用户敲的。</summary>
            public bool Auto { get; init; }
            public string ImageModel { get; init; }
            public IReadOnlyList<string> Attachments { get; init; }
        }
        private sealed class Session
        {
            public string Id { get; init; }
            public string Workspace { get; init; }
            public string Model { get; set; }
            public string Effort { get; set; }
            public string ImageModel { get; set; }
            public Func<AppSettings> Settings { get; init; }
            public DateTime CreatedAt { get; } = DateTime.UtcNow;
            public List<WorkerTurn> Turns { get; } = [];
            public List<PendingSend> Pending { get; } = [];
            public List<Prompt> Prompts { get; } = [];

            /// <summary>宸茬粡鎻愮ず杩囩殑鏂板悓浼翠細璇?id锛氬悓涓€涓悓浼村彧鍏ラ槦涓€娆★紝鍏嶅緱姣忚疆閮藉埛鎻愮ず銆?/summary>
            public HashSet<string> PeersAnnounced { get; } = new(StringComparer.Ordinal);

            public string State { get; set; } = Idle;
            public int Revision { get; set; }

            /// <summary>用户动过这个会话的次数；免费额度自愈靠它判断该不该继续重试。</summary>
            public int UserEpoch { get; set; }
            public bool Paused { get; set; }

            /// <summary>软停止：让当前轮在下一个 step 前收尾（进行中的工具不打断）。</summary>
            public bool Interrupt { get; set; }

            /// <summary>手动 /compact 进行中：防止并发触发；轮询/发送路径可据此提示。</summary>
            public bool Compacting { get; set; }
            public int Steps { get; set; }
            public string TraceDirectory { get; set; }
            public string Outcome { get; set; }
            public decimal? Cost { get; set; }
            public int? ContextTokens { get; set; }

            /// <summary>这份会话最后一次落盘的时间（从文件 mtime 取）；null = 新会话。</summary>
            public DateTime? LastSavedAt { get; set; }

            /// <summary>上一次中途快照的时间；与 <see cref="CheckpointEvery"/> 一起做节流。</summary>
            public DateTime LastCheckpointAt { get; set; }

            /// <summary>中途快照的节流间隔：开轮时从账本读一次（limit.checkpoint_interval），别每步都读文件。</summary>
            public TimeSpan CheckpointEvery { get; set; } = ChatHistory.DefaultCheckpointInterval;

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
