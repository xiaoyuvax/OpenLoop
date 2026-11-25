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

        /// <summary>上一次自动总结标题（计入收尾）时的轮数：轮次结束时只在之后又聊过才再更新一次。</summary>
        private int _titleTurnCount;

        /// <summary>用户轮数：宿主消息（压缩进度之类，User 为空）不算，与 Worker 同一口径。</summary>
        private int UserTurnCount => _book.Turns.Count(t => t.User != null);
        private bool _filling;
        private bool _fillingProviders;
        private bool _iterating;
        private bool _repoBusy;
        private bool _hasChanges;
        private bool _hasRollback;
        private bool _layoutReady;
        private DateTime _lastCheckpointUtc;
        /// <summary>Initialize 读出的对话条目：模型恢复 / 历史加载 / 冷启动整理共用一次文件解析。</summary>
        private List<ChatEntry> _loadedEntries;

        /// <summary>落盘队列：中途快照与收尾保存串行写，保证最后落盘的是最新那份。</summary>
        private Task _chatWrite = Task.CompletedTask;

        /// <summary>中途快照的节流间隔（账本 limit.checkpoint_interval，开轮时读一次；与 Worker 同源）。</summary>
        private TimeSpan _checkpointEvery = OpenLoop.Service.Storage.ChatHistory.DefaultCheckpointInterval;

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

        /// <summary>只有选中的标签页持续渲染：切走暂停（Loop 轮照常写数据，切回滚到底再按最新数据重建）。
        /// 与 Worker 的 SessionView.SetActive 同一口径。</summary>
        internal void SetActive(bool active) => _chat.SuspendRendering = !active;

        private const int BrowserPage = 5;

        /// <summary>「Harness」页在侧栏标签里的固定下标（可演进参数管理）。</summary>
        private const int HarnessPage = 2;

        /// <summary>「Puppet」页在侧栏标签里的固定下标（Action 清单，/puppet 打开）。</summary>
        private const int PuppetPage = 3;

        /// <summary>「LoopApp」页在侧栏标签里的固定下标（本工作区生成过的应用）。</summary>
        private const int LoopAppPage = 4;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BrowserView Browser => _browser;

        /// <summary>「网页」页现在是不是摆在侧栏里（收起后实例还在，但标签摘了）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool BrowserVisible => _browser != null && !_browserTabHidden;

        /// <summary>展开右侧栏并切到「网页」页（Agent 用内嵌浏览器时的入口）；页被关掉时重新建。</summary>
        public void ShowBrowser()
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            if (_browser == null)
            {
                _browser = new BrowserView { Dock = DockStyle.Fill, Visible = false };
                AttachLoopApp(_browser);
                _sideHost.Controls.Add(_browser);
                _sideTabs.Items.Insert(BrowserPage, new AntdUI.SegmentedItem { Text = Localization.T("网页") });
                if (_filePage >= BrowserPage) _filePage++;
            }
            else if (_browserTabHidden)
            {
                // 收起来过：实例还在，把标签插回原位就是（不重建 WebView2）
                _sideTabs.Items.Insert(BrowserPage, new AntdUI.SegmentedItem { Text = Localization.T("网页") });
                if (_filePage >= BrowserPage) _filePage++;
                _browserTabHidden = false;
            }
            ShowSidePage(BrowserPage);
        }

        /// <summary>关掉「网页」页：销毁 WebView2 实例释放内存，再用时由 ShowBrowser 重建。</summary>
        public void CloseBrowserPage()
        {
            if (_browser == null) return;
            int selected = _sideTabs.SelectIndex;
            // 标签已经被收起（实例还留着）时不再摘一次，否则删掉的是别人
            if (_sideTabs.Items.Count > BrowserPage) _sideTabs.Items.RemoveAt(BrowserPage);
            if (_filePage > BrowserPage) _filePage--;
            var browser = _browser;
            _browser = null;
            _sideHost.Controls.Remove(browser);
            browser.Dispose();
            _browserTabHidden = false;
            // 关掉的不是当前页就停在原页（下标可能前移一位）
            ShowSidePage(selected == BrowserPage ? 0 : Math.Max(0, selected > BrowserPage ? selected - 1 : selected));
        }

        private void OnSideTabsMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _browser == null) return;
            var items = new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem(Localization.T("关闭网页")) { ID = "closebrowser" }
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

        /// <summary>平铺视图只读最新一轮用；与会话的 Session.Book 同形。</summary>
        internal ChatTurnBook Book => _book;

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
            // 打开一个会话只解析一次对话文件（几十 MB 的 JSON 解析很贵，恢复大量标签页时尤其明显）：
            // 模型恢复、历史加载、冷启动整理共用同一份 entries。
            var entries = ChatHistory.Read(_workspace, _sessionId);
            var (savedModel, savedEffort) = ChatHistory.LastModelFrom(entries);
            // 会话按它自己上次用的模型/强度恢复（与 Worker 一致）：分叉、转交过来的会话沿用原参数，
            // 关掉标签页再打开也不会退回全局设置。没有记录时才用全局设置。
            _model = !string.IsNullOrWhiteSpace(savedModel)
                ? savedModel
                : string.IsNullOrWhiteSpace(current.Model) ? "openrouter/free" : current.Model;
            _effort = savedEffort ?? current.ReasoningEffort ?? "";
            _onlyFree = current.OnlyFreeModels;
            _loadedEntries = entries;

            _filling = true;
            try { _freeSwitch.Checked = _onlyFree; }
            finally { _filling = false; }

            FillProviders();
            FillModels();
            SelectEffort(_effort);
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort);
            _tips.SetToolTip(_btnImage, Localization.T("生图模型"));
            _tips.SetToolTip(_btnAttach, Localization.T("添加附件（也可拖放或粘贴文件/图片）"));
            AttachmentsUi.EnableDrop(_inputArea, AddDroppedFiles);
            _tips.SetToolTip(_btnIterate, Localization.T("先出计划（谁升版、改了什么），确认后编译并换 A/I，最后重启壳（界面会短暂退出）"));
            _tips.SetToolTip(_btnCommit, Localization.T("提交托管仓库里的改动（工作树干净时不可用）"));
            _tips.SetToolTip(_btnPush, Localization.T("提交并推送到远端（工作树干净时不可用）"));
            _tips.SetToolTip(_btnRollback, Localization.T("把 A/I 退回上一版本，壳保持当前版本（没有上一版本时不可用）"));
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

            // 历史加载只做轮次投影（不重复读文件）：Initialize 开头读的那份 entries 在这里消费掉；
            // transcript 延迟回读需要文件路径，一起传下去
            _book.ReplaceAll(ChatStore.Turns(_loadedEntries, ChatHistory.PathFor(_workspace, _sessionId)), null);
            ChatStore.PruneCold(_workspace, _sessionId, _model, _book.Turns, _loadedEntries);
            _loadedEntries = null;   // 只在 Initialize 用这一次，别让它挂着几十 MB 不放
            if (_book.Turns.Count > 0)
                LoopIndex.Touch(_workspace, _sessionId, _book.Turns.LastOrDefault(t => t.User != null)?.ToolStepCount);
            _chat.PlaceholderText = Localization.T("NoMessages");
            _chat.UndoRequested += OnChatUndo;
            _chat.CopyRequested += OnChatCopy;
            _chat.LinkClicked += OnChatLinkClicked;
            // 工具栏「操作」下拉与右键菜单的「操作」子菜单共用同一组动作
            _chat.SelectionActions = [new ChatSelectionAction("copy", Localization.T("复制")), new ChatSelectionAction("worker", Localization.T("转给 worker")), new ChatSelectionAction("delete", Localization.T("删除"))];
            _chat.SelectionActionRequested += (_, e) => RunSelectionAction(e.Key);
            _chat.SelectionModeChanged += (_, _) =>
            {
                if (_chkSelect.Checked != _chat.ShowSelection) _chkSelect.Checked = _chat.ShowSelection;
            };
            _chat.FlashInterval = SessionView.FlashIntervalMs(_workspace);   // 工具闪现区节拍（账本可演化）
            _chat.Bind(_book);
            _tips.SetToolTip(_btnSummary, Localization.T("用当前模型重新总结会话标题"));
            string savedTitle = LoopIndex.Title(_workspace, _sessionId);
            // 标题格式与 Worker 对齐（工作区文件夹名 · 内容简述），Loop 身份靠页头图标与颜色
            _lblTitle.Text = Session.ComposeTitle(_workspace, savedTitle);
            // 已有标题（历史）：末次更新只在这次之后又聊过才做；建会话时的占位标题不算
            if (LoopIndex.HasTitle(_workspace, _sessionId)) _titleTurnCount = UserTurnCount;
            else if (!string.IsNullOrWhiteSpace(savedTitle) && _book.Turns.Count > 0)
                _ = SummarizeTitleAsync(notify: false);   // 旧会话的「反馈 15:19」这类占位标题：打开时补一次
            _chat.ScrollToEnd();

            _lblStatus.TextChanged += (_, _) => ApplyDetail();
            _sideBtw.Initialize(_workspace, settings);
            // Harness 页：与 Worker 同一套入口，演进/回滚走 I 的 /api/loop/*
            _harness.Initialize(() => _workspace, settings, () => _model, () => _effort, Notify);
            _harness.SetBusy(_running);
            // Puppet Action 清单页：双击一行把「/puppet <action> 」填回输入框
            _puppetView.CommandRequested += command =>
            {
                _txtInput.Text = command;
                _txtInput.SelectionStart = _txtInput.Text.Length;
                _txtInput.Focus();
            };
            _puppetView.Initialize();
            // LoopApp 页：双击一行在「网页」页打开（应用的虚拟域只有壳里的 WebView 认）
            _loopAppView.Initialize(() => _workspace);
            _loopAppView.OpenRequested += slug =>
            {
                ShowBrowser();
                Browser?.LoopAppOpen(slug);
            };
            _sideBtw.ActivatePage = () => ActivatePage?.Invoke() ?? false;
            _targetPane.CloseRequested += CloseTarget;
            _harness.EditTargetRequested += OpenTarget;
            _harness.AssistRequested += page => HarnessAssistRequested?.Invoke(page);
            ApplyDetail();
            UpdateSendButton();
            _ = RefreshRepoActionsAsync();

            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) =>
        {
            ModelCatalog.Changed -= OnCatalogChanged;
            _targetPane.Shutdown();
            _loopClient?.Dispose();
        };
        }

        /// <summary>Harness 配置窗体里点了「让Agent辅助我配置」：宿主另起一个 Loop 会话按那一页逐项改。</summary>
        internal Action<string> HarnessAssistRequested;

        private void OnSummarize(object sender, EventArgs e) => _ = SummarizeTitleAsync();

        /// <summary>
        /// 用当前模型把这段 Loop 对话总结成标题，覆盖页头并记进 loops.json。
        /// 自动触发的时机见下面各调用点（新会话开跑 5 秒那次 markTurns=false，不算已收尾）；
        /// 失败静默、不重试，点标题左侧的重算图标可手动覆盖。
        /// </summary>
        private async Task SummarizeTitleAsync(bool notify = true, bool markTurns = true)
        {
            var turns = _book.Turns;
            if (turns.Count == 0) return;

            string previous = LoopIndex.Title(_workspace, _sessionId);
            try
            {
                string title = await SessionView.AskTitleAsync(_settings(), _workspace, _model,
                    SessionView.TitleInput(turns), CancellationToken.None);
                if (title.Length == 0)
                {
                    AppLog.Warn("生成 Loop 标题：模型没有返回可用正文（轮数 " + turns.Count + "，模型 " + _model + "）");
                    return;
                }
                if (markTurns) _titleTurnCount = UserTurnCount;
                LoopIndex.Rename(_workspace, _sessionId, title);
                _lblTitle.Text = Session.ComposeTitle(_workspace, title);
                TitleChanged?.Invoke(_lblTitle.Text);
                if (notify) Notify(string.Equals(previous, title, StringComparison.Ordinal) ? Localization.T("标题未变化") : Localization.T("标题已更新"), _lblTitle.Text);
            }
            catch (Exception ex)
            {
                AppLog.Warn("生成 Loop 标题失败：" + ex.Message);
                if (notify) Notify(Localization.T("总结失败"), ex.Message, error: true);
            }
        }

        /// <summary>
        /// 新会话开跑 5 秒后补一次标题：先等主会话把首个请求发出去（否则两条会抢同一个模型），
        /// 再用这条需求起标题。失败静默、不重试。
        /// </summary>
        private async Task SummarizeStartTitleAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (IsDisposed || _closed || UserTurnCount == 0) return;
            if (LoopIndex.HasTitle(_workspace, _sessionId)) return;
            await SummarizeTitleAsync(notify: false, markTurns: false);
        }

        /// <summary>
        /// 会话结束时的末次标题：整段对话再总结一次（自动更新只发生在开跑 5 秒与每轮正常结束）；没活可干返回 null。
        /// 只写 loops.json、不碰控件——关窗流程会在后台线程上等它。
        /// </summary>
        internal Task TryFinalizeTitleAsync()
        {
            if (UserTurnCount <= _titleTurnCount) return null;
            string workspace = _workspace, sessionId = _sessionId, model = _model;
            string input = SessionView.TitleInput(_book.Turns);
            var settings = _settings;
            return Task.Run(async () =>
            {
                try
                {
                    string title = await SessionView.AskTitleAsync(settings(), workspace, model, input,
                        CancellationToken.None);
                    if (title.Length > 0) LoopIndex.Rename(workspace, sessionId, title);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("会话结束时总结 Loop 标题失败：" + ex.Message);
                }
            });
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

        /// <summary>平铺视图方块读的排队队列：与输入框上方队列栏同一份。</summary>
        internal OpenLoop.Service.Pending.PendingQueue Queue => _queue;

        /// <summary>平铺视图方块队列栏的「编辑」：与主队列栏同一套放回输入框逻辑。</summary>
        internal void TileQueueEdit(OpenLoop.Service.Pending.PendingItem item) => BeginEdit(item);

        /// <summary>平铺视图方块队列栏的「立即/等本轮」切换。</summary>
        internal void TileQueueImmediate(OpenLoop.Service.Pending.PendingItem item) => OnImmediateRequested(item);

        /// <summary>平铺视图方块队列栏的「删除」。</summary>
        internal void TileQueueRemoved(OpenLoop.Service.Pending.PendingItem item)
        {
            DropEdit(item);
            SyncInterrupt();
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
        /// <param name="noticeKind">宿主注入的提示（如 /complain 的用户反馈）：界面按协作提示折成一行、淡黄底。</param>
        internal void Post(string text, string noticeKind = null)
        {
            if (string.IsNullOrWhiteSpace(text) || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => Post(text, noticeKind))); }
                catch (InvalidOperationException) { }
                return;
            }
            _queue.Add(text, noticeKind ?? "task", auto: noticeKind != null);
            _pending.Sync();
            DrainQueue();
        }

        internal bool Running => _running;
        internal string Title => LoopIndex.Title(_workspace, _sessionId);

        /// <summary>宿主注入：点会话通知回到本 Loop 会话的页面（页签已关掉就重新挂上）。</summary>
        internal Func<bool> ActivatePage { get; set; }

        /// <summary>协作会话的打开方式（宿主注入）：同工作区新起一个 Loop 会话并返回视图。</summary>
        internal Func<CollabTask, LoopView> CollabOpen { get; set; }

        /// <summary>同工作区的其他 Loop 会话（宿主注入）：协作监测与读取用。</summary>
        internal Func<IReadOnlyList<LoopView>> CollabPeers { get; set; }

        private LoopCollabHost _collab;

        private LoopCollabHost Collab() => _collab ??= new LoopCollabHost(this, CollabOpen, CollabPeers);

        /// <summary>
        /// agent 自动插入一条消息（协作监测汇报）：与 Worker 侧 EnqueueAuto 同语义——
        /// 以「立即」进本会话队列并标 Auto，运行中让当前轮在下一个 step 前收尾后先发它；
        /// 队列栏用特殊底色区分；已停止（暂停）时只排队，不自己动起来。
        /// 监测循环在线程池线程上跑，这里负责切回界面线程。
        /// </summary>
        internal void EnqueueAuto(string text, string kind = "collab")
        {
            if (string.IsNullOrWhiteSpace(text) || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => EnqueueAuto(text, kind))); }
                catch (InvalidOperationException) { }
                return;
            }

            _queue.Add(text, kind ?? "collab", PendingMode.NextStep, auto: true);
            _pending.Sync();
            if (_running) SyncInterrupt();
            else DrainQueue();
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
            // 整批都是 agent 自动塞的（协作提示 / 私信 / Puppet 动作）：记下来，界面上折成一行、淡黄底
            _noticeBatch = batch.All(b => b.Auto);
            _noticeKind = _noticeBatch ? batch[0].Tag : null;
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
            // 最后那份写盘在后台：等它落地再返回，否则关窗/删除会把刚写的文件丢掉或复活已删的会话
            _chatWrite.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>崩溃兜底：不管节流间隔，立刻把当前轮次落一次并等写盘落地（异常退出路径调用）。</summary>
        internal void FlushNow()
        {
            if (_book.Turns.Count == 0) return;
            SaveLoopChat();
            _chatWrite.Wait(TimeSpan.FromSeconds(3));
        }

        /// <summary>留言板身份（I-会话id）：壳按它把别的会话的私信投进本会话队列。</summary>
        internal string BoardIdValue => BoardId();

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
            var last = _book.Turns.LastOrDefault(t => t.User != null);
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
                _chatWrite.Wait(TimeSpan.FromSeconds(3));
                ChatStore.Delete(_workspace, _sessionId);
                LoopIndex.Remove(_workspace, _sessionId);
                return;
            }
            _lastCheckpointUtc = DateTime.UtcNow;
            var saveWatch = Environment.TickCount64;
            // UI 线程只取轮次引用（浅快照）：transcript 回读与 JSON 序列化都在后台做。
            // 回读以前在 UI 线程逐轮触发整份文件解析（几十 MB × N 轮），是「保存时整块界面冻住」的来源。
            var turns = _book.Turns.ToList();
            // 步数取「上一轮」与「正在跑的这轮」的较大者，与 Worker 的 Persist 同口径
            int steps = Math.Max(_book.Turns.LastOrDefault()?.ToolStepCount ?? 0,
                _book.Turns.Count > 1 ? _book.Turns[^2].ToolStepCount : 0);
            string workspace = _workspace, sessionId = _sessionId, model = _model, effort = _effort;
            // 索引文件很小，留在 UI 线程写（它没有跨线程锁，别和界面的改名/删除抢读-改-写）
            LoopIndex.Touch(workspace, sessionId, steps);
            _chatWrite = _chatWrite.ContinueWith(_ =>
            {
                // 批量预热：同一份对话文件只解析一遍，后面逐轮取 transcript 就不再各解析一遍
                ChatStore.WarmTranscripts(turns);
                var snapshot = turns.Select(t => (Turn: t, Transcript: ChatStore.Transcript(t))).ToList();
                OpenLoop.Service.Storage.ChatHistory.Write(workspace, sessionId,
                    ChatStore.EntriesFromSnapshot(snapshot, model, effort));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            var saveMs = Environment.TickCount64 - saveWatch;
            if (saveMs > 1000) OpenLoop.Service.Logging.AppLog.Warn($"[UI 卡顿] SaveLoopChat 快照排队 {saveMs} ms（含 _chatWrite 等待）");
        }

        /// <summary>
        /// 流式期间的中途快照：整轮要跑十几二十分钟，只在收尾落盘的话中途崩溃就整段丢失。
        /// 节流间隔与 Worker 同源（账本 limit.checkpoint_interval，开轮时读一次）：
        /// 对话文件可能几十 MB，每一步都写会拖慢界面。
        /// </summary>
        private void CheckpointLoopChat()
        {
            if (_book.Turns.Count == 0) return;
            if (DateTime.UtcNow - _lastCheckpointUtc < _checkpointEvery) return;
            SaveLoopChat();
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
            ApplyTargetLayout();
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
            bool notice = _noticeBatch;
            string noticeKind = _noticeKind;
            _noticeBatch = false;
            _noticeKind = null;
            if (!Directory.Exists(_workspace))
            {
                Notify(Localization.T("项目不可用"), Localization.T("工作区目录不存在：") + _workspace, error: true);
                return;
            }

            // 附件在本机直接复制进工作区：不走 base64，几十 MB 的录屏与 APK 也能发
            var saved = Attachments.SaveFiles(_workspace, _sessionId, pendingFiles,
                MediaModels.AttachmentLimit(_model), out string attachmentError);
            if (saved == null)
            {
                Notify(Localization.T("附件不可用"), attachmentError, error: true);
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
                Notify(Localization.T("缺少配置"), Localization.T("请先在设置里填写接口地址、模型。"), error: true);
                return;
            }

            // 本会话在留言板上的身份（I-会话id）：提示词、技能与 Puppet 工具都靠它识别自己。
            using var identity = AgentIdentity.Begin(BoardId(), "a", _workspace);
            OpenLoop.Service.Web.SessionBoard.Opening(
                agentId: BoardId(), role: "Loop",
                workspace: _workspace, model: model, task: text, remote: true);

            // 先把用户消息挂上并让界面画出来：技能扫描、工具注册、快照（多次 git 子进程）和 trace 都放后台，
            // 否则按下 Enter 到消息出现之间会卡好几秒。
            // 历史组装可能要回读几十 MB 的 transcript：UI 线程只拍轮次引用，读盘挪进下面的后台准备块
            var historyTurns = _book.Turns.ToList();   // 不含本轮：本轮文本随 RunAsync 单独传入

            var turn = new ChatTurn
            {
                User = new ChatMessage("user", text),
                Assistant = new ChatMessage("assistant", ""),
                Notice = notice,
                NoticeKind = noticeKind ?? "",
                Streaming = true,
                ThinkingExpanded = _settings().ThinkingExpandedByDefault
            };
            _book.AddTurn(turn);
            // 用户这条要立刻画出来：首个 delta/step 之前没人刷新正文，失败轮会几分钟看不到自己发的消息
            RefreshChat();
            // 先把用户这条请求落盘（与 Worker 侧开轮即 Persist 同一口径）：整轮要跑十几二十分钟，
            // 中途崩溃时历史里至少留得下这条需求；节流间隔开轮时从账本读一次
            _checkpointEvery = OpenLoop.Service.Storage.ChatHistory.CheckpointIntervalOf(_workspace);
            SaveLoopChat();
            if (fromInput) _txtInput.Clear();
            _queue.Paused = false;   // 用户又发了一条：停止留下的暂停到此为止，本轮收尾后队列照常续发
            SetRunning(true);
            _lblStatus.Text = Localization.T("状态: 运行中");
            // 新会话：等 5 秒让主会话先跑起来，再用这条需求自动起个标题（失败静默、不重试）
            if (UserTurnCount == 1 && !LoopIndex.HasTitle(_workspace, _sessionId)) _ = SummarizeStartTitleAsync();
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
            List<LlmMessage> history;
            try
            {
                (skills, skillProblems, tools, root, checkpoint, reasoner, trace, history) = await Task.Run(() =>
                {
                    // 历史组装可能回读几十 MB 的 transcript：在后台做（UI 线程只拍了轮次引用 historyTurns）
                    var history = ChatStore.History(historyTurns);
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
                            .Concat(OpenLoop.Service.Web.PuppetTools.For(
                                FeatureCatalog.PolicyOf(_workspace).PuppetResultLimit))
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
                        settings.WebFetchMode, FeatureCatalog.WebFetchesOf(_workspace));
                    var registry = new ToolRegistry(loopTools.Tools.Concat(
                        [new OpenLoop.Core.Tools.Builtin.AskUserTool(_workspace, () => SessionTarget?.Invoke() ?? ("", -1))]));
                    string snapshot = Snapshots.Create(writeScope.Root, "loop");
                    if (snapshot != null) AppLog.Info("snapshot " + snapshot);
                    var created = Reasoners.Create(settings, model, _effort, writeScope.Root, approve);
                    var writer = TraceWriter.Start(_workspace, "loop", text, model);
                    return (activeSkills, problems, registry, writeScope.Root, snapshot, created, writer, history);
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop 准备失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = FailedText(ex.Message);
                _cts?.Dispose();
                _cts = null;
                // 准备阶段就失败也算这一轮结束：留言板上不能一直挂着「运行中」
                UnhookSlow();
                OpenLoop.Service.Web.SessionBoard.Finished(BoardId(), remote: true);
                SetRunning(false);
                _lblStatus.Text = Localization.T("状态: error");
                SetState(SessionState.Interrupted);
                RefreshChat();
                Notify(Localization.T("Loop 执行失败"), ex.Message, error: true);
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

            // 长命令交代：Shell/技能脚本超 5 秒时把「仍在执行」写进同一个状态行
            void ShowSlow(string s) => RunOnUi(() =>
            {
                turn.ToolStatusText = string.IsNullOrEmpty(s) ? null : s;
                RefreshChat();
            });
            OpenLoop.Core.Tools.Builtin.ProcessRunner.SlowCommand += ShowSlow;
            void UnhookSlow() => OpenLoop.Core.Tools.Builtin.ProcessRunner.SlowCommand -= ShowSlow;

            // 宿主接线（与 Worker 共用 ReasonerWiring）：即时通报 → 状态行；原生工具 → trace + 步骤流。
            // 此前 Loop 漏接这两条，免费模型长等待时界面一片空白（2026-11-25 用户反馈）。
            ReasonerWiring.Attach(reasoner, new ReasonerWiring.Host
            {
                Notice = s => RunOnUi(() =>
                {
                    // 等待审批/等待响应等即时状态写进工具状态行；null = 清除
                    turn.ToolStatusText = string.IsNullOrEmpty(s) ? null : s;
                    RefreshChat();
                }),
                NativeTool = tool =>
                {
                    trace.NativeTool(tool);
                    var step = ReasonerWiring.NativeStep(tool);
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
                        RefreshChat();
                    });
                },
                ShouldStop = () => ct.IsCancellationRequested || _interrupt
            });

            // 原生工具密集时 onStep 可能每秒十几次：Render/SyncPhases/RefreshChat 的合计量随轮次线性涨，
            // 全部直投 UI 会把队列灌满（11-25 冻结的合成因素之一）。数据实时累计，界面按 200ms 合并刷新。
            long lastStepUi = 0;
            void ApplyStepUi(AgentStepRecord s)
            {
                turn.ToolStepCount++;
                turn.ToolTotalMs += s.DurationMs;
                string line = s.DisplayLine;
                turn.ToolCallLines = string.IsNullOrEmpty(turn.ToolCallLines)
                    ? line
                    : turn.ToolCallLines + "\n" + line;
                turn.ToolTraceText = turn.ToolCallLines;
                lock (timeline) timeline.Tool(line);
                lock (timeline) turn.TimelineText = timeline.Render();
                lock (timeline) SyncPhases(turn, timeline);
                turn.ToolsFinished();
            }
            var loop = new AgentLoop(tools, reasoner, policy,
                onStep: step =>
                {
                    ApplyStepUi(step);
                    long now = Environment.TickCount64;
                    bool due = now - Interlocked.Read(ref lastStepUi) >= 200;
                    if (due) Interlocked.Exchange(ref lastStepUi, now);
                    if (due) RunOnUi(() =>
                    {
                        RefreshChat();
                        CheckpointLoopChat();
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
            // @ 引用的会话上下文只进模型输入：界面上用户那条消息保持原样（@标题）
            string context = MentionPicker.Context(text, MentionTargets(), SessionView.MentionTurns(_workspace));
            string prompt = context.Length == 0 ? text : text + "\n\n" + context;

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, prompt, ct, ModelChoices.ContextLimit(model, settings),
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
                _lblStatus.Text = Localization.T("状态: ") + string.Format(Localization.T("StatusStepsCost"), outcome, result.Steps.Count, _lastCost);
                SetState(outcome == "ok" ? SessionState.Done : SessionState.Interrupted);
                ApplyDetail();
            }
            catch (OperationCanceledException)
            {
                outcome = "stopped";
                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                if (string.IsNullOrEmpty(turn.AssistantText)) turn.AssistantText = Localization.T("（已停止）");
                trace.Finish("stopped", turn.AssistantText);
                _lblStatus.Text = Localization.T("状态: stopped");
                SetState(SessionState.Interrupted);
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop 会话失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = FailedText(ex.Message);
                trace.Finish("error", turn.AssistantText);
                _lblStatus.Text = Localization.T("状态: error");
                SetState(SessionState.Interrupted);
                Notify(Localization.T("Loop 执行失败"), ex.Message, error: true);
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
                // 这一轮收尾：留言板上改回空闲，同工作区后开的会话不会再把它当成「正在跑的新会话」播报。
                // 队列里还有要发的（马上开下一轮）就不归零：两边的写都是后台任务，顺序会反过来。
                UnhookSlow();
                if (outcome == "error" || IsDisposed || _queue.Count == 0)
                OpenLoop.Service.Web.SessionBoard.Finished(BoardId(), remote: true);
                RefreshChat();
                // 正常收尾：长正文从头读（流式期间已跟随到底），避免用户打开就落在末尾
                if (outcome == "ok" && !IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => _chat.ScrollToAnswerStart(turn)));
                SaveLoopChat();
                // 这一轮可能改过源码/配置：仓库按钮跟着刷新
                _ = RefreshRepoActionsAsync();
                // 正常结束：主题可能执行中变了，按整段对话立刻更新一次标题（与开跑 5 秒那次配对；失败静默）
                if (!IsDisposed && IsHandleCreated && outcome == "ok" && UserTurnCount > _titleTurnCount)
                    _ = SummarizeTitleAsync(notify: false);
                // 排队文本：当前轮收尾后再发，避免重入；失败轮不续发，等新的输入激活会话
                // 正常收尾就是轮后：上一轮「停止」留下的暂停不该再挡住队列；失败轮照旧等新输入
                // 页面交回数据了：这一轮就是那个 LoopApp 的任务，收尾时把网页页收起来
                CollapseBrowserIfDone();
                if (outcome is "ok" or "budget") _queue.Paused = false;
                if (outcome != "error" && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(DrainQueue));
            }
        }

        /// <summary>失败轮的正文：只写失败原因，跑了几步看思维链区。</summary>
        internal static string FailedText(string error)
            => OpenLoop.Core.Messages.ServerNotice.FailedPrefix + error;

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
            if (overflow) Notify(Localization.T("附件太多"), string.Format(Localization.T("TooManyAttachmentsBody"), limit), error: true);
            UpdateAttachments();
        }

        private void OnAttachMenu(object sender, AntdUI.ObjectNEventArgs e)
        {
            string key = _btnAttach.SelectedValue as string;
            _btnAttach.SelectedValue = null;
            if (string.IsNullOrEmpty(key)) return;
            if (key == AttachmentsUi.AddAttachmentTag) { OnAttachClick(sender, EventArgs.Empty); return; }
            AttachmentsUi.AskRemove(FindForm(), _attachments, key);
            UpdateAttachments();
        }

        private void UpdateAttachments()
        {
            AttachmentsUi.FillMenu(_btnAttach, _attachments);
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
                Notify(Localization.T("附件太多"), string.Format(Localization.T("TooManyAttachmentsBody"), limit), error: true);
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
            bool opening = _upperSplit.Panel2Collapsed;
            if (opening)
            {
                // 侧栏本体先长到目标宽度：展开瞬间 Panel2 就接近最终尺寸，SplitterDistance 只需微调，
                // resize 的重画面积最小（折叠态下 Panel2 是一个没有内容的 Dock=Fill 容器，改宽不闪）。
                if (_side.Width < Dip(360)) _side.Width = Dip(360);
                // 再预摆分隔条（挂起重绘一次到位，见 ApplySplit）：展开即最终布局，无中间帧。
                SessionView.ApplySplit(_upperSplit, vertical: true,
                    panel1Min: Dip(400), panel2Min: Dip(360), targetPanel2: Dip(400));
                // 翻开前先把前台页准备好：详情页字段就绪、git 状态的异步查询现在发起——
                // 等展开后才做这些，用户会先看到一块没有内容/按钮全灰的侧栏，再逐项刷新（绘制慢的观感）。
                ApplyDetail();
                _ = RefreshRepoActionsAsync();
            }
            _upperSplit.Panel2Collapsed = !opening;
            _btnPanel.IconSvg = UiIcons.Of(
                _upperSplit.Panel2Collapsed ? UiIcons.PanelOpen : UiIcons.PanelClose, UiStyle.Text);
            if (opening) ApplyLayout();
            // 侧栏整体收起：旁路页即便仍停在那一项也看不到，跟着停渲染
            _sideBtw.SetActive(!_upperSplit.Panel2Collapsed && _sideTabs.SelectIndex == 1);
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
            _sideBtw.SetActive(index == 1);   // 没摆在前台的旁路页不渲染
            _harness.Visible = index == HarnessPage;
            if (index == HarnessPage) _harness.Reload();
            if (_browser != null)
            {
                _browser.Visible = index == BrowserPage;
                if (index == BrowserPage)
                {
                    _browser.EnsureLoaded();
                    _browser.MarkShown();
                }
            }
            _fileView.Visible = index >= 0 && index == _filePage;
            _puppetView.Visible = index == PuppetPage;
            if (index == PuppetPage) _puppetView.Reload();
            _loopAppView.Visible = index == LoopAppPage;
            if (index == LoopAppPage) _loopAppView.Reload();
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
                if (!Browser.Navigate(e.Target, out string urlError)) Notify(Localization.T("无法打开网址"), urlError, error: true);
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
                _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("文件") });
                _filePage = _sideTabs.Items.Count - 1;
            }
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(_filePage);
        }

        private void ApplyDetail()
        {
            UiTiming.Measure("ApplyDetail", () => ApplyDetailCore());
        }

        private void ApplyDetailCore()
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

        /// <summary>侧栏按钮组水平居中：FlowLayoutPanel 只按 Margin 摆放，宽度变化后重算左右留白。</summary>
        private void OnFlowLayout(object sender, LayoutEventArgs e)
            => FlowButtons.Center(sender as FlowLayoutPanel);

        /// <summary>详情页的「打开工作区」：用资源管理器打开本 Loop 会话的工作区。</summary>
        private void OnOpenWorkspace(object sender, EventArgs e)
            => ChatPathActions.OpenFolder(this, _workspace);

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void ApplyLayout()
        {
            if (IsDisposed || _upperSplit.Width < Dip(700)) return;
            if (_upperSplit.Panel2Collapsed) return;
            SessionView.ApplySplit(_upperSplit, vertical: true,
                panel1Min: Dip(400), panel2Min: Dip(360), targetPanel2: Dip(400));
            _layoutReady = true;
        }

        /// <summary>
        /// Prompt / AGENTS.md 实际生效的工作区：与运行期同一口径（源码根，见下方 BuildSystemPrompt 的调用）。
        /// 不能按会话工作区找：Loop 从 /complain 起时会话工作区是别人的项目，部署态下安装根里
        /// 也没有仓库那份 AGENTS.md（2026-11-24：Harness 页「编辑 AGENTS.md」打开后一片空白）。
        /// </summary>
        private string InstructionRoot
        {
            get
            {
                try
                {
                    string root = OpenLoop.Service.Source.LoopScope.SourceRoot;
                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return root;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("解析源码根失败：" + ex.Message);
                }
                return _workspace;
            }
        }

        /// <summary>目标面板（TCF 的 Puppet 动作按会话 id 转发到这里）。</summary>
        internal TargetPane TargetPane => _targetPane;

        /// <summary>TargetCentricSession：左栏挂可修订目标（与 Worker 侧同一套 TargetPane 语义）。</summary>
        internal void OpenTarget(string kind)
        {
            string workspace = _workspace;
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            // 直配模式编辑运行期真正注入的那份（源码根）；A/B 评测模式改的是本工作区的候选包
            // （播种来源仍是源码根那份），考核通过后才覆盖正式文件。
            // 「实际」页去会话工作区找 trace：Loop 的 trace 一直写在会话工作区。
            string seed = InstructionRoot;
            string target = HarnessMode.IsEval(workspace) ? workspace : seed;
            if (!Directory.Exists(target))
            {
                Notify("请选择工作目录", "工作目录不存在：" + target, error: true);
                return;
            }

            TargetSpec spec;
            try
            {
                spec = kind == PromptCatalog.AgentsKind
                    ? TargetSpec.Agents(target, seed)
                    : TargetSpec.Prompt(target, seed, actualWorkspace: workspace);
            }
            catch (Exception ex)
            {
                Notify("打不开目标", ex.Message, error: true);
                return;
            }

            // 启用提示的动作清单从 manifest 生成（不硬编码）；缓存没热就先用兜底句
            _ = PuppetActions.LoadAsync();
            if (!_targetPane.Bind(spec, SessionId, _ => SyncTarget(kind, target))) return;
            _targetSplit.Panel1Collapsed = false;
            ApplyTargetLayout();
        }

        /// <summary>关闭只是折叠：内容与脏标志留在面板里（TCF 的「关闭=隐藏」）。</summary>
        private void CloseTarget()
        {
            _targetPane.Collapse();
            _targetSplit.Panel1Collapsed = true;
        }

        private void SyncTarget(string kind, string workspace)
        {
            try
            {
                // A/B 评测模式下改的是候选包：未过考核不登记、不激活
                if (HarnessMode.IsEval(workspace)) return;
                var ledger = Ledger.Open(workspace);
                if (kind == PromptCatalog.AgentsKind) PromptCatalog.SyncAgents(ledger, workspace);
                else PromptCatalog.Sync(ledger, workspace);
            }
            catch (Exception ex)
            {
                AppLog.Warn("登记 Prompt 类对象失败：" + ex.Message);
            }
        }

        private void ApplyTargetLayout()
        {
            if (_targetSplit.Panel1Collapsed || _targetSplit.Width <= 0) return;
            int total = _targetSplit.Width;
            int splitter = _targetSplit.SplitterWidth;
            // 两侧都要留够最小宽度，否则 SplitterDistance 赋值会抛异常
            int max = total - splitter - Math.Max(_targetSplit.Panel2MinSize, Dip(360));
            int distance = Math.Min(Dip(420), max);
            if (distance >= _targetSplit.Panel1MinSize)
                _targetSplit.SplitterDistance = distance;
        }

        /// <summary>目标会话的每次输入都带上启用提示：唯一编辑目标 + 动作 + 保存语义。</summary>
        private string TargetDirective() => _targetPane.Prompt("self");

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
                || SessionView.IsCommand(text, "/report") || SessionView.IsCommand(text, "/fork")
                || SessionView.IsCommand(text, "/review") || SessionView.IsCommand(text, "/optimize")) return;

            //「/技能名 请求…」展开成让 Worker 先读技能再执行的任务，后面按普通输入发送/排队
            text = SlashCommands.SkillTask(text, _skillCommands) ?? text;

            // 目标会话：每次输入都带上唯一编辑目标
            if (!_targetSplit.Panel1Collapsed && text.Length > 0)
                text = TargetDirective() + "\n\n" + text;

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

        /// <summary>展开右侧栏并切到「Puppet」页（Action 清单）。</summary>
        public void ShowPuppetActions()
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(PuppetPage);
            _puppetView.Reload();
        }

        /// <summary>
        /// /puppet &lt;action&gt; &lt;参数…&gt;：作为「协作提示」插进本 Loop 会话队列（淡黄底），
        /// 由本会话的 agent 用 Puppet 工具执行并回报结果；Loop 会话就在壳进程里，target=self。
        /// </summary>
        private async Task RunPuppetActionAsync(string request)
        {
            try
            {
                var sheet = await PuppetActions.LoadAsync();
                EnqueueAuto(PuppetActions.NoticeText(sheet, request, "self"), "puppet");
            }
            catch (Exception ex)
            {
                AppLog.Error("执行 Puppet action 失败", ex);
                Notify(Localization.T("未执行"), ex.Message, error: true);
            }
        }

        /// <summary>反馈里的会话名：标题为空时用会话 id。</summary>
        private string FeedbackTitle()
        {
            string title = LoopIndex.Title(_workspace, _sessionId);
            return string.IsNullOrWhiteSpace(title) ? _sessionId : title;
        }

        /// <summary>识别 /btw、/fork；返回 true 表示这条输入已被命令消费。</summary>
        private bool TryRunCommand(string text)
        {
            if (SessionView.IsCommand(text, "/btw"))
            {
                string request = text[4..].Trim();
                if (request.Length == 0)
                {
                    Notify(Localization.T("缺少请求"), Localization.T("/btw 后面要写具体请求，例如：/btw 顺便看看还有哪些调用点。"), error: true);
                    return false;
                }
                RunSideWork(request);
                return true;
            }

            if (SessionView.IsCommand(text, "/complain"))
            {
                string title = FeedbackTitle();
                string complaint = text[9..].Trim();
                if (complaint.Length == 0)
                {
                    Notify(Localization.T("缺少内容"), Localization.T("/complain 后面要写具体不满，例如：/complain 它没看证据就下了结论。"), error: true);
                    return false;
                }
                ComplaintRequested?.Invoke(SessionView.ComposeComplaint(
                    "Loop", title, complaint, ChatHistory.PathFor(_workspace, _sessionId),
                    _workspace));
                return true;
            }

            if (SessionView.IsCommand(text, "/report"))
            {
                string note = text[7..].Trim();
                if (note.Length == 0)
                {
                    Notify(Localization.T("缺少内容"), Localization.T("/report 后面要写观察，例如：/report harness 思考链闪现太快。"), error: true);
                    return false;
                }
                bool ok = FindingCommand.TryRecord(_workspace, note, out string message);
                Notify(ok ? Localization.T("已记录") : Localization.T("未记录"), message, error: !ok);
                return true;
            }

            if (SessionView.IsCommand(text, "/sysprompt"))
            {
                OpenTarget(PromptCatalog.SystemKind);
                return true;
            }

            if (SessionView.IsCommand(text, "/agentsmd"))
            {
                OpenTarget(PromptCatalog.AgentsKind);
                return true;
            }

            if (SessionView.IsCommand(text, "/puppet"))
            {
                string request = text[7..].Trim();
                ShowPuppetActions();
                if (request.Length > 0) _ = RunPuppetActionAsync(request);
                return true;
            }

            if (SessionView.IsCommand(text, "/compact"))
            {
                if (_running)
                {
                    Notify(Localization.T("正在运行"), Localization.T("请先停止当前任务，再压缩上下文。"), error: true);
                    return true;
                }
                if (_compacting)
                {
                    Notify(Localization.T("正在压缩"), Localization.T("上一次压缩还没结束，请稍候。"), error: true);
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
                    Notify(Localization.T("缺少请求"), Localization.T("/fork 后面要写具体请求，例如：/fork 继续把第二项改完。"), error: true);
                    return false;
                }
                if (_running)
                {
                    Notify(Localization.T("正在运行"), Localization.T("请先停止当前任务，再分叉对话。"), error: true);
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

            bool review = SessionView.IsCommand(text, "/review");
            if (review || SessionView.IsCommand(text, "/optimize"))
            {
                string prompt = ComposeReview(FeedbackTitle(),
                    ChatHistory.PathFor(_workspace, _sessionId), _workspace, !review,
                    text[(review ? 7 : 9)..].Trim());
                // /review 新开一个会话只做盘点并切过去等确认；/optimize 留在本会话、盘点完直接改
                if (review) NewSessionRequested?.Invoke(this, prompt);
                else Post(prompt);
                return true;
            }

            return false;
        }

        /// <summary>
        /// /review、/optimize 的任务：先盘点来源会话里的问题再动手。清单只写问题与需求
        /// （现象/复现条件/影响面/证据），解法留给确认之后或实施时，免得还没对齐就大改。
        /// </summary>
        private static string ComposeReview(string title, string historyPath, string workspace,
            bool implement, string hint)
        {
            var sb = new StringBuilder();
            sb.Append("[审查] 先盘点 Loop 会话「").Append(title).AppendLine("」里的问题与需求，再决定改动。");
            sb.Append("来源会话记录（每轮正文、工具调用与回复都在里面，先用 ReadSession 读内存会话，读不到再读文件）：")
                .AppendLine(historyPath);
            if (!string.IsNullOrWhiteSpace(workspace))
                sb.Append("来源会话工作区：").AppendLine(workspace);
            if (!string.IsNullOrWhiteSpace(hint))
                sb.Append("用户补充的关注点：").AppendLine(hint);
            sb.AppendLine("汇总两类内容：");
            sb.AppendLine("- 避坑经验：来源会话里走过的弯路、被用户纠正的地方、重复出现的失败模式。");
            sb.AppendLine("- OpenLoop 各组件的优化点：Core/Service、Worker（A）、Loop（I）、壳（App）、Web 壳与可迭代组件。");
            sb.AppendLine("每条写清：现象、复现条件（哪一轮、哪个工具调用）、影响面、证据位置（文件:行号或 trace 路径）。没有证据的、凑数的不提。");
            sb.AppendLine(implement
                ? "只写问题与需求，不写解法；盘点完直接按优先级实施，改完跑构建与测试，最后交代改了什么、验证结果。"
                : "这一轮只输出问题与需求清单，不要写解法也不要改代码；等我确认后再实施。");
            return sb.ToString().TrimEnd();
        }

        /// <summary>下一轮 SendAsync 是否算「协作提示」（agent 自动塞进队列的整批消息）：界面折成一行、淡黄底。</summary>
        private bool _noticeBatch;
        private string _noticeKind;

        private bool _compacting;

        /// <summary>/compact：把较早的消息换成摘要并挂到最近一轮的 transcript；下一轮起按压缩后的历史发。</summary>
        private async Task CompactContextAsync()
        {
            if (_compacting) return;
            _compacting = true;
            try
            {
                await CompactContextCoreAsync();
            }
            finally
            {
                _compacting = false;
            }
        }

        /// <summary>宿主消息（不是模型回复）：写进对话流让用户看见壳在做什么；不进模型上下文。</summary>
        private ChatTurn ShowHostLine(string text)
        {
            var turn = new ChatTurn { Assistant = new ChatMessage("assistant", text) };
            _book.AddTurn(turn);
            RefreshChat();
            _chat.ScrollToEnd();
            return turn;
        }

        private async Task CompactContextCoreAsync()
        {
            var history = BuildHistory();
            if (history.Count == 0)
            {
                Notify(Localization.T("没有可压缩的上下文"), Localization.T("当前会话还没有历史。"), error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            if (model.Length == 0)
            {
                Notify(Localization.T("缺少配置"), Localization.T("请先选择模型。"), error: true);
                return;
            }

            // 压缩要调一次模型（最长 2 分钟）：先在对话里留一条宿主消息，别让用户以为命令没生效
            var host = ShowHostLine(Localization.T("正在压缩上下文…（最长约 2 分钟）"));
            string status = _lblStatus.Text;
            _lblStatus.Text = Localization.T("状态: 压缩上下文…");
            try
            {
                var settings = _settings();
                int before = AgentContext.EstimateTokens(history);
                var policy = FeatureCatalog.PolicyOf(_workspace);
                var compacted = await Task.Run(async () =>
                {
                    var reasoner = Reasoners.Create(settings, model, _effort, _workspace, AskApproval);
                    return await CompressionStep.CompressAsync(history, reasoner, CancellationToken.None,
                        AgentContext.TailTokenBudget(ModelChoices.ContextLimit(model, settings),
                            policy.CompressKeepTokens), policy.CompressKeepMessages);
                });
                if (compacted == null)
                {
                    host.AssistantText = Localization.T("未压缩") + "：" + Localization.T("上下文太短，没有可压缩的中间部分。");
                    RefreshChat();
                    Notify(Localization.T("未压缩"), Localization.T("上下文太短，没有可压缩的中间部分。"), error: true);
                    return;
                }

                // 挂到最后一条真实轮次：宿主行不进模型上下文，也不参与 transcript 回读对号（挂它重启后摘要会丢）
                var anchor = _book.Turns.LastOrDefault(t => t.User != null);
                if (anchor != null) ChatStore.AttachTranscript(anchor, compacted);
                int after = AgentContext.EstimateTokens(compacted);
                _contextTokens = after;
                host.AssistantText = Localization.T("已压缩上下文") + $"：{before:N0} → {after:N0} tokens";
                RefreshChat();
                SaveLoopChat();
                Notify(Localization.T("已压缩上下文"), $"{before:N0} → {after:N0} tokens");
            }
            catch (Exception ex)
            {
                AppLog.Error("压缩上下文失败", ex);
                host.AssistantText = Localization.T("压缩失败") + "：" + ex.Message;
                RefreshChat();
                Notify(Localization.T("压缩失败"), ex.Message, error: true);
            }
            finally
            {
                _lblStatus.Text = status;
                ApplyDetail();
            }
        }

        private static readonly (string Command, string Description)[] Commands =
        {
            ("/btw", Localization.T("在右侧栏用当前上下文跑一个只读旁路任务")),
            ("/complain", Localization.T("把对 Loop 表现的不满发给 Loop（不附上下文，Loop 按需读源会话）")),
            ("/report", Localization.T("把一条值得改进的观察记入 findings（不立即处理，演进时分诊）")),
            ("/sysprompt", Localization.T("打开目标会话：左栏显示系统提示词模板，用聊天让 agent 改")),
            ("/agentsmd", Localization.T("打开目标会话：左栏显示 AGENTS.md，用聊天让 agent 改")),
            ("/compact", Localization.T("把较早的对话压缩成摘要，省下上下文")),
            ("/fork", Localization.T("复制当前上下文开一个新的 Loop 会话（可继续对话）并跳转")),
            ("/puppet", Localization.T("打开右侧 Puppet Action 清单；/puppet <action> <参数> 直接执行")),
            ("/review", Localization.T("盘点来源会话的避坑经验与各组件优化点，新开 Loop 会话等确认后实施")),
            ("/optimize", Localization.T("盘点同上并直接实施（不切会话）")),
            ("/new", Localization.T("开一个新的 Loop 会话，沿用当前的模型与强度"))
        };

        private readonly List<(string Command, string Description)> _cmdMatches = [];
        private IReadOnlyList<(string Command, string Description)> _skillCommands = [];
        private DateTime _skillsAt;
        /// <summary>浮层当前列的是 @ 会话（否则是 / 命令）：选中后替换 @token 而不是整段输入。</summary>
        private bool _mentionMode;

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

        /// <summary>输入以 / 开头且还没写参数时列命令；末尾是 @token 时列同工作区其他会话。</summary>
        private void UpdateCommandPopup()
        {
            string text = _txtInput.Text ?? "";
            bool want = text.StartsWith('/') && !text.Contains(' ') && !text.Contains('\n');
            _cmdMatches.Clear();
            _mentionMode = false;
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
            else if (MentionPicker.Detect(text) is { } mention)
            {
                _mentionMode = true;
                _cmdMatches.AddRange(MentionPicker.Items(MentionTargets(), mention.Query));
            }

            if (_cmdMatches.Count == 0)
            {
                _mentionMode = false;
                _cmdPopup.Close();
                return;
            }

            _cmdPopup.Open(_cmdMatches);
            _cmdPopup.PlaceAbove(_inputArea);
        }

        /// <summary>@ 可引用的会话：同工作区的 Worker 会话与其他 Loop 会话（都不含自己）。</summary>
        private List<MentionTarget> MentionTargets()
        {
            var targets = new List<MentionTarget>();
            foreach (var session in Sessions?.Invoke() ?? [])
            {
                if (session == null || !Paths.Same(session.Workspace, _workspace)) continue;
                targets.Add(new MentionTarget
                {
                    Title = session.Title,
                    Id = session.Id,
                    Turns = session.Book.Turns.Count,
                    State = session.State,
                    Read = () => session.Book.Turns.Count > 0
                        ? session.Book.Turns
                        : ChatStore.Load(session.Workspace, session.Id)
                });
            }
            foreach (var peer in CollabPeers?.Invoke() ?? [])
            {
                if (peer == null || ReferenceEquals(peer, this) || !Paths.Same(peer.Workspace, _workspace)) continue;
                targets.Add(new MentionTarget
                {
                    Title = peer.Title,
                    Id = peer.SessionId,
                    Turns = peer.Turns.Count,
                    State = peer.State,
                    Read = () => peer.Turns
                });
            }
            return targets;
        }

        /// <summary>选中命令/会话：命令整段替换，@ 只替换末尾那个 token。</summary>
        private void OnCommandAccepted(string command)
        {
            string text = _mentionMode ? _txtInput.Text ?? "" : "";
            if (_mentionMode && MentionPicker.Detect(text) is { } mention) text = text[..mention.Start];
            _mentionMode = false;
            _txtInput.Text = text + command + " ";
            _txtInput.SelectionStart = _txtInput.Text.Length;
            _txtInput.Focus();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (!_cmdPopup.IsOpen) return;
            if (e.KeyCode == Keys.Down) { _cmdPopup.MoveSelection(1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Up) { _cmdPopup.MoveSelection(-1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Escape) { _cmdPopup.Close(); e.SuppressKeyPress = true; return; }
        }

        private void OnSendClick(object sender, EventArgs e)
        {
            // 联想浮层开着时点发送等同回车：先选中，不把「@…」「/…」当正文发出去
            if (_cmdPopup.IsOpen && !_cmdPopup.IsExact(_txtInput.Text)) { _cmdPopup.Accept(); return; }
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
                Notify(Localization.T("正在运行"), Localization.T("请先停止当前任务，再撤回对话。"), error: true);
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
                    Localization.T("撤回对话"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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

            string note = restoreFailed ? Localization.T("RevertRestoreFailed")
                : hasSnapshot ? Localization.T("，工作区已还原（.gitignore 除外）")
                : Localization.T("（该轮没有可用快照，未还原文件）");
            Notify(Localization.T("撤回完成"), string.Format(Localization.T("RevertedTurns"), remove.Count) + note, restoreFailed);
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
                Notify(Localization.T("未选择对话"), Localization.T("先打开「选择」并勾选要处理的对话。"), error: true);
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
            Notify(Localization.T("已复制"), string.Format(Localization.T("CopiedTurns"), turns.Count));
        }

        private void TransferTurns(IReadOnlyList<ChatTurn> turns)
        {
            if (TransferToWorker == null || !TransferToWorker(turns))
            {
                Notify(Localization.T("无法转交"), Localization.T("宿主没有提供 Worker 入口。"), error: true);
                return;
            }
            _chat.SelectTurns(_ => false);
        }

        /// <summary>真删历史条目（不是撤回）：删完这一轮之后的 KV cache 前缀会断开，后续要重算。</summary>
        private void DeleteTurns(IReadOnlyList<ChatTurn> turns)
        {
            if (_running)
            {
                Notify(Localization.T("正在运行"), Localization.T("请先停止当前任务，再删除对话。"), error: true);
                return;
            }
            if (MessageBox.Show(this,
                    $"删除选中的 {turns.Count} 组对话？\n"
                    + "会真的从历史里删掉，不能恢复；之后的 KV cache 前缀也会从这里断开、后面要重算。",
                    Localization.T("删除对话"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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
            _lblStatus.Text = push ? Localization.T("状态: 正在提交并推送…") : Localization.T("状态: 正在提交…");
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
                    _lblStatus.Text = Localization.T("状态: 没有改动");
                    Notify(Localization.T("提交"), Localization.T("工作树是干净的，没有可提交的改动。"));
                    return;
                }

                var tokens = repos.ToDictionary(r => r.Id, r => r.Token, StringComparer.OrdinalIgnoreCase);
                var result = await client.GitCommitAsync(
                    repos.Select(r => r.Id), null, push, tokens, force: true);
                var failed = (result?.Repos ?? []).Where(r => !r.Ok).ToList();
                string summary = failed.Count == 0
                    ? (push ? "已提交并推送" : "已提交")
                    : "提交未全部成功：" + string.Join("；", failed.Select(r => $"{r.Id}: {r.Error}"));
                _lblStatus.Text = Localization.T("状态: ") + summary;
                Notify(failed.Count == 0 ? Localization.T("提交") : Localization.T("提交失败"),
                    summary + (skipped.Length == 0 ? "" : "\n跳过 " + skipped), failed.Count > 0);
            }
            catch (Exception ex)
            {
                AppLog.Error("提交失败", ex);
                _lblStatus.Text = Localization.T("状态: 提交失败");
                Notify(Localization.T("提交失败"), ex.Message, error: true);
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
                _lblStatus.Text = Localization.T("状态: 当前已经是上一轮之前的版本");
                return;
            }

            string where = string.Join("、", targets.Select(t => $"{t.Key.ToUpperInvariant()} {t.Value.Version}"));
            if (MessageBox.Show(this, string.Format(Localization.T("ConfirmRevert"), where), Localization.T("退回上一版本"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _repoBusy = true;
            SyncRepoButtons();
            _lblStatus.Text = Localization.T("状态: 正在退回…");
            try
            {
                var steps = await IterationRunner.RollbackAsync(targets, _ => { });
                if (steps.Count > 0 && steps.All(s => s.Ok))
                {
                    VersionState.Forget(targets.Keys);
                    _lblStatus.Text = Localization.T("状态: 已退回上一版本（壳未变）");
                    Notify(Localization.T("已退回上一版本"), where + Localization.T("（壳未变）"));
                }
                else
                {
                    _lblStatus.Text = Localization.T("状态: 退回未全部成功");
                    Notify(Localization.T("退回失败"), Localization.T("worker 忙就等这一轮跑完再试。"), error: true);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("退回失败", ex);
                _lblStatus.Text = Localization.T("状态: 退回失败");
                Notify(Localization.T("退回失败"), ex.Message, error: true);
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
            _lblStatus.Text = Localization.T("状态: 迭代中…");
            try
            {
                string summary = await Iterate();
                _lblStatus.Text = Localization.T("状态: ") + summary;
                Notify(Localization.T("迭代"), summary);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = Localization.T("状态: 迭代失败");
                Notify(Localization.T("迭代失败"), ex.Message, error: true);
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
                    if (overflow) Notify(Localization.T("附件太多"), string.Format(Localization.T("TooManyAttachmentsBody"), pasteLimit), error: true);
                    UpdateAttachments();
                    return;
                }
            }
            // Tab 与回车一样确认联想（/ 命令、@ 会话）；没有联想时照旧切到下一个控件
            if ((e.KeyData & Keys.KeyCode) == Keys.Tab)
            {
                e.Result = false;
                if (_cmdPopup.IsOpen) _cmdPopup.Accept();
                else FindForm()?.SelectNextControl(_txtInput, !e.KeyData.HasFlag(Keys.Shift), true, true, true);
                return;
            }
            bool shift = e.KeyData == (Keys.Shift | Keys.Enter);
            bool ctrl = e.KeyData == (Keys.Control | Keys.Enter);
            if (!shift && e.KeyData != Keys.Enter && !ctrl) return;
            e.Result = false;
            if (shift) { InsertSoftNewline(); return; }
            // 只挂附件、不写字也是合法消息：这里必须和 Submit 的判据一致，否则回车被静默吞掉
            if (_editing == null && _txtInput.Text.Trim().Length == 0 && _attachments.Count == 0) return;
            if (_cmdPopup.IsOpen && !_cmdPopup.IsExact(_txtInput.Text)) { _cmdPopup.Accept(); return; }
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
            _harness.SetBusy(running);   // Harness 页的演进按钮跟着会话忙闲走
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
                var last = _book.Turns.LastOrDefault(t => t.User != null);
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
                if (!IsDisposed) UiTiming.Measure("RefreshChat(RaiseTextChanged)", () => _book.RaiseTextChanged());
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
            return MessageBox.Show(FindForm(), message, Localization.T("继续执行？"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
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
            // 与 Worker 会话同一口径：留久一点，并给一键回到本会话的入口
            var config = new AntdUI.Notification.Config(form, title, text,
                error ? AntdUI.TType.Error : AntdUI.TType.Info, AntdUI.TAlignFrom.TR,
                null, error ? 12 : 10);
            if (ActivatePage != null)
                config.Link = new AntdUI.Notification.ConfigLink("打开会话", () =>
                {
                    ActivatePage();
                    return false;
                });
            AntdUI.Notification.open(config);
        }
    }
}
