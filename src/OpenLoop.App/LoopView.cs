using System.ComponentModel;
using System.Text;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Trace;
using OpenLoop.Service.Pending;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 的会话视图（观察者）：与 Worker 页相似的聊天界面，模型是同一个 Loop agent，
    /// 但提示词是观察者/改进者角色，历史单独存（ChatStore kind="loop"）。
    /// </summary>
    public partial class LoopView : UserControl
    {
        private readonly ChatTurnBook _book = new();

        /// <summary>每轮开始前的 git 快照，供「撤回」把 Loop 写过的源码/配置还原。</summary>
        private readonly Dictionary<ChatTurn, (string Root, string Checkpoint)> _turnSnapshots = [];
        private readonly StringBuilder _streamBuffer = new();
        private Func<AppSettings> _settings;
        private CancellationTokenSource _cts;
        private string _workspace = "";
        private string _sessionId = LoopRef.LegacyId;
        private string _model = "";
        private string _effort = "";
        private bool _onlyFree = true;
        private string _lastCost = "—";
        private int? _contextTokens;
        // 上下文占比的实时估算：本轮请求的 prompt + 已生成内容（按增量累加）
        private int _promptTokens, _liveTokens;
        private bool _running;
        private bool _filling;
        private bool _fillingProviders;
        private bool _iterating;
        private bool _layoutReady;

        /// <summary>「文件」页在侧栏标签里的下标；第一次点文件路径时才加出来（-1 = 还没有）。</summary>
        private int _filePage = -1;

        /// <summary>运行中提交的文本排队（含 /complain 投递）：默认等本轮返回，可切「立即」插到下一步。</summary>
        private readonly OpenLoop.Service.Pending.PendingQueue _queue = new();
        private OpenLoop.Service.Pending.PendingItem _editing;

        /// <summary>「立即」标记：当前轮在下一个 step 前收尾（进行中的工具不打断）。</summary>
        private bool _interrupt;

        /// <summary>账本归 I 所有：技能激活集从这里读，本进程不写账本。</summary>
        private OpenLoop.Service.Web.LoopApiClient _loopClient;
        private OpenLoop.Service.Web.LoopApiClient Loop => _loopClient ??= new OpenLoop.Service.Web.LoopApiClient();

        public LoopView()
        {
            InitializeComponent();
            _txtInput.VerifyKeyboard += OnComposerKeyboard;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status => _lblStatus.Text;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Workspace => _workspace;

        /// <summary>Loop 会话 id（对话文件的 kind）；同一工作区可以有多个。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SessionId => _sessionId;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Model => _model;
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool OnlyFreeModels
        {
            get => _onlyFree;
            set
            {
                _onlyFree = value;
                _filling = true;
                try { _freeSwitch.Checked = value; }
                finally { _filling = false; }
                Persist(s => s.OnlyFreeModels = value);
                FillModels();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ModelOptionCount => _selModel.Items.Count;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SideTabNames => string.Join("\n",
            _sideTabs.Items.OfType<AntdUI.SegmentedItem>().Select(i => i.Text));

        public void ShowSidePage(int index)
        {
            _sideTabs.SelectIndex = index;
            UpdateSideVisibility(index);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ComposerText
        {
            get => _txtInput.Text;
            set => _txtInput.Text = value;
        }

        /// <summary>会话语境（由宿主注入）：调查卡插到哪个会话、锚定第几轮。</summary>
        public Func<(string Id, int Turn)> SessionTarget { get; set; }

        /// <summary>/fork：把当前 Loop 对话复制进一个新 Loop 会话，并把新请求发出去。</summary>
        internal event Action<LoopView, string> ForkRequested;

        /// <summary>/new：开一个新的空 Loop 会话（沿用当前模型与强度）。</summary>
        internal event Action<LoopView, string> NewSessionRequested;

        /// <summary>标题（重新总结后）变了：宿主用它刷新标签页文本。</summary>
        internal event Action<string> TitleChanged;

        internal IReadOnlyList<ChatTurn> Turns => _book.Turns;

        internal void Initialize(string workspace, Func<AppSettings> settings,
            string sessionId = LoopRef.LegacyId)
        {
            _workspace = workspace ?? "";
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            _settings = settings;

            var current = settings();
            _model = string.IsNullOrWhiteSpace(current.Model) ? "openrouter/free" : current.Model;
            _effort = current.ReasoningEffort ?? "";
            _onlyFree = current.OnlyFreeModels;

            _filling = true;
            try { _freeSwitch.Checked = _onlyFree; }
            finally { _filling = false; }

            FillProviders();
            FillModels();
            SelectEffort(_effort);
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort);
            _tips.SetToolTip(_btnIterate, "把当前源码编译并登记为 A/I 新版本（不自动重启）");
            _pending.Bind(_queue);
            _pending.EditRequested += BeginEdit;
            _pending.ImmediateRequested += OnImmediateRequested;
            _pending.Removed += DropEdit;
            // 队列跟着会话落盘：进程被强杀也不丢已经排好的消息
            var savedQueue = PendingStore.Load(_workspace, _sessionId);
            _queue.Restore(savedQueue.Items, savedQueue.Paused);
            _queue.Changed += () => PendingStore.Save(_workspace, _sessionId, _queue);
            _pending.Sync();
            _txtInput.TextChanged += (_, _) => { UpdateSendButton(); UpdateCommandPopup(); };

            _book.ReplaceAll(ChatStore.Load(_workspace, _sessionId), null);
            ChatStore.PruneCold(_workspace, _sessionId, _model, _book.Turns);
            LoopIndex.Touch(_workspace, _sessionId);
            _chat.PlaceholderText = Localization.T("NoMessages");
            _chat.UndoRequested += OnChatUndo;
            _chat.CopyRequested += OnChatCopy;
            _chat.LinkClicked += OnChatLinkClicked;
            // 工具栏「操作」下拉与右键菜单的「操作」子菜单共用同一组动作
            _chat.SelectionActions = [new ChatSelectionAction("copy", "复制"), new ChatSelectionAction("worker", "转给 worker"), new ChatSelectionAction("delete", "删除")];
            _chat.SelectionActionRequested += (_, e) => RunSelectionAction(e.Key);
            _chat.SelectionModeChanged += (_, _) =>
            {
                if (_chkSelect.Checked != _chat.ShowSelection) _chkSelect.Checked = _chat.ShowSelection;
            };
            _chat.Bind(_book);
            _lblSub.Text = "工作区 · " + UiStyle.ShortPath(_workspace);
            _tips.SetToolTip(_lblSub, _workspace);
            _tips.SetToolTip(_btnSummary, "用当前模型重新总结会话标题");
            string savedTitle = LoopIndex.Title(_workspace, _sessionId);
            _lblTitle.Text = Session.ComposeTitle(_workspace, savedTitle, loop: true);
            _chat.ScrollToEnd();

            _lblStatus.TextChanged += (_, _) => ApplyDetail();
            _sideBtw.Initialize(_workspace, settings);
            ApplyDetail();

            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) =>
        {
            ModelCatalog.Changed -= OnCatalogChanged;
            _loopClient?.Dispose();
        };
        }

        private void OnSummarize(object sender, EventArgs e) => _ = SummarizeTitleAsync();

        /// <summary>用当前模型把这段 Loop 对话总结成标题，覆盖页头并记进 loops.json。</summary>
        private async Task SummarizeTitleAsync(bool notify = true)
        {
            var turns = _book.Turns;
            if (turns.Count == 0) return;

            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = _settings().EndpointFor(model);
            if (baseUrl?.Trim().Length == 0 || model.Length == 0) return;

            var messages = new List<LlmMessage>
            {
                new("system", "你给对话起标题。只输出标题本身：不超过 12 个汉字，不加引号、不加句号、不解释。"),
                new("user", SessionView.TitleInput(turns))
            };
            try
            {
                var reasoner = Reasoners.Create(_settings(), model, "", _workspace);
                var completion = await reasoner.CompleteAsync(messages, CancellationToken.None);
                string title = SessionView.CleanTitle(completion?.Content);
                if (title.Length == 0) return;
                bool same = string.Equals(LoopIndex.Title(_workspace, _sessionId), title, StringComparison.Ordinal);
                LoopIndex.Rename(_workspace, _sessionId, title);
                _lblTitle.Text = Session.ComposeTitle(_workspace, title, loop: true);
                TitleChanged?.Invoke(_lblTitle.Text);
                if (notify) Notify(same ? "标题未变化" : "标题已更新", _lblTitle.Text);
            }
            catch (Exception ex)
            {
                AppLog.Warn("生成 Loop 标题失败：" + ex.Message);
                if (notify) Notify("总结失败", ex.Message, error: true);
            }
        }

        public void SetModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            _model = model.Trim();
            var provider = _selProvider.SelectedValue as ProviderConfig;
            if (provider != null && !(provider.Models ?? []).Any(m =>
                    string.Equals(m, _model, StringComparison.OrdinalIgnoreCase)))
            {
                _fillingProviders = true;
                try { _selProvider.SelectedIndex = 0; }
                finally { _fillingProviders = false; }
            }
            _selModel.SelectedValue = _model;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Effort => _effort;

        public void SetEffort(string effort) => SelectEffort(effort);

        public void Send(string text = null)
        {
            if (!string.IsNullOrWhiteSpace(text)) SendAsync(text);
            else SendAsync(null);
        }

        /// <summary>外部投递（/complain）：空闲就立即处理，忙就排队等当前轮结束后按顺序处理。</summary>
        internal void Post(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _queue.Add(text);
            _pending.Sync();
            DrainQueue();
        }

        /// <summary>当前轮结束后调用：优先「立即」，其次按入队顺序。</summary>
        private void DrainQueue()
        {
            if (IsDisposed || _running) return;
            var next = _queue.TakeNext();
            if (next == null) return;
            _pending.Sync();
            Send(next.Text);
        }

        public void Stop() => _cts?.Cancel();

        public void Shutdown()
        {
            _cts?.Cancel();
            _sideBtw.Shutdown();
            SaveLoopChat();
        }

        /// <summary>对话正文按「工作区|会话 id」存；顺带把项目记进 Loop 索引，供 [Loop] 页列出。</summary>
        private void SaveLoopChat()
        {
            ChatStore.Save(_workspace, _book.Turns, _sessionId, _model);
            LoopIndex.Touch(_workspace, _sessionId);
        }

        private void OnViewLoad(object sender, EventArgs e)
        {
            if (_book.Turns.Count > 0) _chat.ScrollToEnd();
            BeginInvoke(new Action(ApplyLayout));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_layoutReady) ApplyLayout();
        }

        private async void SendAsync(string text = null)
        {
            if (_running) return;

            var settings = _settings();
            bool fromInput = text == null;
            text = (fromInput ? _txtInput.Text : text).Trim();
            if (text.Length == 0) return;
            if (!Directory.Exists(_workspace))
            {
                Notify("项目不可用", "工作区目录不存在：" + _workspace, error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "请先在设置里填写接口地址、模型。", error: true);
                return;
            }

            // 先把用户消息挂上并让界面画出来：技能扫描、工具注册、快照（多次 git 子进程）和 trace 都放后台，
            // 否则按下 Enter 到消息出现之间会卡好几秒。
            var history = BuildHistory();   // 不含本轮：本轮文本随 RunAsync 单独传入

            var turn = new ChatTurn
            {
                User = new ChatMessage("user", text),
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true,
                ThinkingExpanded = _settings().ThinkingExpandedByDefault
            };
            _book.AddTurn(turn);
            if (fromInput) _txtInput.Clear();
            SetRunning(true);
            _lblStatus.Text = "状态: 运行中";
            await Task.Yield();
            _chat.ScrollToEnd();

            var policy = new AgentPolicy();

            // 提前建好取消令牌：准备阶段也能点「停止」，准备完会立刻按已取消处理
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            IReadOnlyList<SkillInfo> skills;
            IReadOnlyList<string> skillProblems;
            ToolRegistry tools;
            string root;
            string checkpoint;
            IReasoner reasoner;
            TraceWriter trace;
            try
            {
                (skills, skillProblems, tools, root, checkpoint, reasoner, trace) = await Task.Run(() =>
                {
                    var skillCatalog = SkillCatalog.Load(_workspace, AppSettings.GlobalSkillsPath);
                    // 与 Worker/旁路一致：策略来自当前工作区的激活集（拿不到就保持默认策略）
                    try
                    {
                        var activation = Loop.GetActivation(_workspace);
                        if (activation?.Activation != null)
                            policy = FeatureCatalog.PolicyFrom(activation.Activation, activation.Features);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("读取策略激活集失败：" + ex.Message);
                    }
                    var activeSkills = ActiveSkills(skillCatalog, out var problems);
                    OpenLoop.Core.Safety.ApprovalHandler approve = AskApproval;
                    // 章程 §2：Loop 读与 Worker 同宽，写只限配置/源码/安装路径
                    var (readScope, writeScope) = OpenLoop.Service.Source.LoopScope.Create(approve);
                    var loopTools = ToolRegistry.Loop(readScope, writeScope, _workspace, approve, activeSkills,
                        OpenCodeTools.For(writeScope.Root, approve, settings.OpenCodeAskPermissions)
                            .Concat(DshTools.For(settings, writeScope.Root, approve))
                            .Concat(OpenLoop.Service.Web.PuppetTools.For()));
                    var registry = new ToolRegistry(loopTools.Tools.Concat(
                        [new OpenLoop.Core.Tools.Builtin.AskUserTool(_workspace, () => SessionTarget?.Invoke() ?? ("", -1))]));
                    string snapshot = Snapshots.Create(writeScope.Root, "loop");
                    if (snapshot != null) AppLog.Info("snapshot " + snapshot);
                    var created = Reasoners.Create(settings, model, _effort, writeScope.Root, approve);
                    var writer = TraceWriter.Start(_workspace, "loop", text, model);
                    return (activeSkills, problems, registry, writeScope.Root, snapshot, created, writer);
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop 准备失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = "执行失败：" + ex.Message;
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                _lblStatus.Text = "状态: error";
                RefreshChat();
                Notify("Loop 执行失败", ex.Message, error: true);
                return;
            }

            if (checkpoint != null) _turnSnapshots[turn] = (root, checkpoint);
            trace.SkillSet(skills, skillProblems);
            trace.Message("user", text);

            _streamBuffer.Clear();
            _promptTokens = 0;
            _liveTokens = 0;
            var timeline = new RunTimeline();
            Action<string> onDelta = piece =>
            {
                lock (_streamBuffer) _streamBuffer.Append(piece);
                _liveTokens += AgentContext.EstimateTokens(piece);
                string snapshot;
                lock (_streamBuffer) snapshot = _streamBuffer.ToString();
                if (AgentToolCallParser.LooksLikeToolCallText(snapshot)) return;
                RunOnUi(() =>
                {
                    _contextTokens = _promptTokens + _liveTokens;
                    ApplyDetail();
                    _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(snapshot));
                });
            };

            //推理通道：推理模型只吐 reasoning 时也要有内容可看
            var reasoningBuffer = new StringBuilder();
            long lastReasoningUi = 0;
            Action<string> onReasoning = piece =>
            {
                lock (reasoningBuffer) reasoningBuffer.Append(piece);
                _liveTokens += AgentContext.EstimateTokens(piece);
                lock (timeline) timeline.Thinking(piece);
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastReasoningUi) < 250) return;
                Interlocked.Exchange(ref lastReasoningUi, now);
                string snapshot;
                lock (reasoningBuffer) snapshot = reasoningBuffer.ToString();
                RunOnUi(() =>
                {
                    turn.ReasoningText = snapshot;
                    lock (timeline) turn.TimelineText = timeline.Render();
                    // 就地重绑最后一行：避免全量重建把高度打回估算值导致滚动条跳动
                    string text;
                    lock (_streamBuffer) text = _streamBuffer.ToString();
                    _contextTokens = _promptTokens + _liveTokens;
                    ApplyDetail();
                    _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(text));
                });
            };

            // 断流整轮重发：清掉已显示的半截正文/思考，避免重发后出现两遍
            void ResetStream()
            {
                lock (_streamBuffer) _streamBuffer.Clear();
                lock (reasoningBuffer) reasoningBuffer.Clear();
                _liveTokens = 0;
                lock (timeline) timeline.DropThinking();
                RunOnUi(() =>
                {
                    turn.ReasoningText = "";
                    lock (timeline) turn.TimelineText = timeline.Render();
                    _chat.UpdateStreamingMessage(turn, "", true);
                    RefreshChat();
                });
            }

            var loop = new AgentLoop(tools, reasoner, policy,
                onStep: step =>
                {
                    RunOnUi(() =>
                    {
                        turn.ToolStepCount++;
                        turn.ToolTotalMs += step.DurationMs;
                        string line = step.DisplayLine;
                        turn.ToolCallLines = string.IsNullOrEmpty(turn.ToolCallLines)
                            ? line
                            : turn.ToolCallLines + "\n" + line;
                        turn.ToolTraceText = turn.ToolCallLines;
                        lock (timeline) timeline.Tool(line);
                        lock (timeline) turn.TimelineText = timeline.Render();
                        turn.ToolStatusText = null;
                        RefreshChat();
                        _chat.ScrollToEnd();
                    });
                    trace.Step(step);
                },
                onToolStart: (name, _) =>
                {
                    lock (_streamBuffer) _streamBuffer.Clear();
                    RunOnUi(() =>
                    {
                        turn.AssistantText = "";
                        turn.ToolStatusText = $"正在调用 {name}…";
                        _chat.UpdateStreamingMessage(turn, "", true);
                        RefreshChat();
                    });
                },
                confirmContinue: AskOnUi,
                skills: skills);

            string systemPrompt = loop.BuildSystemPrompt(
                LoopPrompt.Role, LoopPrompt.Rules,
                settings.UseAgentsMd
                    ? AgentsInstructions.Load(_workspace, AppSettings.GlobalAgentsPath)
                    : null,
                _workspace);
            history.Insert(0, new LlmMessage("system", systemPrompt));
            trace.SystemPrompt(systemPrompt);

            _interrupt = false;
            string outcome = "error";

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, text, ct, ModelChoices.ContextLimit(model, settings),
                        () => ct.IsCancellationRequested || _interrupt, onDelta, onReasoning, ResetStream,
                        (messages, tokens) =>
                        {
                            _promptTokens = tokens;
                            _liveTokens = 0;
                            RunOnUi(() =>
                            {
                                _contextTokens = tokens;
                                ApplyDetail();
                            });
                        }),
                    ct);

                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                lock (timeline) turn.TimelineText = timeline.Render();
                turn.AssistantText = result.StoppedByUser && string.IsNullOrWhiteSpace(result.FinalAnswer)
                    ? "（已停止）"
                    : CleanAnswer(result.FinalAnswer, result.FinishReason);
                ChatStore.AttachTranscript(turn, result.Transcript);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser || result.StoppedByGuard ? "stopped"
                    : "ok";
                trace.Finish(outcome, turn.AssistantText, result.ChatCost, result.Guards);
                _lastCost = result.ChatCost?.ToString("0.0000") ?? "—";
                _contextTokens = result.ContextTokens ?? _contextTokens;
                _lblStatus.Text = $"状态: {outcome} · {result.Steps.Count} 步 · 成本 {_lastCost}";
                ApplyDetail();
            }
            catch (OperationCanceledException)
            {
                outcome = "stopped";
                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                if (string.IsNullOrEmpty(turn.AssistantText)) turn.AssistantText = "（已停止）";
                trace.Finish("stopped", turn.AssistantText);
                _lblStatus.Text = "状态: stopped";
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop 会话失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = "执行失败：" + ex.Message;
                trace.Finish("error", turn.AssistantText);
                _lblStatus.Text = "状态: error";
                Notify("Loop 执行失败", ex.Message, error: true);
            }
            finally
            {
                trace.Dispose();
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                RefreshChat();
                _chat.ScrollToEnd();
                SaveLoopChat();
                // 首轮结束后自动总结一次标题（已有标题就不再覆盖），和 Worker 一致
                if (!IsDisposed && IsHandleCreated && _book.Turns.Count == 1
                    && string.IsNullOrWhiteSpace(LoopIndex.Title(_workspace, _sessionId)))
                    _ = SummarizeTitleAsync(notify: false);
                // 排队文本：当前轮收尾后再发，避免重入
                if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(DrainQueue));
            }
        }

        private static string CleanAnswer(string text, string finishReason = null)
        {
            string raw = text ?? "";
            string clean = LlmTextSanitizer.StripSafetyLines(raw).Trim();
            if (clean.Length > 0) return clean;

            bool blocked = finishReason != null
                && (finishReason.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
                    || finishReason.Contains("refusal", StringComparison.OrdinalIgnoreCase)
                    || finishReason.Contains("blocked", StringComparison.OrdinalIgnoreCase));
            string head = blocked
                ? Localization.T("EmptyBlocked")
                : LlmTextSanitizer.IsSafetyOnly(raw)
                    ? Localization.T("EmptySafetyOnly")
                    : Localization.T("EmptyNoBody");
            if (raw.Trim().Length == 0) return head;

            string detail = raw.Trim();
            if (detail.Length > 800) detail = detail[..800] + "…";
            return head + "\n\n" + Localization.T("RawResponse") + "\n```\n" + detail + "\n```";
        }

        public void NoteSurveyAnswer(string workspace, string title, string summary)
        {
            if (!string.Equals(workspace, _workspace, StringComparison.OrdinalIgnoreCase)) return;
            void Apply()
            {
                _book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", $"[调查回答] 「{title}」：{summary}")
                });
                SaveLoopChat();
                RefreshChat();
                _chat.ScrollToEnd();
            }
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(new Action(Apply)); else Apply();
        }

        private List<LlmMessage> BuildHistory() => ChatStore.History(_book.Turns);

        private void FillProviders()
        {
            string selected = (_selProvider.SelectedValue as ProviderConfig)?.Name;
            _fillingProviders = true;
            try
            {
                _selProvider.Items.Clear();
                _selProvider.Items.Add(new AntdUI.SelectItem(Localization.T("AllProviders")) { Tag = null });
                foreach (var (name, tag) in ProviderList.All(_settings?.Invoke()))
                    _selProvider.Items.Add(new AntdUI.SelectItem(name) { Tag = tag });

                int index = 0;
                for (int i = 1; i < _selProvider.Items.Count; i++)
                {
                    if (_selProvider.Items[i] is AntdUI.SelectItem item
                        && item.Tag is ProviderConfig provider
                        && string.Equals(provider.Name, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                _selProvider.SelectedIndex = index;
            }
            finally
            {
                _fillingProviders = false;
            }

            PluginRefill.WhenReady(this, FillProviders);
        }

        private void OnProviderChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_filling || _fillingProviders) return;
            FillModels();
        }

        private void FillModels()
        {
            _filling = true;
            try
            {
                _selModel.Items.Clear();
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var models = ModelChoices.FilterByProvider(ModelChoices.Build(_settings(), _onlyFree), provider);

                foreach (var model in models)
                    _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                    {
                        Tag = model.Id,
                        IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
                        SubText = model.IsFree ? "免费" : null
                    });

                if (provider == null && !string.IsNullOrWhiteSpace(_model) && models.All(m => m.Id != _model))
                    _selModel.Items.Add(new AntdUI.SelectItem(_model) { Tag = _model });

                if (provider != null && models.All(m => m.Id != _model))
                    _model = models.FirstOrDefault()?.Id ?? _model;

                _selModel.SelectedValue = _model;
            }
            finally
            {
                _filling = false;
            }
        }

        private void SelectEffort(string effort)
        {
            _effort = effort ?? "";
            _filling = true;
            try { _selEffort.SelectedValue = _effort; }
            finally { _filling = false; }
        }

        private void OnCatalogChanged()
        {
            if (_running) return;
            RunOnUi(() =>
            {
                FillProviders();
                FillModels();
            });
        }

        private void OnModelChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            _model = _selModel.SelectedValue as string ?? _model;
            Persist(s => s.Model = _model);
            ApplyDetail();
        }

        private void OnEffortChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            _effort = _selEffort.SelectedValue as string ?? "";
            Persist(s => s.ReasoningEffort = _effort);
            ApplyDetail();
        }

        private void OnFreeToggle(object sender, AntdUI.BoolEventArgs e)
        {
            if (_settings == null || _filling) return;
            _onlyFree = e.Value;
            Persist(s => s.OnlyFreeModels = _onlyFree);
            FillModels();
        }

        private void OnToggleSide(object sender, EventArgs e)
        {
            _upperSplit.Panel2Collapsed = !_upperSplit.Panel2Collapsed;
            _btnPanel.IconSvg = UiIcons.Of(
                _upperSplit.Panel2Collapsed ? UiIcons.PanelOpen : UiIcons.PanelClose, UiStyle.Text);
            if (!_upperSplit.Panel2Collapsed) ApplyLayout();
        }

        /// <summary>工具条纵向居中：FlowLayoutPanel 不会替子控件做垂直对齐，按当前行高等分上下留白。</summary>
        private void OnHeaderRightLayout(object sender, LayoutEventArgs e)
        {
            int row = _headerRight.ClientSize.Height - _headerRight.Padding.Vertical;
            if (row <= 0) return;
            foreach (Control control in _headerRight.Controls)
            {
                int top = Math.Max(0, (row - control.Height) / 2);
                if (control.Margin.Top == top) continue;
                control.Margin = new Padding(control.Margin.Left, top, control.Margin.Right, 0);
            }
        }

        private void OnSidePageChanged(object sender, AntdUI.IntEventArgs e)
            => UpdateSideVisibility(e?.Value ?? 0);

        private void OnContextClick(object sender, EventArgs e)
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(0);
        }

        private void UpdateSideVisibility(int index)
        {
            _detailFlow.Visible = index == 0;
            _sideBtw.Visible = index == 1;
            _fileView.Visible = index >= 0 && index == _filePage;
            if (index == 0) ApplyDetail();
        }

        /// <summary>聊天区里点了本地文件路径：在右侧「文件」页打开（第一次点才把该页加出来）。</summary>
        private void OnChatLinkClicked(object sender, ChatLinkClickedEventArgs e)
        {
            if (e?.Kind != ChatTextStyler.ChatLinkKind.File) return;
            if (ChatPathActions.HandleClick(this, e.Target, _workspace)) return;

            if (!_fileView.TryShowFile(e.Target, _workspace, out string error))
            {
                Notify("无法预览", error, error: true);
                return;
            }
            if (_filePage < 0)
            {
                _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "文件" });
                _filePage = _sideTabs.Items.Count - 1;
            }
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(_filePage);
        }

        private void ApplyDetail()
        {
            string effort = _effort switch
            {
                "low" => Localization.T("EffortLow"),
                "medium" => Localization.T("EffortMedium"),
                "high" => Localization.T("EffortHigh"),
                _ => Localization.T("EffortAuto")
            };
            string status = _lblStatus.Text;
            string prefix = Localization.Translate("状态") + ": ";
            if (status.StartsWith(prefix, StringComparison.Ordinal)) status = status[prefix.Length..];
            if (string.IsNullOrWhiteSpace(status)) status = "—";

            int contextLimit = ModelChoices.ContextLimit(_model, _settings());
            float ratio = _contextTokens.HasValue && contextLimit > 0
                ? Math.Clamp(_contextTokens.Value / (float)contextLimit, 0F, 1F)
                : 0F;
            _ctx.Value = ratio;
            _ctx.Text = _contextTokens.HasValue ? $"{ratio * 100F:0}%" : "—";

            _lblDModel.Text = Localization.T("Model") + ": " + (string.IsNullOrWhiteSpace(_model) ? "—" : _model);
            _lblDEffort.Text = Localization.T("Effort") + ": " + effort;
            _lblDCtx.Text = Localization.T("Context") + ": "
                + (_contextTokens.HasValue ? _contextTokens.Value.ToString("N0") : "—")
                + " / " + contextLimit.ToString("N0");
            _lblDCost.Text = Localization.T("Cost") + ": " + _lastCost;
            _lblDStatus.Text = Localization.T("Status") + ": " + status;
        }

        private void RunSideWork(string request)
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(1);
            _sideBtw.SetModel(_model);
            _sideBtw.SetEffort(_effort);
            _sideBtw.Run(request, BuildHistory());
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void ApplyLayout()
        {
            if (IsDisposed || _upperSplit.Width < Dip(700)) return;
            if (_upperSplit.Panel2Collapsed) return;
            SessionView.ApplySplit(_upperSplit, vertical: true,
                panel1Min: Dip(400), panel2Min: Dip(280), targetPanel2: Dip(320));
            _layoutReady = true;
        }

        private void Submit(bool immediate = false)
        {
            string text = _txtInput.Text.Trim();

            // 正在编辑排队项：这一下只保存，不发送
            if (_editing != null)
            {
                if (text.Length == 0) _queue.Remove(_editing);
                else _editing.Text = text;
                _editing = null;
                _txtInput.Clear();
                _pending.Sync();
                UpdateSendButton();
                return;
            }

            //「/命令」先在这里分流：命令不走普通对话
            if (TryRunCommand(text))
            {
                _txtInput.Clear();
                return;
            }
            //命令写了但没写请求：只提示，不要把「/btw」本身当任务发出去
            if (SessionView.IsCommand(text, "/btw") || SessionView.IsCommand(text, "/fork")) return;

            if (_running)
            {
                if (text.Length == 0)
                {
                    Stop();
                    return;
                }
                _queue.Add(text, "task",
                    immediate ? OpenLoop.Service.Pending.PendingMode.NextStep : OpenLoop.Service.Pending.PendingMode.AfterTurn);
                _txtInput.Clear();
                _pending.Sync();
                if (immediate) _interrupt = true;
                return;
            }

            if (text.Length == 0) return;
            SendAsync(null);
        }

        /// <summary>识别 /btw、/fork；返回 true 表示这条输入已被命令消费。</summary>
        private bool TryRunCommand(string text)
        {
            if (SessionView.IsCommand(text, "/btw"))
            {
                string request = text[4..].Trim();
                if (request.Length == 0)
                {
                    Notify("缺少请求", "/btw 后面要写具体请求，例如：/btw 顺便看看还有哪些调用点。", error: true);
                    return false;
                }
                RunSideWork(request);
                return true;
            }

            if (SessionView.IsCommand(text, "/fork"))
            {
                string request = text[5..].Trim();
                if (request.Length == 0)
                {
                    Notify("缺少请求", "/fork 后面要写具体请求，例如：/fork 继续把第二项改完。", error: true);
                    return false;
                }
                if (_running)
                {
                    Notify("正在运行", "请先停止当前任务，再分叉对话。", error: true);
                    return true;
                }
                ForkRequested?.Invoke(this, request);
                return true;
            }

            if (SessionView.IsCommand(text, "/new"))
            {
                NewSessionRequested?.Invoke(this, text[4..].Trim());
                return true;
            }

            return false;
        }

        private static readonly (string Command, string Description)[] Commands =
        {
            ("/btw", "在右侧栏用当前上下文跑一个只读旁路任务"),
            ("/fork", "复制当前上下文开一个新的 Loop 会话（可继续对话）并跳转"),
            ("/new", "开一个新的 Loop 会话，沿用当前的模型与强度")
        };

        private readonly List<(string Command, string Description)> _cmdMatches = [];

        /// <summary>输入以 / 开头且还没写参数时，弹出可用命令的联想列表。</summary>
        private void UpdateCommandPopup()
        {
            string text = _txtInput.Text ?? "";
            bool want = text.StartsWith('/') && !text.Contains(' ') && !text.Contains('\n');
            _cmdMatches.Clear();
            if (want)
                foreach (var command in Commands)
                    if (command.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _cmdMatches.Add(command);

            if (_cmdMatches.Count == 0)
            {
                _cmdPopup.Close();
                _inputArea.RowStyles[0].Height = 0F;
                return;
            }

            _cmdPopup.Open(_cmdMatches);
            _inputArea.RowStyles[0].Height = _cmdPopup.PreferredHeight;
        }

        /// <summary>选中命令：补进输入框，光标停在参数位。</summary>
        private void OnCommandAccepted(string command)
        {
            _txtInput.Text = command + " ";
            _txtInput.SelectionStart = _txtInput.Text.Length;
            _txtInput.Focus();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (!_cmdPopup.IsOpen) return;
            if (e.KeyCode == Keys.Down) { _cmdPopup.MoveSelection(1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Up) { _cmdPopup.MoveSelection(-1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Escape) { _cmdPopup.Close(); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Tab) { _cmdPopup.Accept(); e.SuppressKeyPress = true; }
        }

        private void OnSendClick(object sender, EventArgs e) => Submit(immediate: true);

        private void OnChatCopy(object sender, ChatFlowActionEventArgs e)
        {
            var turn = e.Turn;
            if (turn == null) return;
            string text = $"用户：\n{turn.UserText}\n\nAI：\n{turn.AssistantText}".Trim();
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); } catch { }
        }

        private void OnChatUndo(object sender, ChatFlowActionEventArgs e)
        {
            var selected = e.Turn;
            if (selected == null) return;
            if (_running)
            {
                Notify("正在运行", "请先停止当前任务，再撤回对话。", error: true);
                return;
            }

            var turns = _book.Turns;
            int index = -1;
            for (int i = 0; i < turns.Count; i++)
                if (ReferenceEquals(turns[i], selected)) { index = i; break; }
            if (index < 0) return;

            int count = turns.Count - index;
            if (MessageBox.Show(this,
                    $"将撤回从所选对话到最新一条的全部 {count} 组对话，并把源码工作区还原到该轮开始前"
                    + "（含新建的未跟踪文件，.gitignore 的除外；工作区外的写入不还原）。确定？",
                    "撤回对话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            bool hasSnapshot = _turnSnapshots.TryGetValue(selected, out var snapshot);
            bool restoreFailed = hasSnapshot && !Snapshots.Restore(snapshot.Root, snapshot.Checkpoint);

            var remove = new HashSet<ChatTurn>(turns.Skip(index));
            _book.RemoveTurns(remove);
            foreach (var turn in remove) _turnSnapshots.Remove(turn);
            SaveLoopChat();
            RefreshChat();
            _chat.EnsureLastTurnVisible(true);

            string undone = selected.UserText?.Trim() ?? "";
            if (undone.Length > 0)
            {
                string draft = _txtInput.Text.Trim();
                _txtInput.Text = draft.Length == 0 ? undone : draft + "\n\n" + undone;
                _txtInput.SelectionStart = _txtInput.Text.Length;
                _txtInput.Focus();
            }

            string note = restoreFailed ? "\n还原失败"
                : hasSnapshot ? "，工作区已还原（.gitignore 除外）"
                : "（该轮没有可用快照，未还原文件）";
            Notify("撤回完成", $"已撤回 {remove.Count} 组对话" + note, restoreFailed);
        }

        /// <summary>壳注入的迭代动作：编译 + 发布 + 登记 A/I。Loop 的工具里没有这个入口。</summary>
        public Func<Task<string>> Iterate { get; set; }

        /// <summary>壳注入：把选中的对话作为初始上下文开一个 Worker 会话。</summary>
        public Func<IReadOnlyList<ChatTurn>, bool> TransferToWorker { get; set; }

        private void OnSelectModeChanged(object sender, AntdUI.BoolEventArgs e)
        {
            _chat.ShowSelection = e.Value;
            if (!e.Value) _chat.SelectTurns(_ => false);
        }

        private void OnSelectAction(object sender, AntdUI.ObjectNEventArgs e)
        {
            string key = _selMenu.SelectedValue as string;
            _selMenu.SelectedValue = null;
            RunSelectionAction(key);
        }

        /// <summary>工具栏下拉与右键「操作」子菜单的公共入口。</summary>
        private void RunSelectionAction(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            var picked = _book.Turns.Where(t => t.Selected).ToList();
            if (picked.Count == 0)
            {
                Notify("未选择对话", "先打开「选择」并勾选要处理的对话。", error: true);
                return;
            }

            switch (key)
            {
                case "copy": CopyTurns(picked); break;
                case "worker": TransferTurns(picked); break;
                case "delete": DeleteTurns(picked); break;
            }
        }

        private void CopyTurns(IReadOnlyList<ChatTurn> turns)
        {
            string text = string.Join("\n\n", turns.Select(t =>
            {
                string user = t.UserText?.Trim() ?? "";
                string ai = t.AssistantText?.Trim() ?? "";
                return (user.Length == 0 ? "" : "用户：\n" + user)
                       + (ai.Length == 0 ? "" : (user.Length == 0 ? "" : "\n\n") + "AI：\n" + ai);
            }).Where(block => block.Length > 0));
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); } catch { }
            Notify("已复制", $"复制了 {turns.Count} 组对话。");
        }

        private void TransferTurns(IReadOnlyList<ChatTurn> turns)
        {
            if (TransferToWorker == null || !TransferToWorker(turns))
            {
                Notify("无法转交", "宿主没有提供 Worker 入口。", error: true);
                return;
            }
            _chat.SelectTurns(_ => false);
        }

        /// <summary>真删历史条目（不是撤回）：删完这一轮之后的 KV cache 前缀会断开，后续要重算。</summary>
        private void DeleteTurns(IReadOnlyList<ChatTurn> turns)
        {
            if (_running)
            {
                Notify("正在运行", "请先停止当前任务，再删除对话。", error: true);
                return;
            }
            if (MessageBox.Show(this,
                    $"删除选中的 {turns.Count} 组对话？\n"
                    + "会真的从历史里删掉，不能恢复；之后的 KV cache 前缀也会从这里断开、后面要重算。",
                    "删除对话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var remove = new HashSet<ChatTurn>(turns);
            _book.RemoveTurns(remove);
            foreach (var turn in remove) _turnSnapshots.Remove(turn);
            _chat.SelectTurns(_ => false);
            SaveLoopChat();
            RefreshChat();
        }

        internal string IterateEntry => _btnIterate.Text;

        private async void OnIterateClick(object sender, EventArgs e)
        {
            if (_iterating || Iterate == null) return;
            _iterating = true;
            _btnIterate.Enabled = false;
            _lblStatus.Text = "状态: 迭代中…";
            try
            {
                string summary = await Iterate();
                _lblStatus.Text = "状态: " + summary;
                Notify("迭代", summary);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "状态: 迭代失败";
                Notify("迭代失败", ex.Message, error: true);
            }
            finally
            {
                _iterating = false;
                _btnIterate.Enabled = true;
            }
        }

        /// <summary>
        /// Enter 在 AntdUI 的 WM_KEYDOWN 里先被消化成换行（早于 KeyDown 事件），
        /// 所以只能在这里拦：Enter 发送（运行中=步后插入），Ctrl+Enter 等本轮排队，Shift+Enter 换行。
        /// </summary>
        private void InsertSoftNewline()
        {
            if (_txtInput == null || !_txtInput.IsHandleCreated) return;
            var selStart = _txtInput.SelectionStart;
            var selLen = _txtInput.SelectionLength;
            var text = _txtInput.Text;
            _txtInput.Text = text.Substring(0, selStart) + Environment.NewLine + text.Substring(selStart + selLen);
            _txtInput.SelectionStart = selStart + Environment.NewLine.Length;
            _txtInput.SelectionLength = 0;
            _txtInput.Focus();
            UpdateSendButton();
        }

        private void OnComposerKeyboard(object sender, AntdUI.InputVerifyKeyboardEventArgs e)
        {
            bool shift = e.KeyData == (Keys.Shift | Keys.Enter);
            bool ctrl = e.KeyData == (Keys.Control | Keys.Enter);
            if (!shift && e.KeyData != Keys.Enter && !ctrl) return;
            e.Result = false;
            if (shift) { InsertSoftNewline(); return; }
            if (!ctrl && _cmdPopup.IsOpen) { _cmdPopup.Accept(); return; }
            if (_editing == null && _txtInput.Text.Trim().Length == 0) return;

            e.Result = false;
            if (_editing == null && _txtInput.Text.Trim().Length == 0) return;
            Submit(immediate: !ctrl);
        }

        private IReadOnlyList<SkillInfo> ActiveSkills(
            IReadOnlyList<SkillInfo> catalog, out IReadOnlyList<string> problems)
        {
            problems = [];
            try
            {
                var payload = Loop.GetActivation(_workspace);
                if (payload?.Activation == null) return [];
                return SkillCatalog.Active(payload.Activation, payload.Features, catalog, out problems);
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取技能激活集失败：" + ex.Message);
                return [];
            }
        }

        private void Persist(Action<AppSettings> change)
        {
            try
            {
                var settings = _settings();
                change(settings);
                settings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Warn("保存设置失败：" + ex.Message);
            }
        }

        private void SetRunning(bool running)
        {
            _running = running;
            UpdateSendButton();
        }

        /// <summary>运行中且输入为空 = 停止；有内容 = 排队发送；编辑排队项时 = 保存。</summary>
        private void UpdateSendButton()
        {
            bool editing = _editing != null;
            bool stop = !editing && _running && _txtInput.Text.Trim().Length == 0;
            _btnSend.Text = editing
                ? Localization.T("SettingsSave")
                : Localization.T(stop ? "Stop" : "Send");
            _btnSend.IconSvg = UiIcons.Of(stop ? UiIcons.Stop : UiIcons.Send,
                stop ? UiStyle.Error : Color.White);
            _btnSend.Type = stop ? AntdUI.TTypeMini.Error : AntdUI.TTypeMini.Primary;
            _btnSend.Ghost = stop;
        }

        /// <summary>「立即」：先让当前轮在下一个 step 前收尾，收尾后 DrainQueue 会先发这一条。</summary>
        private void OnImmediateRequested(OpenLoop.Service.Pending.PendingItem item)
        {
            if (item == null || !item.Immediate) return;
            _pending.Sync();
            if (_running) _interrupt = true;
            else DrainQueue();
        }

        /// <summary>队列里的条目被删掉时退出编辑态，别让「保存」留在按钮上。</summary>
        private void DropEdit(OpenLoop.Service.Pending.PendingItem item)
        {
            if (!ReferenceEquals(_editing, item)) return;
            _editing = null;
            UpdateSendButton();
        }

        /// <summary>把排队文本放回输入框编辑，发送按钮变成「保存」。</summary>
        private void BeginEdit(OpenLoop.Service.Pending.PendingItem item)
        {
            if (item == null || !_queue.Items.Contains(item)) return;
            _editing = item;
            _txtInput.Text = item.Text;
            _txtInput.SelectionStart = _txtInput.Text.Length;
            _txtInput.Focus();
            UpdateSendButton();
        }

        /// <summary>
        /// 整表重建是一次全量重排（可见行重绑 + 高度校正）。流式期间事件比重建快，
        /// 每来一次就排一次会让 UI 线程永远追不上，所以合并成「最多一个待办」：
        /// 排队期间再来直接丢弃，执行时读的是最新状态，最终画面不会缺。
        /// </summary>
        private void RefreshChat()
        {
            if (_chatRefreshQueued || !IsHandleCreated) return;
            _chatRefreshQueued = true;
            BeginInvoke(new Action(() =>
            {
                _chatRefreshQueued = false;
                if (!IsDisposed) _book.RaiseTextChanged();
            }));
        }

        private bool _chatRefreshQueued;

        private void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private bool AskOnUi(string message)
        {
            if (InvokeRequired)
                return (bool)Invoke(new Func<bool>(() => AskOnUi(message)));
            return MessageBox.Show(FindForm(), message, "继续执行？", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                == DialogResult.Yes;
        }

        /// <summary>分级审批：越界访问与风险操作才问用户，可「后续都许可」。</summary>
        private OpenLoop.Core.Safety.ApprovalDecision AskApproval(OpenLoop.Core.Safety.ApprovalRequest request)
            => ApprovalGate.Ask(this, _settings, request);

        private void Notify(string title, string text, bool error = false)
        {
            var form = FindForm();
            if (form == null) return;
            title = Localization.Translate(title);
            text = Localization.Translate(text);
            if (error)
                AntdUI.Notification.error(form, title, text, AntdUI.TAlignFrom.TR, null, 5);
            else
                AntdUI.Notification.info(form, title, text, AntdUI.TAlignFrom.TR, null, 3);
        }
    }
}
