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
using OpenLoop.Service.Media;
using OpenLoop.Service.Pending;
using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 的会话视图：与 Worker 页同一套聊天界面，但提示词是「OpenLoop 自己的 Worker」角色
    /// （源码开发与迭代编排），历史单独存（ChatStore kind="loop"）。
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
        private string _imageModel = "";
        private readonly List<string> _attachments = [];
        private bool _onlyFree = true;
        // 生图模型下拉默认收起，点图标按钮才展开（省工具栏宽度）
        private bool _imageOpen;
        private string _lastCost = "—";
        private int? _contextTokens;
        // 上下文占比的实时估算：本轮请求的 prompt + 已生成内容（按增量累加）
        private int _promptTokens, _liveTokens;
        private bool _running;
        private bool _filling;
        private bool _fillingProviders;
        private bool _iterating;
        private bool _repoBusy;
        private bool _hasChanges;
        private bool _hasRollback;
        private bool _layoutReady;

        /// <summary>「文件」页在侧栏标签里的下标；第一次点文件路径时才加出来（-1 = 还没有）。</summary>
        private int _filePage = -1;

        /// <summary>运行中提交的文本排队（含 /complain 投递）：Enter 走「立即」插到下一步，Ctrl+Enter 等本轮返回。</summary>
        private readonly OpenLoop.Service.Pending.PendingQueue _queue = new();
        private OpenLoop.Service.Pending.PendingItem _editing;

        /// <summary>「立即」标记：当前轮在下一个 step 前收尾（进行中的工具不打断）。</summary>
        private bool _interrupt;

        /// <summary>用户动作计数（发消息、停、换模型、关页都会加）：免费额度自愈据此判断用户动没动过这个会话。</summary>
        private int _userEpoch;
        private bool _closed;

        /// <summary>账本归 I 所有：技能激活集从这里读，本进程不写账本。</summary>
        private OpenLoop.Service.Web.LoopApiClient _loopClient;
        private OpenLoop.Service.Web.LoopApiClient Loop => _loopClient ??= new OpenLoop.Service.Web.LoopApiClient();

        public LoopView()
        {
            InitializeComponent();
            _txtInput.VerifyKeyboard += OnComposerKeyboard;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running || _sideBtw.Busy;

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

        private const int BrowserPage = 2;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BrowserView Browser => _browser;

        /// <summary>展开右侧栏并切到「网页」页（Agent 用内嵌浏览器时的入口）；页被关掉时重新建。</summary>
        public void ShowBrowser()
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            if (_browser == null)
            {
                _browser = new BrowserView { Dock = DockStyle.Fill, Visible = false };
                AttachLoopApp(_browser);
                _sideHost.Controls.Add(_browser);
                _sideTabs.Items.Insert(BrowserPage, new AntdUI.SegmentedItem { Text = "网页" });
                if (_filePage >= BrowserPage) _filePage++;
            }
            ShowSidePage(BrowserPage);
        }

        /// <summary>关掉「网页」页：销毁 WebView2 实例释放内存，再用时由 ShowBrowser 重建。</summary>
        public void CloseBrowserPage()
        {
            if (_browser == null) return;
            int selected = _sideTabs.SelectIndex;
            _sideTabs.Items.RemoveAt(BrowserPage);
            if (_filePage > BrowserPage) _filePage--;
            var browser = _browser;
            _browser = null;
            _sideHost.Controls.Remove(browser);
            browser.Dispose();
            // 关掉的不是当前页就停在原页（下标可能前移一位）
            ShowSidePage(selected == BrowserPage ? 0 : Math.Max(0, selected > BrowserPage ? selected - 1 : selected));
        }

        private void OnSideTabsMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _browser == null) return;
            var items = new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem("关闭网页") { ID = "closebrowser" }
            };
            AntdUI.ContextMenuStrip.open(_sideTabs, item =>
            {
                if (item?.ID == "closebrowser") CloseBrowserPage();
            }, items);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ComposerText
        {
            get => _txtInput.Text;
            set => _txtInput.Text = value;
        }

        /// <summary>会话语境（由宿主注入）：调查卡插到哪个会话、锚定第几轮。</summary>
        public Func<(string Id, int Turn)> SessionTarget { get; set; }

        /// <summary>壳内存里打开的会话（由宿主注入）：ReadSession 工具的数据源。</summary>
        internal Func<IReadOnlyList<Session>> Sessions { get; set; }

        /// <summary>
        /// ReadSession：只放开读范围内的会话（章程 §2 的读边界照样生效），
        /// 别的项目的会话只给标题，不给内容。
        /// 协作打开时也覆盖同工作区的其他 Loop 会话（协作会话就是它们），与 Worker 的读法对齐。
        /// </summary>
        private string ReadSessions(string title, int offset, int count, WorkspaceScope read)
        {
            var sessions = Sessions?.Invoke() ?? [];
            var peers = _settings?.Invoke()?.EnableCollab == true
                ? (CollabPeers?.Invoke() ?? []).Where(v => v != null && !ReferenceEquals(v, this)
                    && Paths.Same(v.Workspace, _workspace)).ToList()
                : [];

            if (string.IsNullOrWhiteSpace(title))
                return SessionMemoryTool.Listing(sessions)
                       + (peers.Count > 0 ? "\n\n" + LoopCollabHost.Listing(peers) : "");

            var session = sessions.FirstOrDefault(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault(s => s.Id.StartsWith(title, StringComparison.OrdinalIgnoreCase));
            if (session != null)
            {
                if (!read.TryResolve(session.Workspace, out _, out _))
                    return $"会话「{session.Title}」的工作区不在读范围，只给标题：{session.Workspace}";
                return SessionMemoryTool.Describe(session, offset, count);
            }

            var peer = peers.FirstOrDefault(v => string.Equals(v.SessionId, title, StringComparison.OrdinalIgnoreCase))
                       ?? peers.FirstOrDefault(v => v.SessionId.StartsWith(title, StringComparison.OrdinalIgnoreCase));
            if (peer == null)
                return $"没有找到会话：{title}\n" + SessionMemoryTool.Listing(sessions);
            return LoopCollabHost.Describe(peer, offset, count);
        }

        /// <summary>/fork：把当前 Loop 对话复制进一个新 Loop 会话，并把新请求发出去。</summary>
        internal event Action<LoopView, string> ForkRequested;

        /// <summary>/new：开一个新的空 Loop 会话（沿用当前模型与强度）。</summary>
        internal event Action<LoopView, string> NewSessionRequested;

        /// <summary>/complain：把对 Loop 表现的不满交给壳新起一个 Loop 会话处理（与 Worker 同一条路径）。</summary>
        internal event Action<string> ComplaintRequested;

        /// <summary>标题（重新总结后）变了：宿主用它刷新标签页文本。</summary>
        internal event Action<string> TitleChanged;

        /// <summary>运行状态变了：宿主用它刷新标签页图标的颜色。</summary>
        internal event Action<LoopView> StateChanged;

        /// <summary>当前轮状态（Running=运行中，Done=上一轮正常收尾，Interrupted=停止/失败）。</summary>
        internal SessionState State { get; private set; }

        internal IReadOnlyList<ChatTurn> Turns => _book.Turns;

        /// <summary>
        /// 后台线程（协作监测）读轮次用：切回界面线程拍一份快照，避免与界面的增删撞车。
        /// 视图已销毁或句柄还没建时返回空表，调用方按「没有轮次」处理即可。
        /// </summary>
        internal IReadOnlyList<ChatTurn> TurnSnapshot()
        {
            if (IsDisposed) return [];
            if (!InvokeRequired) return _book.Turns.ToArray();
            try
            {
                return (IReadOnlyList<ChatTurn>)Invoke(
                    new Func<IReadOnlyList<ChatTurn>>(() => _book.Turns.ToArray()));
            }
            catch (Exception)
            {
                return [];
            }
        }

        internal void Initialize(string workspace, Func<AppSettings> settings,
            string sessionId = LoopRef.LegacyId)
        {
            _workspace = workspace ?? "";
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? LoopRef.LegacyId : sessionId;
            _settings = settings;

            var current = settings();
            // 会话按它自己上次用的模型/强度恢复（与 Worker 一致）：分叉、转交过来的会话沿用原参数，
            // 关掉标签页再打开也不会退回全局设置。没有记录时才用全局设置。
            var (savedModel, savedEffort) = ChatHistory.LastModel(_workspace, _sessionId);
            _model = !string.IsNullOrWhiteSpace(savedModel)
                ? savedModel
                : string.IsNullOrWhiteSpace(current.Model) ? "openrouter/free" : current.Model;
            _effort = savedEffort ?? current.ReasoningEffort ?? "";
            _onlyFree = current.OnlyFreeModels;

            _filling = true;
            try { _freeSwitch.Checked = _onlyFree; }
            finally { _filling = false; }

            FillProviders();
            FillModels();
            SelectEffort(_effort);
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort);
            _tips.SetToolTip(_btnImage, "生图模型");
            _tips.SetToolTip(_btnAttach, "添加附件（也可拖放或粘贴文件/图片）");
            AttachmentsUi.EnableDrop(_inputArea, AddDroppedFiles);
            _tips.SetToolTip(_btnIterate, "先出计划（谁升版、改了什么），确认后编译并换 A/I，最后重启壳（界面会短暂退出）");
            _tips.SetToolTip(_btnCommit, "提交托管仓库里的改动（工作树干净时不可用）");
            _tips.SetToolTip(_btnPush, "提交并推送到远端（工作树干净时不可用）");
            _tips.SetToolTip(_btnRollback, "把 A/I 退回上一版本，壳保持当前版本（没有上一版本时不可用）");
            SyncRepoButtons();
            _pending.Bind(_queue);
            _pending.EditRequested += BeginEdit;
            _pending.ImmediateRequested += OnImmediateRequested;
            _pending.Removed += item =>
            {
                DropEdit(item);
                // 撤回最后一条「立即」也要收回打断，否则这一轮仍会在 step 前收尾
                SyncInterrupt();
            };
            // 队列跟着会话落盘：进程被强杀也不丢已经排好的消息
            var savedQueue = PendingStore.Load(_workspace, _sessionId);
            _queue.Restore(savedQueue.Items, savedQueue.Paused);
            _queue.Changed += () => PendingStore.Save(_workspace, _sessionId, _queue);
            _pending.Sync();
            _txtInput.TextChanged += (_, _) => { UpdateSendButton(); UpdateCommandPopup(); };

            _book.ReplaceAll(ChatStore.Load(_workspace, _sessionId), null);
            ChatStore.PruneCold(_workspace, _sessionId, _model, _book.Turns);
            if (_book.Turns.Count > 0)
                LoopIndex.Touch(_workspace, _sessionId, _book.Turns.LastOrDefault()?.ToolStepCount);
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
            _tips.SetToolTip(_btnSummary, "用当前模型重新总结会话标题");
            string savedTitle = LoopIndex.Title(_workspace, _sessionId);
            // 标题格式与 Worker 对齐（工作区文件夹名 · 内容简述），Loop 身份靠页头图标与颜色
            _lblTitle.Text = Session.ComposeTitle(_workspace, savedTitle);
            _chat.ScrollToEnd();

            _lblStatus.TextChanged += (_, _) => ApplyDetail();
            _sideBtw.Initialize(_workspace, settings);
            ApplyDetail();
            UpdateSendButton();
            _ = RefreshRepoActionsAsync();

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
                _lblTitle.Text = Session.ComposeTitle(_workspace, title);
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
            _userEpoch++;
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

        /// <summary>
        /// 宿主注入会话参数（/complain、/fork、转交、协作带过来的模型与强度）：只改本会话，
        /// 不写全局设置——沿用别人的参数不该顺手把默认模型改掉。
        /// </summary>
        internal void AdoptParameters(string model, string effort)
        {
            _filling = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(model)) SetModel(model);
                if (effort != null) SetEffort(effort);
            }
            finally
            {
                _filling = false;
            }
            ApplyDetail();
        }

        /// <summary>发到 Loop：空闲直接开跑；运行中 immediate=插到下一步（软打断本轮），否则等本轮返回；只取输入框内容时 text 传 null。</summary>
        public string Send(string text = null, bool immediate = true)
        {
            if (!_running)
            {
                SendAsync(text);
                return "已发送到 Loop";
            }

            bool fromInput = text == null;
            string body = (fromInput ? _txtInput.Text : text).Trim();
            if (body.Length == 0) return "输入框为空，没有发送";
            var item = _queue.Add(body, "task", immediate ? PendingMode.NextStep : PendingMode.AfterTurn);
            _queue.Paused = false;
            if (fromInput) _txtInput.Clear();
            _pending.Sync();
            if (item != null && item.Immediate) OnImmediateRequested(item);
            return immediate ? "已插队，本轮收尾后立刻发送" : "已入队，本轮结束后发送";
        }

        /// <summary>
        /// 外部投递（/complain、协作会话派活）：空闲就立即处理，忙就排队等当前轮结束后按顺序处理。
        /// 可能在 agent 的工具线程上调用，这里负责切回界面线程。
        /// </summary>
        internal void Post(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => Post(text))); }
                catch (InvalidOperationException) { }
                return;
            }
            _queue.Add(text);
            _pending.Sync();
            DrainQueue();
        }

        internal bool Running => _running;
        internal string Title => LoopIndex.Title(_workspace, _sessionId);

        /// <summary>协作会话的打开方式（宿主注入）：同工作区新起一个 Loop 会话并返回视图。</summary>
        internal Func<CollabTask, LoopView> CollabOpen { get; set; }

        /// <summary>同工作区的其他 Loop 会话（宿主注入）：协作监测与读取用。</summary>
        internal Func<IReadOnlyList<LoopView>> CollabPeers { get; set; }

        private LoopCollabHost _collab;

        private LoopCollabHost Collab() => _collab ??= new LoopCollabHost(this, CollabOpen, CollabPeers);

        /// <summary>
        /// agent 自动插入一条消息（协作监测汇报）：与 Worker 侧 EnqueueAuto 同语义——
        /// 进本会话队列并标 Auto，队列栏用特殊底色区分；已停止（暂停）时只排队，不自己动起来。
        /// 监测循环在线程池线程上跑，这里负责切回界面线程。
        /// </summary>
        internal void EnqueueAuto(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => EnqueueAuto(text))); }
                catch (InvalidOperationException) { }
                return;
            }

            _queue.Add(text, "collab", PendingMode.AfterTurn, auto: true);
            _pending.Sync();
            DrainQueue();
        }

        /// <summary>当前轮结束后调用：优先「立即」，其次按入队顺序。</summary>
        private void DrainQueue()
        {
            if (IsDisposed || _running) return;

            var batch = _queue.TakeBatch();
            if (batch.Count == 0) return;
            _pending.Sync();
            string merged = batch.Count == 1
                ? batch[0].Text
                : string.Join("\n\n", batch.Select((b, i) => $"{i + 1}. {b.Text}"));
            var files = batch.SelectMany(b => b.Attachments ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SendAsync(merged, files.Count > 0 ? files : null);
        }

        /// <summary>停止当前轮，并暂停自动续跑（队列保留），与 Worker 一致。</summary>
        public void Stop()
        {
            _userEpoch++;
            _queue.Paused = true;
            _cts?.Cancel();
        }

        public void Shutdown()
        {
            _userEpoch++;
            _closed = true;
            _cts?.Cancel();
            _collab?.Stop();
            OpenLoop.Service.Web.SessionBoard.Leaving(BoardId(), LastWords(), remote: true);
            _sideBtw.Shutdown();
            CloseBrowserPage();
            SaveLoopChat();
        }

        /// <summary>留言板身份与 Worker 一致（A-会话id / I-会话id）：去掉 "loop-" 前缀；老的单例会话 id 是固定的 "loop"，用工作区散列区分。</summary>
        private string BoardId()
        {
            string id = _sessionId;
            if (id?.StartsWith("loop-", StringComparison.OrdinalIgnoreCase) == true)
                id = id["loop-".Length..];
            if (string.Equals(id, LoopRef.LegacyId, StringComparison.Ordinal))
                id += "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes((_workspace ?? "").ToLowerInvariant())))[..6].ToLowerInvariant();
            return AgentIdentity.IdFor("I", id);
        }

        private string LastWords()
        {
            var last = _book.Turns.LastOrDefault();
            if (last?.Streaming == true) return "（Loop 会话被关闭，任务未完成）";
            return string.IsNullOrWhiteSpace(last?.AssistantText) ? "（Loop 会话结束）" : last.AssistantText;
        }

        /// <summary>免费额度自愈读的用户动作计数；这个 Loop 页已关/已销毁返回 null（自愈据此收工）。</summary>
        private int? UserEpochOf() => _closed || IsDisposed ? null : _userEpoch;

        /// <summary>对话正文按「工作区|会话 id」存；顺带把项目记进 Loop 索引，供 [Loop] 页列出。</summary>
        private void SaveLoopChat()
        {
            // 一句都没聊过：关窗不该把空会话留在列表里
            if (_book.Turns.Count == 0)
            {
                ChatStore.Delete(_workspace, _sessionId);
                LoopIndex.Remove(_workspace, _sessionId);
                return;
            }
            ChatStore.Save(_workspace, _book.Turns, _sessionId, _model, _effort);
            LoopIndex.Touch(_workspace, _sessionId, _book.Turns.LastOrDefault()?.ToolStepCount);
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
            if (_cmdPopup.IsOpen) _cmdPopup.PlaceAbove(_inputArea);
        }

        private async void SendAsync(string text = null, IReadOnlyList<string> files = null)
        {
            if (_running) return;
            _userEpoch++;

            var settings = _settings();
            bool fromInput = text == null;
            text = (fromInput ? _txtInput.Text : text).Trim();
            bool fromComposer = files == null;
            var pendingFiles = (files ?? _attachments).ToList();
            if (text.Length == 0 && pendingFiles.Count == 0) return;
            if (!Directory.Exists(_workspace))
            {
                Notify("项目不可用", "工作区目录不存在：" + _workspace, error: true);
                return;
            }

            // 附件在本机直接复制进工作区：不走 base64，几十 MB 的录屏与 APK 也能发
            var saved = Attachments.SaveFiles(_workspace, _sessionId, pendingFiles,
                MediaModels.AttachmentLimit(_model), out string attachmentError);
            if (saved == null)
            {
                Notify("附件不可用", attachmentError, error: true);
                return;
            }
            if (saved.Count > 0)
            {
                string block = Attachments.Describe(saved);
                text = text.Length == 0 ? block : text + "\n\n" + block;
                if (fromComposer)
                {
                    _attachments.Clear();
                    UpdateAttachments();
                }
            }

            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "请先在设置里填写接口地址、模型。", error: true);
                return;
            }

            // 本会话在留言板上的身份（I-会话id）：提示词、技能与 Puppet 工具都靠它识别自己。
            using var identity = AgentIdentity.Begin(BoardId(), "a");
            OpenLoop.Service.Web.SessionBoard.Opening(
                agentId: BoardId(), role: "Loop",
                workspace: _workspace, model: model, task: text, remote: true);

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
            _queue.Paused = false;   // 用户又发了一条：停止留下的暂停到此为止，本轮收尾后队列照常续发
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
                    var peers = OpenLoop.Service.Web.SessionBoard.Peers(_workspace, BoardId(), remote: true);
                    var activeSkills = BuiltinSkills.Merge(ActiveSkills(skillCatalog, out var problems),
                        peers.Select(p => p.Summary()).ToList());
                    OpenLoop.Core.Safety.ApprovalHandler approve = AskApproval;
                    // 章程 §2：Loop 只读来源会话的工作区（/complain、worker 转来）与安装/配置路径，
                    // 写只限配置/源码/安装路径
                    var (readScope, writeScope) = OpenLoop.Service.Source.LoopScope.Create(
                        approve, sessionWorkspace: _workspace);
                    var loopTools = ToolRegistry.Loop(readScope, writeScope, _workspace, approve, activeSkills,
                        OpenCodeTools.For(writeScope.Root, approve, settings.OpenCodeAskPermissions)
                            .Concat(DshTools.For(settings, writeScope.Root, approve))
                            .Concat(OpenLoop.Service.Web.PuppetTools.For())
                            .Concat(ImageTools.For(_workspace, settings, _imageModel, saved))
                            // 内存会话优先：文件里的对话记录可能几十 MB，按轮读内存不受上限影响
                            .Concat([new SessionMemoryTool((title, offset, count) =>
                                ReadSessions(title, offset, count, readScope))])
                            // 协作：与 Worker 同一套工具，协作会话是同工作区的其他 Loop 会话。
                            // Loop 自己已有 ReadSession（已扩到能读这些会话），不再重复注册同名工具。
                            .Concat(CollabTools.For(settings.EnableCollab ? Collab() : null, _sessionId,
                                includeReadSession: false))
                            // LoopApp：与 Worker 同一份工具，owner 是本 Loop 会话
                            .Concat(OpenLoop.Service.Worker.WorkerScopeRules.LoopApp(_workspace, _sessionId)),
                        settings.WebFetchMode);
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
                turn.AssistantText = FailedText(turn, ex.Message);
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                _lblStatus.Text = "状态: error";
                SetState(SessionState.Interrupted);
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
            // 正文喂进时间线的进度：正文出现即分段，正文之后的思维另起一段折叠区
            int answerLen = 0;
            long lastDeltaUi = 0;
            // 把缓冲区里还没进时间线的正文补进去。250ms 节流会跳过大部分 delta，只在采样点补的话，
            // 每步正文只剩开头一两个字符（实测「现在现在现在现在现在补 `CoreCore」），工具开始前必须补一次。
            string FlushAnswer()
            {
                string snapshot;
                lock (_streamBuffer) snapshot = _streamBuffer.ToString();
                string visible = AgentToolCallParser.CutToolCallText(snapshot);
                if (visible.Length > answerLen)
                {
                    lock (timeline) timeline.Answer(visible[answerLen..]);
                    answerLen = visible.Length;
                    // 正文先进数据：标签页关掉（句柄失效）时界面更新会被跳过，重开后按数据重建
                    turn.AssistantText = LlmTextSanitizer.StripSafetyLines(visible);
                }
                return visible;
            }
            Action<string> onDelta = piece =>
            {
                lock (_streamBuffer) _streamBuffer.Append(piece);
                _liveTokens += AgentContext.EstimateTokens(piece);
                // 每个 token 都整段 ToString + 清洗再投 UI，等于 O(n²)：按 250ms 合并，收尾时用最终正文重建
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastDeltaUi) < 250) return;
                Interlocked.Exchange(ref lastDeltaUi, now);
                if (FlushAnswer().Length == 0) return;
                RunOnUi(() =>
                {
                    lock (timeline) SyncPhases(turn, timeline);
                    _contextTokens = _promptTokens + _liveTokens;
                    ApplyDetail();
                    _chat.UpdateStreamingMessage(turn, null);
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
                    lock (timeline) SyncPhases(turn, timeline);
                    _contextTokens = _promptTokens + _liveTokens;
                    ApplyDetail();
                    // 与 Worker 一致：正文只由 onDelta 写（已过滤协议原文），推理增量只刷新思考区
                    _chat.UpdateStreamingMessage(turn, null);
                });
            };

            // 断流整轮重发：清掉已显示的半截正文/思考，避免重发后出现两遍
            void ResetStream()
            {
                lock (_streamBuffer) _streamBuffer.Clear();
                lock (reasoningBuffer) reasoningBuffer.Clear();
                _liveTokens = 0;
                answerLen = 0;
                lock (timeline) timeline.DropThinking();
                RunOnUi(() =>
                {
                    turn.ReasoningText = "";
                    lock (timeline) turn.TimelineText = timeline.Render();
                    lock (timeline) SyncPhases(turn, timeline);
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
                        lock (timeline) SyncPhases(turn, timeline);
                        turn.ToolsFinished();
                        RefreshChat();
                    });
                    trace.Step(step);
                    string note = step.DisplayLine.Replace(" >> ", "");
                    if (!string.IsNullOrEmpty(step.Summary)) note += " → " + step.Summary;
                    OpenLoop.Service.Web.SessionBoard.Working(BoardId(), note, remote: true);
                },
                onToolStart: (name, args) =>
                {
                    // 本步正文先补进时间线，再清缓冲：节流窗口里的尾巴不能跟着缓冲区一起丢掉
                    FlushAnswer();
                    lock (_streamBuffer) _streamBuffer.Clear();
                    answerLen = 0;
                    string hint = AgentStepRecord.InFlightLine(name, args);
                    RunOnUi(() =>
                    {
                        lock (timeline) SyncPhases(turn, timeline);
                        turn.AssistantText = "";
                        turn.ToolStarted(hint);
                        _chat.UpdateStreamingMessage(turn, "", true);
                        RefreshChat();
                    });
                },
                confirmContinue: AskOnUi,
                skills: skills);

            // 约定按实际动手的目录（源码根）取：Loop 从 /complain 起时会话工作区是别人的项目，
            // 那份 AGENTS.md 讲的是那个项目，不是这里要维护的仓库
            string systemPrompt = loop.BuildSystemPrompt(
                LoopPrompt.Role, LoopPrompt.Rules,
                settings.UseAgentsMd
                    ? AgentsInstructions.Load(root, AppSettings.GlobalAgentsPath)
                    : null,
                root, LoopPrompt.Access);
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

                FlushAnswer();
                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                lock (timeline) turn.TimelineText = timeline.Render();
                turn.AssistantText = result.StoppedByUser && string.IsNullOrWhiteSpace(result.FinalAnswer)
                    ? "（已停止）"
                    : CleanAnswer(result.FinalAnswer, result.FinishReason);
                lock (timeline) timeline.FeedAnswerTail(turn.AssistantText);
                lock (timeline) SyncPhases(turn, timeline);
                ChatStore.AttachTranscript(turn, result.Transcript);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser || result.StoppedByGuard ? "stopped"
                    : "ok";
                trace.Finish(outcome, turn.AssistantText, result.ChatCost, result.Guards);
                _lastCost = result.ChatCost?.ToString("0.0000") ?? "—";
                _contextTokens = result.ContextTokens ?? _contextTokens;
                _lblStatus.Text = $"状态: {outcome} · {result.Steps.Count} 步 · 成本 {_lastCost}";
                SetState(outcome == "ok" ? SessionState.Done : SessionState.Interrupted);
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
                SetState(SessionState.Interrupted);
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop 会话失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = FailedText(turn, ex.Message);
                trace.Finish("error", turn.AssistantText);
                _lblStatus.Text = "状态: error";
                SetState(SessionState.Interrupted);
                Notify("Loop 执行失败", ex.Message, error: true);
                // 免费额度耗尽：让 ConnMan 换节点，就绪后由它回调这里给原会话发「继续」
                if (ServerNotice.IsQuotaFailure(turn.AssistantText))
                {
                    // 回调在后台线程：SendAsync 会碰控件，必须回 UI 线程
                    FreeLimitRecovery.TryStart(_sessionId, model, UserEpochOf, text =>
                    {
                        if (UserEpochOf() == null) return false;
                        RunOnUi(() => SendAsync(text));
                        return true;
                    }, _workspace + "|" + _sessionId);
                }
            }
            finally
            {
                trace.Dispose();
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                RefreshChat();
                // 正常收尾：长正文从头读（流式期间已跟随到底），避免用户打开就落在末尾
                if (outcome == "ok" && !IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => _chat.ScrollToAnswerStart(turn)));
                SaveLoopChat();
                // 这一轮可能改过源码/配置：仓库按钮跟着刷新
                _ = RefreshRepoActionsAsync();
                // 首轮结束后自动总结一次标题（已有标题就不再覆盖），和 Worker 一致
                if (!IsDisposed && IsHandleCreated && _book.Turns.Count == 1
                    && string.IsNullOrWhiteSpace(LoopIndex.Title(_workspace, _sessionId)))
                    _ = SummarizeTitleAsync(notify: false);
                // 排队文本：当前轮收尾后再发，避免重入；失败轮不续发，等新的输入激活会话
                // 正常收尾就是轮后：上一轮「停止」留下的暂停不该再挡住队列；失败轮照旧等新输入
                if (outcome is "ok" or "budget") _queue.Paused = false;
                if (outcome != "error" && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(DrainQueue));
            }
        }

        /// <summary>
        /// 失败轮的正文：先交代这一轮已经做了什么，再写失败原因。
        /// 只留一句错误信息时，几十步的进展在界面上等于凭空消失。
        /// </summary>
        internal static string FailedText(ChatTurn turn, string error)
        {
            string head = OpenLoop.Core.Messages.ServerNotice.FailedPrefix + error;
            string tools = turn?.ToolCallLines?.Trim();
            if (string.IsNullOrEmpty(tools)) return head;
            var lines = tools.Split('\n');
            var tail = lines.Length <= 6 ? lines : lines[(lines.Length - 6)..];
            return head + "\n\n本轮已执行 " + lines.Length + " 步，最近几步：\n" + string.Join("\n", tail);
        }

        /// <summary>把时间线的分段同步进回话数据；分段正文过一遍安全行清洗，与正文区一致。</summary>
        private static void SyncPhases(ChatTurn turn, RunTimeline timeline)
        {
            turn.Phases.Clear();
            foreach (var (thinking, answer) in timeline.RenderPhases())
                turn.Phases.Add((thinking, LlmTextSanitizer.StripSafetyLines(answer ?? "")));
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
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var models = ModelChoices.FilterByProvider(ModelChoices.Build(_settings(), _onlyFree), provider);

                ModelChoices.Fill(_selModel, models, _settings());

                if (provider == null && !string.IsNullOrWhiteSpace(_model) && models.All(m => m.Id != _model))
                    _selModel.Items.Add(new AntdUI.SelectItem(_model) { Tag = _model });

                if (provider != null && models.All(m => m.Id != _model))
                    _model = models.FirstOrDefault()?.Id ?? _model;

                _selModel.SelectedValue = _model;

                var images = ModelChoices.FilterByProvider(ModelChoices.Images(_settings(), _onlyFree), provider);
                _selImage.Items.Clear();
                foreach (var image in images)
                    _selImage.Items.Add(new AntdUI.SelectItem(image.Name ?? image.Id) { Tag = image.Id });
                _btnImage.Visible = images.Count > 0;
                _selImage.Visible = images.Count > 0 && _imageOpen;
                if (string.IsNullOrWhiteSpace(_imageModel) || images.All(m => m.Id != _imageModel))
                    _imageModel = images.FirstOrDefault()?.Id ?? "";
                _selImage.SelectedValue = _imageModel;
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
            Persist(s =>
            {
                s.Model = _model;
                ModelChoices.RecordUse(s, _model);
            });
            ApplyDetail();
        }

        private void OnEffortChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            _effort = _selEffort.SelectedValue as string ?? "";
            Persist(s => s.ReasoningEffort = _effort);
            ApplyDetail();
        }

        private void OnImageModelChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            if (_selImage.SelectedValue is string id) _imageModel = id;
        }

        private void OnAttachClick(object sender, EventArgs e)
        {
            int limit = MediaModels.AttachmentLimit(_model);
            var picked = AttachmentsUi.Pick(FindForm(), _attachments, limit, out bool overflow);
            if (picked == null) return;
            _attachments.Clear();
            _attachments.AddRange(picked);
            if (overflow) Notify("附件太多", $"当前模型最多 {limit} 个附件", error: true);
            UpdateAttachments();
        }

        private void OnClearAttachments(object sender, EventArgs e)
        {
            _attachments.Clear();
            UpdateAttachments();
        }

        private void UpdateAttachments()
        {
            _lblAttach.Text = AttachmentsUi.Label(_attachments, MediaModels.AttachmentLimit(_model));
            _lblAttach.Visible = _attachments.Count > 0;
            if (_attachments.Count > 0) _tips.SetToolTip(_lblAttach, string.Join("\n", _attachments));
            UpdateSendButton();
        }

        private void OnToggleImagePicker(object sender, EventArgs e)
        {
            _imageOpen = !_imageOpen;
            _btnImage.Toggle = _imageOpen;
            _selImage.Visible = _imageOpen && _selImage.Items.Count > 0;
        }

        /// <summary>拖放到输入区的文件追加成附件（和附件按钮同一套上限与去重）。</summary>
        private void AddDroppedFiles(string[] paths)
        {
            int limit = MediaModels.AttachmentLimit(_model);
            if (AttachmentsUi.Add(_attachments, paths, limit))
                Notify("附件太多", $"当前模型最多 {limit} 个附件", error: true);
            UpdateAttachments();
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
            if (!_upperSplit.Panel2Collapsed) _ = RefreshRepoActionsAsync();
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
            if (_browser != null)
            {
                _browser.Visible = index == BrowserPage;
                if (index == BrowserPage) _browser.EnsureLoaded();
            }
            _fileView.Visible = index >= 0 && index == _filePage;
            if (index == 0)
            {
                ApplyDetail();
                _ = RefreshRepoActionsAsync();
            }
        }

        /// <summary>聊天区里点链接：网址进右侧「网页」页，本地路径进「文件」页（文件夹直接用资源管理器打开）。</summary>
        private void OnChatLinkClicked(object sender, ChatLinkClickedEventArgs e)
        {
            if (e == null) return;
            if (e.Kind == ChatTextStyler.ChatLinkKind.Url)
            {
                ShowBrowser();
                if (!Browser.Navigate(e.Target, out string urlError)) Notify("无法打开网址", urlError, error: true);
                return;
            }

            if (ChatPathActions.HandleClick(this, e.Target, _workspace)) return;

            if (!_fileView.TryShowFile(e.Target, _workspace, out string error))
            {
                // 二进制/太大/解码失败：改问要不要用系统默认程序打开
                ChatPathActions.AskSystemOpen(this, e.Target, _workspace, error);
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
                panel1Min: Dip(400), panel2Min: Dip(360), targetPanel2: Dip(400));
            _layoutReady = true;
        }

        /// <summary>提交输入框。immediate=true 时运行中会打断本轮，收尾后立刻发这一条（Enter 的语义）。</summary>
        private void Submit(bool immediate = false)
        {
            string text = _txtInput.Text.Trim();

            // 正在编辑排队项：这一下只保存，不发送
            if (_editing != null)
            {
                var editing = _editing;
                _editing = null;
                bool removed = text.Length == 0;
                if (removed) _queue.Remove(editing);
                else
                {
                    editing.Text = text;
                    // 编辑期间这一条被搁置，保存后才重新可发
                    _queue.Release(editing);
                }
                _txtInput.Clear();
                _pending.Sync();
                // 编辑保存/删除也会改变「立即」项有无：打断状态跟着队列走
                SyncInterrupt();
                UpdateSendButton();
                // 本轮可能早在编辑时就收尾了（队列因搁置取不到东西）：保存后补发
                if (!removed && !_running) DrainQueue();
                return;
            }

            //「/命令」先在这里分流：命令不走普通对话
            if (TryRunCommand(text))
            {
                _txtInput.Clear();
                return;
            }
            //命令写了但没写请求：只提示，不要把「/btw」本身当任务发出去
            if (SessionView.IsCommand(text, "/btw") || SessionView.IsCommand(text, "/complain")
                || SessionView.IsCommand(text, "/fork")) return;

            //「/技能名 请求…」展开成让 Worker 先读技能再执行的任务，后面按普通输入发送/排队
            text = SlashCommands.SkillTask(text, _skillCommands) ?? text;

            if (_running)
            {
                if (text.Length == 0 && _attachments.Count == 0)
                {
                    Stop();
                    return;
                }
                // shortcut: 排队项只存附件路径，发送时才读文件；源文件被删/改动会按当时的文件内容发送
                var files = _attachments.ToList();
                var item = _queue.Add(text, "task", immediate
                    ? OpenLoop.Service.Pending.PendingMode.NextStep
                    : OpenLoop.Service.Pending.PendingMode.AfterTurn, files);
                // 和 Worker 一样：新排一条就恢复自动续发（Stop 暂停过队列）
                _queue.Paused = false;
                _txtInput.Clear();
                if (files.Count > 0)
                {
                    _attachments.Clear();
                    UpdateAttachments();
                }
                _pending.Sync();
                if (item != null && item.Immediate) OnImmediateRequested(item);
                return;
            }

            if (text.Length == 0 && _attachments.Count == 0) return;
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

            if (SessionView.IsCommand(text, "/complain"))
            {
                string complaint = text[9..].Trim();
                if (complaint.Length == 0)
                {
                    Notify("缺少内容", "/complain 后面要写具体不满，例如：/complain 它没看证据就下了结论。", error: true);
                    return false;
                }
                string title = LoopIndex.Title(_workspace, _sessionId);
                if (string.IsNullOrWhiteSpace(title)) title = _sessionId;
                ComplaintRequested?.Invoke(
                    SessionView.ComposeComplaint("Loop", title, _book.Turns, complaint,
                        ChatHistory.PathFor(_workspace, _sessionId)));
                return true;
            }

            if (SessionView.IsCommand(text, "/compact"))
            {
                if (_running)
                {
                    Notify("正在运行", "请先停止当前任务，再压缩上下文。", error: true);
                    return true;
                }
                _ = CompactContextAsync();
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

        /// <summary>/compact：把较早的消息换成摘要并挂到最近一轮的 transcript；下一轮起按压缩后的历史发。</summary>
        private async Task CompactContextAsync()
        {
            var history = BuildHistory();
            if (history.Count == 0)
            {
                Notify("没有可压缩的上下文", "当前会话还没有历史。", error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            if (model.Length == 0)
            {
                Notify("缺少配置", "请先选择模型。", error: true);
                return;
            }

            string status = _lblStatus.Text;
            _lblStatus.Text = "状态: 压缩上下文…";
            try
            {
                var settings = _settings();
                int before = AgentContext.EstimateTokens(history);
                var compacted = await Task.Run(async () =>
                {
                    var reasoner = Reasoners.Create(settings, model, _effort, _workspace, AskApproval);
                    return await CompressionStep.CompressAsync(history, reasoner, CancellationToken.None,
                        AgentContext.TailTokenBudget(ModelChoices.ContextLimit(model, settings)));
                });
                if (compacted == null)
                {
                    Notify("未压缩", "上下文太短，没有可压缩的中间部分。", error: true);
                    return;
                }

                var last = _book.Turns.LastOrDefault();
                if (last != null) ChatStore.AttachTranscript(last, compacted);
                int after = AgentContext.EstimateTokens(compacted);
                _contextTokens = after;
                SaveLoopChat();
                Notify("已压缩上下文", $"{before:N0} → {after:N0} tokens");
            }
            catch (Exception ex)
            {
                AppLog.Error("压缩上下文失败", ex);
                Notify("压缩失败", ex.Message, error: true);
            }
            finally
            {
                _lblStatus.Text = status;
                ApplyDetail();
            }
        }

        private static readonly (string Command, string Description)[] Commands =
        {
            ("/btw", "在右侧栏用当前上下文跑一个只读旁路任务"),
            ("/complain", "把对 Loop 表现的不满发给 Loop 立即处理"),
            ("/compact", "把较早的对话压缩成摘要，省下上下文"),
            ("/fork", "复制当前上下文开一个新的 Loop 会话（可继续对话）并跳转"),
            ("/new", "开一个新的 Loop 会话，沿用当前的模型与强度")
        };

        private readonly List<(string Command, string Description)> _cmdMatches = [];
        private IReadOnlyList<(string Command, string Description)> _skillCommands = [];
        private DateTime _skillsAt;

        /// <summary>启用中的技能也进「/」列表；30 秒内复用上次结果，避免每敲一个字都去问 I。</summary>
        private void EnsureSkillCommands()
        {
            if (DateTime.UtcNow - _skillsAt < TimeSpan.FromSeconds(30)) return;
            _skillsAt = DateTime.UtcNow;
            string workspace = _workspace;
            var loop = Loop;
            _ = Task.Run(async () =>
            {
                var commands = await SlashCommands.SkillsAsync(loop, workspace);
                if (IsDisposed || workspace != _workspace) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        _skillCommands = commands;
                        UpdateCommandPopup();
                    }));
                }
                catch (ObjectDisposedException) { }
            });
        }

        /// <summary>输入以 / 开头且还没写参数时，弹出可用命令的联想列表。</summary>
        private void UpdateCommandPopup()
        {
            string text = _txtInput.Text ?? "";
            bool want = text.StartsWith('/') && !text.Contains(' ') && !text.Contains('\n');
            _cmdMatches.Clear();
            if (want)
            {
                EnsureSkillCommands();
                foreach (var command in Commands)
                    if (command.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _cmdMatches.Add(command);
                foreach (var command in _skillCommands)
                    if (!Commands.Any(c => string.Equals(c.Command, command.Command, StringComparison.OrdinalIgnoreCase))
                        && command.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _cmdMatches.Add(command);
            }

            if (_cmdMatches.Count == 0)
            {
                _cmdPopup.Close();
                return;
            }

            _cmdPopup.Open(_cmdMatches);
            _cmdPopup.PlaceAbove(_inputArea);
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

        private void OnSendClick(object sender, EventArgs e)
        {
            // 免费额度撞墙后按钮是「继续」：输入框为空时点它就把继续发出去
            if (_txtInput.Text.Trim().Length == 0 && _attachments.Count == 0 && !_running && QuotaOnLastTurn)
            {
                SendAsync(Localization.T("ContinuePrompt"));
                return;
            }
            // 运行中点发送与 Enter 一致：打断本轮、收尾后立刻发；要等本轮返回用 Ctrl+Enter
            Submit(immediate: true);
        }

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

        /// <summary>按钮可用性只有一处口径：有改动才能提交，有上一版本才能退回，跑迭代时整组灰掉。</summary>
        private void SyncRepoButtons()
        {
            bool idle = !_iterating && !_repoBusy;
            _btnCommit.Enabled = _btnPush.Enabled = idle && _hasChanges;
            _btnRollback.Enabled = idle && _hasRollback;
        }

        /// <summary>问 I 一次工作树与版本表：I 不可达时保持禁用（提交本来也得走 I）。</summary>
        private async Task RefreshRepoActionsAsync()
        {
            bool hasChanges = false;
            try
            {
                using var client = new OpenLoop.Service.Web.LoopApiClient();
                var status = await client.GitStatusAsync();
                hasChanges = (status?.Repos ?? []).Any(r => r.Files.Count > 0);
            }
            catch
            {
            }
            if (IsDisposed) return;
            _hasChanges = hasChanges;
            _hasRollback = RollbackTargets().Count > 0;
            SyncRepoButtons();
        }

        /// <summary>可退回的上一版本：previous.json 里有登记、目录还在，且与当前跑的不是同一份。</summary>
        private static Dictionary<string, VersionEntry> RollbackTargets()
        {
            var store = new VersionStore();
            var map = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string role in new[] { "a", "i" })
            {
                var entry = store.Previous(role);
                if (string.IsNullOrWhiteSpace(entry?.Dir) || !Directory.Exists(entry.Dir)) continue;
                if (string.Equals(store.Current(role)?.Dir, entry.Dir, StringComparison.OrdinalIgnoreCase)) continue;
                map[role] = entry;
            }
            return map;
        }

        private async void OnCommitClick(object sender, EventArgs e) => await CommitReposAsync(push: false);

        private async void OnPushClick(object sender, EventArgs e) => await CommitReposAsync(push: true);

        /// <summary>先取预览（每个仓库的文件清单 + token），原样带回再提交；点按钮本身就是显式确认。</summary>
        private async Task CommitReposAsync(bool push)
        {
            if (_repoBusy || _iterating) return;
            _repoBusy = true;
            SyncRepoButtons();
            _lblStatus.Text = push ? "状态: 正在提交并推送…" : "状态: 正在提交…";
            try
            {
                using var client = new OpenLoop.Service.Web.LoopApiClient(timeout: TimeSpan.FromMinutes(5));
                var status = await client.GitStatusAsync();
                var repos = (status?.Repos ?? []).Where(r => r.Files.Count > 0).ToList();
                string skipped = string.Join("；", (status?.Repos ?? [])
                    .Where(r => !string.IsNullOrWhiteSpace(r.Error))
                    .Select(r => $"{r.Id}: {r.Error}"));
                if (repos.Count == 0)
                {
                    _lblStatus.Text = "状态: 没有改动";
                    Notify("提交", "工作树是干净的，没有可提交的改动。");
                    return;
                }

                var tokens = repos.ToDictionary(r => r.Id, r => r.Token, StringComparer.OrdinalIgnoreCase);
                var result = await client.GitCommitAsync(
                    repos.Select(r => r.Id), null, push, tokens, force: true);
                var failed = (result?.Repos ?? []).Where(r => !r.Ok).ToList();
                string summary = failed.Count == 0
                    ? (push ? "已提交并推送" : "已提交")
                    : "提交未全部成功：" + string.Join("；", failed.Select(r => $"{r.Id}: {r.Error}"));
                _lblStatus.Text = "状态: " + summary;
                Notify(failed.Count == 0 ? "提交" : "提交失败",
                    summary + (skipped.Length == 0 ? "" : "\n跳过 " + skipped), failed.Count > 0);
            }
            catch (Exception ex)
            {
                AppLog.Error("提交失败", ex);
                _lblStatus.Text = "状态: 提交失败";
                Notify("提交失败", ex.Message, error: true);
            }
            finally
            {
                _repoBusy = false;
                SyncRepoButtons();
                _ = RefreshRepoActionsAsync();
            }
        }

        /// <summary>手动退回：把 A/I 换回上一版本并重启（壳不动），之后这些角色当「未发布」重编。</summary>
        private async void OnRollbackClick(object sender, EventArgs e)
        {
            if (_repoBusy || _iterating) return;
            var targets = RollbackTargets();
            if (targets.Count == 0)
            {
                _hasRollback = false;
                SyncRepoButtons();
                _lblStatus.Text = "状态: 当前已经是上一轮之前的版本";
                return;
            }

            string where = string.Join("、", targets.Select(t => $"{t.Key.ToUpperInvariant()} {t.Value.Version}"));
            if (MessageBox.Show(this, $"退回上一版本？\n{where}\n壳保持当前版本。", "退回上一版本",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _repoBusy = true;
            SyncRepoButtons();
            _lblStatus.Text = "状态: 正在退回…";
            try
            {
                var steps = await IterationRunner.RollbackAsync(targets, _ => { });
                if (steps.Count > 0 && steps.All(s => s.Ok))
                {
                    VersionState.Forget(targets.Keys);
                    _lblStatus.Text = "状态: 已退回上一版本（壳未变）";
                    Notify("已退回上一版本", where + "（壳未变）");
                }
                else
                {
                    _lblStatus.Text = "状态: 退回未全部成功";
                    Notify("退回失败", "worker 忙就等这一轮跑完再试。", error: true);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("退回失败", ex);
                _lblStatus.Text = "状态: 退回失败";
                Notify("退回失败", ex.Message, error: true);
            }
            finally
            {
                _repoBusy = false;
                SyncRepoButtons();
                _ = RefreshRepoActionsAsync();
            }
        }

        private async void OnIterateClick(object sender, EventArgs e)
        {
            if (_iterating || Iterate == null) return;
            _iterating = true;
            _btnIterate.Enabled = false;
            SyncRepoButtons();
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
                SyncRepoButtons();
                // 迭代会改版本号与工作树：两个判断都要重问
                _ = RefreshRepoActionsAsync();
            }
        }

        /// <summary>
        /// Enter 在 AntdUI 的 WM_KEYDOWN 里先被消化成换行（早于 KeyDown 事件），
        /// 所以只能在这里拦：Enter 发送（运行中=打断本轮，收尾后立刻发），
        /// Ctrl+Enter 发送（运行中=等本轮返回），Shift+Enter 换行。
        /// </summary>
        private void InsertSoftNewline() => ComposerKeys.InsertSoftNewline(_txtInput);

        private void OnComposerKeyboard(object sender, AntdUI.InputVerifyKeyboardEventArgs e)
        {
            if (e.KeyData == (Keys.Control | Keys.V))
            {
                int pasteLimit = MediaModels.AttachmentLimit(_model);
                if (AttachmentsUi.TryPaste(_attachments, pasteLimit, out bool overflow))
                {
                    e.Result = false;
                    if (overflow) Notify("附件太多", $"当前模型最多 {pasteLimit} 个附件", error: true);
                    UpdateAttachments();
                    return;
                }
            }
            bool shift = e.KeyData == (Keys.Shift | Keys.Enter);
            bool ctrl = e.KeyData == (Keys.Control | Keys.Enter);
            if (!shift && e.KeyData != Keys.Enter && !ctrl) return;
            e.Result = false;
            if (shift) { InsertSoftNewline(); return; }
            // 只挂附件、不写字也是合法消息：这里必须和 Submit 的判据一致，否则回车被静默吞掉
            if (_editing == null && _txtInput.Text.Trim().Length == 0 && _attachments.Count == 0) return;
            if (_cmdPopup.IsOpen) { _cmdPopup.Accept(); return; }
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
            if (running) SetState(SessionState.Running);
            UpdateSendButton();
        }

        private void SetState(SessionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(this);
        }

        /// <summary>运行中且输入为空 = 停止；有内容 = 立即发送（打断本轮）；编辑排队项时 = 保存。</summary>
        private void UpdateSendButton()
        {
            bool editing = _editing != null;
            bool hasAttachments = _attachments.Count > 0;
            bool stop = !editing && _running && !hasAttachments && _txtInput.Text.Trim().Length == 0;
            bool retry = !editing && !stop && !hasAttachments && _txtInput.Text.Trim().Length == 0 && QuotaOnLastTurn;
            _btnSend.Text = editing
                ? Localization.T("SettingsSave")
                : Localization.T(retry ? "Continue" : stop ? "Stop" : "Send");
            _btnSend.IconSvg = UiIcons.Of(stop ? UiIcons.Stop : UiIcons.Send,
                stop ? UiStyle.Error : Color.White);
            _btnSend.Type = stop ? AntdUI.TTypeMini.Error : AntdUI.TTypeMini.Primary;
            _btnSend.Ghost = stop;
        }

        private bool QuotaOnLastTurn
        {
            get
            {
                var last = _book.Turns.LastOrDefault();
                return last != null && ServerNotice.IsQuotaFailure(last.AssistantText);
            }
        }

        /// <summary>
        /// 「立即」：先让当前轮在下一个 step 前收尾，收尾后 DrainQueue 会先发这一条；
        /// 改回「等本轮」时收回打断，否则这一轮照样在 step 后收尾、排队项仍被提前发出去。
        /// </summary>
        private void OnImmediateRequested(OpenLoop.Service.Pending.PendingItem item)
        {
            if (item == null) return;
            _queue.Paused = false;
            _pending.Sync();
            if (!_running)
            {
                if (item.Immediate) DrainQueue();
                return;
            }
            SyncInterrupt();
        }

        /// <summary>打断状态以队列为准，与 Worker 的 SessionView.SyncInterrupt 同一语义。</summary>
        private void SyncInterrupt() => _interrupt = _running && _queue.AnyImmediate;

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
            // 编辑期间先搁置：本轮结束也不会被自动发出去，保存后才重新入列
            _queue.Hold(item);
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
            // 标签页关掉后视图不再挂在窗体上：句柄可能已销毁，BeginInvoke 会抛，直接跳过（重开时按 _book 重建界面）
            if (IsDisposed || !IsHandleCreated) return;
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
