using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Trace;
using OpenLoop.Service.Config;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Media;
using OpenLoop.Service.Pending;
using OpenLoop.Service.Plugins;
using OpenLoop.Service.Storage;
using OpenLoop.Service.Worker;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>
    /// 单个会话的界面与执行逻辑（对话、输入框、模型选择、Loop 侧栏）。
    /// 一个实例对应一个 <see cref="Session"/>；页面被关闭时视图仍然存活。
    /// </summary>
    public partial class SessionView : UserControl
    {
        private Func<AppSettings> _settings;
        private CancellationTokenSource _cts;
        private bool _running;
        private bool _fillingModels;
        private string _mode = "task";
        private string _model = "";
        private string _effort = "";
        private int? _ctxTokens;
        private bool _onlyFree = true;
        private bool _fillingProviders;
        private bool _initializing;
        private bool _layoutReady;
        private readonly Dictionary<ChatTurn, string> _turnCheckpoints = [];
        private string _tracePath = "";

        /// <summary>上一次自动总结标题时的轮数：会话结束时只在首轮之后又聊过才再更新一次。</summary>
        private int _titleTurnCount;

        /// <summary>上一次并进界面的 A 快照版本；相同就整段跳过（轮询 4 次/秒，历史越长越贵）。</summary>
        private int _snapshotRevision = -1;

        /// <summary>每轮上次清洗过的原始正文；原样没变就不再跑正则、不再重挂 transcript。</summary>
        private readonly Dictionary<ChatTurn, string> _rawAnswers = [];

        /// <summary>已经挂过收尾 transcript 的轮次。</summary>
        private readonly HashSet<ChatTurn> _attachedTranscripts = [];

        /// <summary>「文件」页在侧栏标签里的下标；第一次点文件路径时才加出来（-1 = 还没有）。</summary>
        private int _filePage = -1;

        /// <summary>「网页」页在侧栏标签里的固定下标（内嵌浏览器）。</summary>
        private const int BrowserPage = 4;

        /// <summary>「Puppet」页在侧栏标签里的固定下标（Action 清单，/puppet 打开）。</summary>
        private const int PuppetPage = 3;

        /// <summary>访问 A 的 /api/sessions；界面不再自己跑 Agent。</summary>
        private WorkerApiClient _client;
        private readonly HashSet<string> _answeredPrompts = [];

        /// <summary>待发送的附件（本地路径，发送时读成 base64 交给 A 落盘）。</summary>
        private readonly List<string> _attachments = [];

        /// <summary>工具栏选中的生图模型（仅选区画工具用，不改变聊天模型）。</summary>
        private string _imageModel;

        /// <summary>生图模型下拉是否展开（默认收起，与 Loop 页一致）。</summary>
        private bool _imageOpen;

        /// <summary>访问 I 的 /api/loop/*；演进与回滚的写入口在 I。</summary>
        private LoopApiClient _loopClient;

        private WorkerApiClient Client => _client ??= new WorkerApiClient();
        private LoopApiClient Loop => _loopClient ??= new LoopApiClient();

        /// <summary>运行中提交的文本排队：默认等这一轮返回，可切「立即」插到下一步。</summary>
        private readonly PendingQueue _queue = new();
        private PendingItem _editing;

        public SessionView()
        {
            InitializeComponent();
            _txtInput.VerifyKeyboard += OnComposerKeyboard;
        }

        internal void Initialize(Session session, Func<AppSettings> settings)
        {
            Session = session;
            _settings = settings;
            //初始化时按会话恢复的模型/力度不能被当成「用户改了设置」写回全局
            _initializing = true;

            var current = settings();
            //重开会话按它上次用的模型/力度恢复；新会话用最近用过的第一条
            var (savedModel, savedEffort) = ChatHistory.LastModel(session.Workspace, session.Id);
            _model = !string.IsNullOrWhiteSpace(savedModel) ? savedModel : ModelCatalog.DefaultModel(current);
            if (string.IsNullOrWhiteSpace(_model)) _model = "openrouter/free";
            _effort = savedEffort ?? current.ReasoningEffort ?? "";
            _onlyFree = current.OnlyFreeModels;

            _lblTitle.Text = session.Title;
            _tips.SetToolTip(_btnSummary, "用当前模型重新总结会话标题");
            _tips.SetToolTip(_btnAttach, "添加附件（也可拖放或粘贴文件/图片）");
            _lblPath.Text = UiStyle.ShortPath(session.Workspace);
            _tips.SetToolTip(_lblPath, session.Workspace + "（点击在资源管理器中打开）");
            _chat.PlaceholderText = Localization.T("NoMessages");
            //每轮都落过盘，但要在这里读回来，否则重启后会话是空的
            var saved = ChatStore.Load(session.Workspace, session.Id);
            if (saved.Count > 0)
            {
                session.Book.ReplaceAll(saved, null);
                // 列表的「步数」取最后一轮（与运行中的同步口径一致）
                session.Steps = saved[^1].ToolStepCount;
                // 旧索引没有步数：打开一次就补上，列表里不用再读整份对话文件
                if (session.Steps > 0) ChatStore.Touch(session.Workspace, session.Id, null, session.Steps);
            }
            // 已有标题（人工/历史）：末次更新只在这次之后又聊过才做
            if (!string.IsNullOrWhiteSpace(session.CustomTitle)) _titleTurnCount = Session.Book.Turns.Count;
            _chat.UndoRequested += OnChatUndo;
            _chat.CopyRequested += OnChatCopy;
            _chat.LinkClicked += OnChatLinkClicked;
            // 与 Loop 页一致：ChatFlow 右键「操作」与右上角工具栏共用这三个动作
            _chat.SelectionActions =
            [
                new ChatSelectionAction("copy", "复制"),
                new ChatSelectionAction("loop", "转给 Loop"),
                new ChatSelectionAction("delete", "删除")
            ];
            _chat.SelectionActionRequested += (_, e) => RunSelectionAction(e.Key);
            _chat.SelectionModeChanged += (_, _) =>
            {
                if (_chkSelect.Checked != _chat.ShowSelection) _chkSelect.Checked = _chat.ShowSelection;
            };
            _chat.FlashInterval = FlashIntervalMs(session.Workspace);   // 工具闪现区节拍（账本可演化）
            _chat.Bind(session.Book);
            SelectEffort(_effort);
            _freeSwitch.Checked = _onlyFree;
            FillModels();
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort);
            AttachmentsUi.EnableDrop(_inputArea, AddDroppedFiles);
            _pending.Bind(_queue);
            _pending.EditRequested += BeginEdit;
            _pending.ImmediateRequested += OnImmediateRequested;
            _pending.Removed += item =>
            {
                DropEdit(item);
                SyncInterrupt();
            };
            // 队列跟着会话落盘：进程被强杀也不丢已经排好的消息
            var savedQueue = PendingStore.Load(session.Workspace, session.Id);
            _queue.Restore(savedQueue.Items, savedQueue.Paused);
            _queue.Changed += () => PendingStore.Save(Session.Workspace, Session.Id, _queue);
            _pending.Sync();
            SetMode("task");
            RefreshLedgerInfo();
            UpdateContext(null);

            _lblStatus.TextChanged += OnStatusTextChanged;
            _loopFlow.SizeChanged += (_, _) => FitLoopLabels();
            _chat.SurveySubmitted += OnSurveySubmitted;
            OpenLoop.Core.Store.SurveyStore.Changed += OnSurveyChanged;
            _chat.SurveySkipped += (_, _) => { };
            LoadSurveys();
            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) =>
            {
                ModelCatalog.Changed -= OnCatalogChanged;
                _client?.Dispose();
                _loopClient?.Dispose();
            };
            _tips.SetToolTip(_lblDef, "点击查看特征与规则");
            _tips.SetToolTip(_lblTasks, "点击查看考核题");
            _sideBtw.Initialize(session.Workspace, settings);
            _sideBtw.ActivatePage = () => ActivatePage?.Invoke() ?? false;
            // Puppet Action 清单页：双击一行把「/puppet <action> 」填回输入框
            _puppetView.CommandRequested += command =>
            {
                _txtInput.Text = command;
                _txtInput.SelectionStart = _txtInput.Text.Length;
                _txtInput.Focus();
            };
            _puppetView.Initialize();
            FitLoopLabels();
            UpdateEvolveButtonEnabled();
            UpdateSendButton();
            _initializing = false;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Session Session { get; private set; }

        internal event Action<Session> StateChanged;
        internal event Action<string> OpenLoopRequested;
        internal event Action<Session, string> ComplaintRequested;
        internal event Action<Session, string> ForkRequested;
        internal event Action<Session, string> NewSessionRequested;
        internal event Action<Session> TitleChanged;
        internal event Action<string, IReadOnlyList<ChatTurn>, string, string> SideWorkEvicted;

        /// <summary>壳内存里打开的会话（由宿主注入）：@ 引用同工作区其他会话的数据源。</summary>
        internal Func<IReadOnlyList<Session>> Sessions { get; set; }

        /// <summary>同工作区的 Loop 会话（由宿主注入）：@ 列表与 Worker 会话一起给。</summary>
        internal Func<IReadOnlyList<LoopView>> MentionPeers { get; set; }

        /// <summary>宿主注入：点会话通知回到本会话的页面（页签已关掉就重新挂上）。</summary>
        internal Func<bool> ActivatePage { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Workspace => Session?.Workspace ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Mode => _mode;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running;

        /// <summary>主轮或右侧旁路还在跑：重启壳会把它们一起中断（迭代换壳前要据此问用户）。</summary>
        internal bool AnyBusy => _running || _sideBtw.Busy;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status => _lblStatus.Text;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Model => _model;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Effort => _effort;

        /// <summary>A 面自检：运行中标志 / 最后一轮正文长度 / 步数。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RunStatus => $"{(_running ? "running" : "idle")}|{Session.Book.Turns.LastOrDefault()?.AssistantText?.Length ?? 0}|{Session.Steps}";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ModelOptionCount => _selModel.Items.Count;

        /// <summary>提供商下拉里的条目（插件贡献的提供商会排在内置之后），给 Puppet/诊断读。</summary>
        public string ProviderOptionNames => string.Join("\n",
            _selProvider.Items.OfType<AntdUI.SelectItem>().Select(i => i.Text));

        /// <summary>模型下拉里的条目（SetModel 的可选值），给 Puppet/诊断读。</summary>
        public string ModelOptionNames => string.Join("\n",
            _selModel.Items.OfType<AntdUI.SelectItem>().Select(i => i.Text));

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ReasoningText =>
            Session.Book.Turns.Count == 0 ? "" : Session.Book.Turns[^1].ReasoningText;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ChatDiagnostics => _chat.GetLayoutDiagnostics();

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int PendingCount => _queue.Count;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string PendingSummary => string.Join("\n",
            _queue.Items.Select(i => (i.Immediate ? "[立即] " : "[等本轮] ")
                + (i.Text.Length > 0 ? i.Text : "📎 附件")));

        /// <summary>A 面驱动：移除第 index 个排队请求。</summary>
        public void RemovePending(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            var item = _queue.Items[index];
            _queue.Remove(item);
            DropEdit(item);
            SyncInterrupt();
            _pending.Sync();
        }

        /// <summary>A 面驱动：把第 index 个排队请求放回输入框编辑。</summary>
        public void RetractPending(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            BeginEdit(_queue.Items[index]);
        }

        /// <summary>A 面驱动：把第 index 个排队请求设为「立即」（已经是「立即」就不变）。</summary>
        public void SendPendingNow(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            var item = _queue.Items[index];
            OnImmediateRequested(item.Immediate ? item : _queue.Toggle(item));
        }

        /// <summary>A 面驱动：恢复被 Stop 暂停的队列并按序发送。</summary>
        public void SendAllPending()
        {
            _queue.Paused = false;
            DrainQueue();
        }

        /// <summary>Puppet 用：只入队不发送（验证队列控件与两种模式）；immediate=进来就是「立即」。</summary>
        public void EnqueueText(string text, bool immediate = false)
        {
            var item = _queue.Add(text, _mode, immediate ? PendingMode.NextStep : PendingMode.AfterTurn);
            if (item == null) return;
            _queue.Paused = false;
            _pending.Sync();
            if (item.Immediate) OnImmediateRequested(item);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SendText => _btnSend.Text;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EvolveEnabled => _btnEvolve.Enabled;

        public void ShowDefinitions() => OnShowDefinitions(null, EventArgs.Empty);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool OnlyFreeModels
        {
            get => _onlyFree;
            set
            {
                _onlyFree = value;
                _freeSwitch.Checked = value;
                Persist(s => s.OnlyFreeModels = value);
                FillModels();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ComposerText
        {
            get => _txtInput.Text;
            set => _txtInput.Text = value;
        }

        public void ShowSidePage(int index)
        {
            _sideTabs.SelectIndex = index;
            UpdateSideVisibility(index);
        }

        /// <summary>只有选中的会话页持续渲染：切走暂停（A 的快照照常并进数据），切回滚到底再按最新数据重建。</summary>
        internal void SetActive(bool active) => _chat.SuspendRendering = !active;

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
                _sideTabs.Items.Insert(BrowserPage, new AntdUI.SegmentedItem { Text = "网页" });
                if (_filePage >= BrowserPage) _filePage++;
            }
            else if (_browserTabHidden)
            {
                // 收起来过：实例还在，把标签插回原位就是（不重建 WebView2）
                _sideTabs.Items.Insert(BrowserPage, new AntdUI.SegmentedItem { Text = "网页" });
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
                new AntdUI.ContextMenuStripItem("关闭网页") { ID = "closebrowser" }
            };
            AntdUI.ContextMenuStrip.open(_sideTabs, item =>
            {
                if (item?.ID == "closebrowser") CloseBrowserPage();
            }, items);
        }

        private void UpdateSideVisibility(int index)
        {
            _loopFlow.Visible = index == 0;
            _detailFlow.Visible = index == 1;
            _sideBtw.Visible = index == 2;
            _sideBtw.SetActive(index == 2);   // 没摆在前台的旁路页不渲染
            _fileView.Visible = index >= 0 && index == _filePage;
            _puppetView.Visible = index == PuppetPage;
            if (index == PuppetPage) _puppetView.Reload();
            if (_browser != null)
            {
                _browser.Visible = index == BrowserPage;
                if (index == BrowserPage)
                {
                    _browser.EnsureLoaded();
                    _browser.MarkShown();
                }
            }
            if (index == 1) ApplyDetail();
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

            if (ChatPathActions.HandleClick(this, e.Target, Session?.Workspace ?? "")) return;

            if (!_fileView.TryShowFile(e.Target, Session?.Workspace ?? "", out string error))
            {
                // 二进制/太大/解码失败：改问要不要用系统默认程序打开
                ChatPathActions.AskSystemOpen(this, e.Target, Session?.Workspace ?? "", error);
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

        public void SetModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            _model = model.Trim();
            var provider = _selProvider.SelectedValue as ProviderConfig;
            if (provider != null && !(provider.Models ?? []).Contains(_model, StringComparer.OrdinalIgnoreCase))
            {
                _fillingProviders = true;
                try { _selProvider.SelectedIndex = 0; }
                finally { _fillingProviders = false; }
                FillModels();
            }
            _selModel.SelectedValue = _model;
            Persist(s => s.Model = _model);
        }

        public void SetEffort(string effort)
        {
            _effort = effort ?? "";
            SelectEffort(_effort);
            Persist(s => s.ReasoningEffort = _effort);
        }

        public void SelectMode(string mode)
        {
            if (mode is "task" or "criterion" or "correction") SetMode(mode);
        }

        /// <summary>发送（mode 决定语义）；immediate=运行中软打断本轮、收尾后先发（Enter），false=等本轮返回（Ctrl+Enter）。返回是否成功 + 一句给用户看的结果说明。</summary>
        public (bool Ok, string Message) SendMode(string mode = "task", bool immediate = true)
        {
            SelectMode(mode);
            var result = Submit(immediate);
            SetMode("task");
            return result;
        }

        /// <summary>停止当前轮（硬停：A 侧丢弃这一轮）。返回是否本来在跑。</summary>
        public bool Stop()
        {
            if (!_running) return false;
            _queue.Paused = true;
            StopRun();
            return true;
        }

        /// <summary>软打断：当前轮在下一个 step 前收尾，忙着的工具不打断。</summary>
        public bool Interrupt()
        {
            if (!_running) return false;
            RequestInterrupt();
            return true;
        }

        /// <summary>A 面驱动：点「演进」（走 I 的 /api/loop/evolve）。</summary>
        public void StartEvolve() => OnEvolve(this, EventArgs.Empty);

        public void Shutdown()
        {
            _cts?.Cancel();
            _sideBtw.Shutdown();
            CloseBrowserPage();
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id, _model, _effort);
        }

        /// <summary>停止：取消轮询并让 A 停掉当前轮（否则它会把这一轮跑完）。</summary>
        private async void StopRun()
        {
            _cts?.Cancel();
            try { await Client.StopAsync(Session.Id); }
            catch (Exception ex) { AppLog.Warn("停止任务失败：" + ex.Message); }
        }

        private void OnViewLoad(object sender, EventArgs e)
        {
            if (Session == null) return;
            BeginInvoke(new Action(ApplyLayout));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_layoutReady) ApplyLayout();
            if (_cmdPopup.IsOpen) _cmdPopup.PlaceAbove(_inputArea);
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void ApplyLayout()
        {
            if (IsDisposed || Width < Dip(800) || Height < Dip(300)) return;
            if (_upperSplit.Panel2Collapsed) return;

            ApplySplit(_upperSplit, vertical: true,
                panel1Min: Dip(400), panel2Min: Dip(360), targetPanel2: Dip(400));
            _layoutReady = true;
        }

        internal static void ApplySplit(
            SplitContainer split, bool vertical, int panel1Min, int panel2Min, int targetPanel2)
        {
            int total = vertical ? split.Width : split.Height;
            int splitter = split.SplitterWidth;
            if (total <= splitter + 2) return;

            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;

            int p1 = Math.Min(panel1Min, total - splitter - 1);
            split.Panel1MinSize = p1;

            int p2 = Math.Min(panel2Min, total - splitter - p1 - 1);
            split.Panel2MinSize = p2;

            int maxDistance = total - p2 - splitter;
            if (maxDistance <= p1) return;

            int desired = total - targetPanel2 - splitter;
            split.SplitterDistance = Math.Max(p1, Math.Min(maxDistance, desired));
        }

        private void SetState(SessionState state)
        {
            Session.State = state;
            StateChanged?.Invoke(Session);
        }

        private void OnToggleSide(object sender, EventArgs e)
        {
            _upperSplit.Panel2Collapsed = !_upperSplit.Panel2Collapsed;
            _btnPanel.IconSvg = UiIcons.Of(
                _upperSplit.Panel2Collapsed ? UiIcons.PanelOpen : UiIcons.PanelClose, UiStyle.Text);
            if (!_upperSplit.Panel2Collapsed) ApplyLayout();
            // 侧栏整体收起：旁路页即便仍停在那一项也看不到，跟着停渲染
            _sideBtw.SetActive(!_upperSplit.Panel2Collapsed && _sideTabs.SelectIndex == 2);
        }

        private void OnContextClick(object sender, EventArgs e)
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(1);
        }

        private void OnSidePageChanged(object sender, AntdUI.IntEventArgs e)
        {
            UpdateSideVisibility(e?.Value ?? 0);
        }

        private void OnOpenLoop(object sender, EventArgs e) => OpenLoopRequested?.Invoke(Session?.Workspace ?? "");

        private void ApplyDetail()
        {
            string effort = _effort switch
            {
                "low" => Localization.T("EffortLow"),
                "medium" => Localization.T("EffortMedium"),
                "high" => Localization.T("EffortHigh"),
                _ => Localization.T("EffortAuto")
            };
            _lblDModel.Text = Localization.T("Model") + ": " + (string.IsNullOrWhiteSpace(_model) ? "—" : _model);
            _lblDEffort.Text = Localization.T("Effort") + ": " + effort;
            _lblDCost.Text = Localization.T("Cost") + ": " + _lblCost.Text.Replace(Localization.T("Cost") + ": ", "");
            _lblDStatus.Text = Localization.T("Status") + ": " + _lblStatus.Text.Replace(Localization.T("Status") + ": ", "");
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (!_cmdPopup.IsOpen) return;
            if (e.KeyCode == Keys.Down) { _cmdPopup.MoveSelection(1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Up) { _cmdPopup.MoveSelection(-1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Escape) { _cmdPopup.Close(); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Tab) { _cmdPopup.Accept(); e.SuppressKeyPress = true; }
        }

        /// <summary>
        /// Enter 在 AntdUI 的 WM_KEYDOWN 里先被消化成换行（早于 KeyDown 事件），
        /// 所以只能在这里拦：Enter 发送（运行中=打断本轮，收尾后立刻发），
        /// Ctrl+Enter 发送（运行中=等本轮返回），Shift+Enter 换行。
        /// </summary>
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
            if (shift) { ComposerKeys.InsertSoftNewline(_txtInput); UpdateSendButton(); return; }
            // 只挂附件、不写字也是合法消息：这里必须和 Submit 的判据一致，否则回车被静默吞掉
            if (_editing == null && _txtInput.Text.Trim().Length == 0 && _attachments.Count == 0) return;
            if (_cmdPopup.IsOpen) { _cmdPopup.Accept(); return; }
            Submit(immediate: !ctrl);
        }

        private void OnInputTextChanged(object sender, EventArgs e)
        {
            UpdateSendButton();
            UpdateCommandPopup();
        }

        private static readonly (string Command, string Description)[] Commands =
        {
            ("/btw", "在右侧栏用当前上下文跑一个只读旁路任务"),
            ("/complain", "把对 Worker 表现的不满发给 Loop（不附上下文，Loop 按需读源会话）"),
            ("/debug", "把对 Worker 的问题连同最近几轮对话发给 Loop"),
            ("/compact", "把较早的对话压缩成摘要，省下上下文"),
            ("/fork", "复制当前上下文开一个新会话（可继续对话）并跳转"),
            ("/puppet", "打开右侧 Puppet Action 清单；/puppet <action> <参数> 直接执行"),
            ("/new", "开一个新会话，沿用当前的模型与强度")
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
            string workspace = Workspace;
            var loop = Loop;
            _ = Task.Run(async () =>
            {
                var commands = await SlashCommands.SkillsAsync(loop, workspace);
                if (IsDisposed || workspace != Workspace) return;
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

        /// <summary>@ 可引用的会话：同工作区、除自己以外的会话（没打开过的按落盘记录读）。</summary>
        private List<MentionTarget> MentionTargets()
        {
            var targets = new List<MentionTarget>();
            foreach (var session in Sessions?.Invoke() ?? [])
            {
                if (session == null || ReferenceEquals(session, Session)
                    || !Paths.Same(session.Workspace, Session.Workspace)) continue;
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
            foreach (var peer in MentionPeers?.Invoke() ?? [])
            {
                if (peer == null || !Paths.Same(peer.Workspace, Session.Workspace)) continue;
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

        private void RunSideWork(string request)
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(2);
            _sideBtw.SetModel(_model);
            _sideBtw.SetEffort(_effort);
            // 只保留最新一条旁路：上一条转成普通会话存进会话历史，之后可恢复继续聊
            var previous = _sideBtw.Snapshot();
            if (previous.Count > 0)
            {
                SideWorkEvicted?.Invoke(Session.Workspace, previous, _sideBtw.Model, _sideBtw.Effort);
                _sideBtw.Reset();
            }
            _sideBtw.Run(request, BuildHistory());
        }

        private void OnSummarize(object sender, EventArgs e) => _ = SummarizeTitleAsync(force: true);

        /// <summary>外部（如旁路转成会话）请求补一个标题，已有标题时不覆盖。</summary>
        internal void RequestTitle() => _ = SummarizeTitleAsync(force: false);

        /// <summary>关窗时把当前旁路落成一个会话，别直接丢掉。</summary>
        internal void FlushSideWork()
        {
            var turns = _sideBtw.Snapshot();
            if (turns.Count == 0) return;
            SideWorkEvicted?.Invoke(Session.Workspace, turns, _sideBtw.Model, _sideBtw.Effort);
            _sideBtw.Reset();
        }

        /// <summary>标题模型的系统提示词：手动重算、首轮与结束时的自动更新共用这一份。</summary>
        internal const string TitlePrompt = "你给对话起标题。只输出标题本身：不超过 12 个汉字，不加引号、不加句号、不解释。";

        /// <summary>调一次标题模型：渠道与聊天一致（会话换模型时标题也能跟着换）；空串 = 这次没生成。</summary>
        internal static async Task<string> AskTitleAsync(AppSettings settings, string workspace, string model,
            string input, CancellationToken ct)
        {
            if (settings == null || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(input)) return "";
            var (baseUrl, _) = settings.EndpointFor(model);
            if (baseUrl?.Trim().Length == 0) return "";
            var messages = new List<LlmMessage>
            {
                new("system", TitlePrompt),
                new("user", input)
            };
            var reasoner = Reasoners.Create(settings, model, "", workspace);
            var completion = await reasoner.CompleteAsync(messages, ct);
            return CleanTitle(completion?.Content);
        }

        /// <summary>
        /// 用当前模型给会话起标题：首轮结束后自动生成一次、会话结束时再更新一次（自动更新只这两次，
        /// 不随后续轮次反复刷新），也可点标题左侧的重算图标覆盖。
        /// </summary>
        private async Task SummarizeTitleAsync(bool force)
        {
            if (!force && Session.ManualTitle) return;
            var turns = Session.Book.Turns;
            if (turns.Count == 0) return;

            string previous = Session.CustomTitle;
            try
            {
                string title = await AskTitleAsync(_settings(), Session.Workspace, _model, TitleInput(turns),
                    CancellationToken.None);
                if (title.Length == 0) return;
                bool same = string.Equals(previous, title, StringComparison.Ordinal);
                _titleTurnCount = turns.Count;
                Session.ManualTitle = false;
                Session.CustomTitle = title;
                _lblTitle.Text = Session.Title;
                TitleChanged?.Invoke(Session);
                // 标题变化很小（同长度、同位置），不提示的话用户会以为没生效
                if (force) Notify(same ? "标题未变化" : "标题已更新", Session.Title);
            }
            catch (Exception ex)
            {
                AppLog.Warn("生成标题失败：" + ex.Message);
                if (force) Notify("总结失败", ex.Message, error: true);
            }
        }

        /// <summary>
        /// 会话结束时的末次标题：整段对话再总结一次（自动更新只发生在首轮与这里）；没活可干返回 null。
        /// 只写索引与 Session、不碰控件——关窗流程会在后台线程上等它，界面线程此时不能再被回调。
        /// </summary>
        internal Task TryFinalizeTitleAsync()
        {
            if (Session.ManualTitle || Session.Book.Turns.Count <= _titleTurnCount) return null;
            string workspace = Session.Workspace, sessionId = Session.Id, model = _model;
            string input = TitleInput(Session.Book.Turns);
            var settings = _settings;
            var session = Session;
            return Task.Run(async () =>
            {
                try
                {
                    string title = await AskTitleAsync(settings(), workspace, model, input, CancellationToken.None);
                    if (title.Length == 0 || string.Equals(session.CustomTitle, title, StringComparison.Ordinal)) return;
                    session.CustomTitle = title;
                    ChatStore.Touch(workspace, sessionId, title);
                    AppLog.Info("会话结束时更新标题：" + title);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("会话结束时总结标题失败：" + ex.Message);
                }
            });
        }

        /// <summary>给标题模型的输入：最近几轮问答，助手正文截断，够判断主题即可。</summary>
        internal static string TitleInput(IReadOnlyList<ChatTurn> turns)
        {
            var sb = new StringBuilder();
            foreach (var turn in turns.Skip(Math.Max(0, turns.Count - 4)))
            {
                sb.Append("用户：").AppendLine(Trim(turn.UserText, 300));
                sb.Append("助手：").AppendLine(Trim(turn.AssistantText, 300));
            }
            return sb.ToString();
        }

        internal static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string line = raw.Replace("\r", "\n").Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "";
            line = line.Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '#', '*', ' ');
            line = line.TrimEnd('。', '.', '：', ':');
            return line.Length <= 20 ? line : line[..20];
        }

        /// <summary>运行中：输入框有内容 = 立即发送（打断本轮），清空 = 停止；编辑排队项时按钮是「保存」。</summary>
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
                var last = Session?.Book?.Turns.LastOrDefault();
                return last != null && ServerNotice.IsQuotaFailure(last.AssistantText);
            }
        }

        /// <summary>提交输入框。immediate=true 时运行中会打断本轮，收尾后立刻发这一条（Enter 的语义）。</summary>
        private (bool Ok, string Message) Submit(bool immediate = false)
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
                UpdateSendButton();
                // 编辑保存/删除也会改变「立即」项有无：打断状态跟着队列走
                SyncInterrupt();
                // 本轮可能早在编辑时就收尾了（队列因搁置取不到东西）：保存后补发
                if (!removed && !_running) DrainQueue();
                return (true, removed ? "已移除排队项" : "已更新排队项");
            }

            //「/命令」先在这里分流：命令不走普通对话
            if (TryRunCommand(text))
            {
                _txtInput.Clear();
                return (true, "已执行命令");
            }
            //命令写了但没写请求：只提示，不要把「/btw」本身当任务发出去
            if (IsCommand(text, "/btw") || IsCommand(text, "/complain") || IsCommand(text, "/debug")
                || IsCommand(text, "/fork"))
                return (false, "命令缺少内容，没有发送");

            //「/技能名 请求…」展开成让 Worker 先读技能再执行的任务，后面按普通输入发送/排队
            text = SlashCommands.SkillTask(text, _skillCommands) ?? text;

            if (_running)
            {
                // shortcut: 壳自己的队列（_queue）和 Worker 的队列（snapshot.Pending）是两套，
                // 轮询还没发现「别人发起的轮」时提交的消息会落到 Worker 队列里看不到；要合并得让队列栏直接读 Pending。
                if (text.Length == 0 && _attachments.Count == 0)
                {
                    _queue.Paused = true;
                    StopRun();
                    return (true, "已停止当前轮");
                }
                // 排队项现在也能带附件（路径存在队列项里，发送时再读），运行中不再拒收
                var pendingFiles = _attachments.ToList();
                var item = Enqueue(text, _mode, immediate ? PendingMode.NextStep : PendingMode.AfterTurn, pendingFiles);
                _txtInput.Clear();
                if (pendingFiles.Count > 0)
                {
                    _attachments.Clear();
                    UpdateAttachments();
                }
                SetMode("task");
                if (item != null && item.Immediate) OnImmediateRequested(item);
                return (true, immediate ? "已插队，本轮收尾后立刻发送" : "已入队，本轮结束后发送");
            }

            if (text.Length == 0 && _attachments.Count == 0) return (false, "输入框为空");
            var files = _attachments.ToList();
            SendTask(_mode, text, files, fromComposer: true);
            SetMode("task");
            return (true, "已发送");
        }

        private void OnSendClick(object sender, EventArgs e)
        {
            // 联想浮层开着时点发送等同回车：先选中，不把「@…」「/…」当正文发出去
            if (_cmdPopup.IsOpen) { _cmdPopup.Accept(); return; }
            // 免费额度撞墙后按钮是「继续」：输入框为空时点它就把继续发出去
            if (_txtInput.Text.Trim().Length == 0 && _attachments.Count == 0 && !_running && QuotaOnLastTurn)
            {
                SendTask("task", Localization.T("ContinuePrompt"));
                SetMode("task");
                return;
            }
            // 运行中点发送与 Enter 一致：打断本轮、收尾后立刻发；要等本轮返回用 Ctrl+Enter
            Submit(immediate: true);
        }

        /// <summary>识别 /btw、/fork；返回 true 表示这条输入已被命令消费。</summary>
        private bool TryRunCommand(string text)
        {
            if (IsCommand(text, "/btw"))
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

            if (IsCommand(text, "/complain"))
            {
                string complaint = text[9..].Trim();
                if (complaint.Length == 0)
                {
                    Notify("缺少内容", "/complain 后面要写具体不满，例如：/complain 它没查调用点就改了方法签名。", error: true);
                    return false;
                }
                ComplaintRequested?.Invoke(Session, ComplaintText(complaint, debug: false));
                return true;
            }

            if (IsCommand(text, "/debug"))
            {
                string complaint = text[6..].Trim();
                if (complaint.Length == 0)
                {
                    Notify("缺少内容", "/debug 后面要写具体问题，例如：/debug 它没查调用点就改了方法签名。", error: true);
                    return false;
                }
                ComplaintRequested?.Invoke(Session, ComplaintText(complaint, debug: true));
                return true;
            }

            if (IsCommand(text, "/puppet"))
            {
                // 只带命令名 = 打开右侧清单；带了 action 就按「协作提示」交给会话执行
                string request = text[7..].Trim();
                ShowPuppetActions();
                if (request.Length > 0) _ = RunPuppetActionAsync(request);
                return true;
            }

            if (IsCommand(text, "/compact"))
            {
                if (_running)
                {
                    Notify("正在运行", "请先停止当前任务，再压缩上下文。", error: true);
                    return true;
                }
                if (_compacting)
                {
                    Notify("正在压缩", "上一次压缩还没结束，请稍候。", error: true);
                    return true;
                }
                _ = CompactContextAsync();
                return true;
            }

            if (IsCommand(text, "/fork"))
            {
                string request = text[5..].Trim();
                if (request.Length == 0)
                {
                    Notify("缺少请求", "/fork 后面要写具体请求，例如：/fork 继续把第二项改完。", error: true);
                    return false;
                }
                ForkRequested?.Invoke(Session, request);
                return true;
            }

            if (IsCommand(text, "/new"))
            {
                NewSessionRequested?.Invoke(Session, text[4..].Trim());
                return true;
            }

            return false;
        }

        internal static bool IsCommand(string text, string command)
            => text.Equals(command, StringComparison.OrdinalIgnoreCase)
                || (text.StartsWith(command, StringComparison.OrdinalIgnoreCase)
                    && text.Length > command.Length
                    && char.IsWhiteSpace(text[command.Length]));

        private bool _compacting;

        /// <summary>/compact：A 把较早的消息换成摘要；返回的压缩历史同步到本地副本，旁路/分叉也用新上下文。</summary>
        private async Task CompactContextAsync()
        {
            if (_compacting) return;
            _compacting = true;
            string status = _lblStatus.Text;
            _lblStatus.Text = Localization.T("Status") + ": 压缩上下文…";
            try
            {
                var result = await Client.CompactAsync(Session.Id);
                if (result == null)
                {
                    Notify("压缩失败", "Worker 不认识这个会话。", error: true);
                    return;
                }
                if (!result.Ok)
                {
                    Notify("未压缩", result.Error ?? "没有可压缩的上下文。", error: true);
                    return;
                }
                var last = Session.Book.Turns.LastOrDefault();
                if (last != null && result.Messages is { Count: > 0 })
                    ChatStore.AttachTranscript(last, result.Messages);
                UpdateContext(result.After);
                Notify("已压缩上下文", $"{result.Before:N0} → {result.After:N0} tokens");
            }
            catch (Exception ex)
            {
                AppLog.Error("压缩上下文失败", ex);
                Notify("压缩失败", ex.Message, error: true);
            }
            finally
            {
                _compacting = false;
                _lblStatus.Text = status;
            }
        }

        /// <summary>展开右侧栏并切到「Puppet」页（Action 清单）。</summary>
        public void ShowPuppetActions()
        {
            if (_upperSplit.Panel2Collapsed) OnToggleSide(null, EventArgs.Empty);
            ShowSidePage(PuppetPage);
            _puppetView.Reload();
        }

        /// <summary>
        /// /puppet &lt;action&gt; &lt;参数…&gt;：把这条请求作为「协作提示」塞进本会话队列（淡黄底、不走用户消息），
        /// 由会话里的 agent 用 Puppet 工具执行并回报结果。壳在 shell 进程，Worker 在 A 进程，所以 target=shell。
        /// </summary>
        private async Task RunPuppetActionAsync(string request)
        {
            try
            {
                var sheet = await PuppetActions.LoadAsync();
                string notice = PuppetActions.NoticeText(sheet, request, "shell");
                var result = await Client.NoticeAsync(Session.Id, notice, "puppet");
                if (result == null) Notify("未执行", "Worker 不认识这个会话。", error: true);
                else Notify("已交给会话执行", request);
            }
            catch (Exception ex)
            {
                AppLog.Error("执行 Puppet action 失败", ex);
                Notify("未执行", ex.Message, error: true);
            }
        }

        /// <summary>转给 Loop 的用户反馈：/complain 只带一句，/debug 另附账本配置的最近几轮与源会话记录。</summary>
        private string ComplaintText(string complaint, bool debug)
            => ComposeComplaint("Worker", Session.Title,
                debug ? Session.Book.Turns : null, complaint,
                ChatHistory.PathFor(Session.Workspace, Session.Id), Session.Workspace,
                debug ? DebugTurns(Session.Workspace) : 0);

        /// <summary>/debug 附带的源会话轮数：读本工作区账本的 limit.debug_turns，可在激活集里配置与演化。</summary>
        internal static int DebugTurns(string workspace)
            => LedgerValue(workspace, FeatureCatalog.DebugTurns, FeatureCatalog.DefaultDebugTurns);

        /// <summary>@ 引用另一个会话时附带的轮数：账本 limit.mention_turns，同样可配置与演化。</summary>
        internal static int MentionTurns(string workspace)
            => LedgerValue(workspace, FeatureCatalog.MentionTurns, FeatureCatalog.DefaultMentionTurns);

        /// <summary>工具闪现区节拍（毫秒）：读本工作区账本的 limit.flash_interval，可在激活集里配置与演化。</summary>
        internal static int FlashIntervalMs(string workspace)
            => LedgerValue(workspace, FeatureCatalog.FlashInterval,
                FeatureCatalog.DefaultFlashInterval, ChatFlow.ChatFlowControl.FlashMinInterval,
                ChatFlow.ChatFlowControl.FlashMaxInterval);

        private static int LedgerValue(string workspace, string id, int fallback,
            int min = 0, int max = 50)
        {
            try
            {
                var ledger = Ledger.Open(workspace);
                int value = FeatureCatalog.ValueOf(ledger.Current,
                    ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal), id, fallback);
                return Math.Clamp(value, min, max);
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取账本数值失败（" + id + "）：" + ex.Message);
                return fallback;
            }
        }

        /// <summary>Worker 与 Loop 的 /complain、/debug 共用同一段反馈格式，只有申诉对象不同。</summary>
        internal static string ComposeComplaint(string role, string title,
            IReadOnlyList<ChatTurn> turns, string complaint, string historyPath = null,
            string workspace = null, int historyTurns = 0)
        {
            var sb = new StringBuilder();
            sb.Append("[用户反馈] 对 ").Append(role).Append(" 表现不满（会话「")
                .Append(title).Append("」）：").AppendLine(complaint);
            if (historyTurns > 0 && turns is { Count: > 0 })
            {
                int from = Math.Max(0, turns.Count - historyTurns);
                sb.Append("来源会话最近 ").Append(turns.Count - from).AppendLine(" 轮：");
                for (int i = from; i < turns.Count; i++)
                {
                    sb.Append("- 用户：").AppendLine(Trim(turns[i].UserText, 300));
                    if (!string.IsNullOrWhiteSpace(turns[i].ToolCallLines))
                        sb.Append("  工具：").AppendLine(Trim(turns[i].ToolCallLines, 600));
                    sb.Append("  回复：").AppendLine(Trim(turns[i].AssistantText, 500));
                }
            }
            sb.Append("需要更多轮次与工具细节时用 ReadSession 工具读内存会话（session=\"").Append(title)
                .AppendLine("\"，offset/count 可选）。");
            if (!string.IsNullOrWhiteSpace(historyPath))
                sb.Append("会话已关闭时再读文件：").AppendLine(historyPath);
            if (!string.IsNullOrWhiteSpace(workspace))
                sb.Append("来源会话工作区：").Append(workspace);
            return sb.ToString().TrimEnd();
        }

        private static string Trim(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

        private PendingItem Enqueue(string text, string mode, PendingMode pending = PendingMode.AfterTurn,
            IReadOnlyList<string> attachments = null)
        {
            var item = _queue.Add(text, mode, pending, attachments);
            _queue.Paused = false;
            _pending.Sync();
            return item;
        }

        /// <summary>
        /// 「立即」：先让当前轮在下一个 step 前停下，停下后 DrainQueue 会先发这一条；
        /// 改回「等本轮」时收回打断，否则这一轮照样在 step 后收尾、排队项仍被提前发出去。
        /// </summary>
        private void OnImmediateRequested(PendingItem item)
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

        /// <summary>
        /// 打断状态以队列为准：还有「立即」项就打断，全部改回「等本轮」（或删掉）就收回。
        /// 只发变化的那一次，连点「立即→等本轮」不会产生两条反向请求。
        /// </summary>
        private void SyncInterrupt()
        {
            bool on = _running && _queue.AnyImmediate;
            if (on == _interruptOn) return;
            _interruptOn = on;
            RequestInterrupt(on);
        }

        /// <summary>壳这边以为的打断状态；A 每次开轮都会把标记清掉，轮结束也跟着复位。</summary>
        private bool _interruptOn;

        /// <summary>
        /// 打断/收回按点击顺序串起来：连点「立即→等本轮」时两条请求不能反序到达，否则打断收不回来。
        /// </summary>
        private Task _interruptChain = Task.CompletedTask;

        private void RequestInterrupt(bool on = true)
        {
            var client = Client;
            string id = Session.Id;
            _interruptChain = ChainInterruptAsync(_interruptChain, client, id, on);
        }

        private static async Task ChainInterruptAsync(Task previous, WorkerApiClient client, string id, bool on)
        {
            await previous;
            try
            {
                await client.InterruptAsync(id, on);
            }
            catch (Exception ex)
            {
                AppLog.Warn("打断当前轮失败：" + ex.Message);
            }
        }

        /// <summary>队列里的条目被删掉时退出编辑态，别让「保存」留在按钮上。</summary>
        private void DropEdit(PendingItem item)
        {
            if (!ReferenceEquals(_editing, item)) return;
            _editing = null;
            UpdateSendButton();
        }

        /// <summary>把排队文本放回输入框编辑，发送按钮变成「保存」。</summary>
        private void BeginEdit(PendingItem item)
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

        /// <summary>当前轮结束后调用：优先「立即」，其次按队列顺序。</summary>
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
            SendTask(batch[0].Tag, merged, files.Count > 0 ? files : null);
        }

        private void SetMode(string tag) => _mode = string.IsNullOrWhiteSpace(tag) ? "task" : tag;

        private void OnModelChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingModels || _initializing) return;
            if (_selModel.SelectedValue is not string id || string.IsNullOrWhiteSpace(id)) return;
            _model = id;
            UpdateContext(_ctxTokens);   // 换了模型上下文上限也变了，标签要跟着刷新
            UpdateAttachments();         // 附件上限按模型类别算，换模型要跟着变
            Persist(s =>
            {
                s.Model = id;
                ModelChoices.RecordUse(s, id);
            });
            UpdateEvolveButtonEnabled();
        }

        private void OnImageModelChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingModels || _initializing) return;
            if (_selImage.SelectedValue is string id) _imageModel = id;
        }

        private void OnToggleImagePicker(object sender, EventArgs e)
        {
            _imageOpen = !_imageOpen;
            _btnImage.Toggle = _imageOpen;
            _selImage.Visible = _imageOpen && _selImage.Items.Count > 0;
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

        /// <summary>拖放到输入区的文件追加成附件（和附件按钮同一套上限与去重）。</summary>
        private void AddDroppedFiles(string[] paths)
        {
            int limit = MediaModels.AttachmentLimit(_model);
            if (AttachmentsUi.Add(_attachments, paths, limit))
                Notify("附件太多", $"当前模型最多 {limit} 个附件", error: true);
            UpdateAttachments();
        }

        private void OnClearAttachments(object sender, EventArgs e)
        {
            _attachments.Clear();
            UpdateAttachments();
        }

        private void UpdateAttachments()
        {
            int limit = MediaModels.AttachmentLimit(_model);
            _lblAttach.Text = AttachmentsUi.Label(_attachments, limit);
            _lblAttach.Visible = _attachments.Count > 0;
            if (_attachments.Count > 0) _tips.SetToolTip(_lblAttach, string.Join("\n", _attachments));
            UpdateSendButton();
        }

        private void OnEffortChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingModels || _initializing) return;
            _effort = _selEffort.SelectedValue as string ?? "";
            ApplyDetail();
            Persist(s => s.ReasoningEffort = _effort);
        }

        private void Persist(Action<AppSettings> apply)
        {
            try
            {
                var settings = _settings();
                apply(settings);
                settings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Warn("保存设置失败：" + ex.Message);
            }
        }

        private void SelectEffort(string effort)
        {
            int index = effort switch { "low" => 1, "medium" => 2, "high" => 3, _ => 0 };
            _selEffort.SelectedIndex = index;
        }

        private void OnFreeToggle(object sender, AntdUI.BoolEventArgs e)
        {
            if (_settings == null || _fillingModels) return;
            _onlyFree = e.Value;
            Persist(s => s.OnlyFreeModels = _onlyFree);
            FillModels();
        }

        /// <summary>侧栏按钮组水平居中：FlowLayoutPanel 只按 Margin 摆放，宽度变化后重算左右留白。</summary>
        private void OnFlowLayout(object sender, LayoutEventArgs e)
            => FlowButtons.Center(sender as FlowLayoutPanel);

        private void FitLoopLabels()
        {
            int width = Math.Max(Dip(120), _loopFlow.ClientSize.Width - Dip(28));
            foreach (var label in new[] { _lblDef, _lblTasks, _lblCost, _lblStatus })
                label.MaximumSize = new Size(width, 0);

            FitWrapped(_lblTrace, width);
            FitWrapped(_lblEvolveHint, width);
        }

        /// <summary>多行说明标签按换行宽度重新量高度：AutoSize 只量单行，长句会被裁掉（侧栏越窄越明显）。</summary>
        private void FitWrapped(AntdUI.Label label, int width)
        {
            label.MaximumSize = new Size(width, 0);
            label.Width = width;
            int height = TextRenderer.MeasureText(
                label.Text,
                label.Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height;
            label.Height = Math.Max(Dip(18), height + Dip(2));
        }

        private bool _statusBusy;

        private void OnStatusTextChanged(object sender, EventArgs e)
        {
            if (_statusBusy) return;
            string translated = Localization.Translate(_lblStatus.Text);
            if (translated == _lblStatus.Text) return;
            _statusBusy = true;
            try { _lblStatus.Text = translated; } finally { _statusBusy = false; }
        }

        private void OnCatalogChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(FillModels));
                return;
            }
            FillModels();
        }

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

            // 后台还在起服务/拉目录时补几拍重填，用户不用自己再点一次
            PluginRefill.WhenReady(this, FillModels);
        }

        private void OnProviderChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingProviders || _fillingModels) return;
            FillModels();
            UpdateEvolveButtonEnabled();
        }

        private void FillModels()
        {
            if (IsDisposed) return;
            FillProviders();

            _fillingModels = true;
            try
            {
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var visible = ModelChoices.FilterByProvider(
                    ModelChoices.Build(_settings(), _onlyFree), provider);

                ModelChoices.Fill(_selModel, visible, _settings());

                if (provider == null && !string.IsNullOrWhiteSpace(_model) && visible.All(m => m.Id != _model))
                {
                    var currentModel = ModelCatalog.All.FirstOrDefault(m => m.Id == _model);
                    _selModel.Items.Add(new AntdUI.SelectItem(currentModel?.Name ?? _model) { Tag = _model });
                }

                if (provider != null && visible.All(m => m.Id != _model))
                    _model = visible.FirstOrDefault()?.Id ?? "";
                else if (string.IsNullOrWhiteSpace(_model) && visible.Count > 0)
                    _model = visible[0].Id;

                _selModel.SelectedValue = _model;

                var images = ModelChoices.Images(_settings(), _onlyFree);
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
                _fillingModels = false;
            }
        }

        private void UpdateContext(int? tokens)
        {
            _ctxTokens = tokens;
            int limit = ModelChoices.ContextLimit(_model, _settings());
            float ratio = tokens.HasValue
                ? Math.Clamp(tokens.Value / (float)limit, 0F, 1F)
                : 0F;
            _ctx.Value = ratio;
            _ctx.Text = tokens.HasValue ? $"{ratio * 100F:0}%" : "—";
            _lblDCtx.Text = Localization.T("Context") + $": {(tokens.HasValue ? tokens.Value.ToString("N0") : "—")} / {limit:N0}";
            ApplyDetail();
        }

        /// <summary>点详情栏的上下文数值：下拉选常用窗口，写进设置里的逐模型覆盖（下次运行生效）。</summary>
        private void OnEditContext(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_model)) return;
            int current = _settings().PropsFor(_model)?.ContextLimit ?? 0;
            var items = new List<AntdUI.IContextMenuStripItem>
            {
                new AntdUI.ContextMenuStripItem("自动（目录 / 内置表）") { ID = "0", Checked = current == 0 }
            };
            foreach (var (label, value) in ContextChoices)
                items.Add(new AntdUI.ContextMenuStripItem(label) { ID = value.ToString(), Checked = current == value });
            AntdUI.ContextMenuStrip.open(_lblDCtx, selected => ApplyContextLimit(selected?.ID), items.ToArray());
        }

        private static readonly (string Label, int Value)[] ContextChoices =
        [
            ("32K", 32768),
            ("64K", 65536),
            ("128K", 131072),
            ("200K", 200000),
            ("256K", 262144),
            ("512K", 524288),
            ("1M", 1000000)
        ];

        private void ApplyContextLimit(string id)
        {
            if (!int.TryParse(id, out int value) || string.IsNullOrWhiteSpace(_model)) return;
            Persist(s =>
            {
                var props = s.PropsFor(_model);
                if (props == null) s.ModelProps[_model] = props = new ModelProps();
                props.ContextLimit = Math.Max(0, value);
            });
            UpdateContext(_ctxTokens);
        }

        /// <summary>执行失败的轮次用这个前缀记在正文里；这类正文不是模型回复，不能再当上下文回灌。</summary>
        private const string FailurePrefix = "执行失败：";

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

        private async void SendTask(string tag, string text, List<string> attachments = null, bool fromComposer = false)
        {
            if (_running) return;

            var settings = _settings();
            if (string.IsNullOrWhiteSpace(text) && (attachments == null || attachments.Count == 0)) return;

            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            // dsh 走 ACP（A 进程拉起），没有 HTTP 接口地址
            if ((baseUrl.Length == 0 && !DshAcpHub.IsDsh(model)) || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }

            // 附件在本机直接复制进工作区（只把路径发给 Worker）：不走 base64 与 HTTP 请求体上限，
            // 也不用把整个文件读进内存。失败时输入框与附件都保留，改完再发。
            var saved = Attachments.SaveFiles(workspace, Session.Id, attachments,
                MediaModels.AttachmentLimit(model), out string attachmentError);
            if (saved == null)
            {
                Notify("附件不可用", attachmentError, error: true);
                return;
            }
            if (fromComposer) _txtInput.Clear();

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var ct = cts.Token;

            _queue.Paused = false;   // 用户又发了一条：停止留下的暂停到此为止，本轮收尾后队列照常续发
            SetRunning(true);
            SetState(SessionState.Running);
            _lblStatus.Text = Localization.T("Status") + ": 运行中…";
            SetTrace(workspace, "");

            var client = Client;
            WorkerSessionSnapshot snapshot = null;
            string failure = null;
            try
            {
                var remote = await client.GetAsync(Session.Id, ct)
                             ?? await client.CreateAsync(Session.Id, workspace, model, _effort, ct);
                if (remote == null) throw new InvalidOperationException("Worker 未就绪（" + client.Endpoint + "）");
                if (!string.Equals(remote.Model, model, StringComparison.Ordinal)
                    || !string.Equals(remote.Effort, _effort, StringComparison.Ordinal))
                    await client.SetModelAsync(Session.Id, model, _effort, ct);

                snapshot = await client.SendAsync(Session.Id, tag, text, ct, _imageModel, paths: saved,
                    context: MentionPicker.Context(text, MentionTargets(), MentionTurns(workspace)));
                if (snapshot == null) throw new InvalidOperationException("Worker 上找不到这个会话");
                if (attachments is { Count: > 0 })
                {
                    _attachments.Clear();
                    UpdateAttachments();
                }
                ApplySnapshot(snapshot);
                //发出即落盘：进程被杀时最多丢流式中的半条回复，而不是整轮对话
                ChatStore.Save(workspace, Session.Book.Turns, Session.Id, _model, _effort);

                snapshot = await AwaitRunAsync(snapshot, client, ct);
            }
            catch (OperationCanceledException)
            {
                failure = "stopped";
            }
            catch (Exception ex)
            {
                AppLog.Error("任务执行失败", ex);
                failure = ex.Message;
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
            }

            await FinishRunAsync(client, workspace, snapshot, failure);
        }

        /// <summary>等这一轮跑完：流式期间把快照并进界面，返回最后一帧。</summary>
        private async Task<WorkerSessionSnapshot> AwaitRunAsync(WorkerSessionSnapshot snapshot, WorkerApiClient client,
            CancellationToken ct)
        {
            while (snapshot.State == WorkerSessionHost.Running)
            {
                await Task.Delay(250, ct);
                snapshot = await client.GetAsync(Session.Id, ct);
                if (snapshot == null) throw new InvalidOperationException("Worker 上找不到这个会话");
                snapshot = AnswerPromptsAsync(client, snapshot, ct);
                ApplySnapshot(snapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// 这一轮已经跑完（不管是谁发起的）：收尾、落盘，然后把队列里的下一条发出去。
        /// </summary>
        private async Task FinishRunAsync(WorkerApiClient client, string workspace, WorkerSessionSnapshot snapshot,
            string failure)
        {
            //停止/失败也取一次最终快照：状态与正文以 Worker 为准
            try
            {
                var final = await client.GetAsync(Session.Id);
                if (final != null)
                {
                    snapshot = final;
                    ApplySnapshot(final);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取会话状态失败：" + ex.Message);
            }

            string outcome = failure == "stopped" ? "stopped"
                : failure != null ? "error"
                : snapshot?.Outcome ?? "error";
            var turn = Session.Book.Turns.Count > 0 ? Session.Book.Turns[^1] : null;
            var remoteTurn = snapshot?.Turns.Count == Session.Book.Turns.Count ? snapshot.Turns[^1] : null;
            if (turn != null)
            {
                turn.Streaming = false;
                turn.ToolStatusText = null;
                string raw = remoteTurn?.Assistant ?? turn.AssistantText;
                turn.AssistantText = outcome == "error" && raw.Trim().Length == 0
                    ? FailurePrefix + (failure ?? "执行失败")
                    : CleanAnswer(raw, remoteTurn?.FinishReason);
            }

            _lblStatus.Text = Localization.T("Status") + outcome switch
            {
                "error" => ": error",
                "stopped" => ": stopped",
                _ => $": {outcome}"
            };

            if (outcome is "ok" or "budget")
            {
                decimal? cost = snapshot?.Cost;
                int steps = remoteTurn?.Steps ?? Session.Steps;
                _lblCost.Text = Localization.T("Cost") + $": {(cost.HasValue ? cost.Value.ToString("0.0000") : "—")} · {steps} {Localization.T("Steps")}";
                UpdateContext(snapshot?.ContextTokens);
            }

            if (outcome == "ok")
                Notify("任务完成", $"{remoteTurn?.Steps ?? 0} 步 · 成本 {(snapshot?.Cost?.ToString("0.0000") ?? "—")}");
            else if (outcome == "budget")
                Notify("预算用尽", "本次执行到达步数上限。", error: true);
            else if (outcome == "error")
                Notify("执行失败", failure ?? "执行失败", error: true);

            SetRunning(false);
            Session.Steps = remoteTurn?.Steps ?? Session.Steps;
            SetState(outcome is "ok" or "budget" ? SessionState.Done : SessionState.Interrupted);
            RefreshChat();
            // 正常收尾：长正文从头读（与 Loop 同一套交互），流式期间已经跟随到底
            if (outcome == "ok" && turn != null) _chat.ScrollToAnswerStart(turn);
            RefreshLedgerInfo();
            ChatStore.Save(workspace, Session.Book.Turns, Session.Id, _model, _effort);
            // 初次：首轮成功结束后由所选模型生成短标题（已有人工/历史标题就不再覆盖）
            if (outcome == "ok" && Session.Book.Turns.Count == 1 && string.IsNullOrWhiteSpace(Session.CustomTitle))
                _ = SummarizeTitleAsync(force: false);
            // 正常收尾就是轮后：上一轮「停止」留下的暂停不该再挡住队列
            if (outcome is "ok" or "budget") _queue.Paused = false;
            // 页面交回数据了：这一轮就是那个 LoopApp 的任务，收尾时把网页页收起来
            CollapseBrowserIfDone();
            // 失败轮不续发队列：等新的输入（用户或自愈的「继续」）激活会话后，队列才按规则继续
            if (outcome != "error" && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(DrainQueue));
        }

        /// <summary>
        /// 这一轮不是壳发起的（免费额度自愈发的「继续」、网页、Puppet…）：跟着它跑到底。
        /// 否则 _running 一直是假的，用户这时输入会被直接塞进 Worker 的队列，队列栏看不到。
        /// </summary>
        private async Task TrackExternalAsync(WorkerSessionSnapshot snapshot)
        {
            var client = Client;
            string workspace = Session.Workspace.Trim();
            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            string failure = null;
            try
            {
                snapshot = await AwaitRunAsync(snapshot, client, cts.Token);
            }
            catch (OperationCanceledException)
            {
                failure = "stopped";
            }
            catch (Exception ex)
            {
                // 轮询断了不算这一轮失败：下一拍 SyncFromWorker 发现还在跑会重新接上
                AppLog.Warn("跟随外部轮次失败：" + ex.Message);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
            }

            await FinishRunAsync(client, workspace, snapshot, failure);
        }

        /// <summary>把 A 的快照映射到本地对话：新轮次建 turn，已有的更新正文/工具行/思考区。</summary>
        private void ApplySnapshot(WorkerSessionSnapshot snapshot)
        {
            if (snapshot == null) return;
            // 版本没动 = 与上次完全一样：整条映射都可以省掉（流式期间每 250ms 来一次）
            if (snapshot.Revision > 0)
            {
                if (snapshot.Revision == _snapshotRevision) return;
                _snapshotRevision = snapshot.Revision;
            }

            bool structure = false;
            while (Session.Book.Turns.Count < snapshot.Turns.Count)
            {
                var remote = snapshot.Turns[Session.Book.Turns.Count];
                var added = new ChatTurn
                {
                    User = new ChatMessage("user", remote.User ?? ""),
                    Assistant = new ChatMessage("assistant", remote.Assistant ?? ""),
                    Streaming = remote.Streaming,
                    // 协作提示：界面折成一行、淡黄底（与用户敲的区分）
                    Notice = remote.Notice,
                    NoticeKind = remote.NoticeKind ?? "",
                    ThinkingExpanded = _settings().ThinkingExpandedByDefault
                };
                Session.Book.AddTurn(added);
                if (!string.IsNullOrEmpty(remote.Checkpoint)) _turnCheckpoints[added] = remote.Checkpoint;
                structure = true;
            }

            for (int i = 0; i < snapshot.Turns.Count; i++)
            {
                var remote = snapshot.Turns[i];
                var turn = Session.Book.Turns[i];
                turn.ReasoningText = remote.Reasoning ?? "";
                turn.ToolCallLines = remote.ToolLines ?? "";
                turn.ToolTraceText = remote.ToolLines ?? "";
                turn.TimelineText = remote.Timeline ?? "";
                turn.Phases.Clear();
                foreach (var phase in remote.Phases ?? [])
                    turn.Phases.Add((phase?.Thinking ?? "", LlmTextSanitizer.StripSafetyLines(phase?.Answer ?? "")));
                turn.ToolStepCount = remote.Steps;
                turn.ToolTotalMs = remote.Ms;
                turn.Streaming = remote.Streaming;
                // 等审批之类的即时状态：Worker 没有就清空，别让旧提示粘住
                turn.ToolStatusText = string.IsNullOrEmpty(remote.Status) ? null : remote.Status;
                if (!string.IsNullOrEmpty(remote.Checkpoint))
                    _turnCheckpoints[turn] = remote.Checkpoint;
                // 正文没变就别再清洗：StripSafetyLines 是整段复制 + 逐行正则，历史越长越贵
                string raw = remote.Assistant ?? "";
                bool changed = !_rawAnswers.TryGetValue(turn, out string lastRaw)
                               || !string.Equals(lastRaw, raw, StringComparison.Ordinal);
                if (changed)
                {
                    _rawAnswers[turn] = raw;
                    turn.AssistantText = LlmTextSanitizer.StripSafetyLines(raw);
                }
                // transcript 收尾时再挂一次（流式中途只跟着正文变化走），避免每拍把所有历史重挂一遍
                if (remote.Transcript is { Count: > 0 }
                    && (changed || (!remote.Streaming && _attachedTranscripts.Add(turn))))
                    ChatStore.AttachTranscript(turn, remote.Transcript);
            }

            if (structure) RefreshChat();
            var last = Session.Book.Turns.LastOrDefault();
            if (last != null) _chat.UpdateStreamingMessage(last, last.AssistantText, force: structure);
            // 上下文占比跟着 Worker 的实时估算走（此前只在跑完时更新一次）
            if (snapshot.ContextTokens != _ctxTokens) UpdateContext(snapshot.ContextTokens);
            if (!string.IsNullOrEmpty(snapshot.TraceDirectory)) SetTrace(snapshot.Workspace, snapshot.TraceDirectory);
            if (snapshot.ActivationVersion > 0)
                _lblDef.Text = $"def: v{snapshot.ActivationVersion} · {snapshot.FeatureCount} {Localization.T("Features")} · {snapshot.RuleCount} {Localization.T("Rules")}";

            // A 队列里 agent 自动插入的消息（协作监测汇报）：只在本机队列栏显示，不参与本机发送
            var autoNotes = new List<string>();
            var pendingTexts = snapshot.Pending ?? [];
            var pendingAuto = snapshot.PendingAuto ?? [];
            for (int i = 0; i < pendingTexts.Count; i++)
                if (i < pendingAuto.Count && pendingAuto[i]) autoNotes.Add(pendingTexts[i]);
            _pending.SetAgentNotes(autoNotes);
        }

        /// <summary>
        /// 这一轮不是壳发起的（会话是直接走 A 的 API / 网页 / 自愈的「继续」/ 别的 agent 跑的）：
        /// 把 A 的快照并进界面；还在跑就跟着跑，界面才算真的「运行中」，用户此时输入才会进队列栏。
        /// </summary>
        internal void SyncFromWorker(WorkerSessionSnapshot snapshot)
        {
            if (snapshot == null || _running || Session == null || IsDisposed || !IsHandleCreated) return;

            if (snapshot.State == WorkerSessionHost.Running)
            {
                ApplySnapshot(snapshot);
                _lblStatus.Text = Localization.T("Status") + ": 运行中…";
                SetState(SessionState.Running);
                SetRunning(true);
                _ = TrackExternalAsync(snapshot);
                return;
            }

            ApplySnapshot(snapshot);
            Session.Steps = snapshot.Turns?.LastOrDefault()?.Steps ?? Session.Steps;
            UpdateContext(snapshot.ContextTokens);
            _lblStatus.Text = Localization.T("Status")
                              + (string.IsNullOrEmpty(snapshot.Outcome) ? "" : ": " + snapshot.Outcome);
            if (snapshot.Cost.HasValue)
                _lblCost.Text = Localization.T("Cost") + $": {snapshot.Cost.Value:0.0000} · {Session.Steps} {Localization.T("Steps")}";
        }

        /// <summary>回答 A 挂起的审批/确认；同一个 prompt 只答一次。</summary>
        /// <summary>把快照里的待答审批挂出去：绝不 await（模态/等待会卡住整条轮询链），
        /// 拿不到答案时 Worker 侧 60 秒超时按拒绝收尾。</summary>
        private WorkerSessionSnapshot AnswerPromptsAsync(WorkerApiClient client,
            WorkerSessionSnapshot snapshot, CancellationToken ct)
        {
            foreach (var prompt in snapshot.Prompts ?? [])
            {
                if (prompt?.Id == null || !_answeredPrompts.Add(prompt.Id)) continue;
                _ = AnswerPromptAsync(client, prompt, ct);
            }
            return snapshot;
        }

        private async Task AnswerPromptAsync(WorkerApiClient client, WorkerPromptView prompt, CancellationToken ct)
        {
            try
            {
                if (prompt.Kind == "continue")
                {
                    int answer = AskOnUi(prompt.Text) ? 1 : 0;
                    await client.ApproveAsync(Session.Id, prompt.Id, answer, ct);
                    return;
                }

                var kind = prompt.Category == "outside" ? ApprovalKind.OutsideWorkspace : ApprovalKind.SensitiveAction;
                var decision = new TaskCompletionSource<ApprovalDecision>();
                ApprovalGate.AskNonBlocking(this, _settings,
                    new ApprovalRequest(kind, prompt.Tool, prompt.Detail, prompt.Reason),
                    d => decision.TrySetResult(d));
                await client.ApproveAsync(Session.Id, prompt.Id, (int)await decision.Task, ct);
            }
            catch (Exception ex)
            {
                AppLog.Warn("审批回填失败：" + ex.Message);
            }
        }

        private void Notify(string title, string text, bool error = false)
        {
            var form = FindForm();
            if (form == null) return;
            // 会话相关通知留久一点（默认 6 秒），点「打开会话」跳到本会话并随手关掉通知
            // （Call 返回 false = 不吞掉这次点击，让它继续走 ClickClose 关闭）
            var config = new AntdUI.Notification.Config(form, title, text,
                error ? AntdUI.TType.Error : AntdUI.TType.Success, AntdUI.TAlignFrom.TR,
                null, error ? 12 : 10);
            if (ActivatePage != null)
                config.Link = new AntdUI.Notification.ConfigLink("打开会话", () =>
                {
                    ActivatePage();
                    return false;
                });
            AntdUI.Notification.open(config);
        }

        private int _ledgerVersion;

        private void RefreshLedgerInfo()
        {
            string workspace = Session.Workspace.Trim();
            int version = ++_ledgerVersion;
            string features = Localization.T("Features");
            string rules = Localization.T("Rules");
            string tasksLabel = Localization.T("Tasks");
            string tasksUnit = Localization.T("TasksUnit");
            if (!Directory.Exists(workspace))
            {
                ApplyLedger("def: —", tasksLabel + ": —", null, 0, 0, version);
                return;
            }

            // 这几个是磁盘读（账本、考核题、草稿、报告各一次）：别放在界面线程上，一轮结束会连刷几次
            _ = Task.Run(() =>
            {
                string def, tasks, tip = null;
                int drafts = 0, reports = 0;
                try
                {
                    var ledger = Ledger.Open(workspace);
                    var current = ledger.Activations().LastOrDefault();
                    if (current == null)
                    {
                        def = "def: —（首次运行后初始化）";
                    }
                    else
                    {
                        var known = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                        int ruleCount = current.Features.Count(r => r?.Id != null
                            && (known.TryGetValue(r.Id, out var f)
                                ? f.Kind == "rule"
                                : r.Id.StartsWith("rule.", StringComparison.Ordinal)));
                        def = $"def: v{current.Version} · {current.Features.Count} {features} · {ruleCount} {rules}  ›";
                    }
                    tasks = tasksLabel + $": {TaskSet.Load(ledger.EvalDir).Tasks.Count} {tasksUnit}  ›";
                    tip = "点击查看考核题\n" + Path.Combine(ledger.EvalDir, "tasks");
                    drafts = TaskDrafts.PendingCount(workspace);
                    reports = EvalReportStore.List(workspace).Count;
                }
                catch (Exception ex)
                {
                    def = "def: 读取失败：" + ex.Message;
                    tasks = tasksLabel + ": —";
                }
                ApplyLedger(def, tasks, tip, drafts, reports, version);
            });
        }

        /// <summary>后台刷新回填用的 UI 上下文（窗口句柄可能还没建，BeginInvoke 会抛）。</summary>
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;

        private void ApplyLedger(string def, string tasks, string tip, int drafts, int reports, int version)
        {
            if (IsDisposed) return;
            if (_ui != null && SynchronizationContext.Current != _ui)
            {
                _ui.Post(_ => ApplyLedger(def, tasks, tip, drafts, reports, version), null);
                return;
            }
            if (version != _ledgerVersion) return;   // 期间又刷新过：丢掉这份过期结果
            _lblDef.Text = def;
            _lblTasks.Text = tasks;
            if (tip != null) _tips.SetToolTip(_lblTasks, tip);
            _btnDrafts.Text = Localization.T("TaskDrafts") + (drafts > 0 ? " " + drafts : "");
            _btnReport.Text = Localization.T("EvalReports") + (reports > 0 ? " " + reports : "");
        }

        internal void SetTraceDebug(string path) => SetTrace(Session.Workspace, path);

        private void SetTrace(string workspace, string path)
        {
            _tracePath = path ?? "";
            _lblTrace.Text = "trace: " + RelativeToWorkspace(workspace, _tracePath);
            _tips.SetToolTip(_lblTrace, _tracePath);
            FitLoopLabels();
        }

        /// <summary>点左下角路径标签：用资源管理器打开当前会话的工作区。</summary>
        private void OnOpenWorkspace(object sender, EventArgs e)
        {
            string workspace = Session?.Workspace?.Trim() ?? "";
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + workspace + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开工作区失败：" + ex.Message);
            }
        }

        private void OnOpenTrace(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_tracePath) || !Directory.Exists(_tracePath)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _tracePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开 trace 目录失败：" + ex.Message);
            }
        }

        private void OnShowDefinitions(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Current;
                if (current == null)
                {
                    Notify("暂无激活集", "先运行一次，账本初始化后再看。", error: true);
                    return;
                }

                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                var active = current.Features.Select(r => r?.Id).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
                var sb = new StringBuilder();
                sb.AppendLine($"当前版本 v{current.Version} · {current.At} · {current.By}");
                if (!string.IsNullOrWhiteSpace(current.Note)) sb.AppendLine(current.Note);
                sb.AppendLine();
                sb.AppendLine($"已启用（{current.Features.Count}）");
                foreach (var reference in current.Features)
                {
                    if (reference?.Id == null) continue;
                    features.TryGetValue(reference.Id, out var feature);
                    sb.AppendLine();
                    sb.AppendLine($"[{feature?.Kind ?? "?"}] {reference.Id}");
                    if (!string.IsNullOrWhiteSpace(feature?.Problem))
                        sb.AppendLine("  问题：" + feature.Problem);
                    string payload = PayloadText(feature);
                    if (!string.IsNullOrWhiteSpace(payload))
                        sb.AppendLine("  内容：" + payload);
                    if (reference.Params is { Count: > 0 })
                        sb.AppendLine("  参数：" + string.Join(", ",
                            reference.Params.Select(p => $"{p.Key}={p.Value}")));
                }

                var dormant = features.Values
                    .Where(f => !active.Contains(f.Id))
                    .OrderBy(f => f.Id, StringComparer.Ordinal)
                    .ToList();
                if (dormant.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"未启用（{dormant.Count}）");
                    foreach (var feature in dormant)
                        sb.AppendLine($"[{feature.Kind}] {feature.Id}");
                }

                ShowText("特征与规则", sb.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Error("读取特征失败", ex);
                Notify("读取失败", ex.Message, error: true);
            }
        }

        private void OnShowTasks(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            try
            {
                var ledger = Ledger.Open(workspace);
                var tasks = TaskSet.Load(ledger.EvalDir);
                var sb = new StringBuilder();
                sb.AppendLine("目录：" + Path.Combine(ledger.EvalDir, "tasks"));
                sb.AppendLine($"共 {tasks.Tasks.Count} 道");
                foreach (var task in tasks.Tasks)
                {
                    sb.AppendLine();
                    sb.AppendLine($"{task.Id}  [{task.Source}]  检查 {task.Checks.Count} 项");
                    if (!string.IsNullOrWhiteSpace(task.Problem)) sb.AppendLine(task.Problem);
                    if (!string.IsNullOrWhiteSpace(task.Prompt)) sb.AppendLine("提示：" + task.Prompt);
                }

                ShowText("考核题", sb.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Error("读取考核题失败", ex);
                Notify("读取失败", ex.Message, error: true);
            }
        }

        private static string PayloadText(FeatureRecord feature)
        {
            if (feature == null) return "";
            try
            {
                var payload = feature.Payload;
                if (payload.ValueKind == JsonValueKind.String) return payload.GetString() ?? "";
                if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null) return "";
                if (payload.ValueKind == JsonValueKind.Object)
                    return string.Join(", ", payload.EnumerateObject().Select(p => $"{p.Name}={p.Value}"));
                return payload.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void ShowText(string title, string text)
        {
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5F),
                Text = text,
            };
            //AntdUI Modal 会对其内容控件再乘一次 DPI（实测 250% 下 ×2.5），
            //所以这里按 96-DPI 单位给尺寸，封顶值也换算到同一单位，最终物理尺寸才不会超出屏幕。
            var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            float scale = DeviceDpi / 96f;
            int maxW96 = screen.Width > 0 ? (int)(screen.Width * 4 / 5 / scale) : 700;
            int maxH96 = screen.Height > 0 ? (int)(screen.Height * 4 / 5 / scale) : 500;
            box.Width = Math.Min(620, maxW96);
            box.Height = Math.Min(420, maxH96);
            var owner = FindForm();
            if (owner != null) AntdUI.Modal.open(owner, title, (object)box);
        }

        private void OnOpenDrafts(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            using var form = new TaskDraftsForm(workspace, SmokeBaselineAsync);
            form.Changed += RefreshLedgerInfo;
            form.ShowDialog(FindForm());
            RefreshLedgerInfo();
        }

        private void OnOpenReports(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            using var form = new EvalReportForm(workspace);
            form.ShowDialog(FindForm());
            RefreshLedgerInfo();
        }

        /// <summary>在某题上跑一次现役版本，判断题目是否有区分度（通过=可能过于简单）。</summary>
        private async Task<string> SmokeBaselineAsync(EvalTask task, CancellationToken ct)
        {
            string workspace = Session.Workspace.Trim();
            var settings = _settings();
            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = settings.EndpointFor(model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return "缺少接口配置，未试跑基线。";

            var ledger = Ledger.Open(workspace);
            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var activation = ledger.Current;
            if (activation == null) return "没有激活版本，未试跑基线。";

            var runner = new AgentTaskRunner(
                _ => Reasoners.Create(settings, model, _effort, workspace),
                features, workspace, ledger.EvalDir, AppSettings.GlobalAgentsPath,
                globalSkillsPath: AppSettings.GlobalSkillsPath,
                contextLimit: ModelChoices.ContextLimit(model, settings));
            var outcome = await runner.RunAsync(task, activation, ct);
            return outcome.Passed
                ? Localization.T("BaselinePassed")
                : Localization.T("BaselineFailed");
        }

        private async void OnEvolve(object sender, EventArgs e)
        {
            if (_running) return;

            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            if (model.Length == 0)
            {
                Notify("缺少配置", "先在设置里选择模型。", error: true);
                return;
            }

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetRunning(true);
            SetState(SessionState.Running);
            _lblStatus.Text = Localization.T("Status") + ": 准备中…";

            try
            {
                var state = await Loop.EvolveAsync(workspace, model, _effort, cts.Token);
                while (state != null && state.State == "running")
                {
                    _lblStatus.Text = Localization.T("Status") + ": " + PhaseText(state);
                    await Task.Delay(2000, cts.Token);
                    state = await Loop.EvolveStateAsync(workspace, cts.Token);
                }

                if (state == null) return;
                string score = $"候选 {state.CandidatePassed}/{state.TaskCount}，基线 {state.BaselinePassed}/{state.TaskCount}";
                switch (state.State)
                {
                    case "done":
                        _lblStatus.Text = Localization.T("Status") +
                                          (state.Adopted ? $": 已采纳 v{state.CandidateVersion}" : ": 保留旧版");
                        Notify(state.Adopted ? "已更替" : "未更替",
                            state.Detail == null ? score : score + "\n" + state.Detail, error: false);
                        SetState(SessionState.Done);
                        break;
                    case "stopped":
                        _lblStatus.Text = Localization.T("Status") + ": 已停止";
                        SetState(SessionState.Interrupted);
                        break;
                    default:
                        _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                        SetState(SessionState.Interrupted);
                        Notify("演进失败", state.Error ?? score, error: true);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = Localization.T("Status") + ": 已停止";
                SetState(SessionState.Interrupted);
            }
            catch (Exception ex)
            {
                AppLog.Error("演进失败", ex);
                _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                SetState(SessionState.Interrupted);
                Notify("演进失败", ex.Message, error: true);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
                SetRunning(false);
                RefreshLedgerInfo();
            }
        }

        private static string PhaseText(LoopEvolveState state) => state.Phase switch
        {
            "evaluating" => string.Format(Localization.T("StatusReviewing"), state.TaskCount, state.TaskCount * 2),
            "proposing" => "构思中…",
            _ => "准备中…"
        };

        private async void OnRollback(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            int currentVersion;
            try
            {
                var current = Ledger.Open(workspace).Current;
                if (current == null || current.Version <= 1)
                {
                    Notify("没有更早的版本", "当前已是最早版本。");
                    return;
                }
                currentVersion = current.Version;
            }
            catch (Exception ex)
            {
                Notify("读取账本失败", ex.Message, error: true);
                return;
            }

            int target = currentVersion - 1;
            if (MessageBox.Show(FindForm(),
                    $"退回 v{target}？会写成 v{currentVersion + 1}，旧版本文件不动。",
                    "回滚", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            try
            {
                var version = await Loop.RollbackAsync(workspace, target);
                RefreshLedgerInfo();
                _lblStatus.Text = Localization.T("Status") + $": 已退回 v{target}（新版本 v{version}）";
                Notify("已回滚", $"当前版本 v{version}");
            }
            catch (Exception ex)
            {
                AppLog.Error("回滚失败", ex);
                Notify("回滚失败", ex.Message, error: true);
            }
        }

        private async void OnAddRule(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            var settings = _settings();
            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }
            using var form = new RuleForm((name, text, ct) =>
                RuleAssistant.ImproveAsync(
                    Reasoners.Create(settings, model, _effort, workspace), name, text, ct));
            if (form.ShowDialog(FindForm()) != DialogResult.OK) return;

            try
            {
                var id = await Loop.AddRuleAsync(workspace, form.RuleName, form.RuleText);
                RefreshLedgerInfo();
                Notify("规则已登记", $"{id}\n当前版本不变；下一轮演进时可被启用。");
            }
            catch (Exception ex)
            {
                AppLog.Error("登记规则失败", ex);
                Notify("登记规则失败", ex.Message, error: true);
            }
        }

        private static string Short(string text)
            => string.IsNullOrEmpty(text) || text.Length <= 8 ? text ?? "" : text[..8];

        private static string RelativeToWorkspace(string workspace, string path)
        {
            if (string.IsNullOrEmpty(workspace) || string.IsNullOrEmpty(path)) return path;
            try
            {
                string root = Path.GetFullPath(workspace);
                string full = Path.GetFullPath(path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full;
                return Path.GetRelativePath(root, full).Replace('\\', '/');
            }
            catch
            {
                return path;
            }
        }

        private List<LlmMessage> BuildHistory()
        {
            var turns = Session.Book.Turns;

            //最近一轮落盘过完整消息（含工具调用与结果）就以它为准：重放与模型上下文一致
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var stored = ChatStore.Transcript(turns[i]);
                if (stored == null || stored.Count == 0) continue;
                var list = new List<LlmMessage>(stored);
                for (int j = i + 1; j < turns.Count; j++)
                {
                    if (!string.IsNullOrEmpty(turns[j].UserText)) list.Add(new LlmMessage("user", turns[j].UserText));
                    if (!string.IsNullOrEmpty(turns[j].AssistantText)
                        && !turns[j].AssistantText.StartsWith(FailurePrefix, StringComparison.Ordinal))
                        list.Add(new LlmMessage("assistant", turns[j].AssistantText));
                }
                return list;
            }

            var fallback = new List<LlmMessage>();
            foreach (var turn in turns)
            {
                //失败轮的正文是接口报错，不是模型说过的话：别当上下文再喂回去，但 user 要留下
                if (!string.IsNullOrEmpty(turn.UserText))
                    fallback.Add(new LlmMessage("user", turn.UserText));
                if (!string.IsNullOrEmpty(turn.AssistantText)
                    && !turn.AssistantText.StartsWith(FailurePrefix, StringComparison.Ordinal))
                    fallback.Add(new LlmMessage("assistant", turn.AssistantText));
            }
            return fallback;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            if (!running) _interruptOn = false;   // A 侧开下一轮会自己清标记
            else SyncInterrupt();
            UpdateSendButton();
            UpdateEvolveButtonEnabled();
            _btnRollback.Enabled = !running;
            _btnRule.Enabled = !running;
            _btnDrafts.Enabled = !running;
            _btnReport.Enabled = !running;
        }

        private void UpdateEvolveButtonEnabled()
        {
            if (_running) { _btnEvolve.Enabled = false; return; }
            var ws = Session.Workspace.Trim();
            if (!Directory.Exists(ws)) { _btnEvolve.Enabled = false; return; }
            var model = (_model ?? "").Trim();
            if (model.Length == 0) { _btnEvolve.Enabled = false; return; }
            var (baseUrl, _) = _settings().EndpointFor(model);
            _btnEvolve.Enabled = !string.IsNullOrWhiteSpace(baseUrl);
        }

        private void OnChatCopy(object sender, ChatFlowActionEventArgs e)
        {
            var turn = e.Turn;
            if (turn == null) return;
            string text = $"用户：\n{turn.UserText}\n\nAI：\n{turn.AssistantText}".Trim();
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); } catch { }
        }

        /// <summary>壳注入：把选中的对话作为初始上下文开一个 Loop 会话（与 Loop 页「转给 worker」对称）。</summary>
        public Func<IReadOnlyList<ChatTurn>, bool> TransferToLoop { get; set; }

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

            var picked = Session.Book.Turns.Where(t => t.Selected).ToList();
            if (picked.Count == 0)
            {
                Notify("未选择对话", "先打开「选择」并勾选要处理的对话。", error: true);
                return;
            }

            switch (key)
            {
                case "copy": CopyTurns(picked); break;
                case "loop": TransferTurnsToLoop(picked); break;
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

        private void TransferTurnsToLoop(IReadOnlyList<ChatTurn> turns)
        {
            if (TransferToLoop == null || !TransferToLoop(turns))
            {
                Notify("无法转交", "宿主没有提供 Loop 入口。", error: true);
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
                    + "会从对话记录里删掉，不能恢复；之后的 KV cache 前缀也会从这里断开。",
                    "删除对话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var remove = new HashSet<ChatTurn>(turns);
            Session.Book.RemoveTurns(remove);
            foreach (var turn in remove) _turnCheckpoints.Remove(turn);
            _chat.SelectTurns(_ => false);
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id);
            RefreshChat();
            _chat.EnsureLastTurnVisible(true);
        }

        private async void OnChatUndo(object sender, ChatFlowActionEventArgs e)
        {
            var selected = e.Turn;
            if (selected == null) return;
            if (_running)
            {
                Notify("正在运行", "请先停止当前任务，再撤回对话。", error: true);
                return;
            }

            var turns = Session.Book.Turns;
            int index = -1;
            for (int i = 0; i < turns.Count; i++)
                if (ReferenceEquals(turns[i], selected)) { index = i; break; }
            if (index < 0) return;

            int count = turns.Count - index;
            if (MessageBox.Show(this,
                    $"将撤回从所选对话到最新一条的全部 {count} 组对话，并恢复这些对话写入过的文件。确定？",
                    "撤回对话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            // 服务端也删：只删本地的话，下一次发送拿到快照会把撤回的轮次重新补回来（看起来像两对重复对话）
            try
            {
                await Client.TruncateAsync(Session.Id, index);
            }
            catch (Exception ex)
            {
                AppLog.Warn("撤回对话时同步 Worker 失败：" + ex.Message);
            }

            var errors = new List<string>();
            bool hasSnapshot = _turnCheckpoints.TryGetValue(selected, out string checkpoint)
                && !string.IsNullOrEmpty(checkpoint);
            bool restoreFailed = hasSnapshot && !Snapshots.Restore(Session.Workspace, checkpoint);
            if (restoreFailed) errors.Add("git 快照恢复失败");

            var remove = new HashSet<ChatTurn>(turns.Skip(index));
            Session.Book.RemoveTurns(remove);
            foreach (var turn in remove) _turnCheckpoints.Remove(turn);
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id, _model, _effort);
            _chat.EnsureLastTurnVisible(true);

            string undone = selected.UserText?.Trim() ?? "";
            if (undone.Length > 0)
            {
                string draft = _txtInput.Text.Trim();
                _txtInput.Text = draft.Length == 0 ? undone : draft + "\n\n" + undone;
                _txtInput.SelectionStart = _txtInput.Text.Length;
                _txtInput.Focus();
            }

            string message = $"已撤回 {remove.Count} 组对话"
                + (restoreFailed ? "" : hasSnapshot ? "，工作区已还原（.gitignore 除外）" : "（该轮没有可用快照，未还原文件）");
            if (errors.Count > 0)
                message += "\n未完全恢复：" + string.Join("；", errors.Take(3));
            Notify("撤回完成", message, errors.Count > 0);
        }

        private void RefreshChat() => Session.Book.RaiseTextChanged();

        public void ReloadSurveys() => LoadSurveys();

        public void AnswerLastSurvey(string summary)
        {
            var last = OpenLoop.Core.Store.SurveyStore.Load(Session.Workspace, Session.Id).LastOrDefault();
            if (last == null) return;
            OpenLoop.Core.Store.SurveyStore.Answer(Session.Workspace, Session.Id, last.Id, summary);
            LoadSurveys();
        }

        public string AddSurvey(string title, string body, string option, int anchorTurn = -2)
        {
            var fields = new List<OpenLoop.Core.Store.SurveyFieldRecord>();
            if (!string.IsNullOrWhiteSpace(option))
                fields.Add(new OpenLoop.Core.Store.SurveyFieldRecord
                {
                    Key = "verdict",
                    Label = "你觉得这一步怎么样？",
                    Kind = "choice",
                    Options = ["合理", "有问题", "说不清"]
                });

            var record = OpenLoop.Core.Store.SurveyStore.Add(
                Session.Workspace, Session.Id, title, body,
                anchorTurn == -2 ? Session.Book.Turns.Count - 1 : anchorTurn,
                "#722ED1", fields);
            LoadSurveys();
            return record?.Id;
        }

        public string TraceDebug => null; // removed temporary diagnostic

        private void LoadSurveys()
        {
            if (Session == null) return;
            var records = OpenLoop.Core.Store.SurveyStore.Load(Session.Workspace, Session.Id);
            int pending = records.Count(r => r.State == "pending");
            _btnPendingSurveys.Text = Localization.T("ReviewPending") + " " + pending;
            _btnPendingSurveys.Visible = pending > 0;
            _lastPendingSurveyId = records.FirstOrDefault(r => r.State == "pending")?.Id;

            var surveys = records.Select(r => new ChatSurvey
            {
                Id = r.Id,
                AnchorTurnIndex = r.AnchorTurn,
                Title = r.Title,
                Body = r.Body,
                AccentHex = r.Accent,
                State = r.State,
                AnswerSummary = r.AnswerSummary,
                Fields = r.Fields.Select(f => new SurveyField
                {
                    Key = f.Key,
                    Label = f.Label,
                    Kind = f.Kind,
                    Options = f.Options,
                    Placeholder = f.Placeholder
                }).ToList()
            }).ToList();
            _chat.SetSurveys(surveys);
        }

        private void OnSurveyChanged(string workspace, string sessionId)
        {
            if (Session == null || !string.Equals(Session.Id, sessionId, StringComparison.Ordinal)) return;
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action(LoadSurveys)); return; }
            LoadSurveys();
        }

        private void OnOpenPendingSurveys(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastPendingSurveyId))
                _chat.ScrollToSurvey(_lastPendingSurveyId);
        }

        private string _lastPendingSurveyId;

        private void OnSurveySubmitted(object sender, ChatSurveyEventArgs e)
        {
            string summary = e.Answers.Count == 0
                ? "已提交"
                : string.Join(" · ", e.Answers.Select(kv => kv.Value));
            OpenLoop.Core.Store.SurveyStore.Answer(Session.Workspace, Session.Id, e.Survey.Id, summary);
        }

        private bool AskOnUi(string message)
        {
            if (IsDisposed || !IsHandleCreated) return false;
            if (InvokeRequired)
                return (bool)Invoke(new Func<bool>(() => AskOnUi(message)));
            var owner = FindForm();
            return MessageBox.Show(owner, message, "继续？", MessageBoxButtons.YesNo) == DialogResult.Yes;
        }
    }
}
