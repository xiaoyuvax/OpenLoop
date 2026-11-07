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
using OpenLoop.Service.Pending;
using OpenLoop.Service.Plugins;
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
        private bool _onlyFree = true;
        private bool _fillingProviders;
        private bool _layoutReady;
        private readonly Dictionary<ChatTurn, TurnUndo> _turnUndo = [];
        private sealed record TurnUndo(FileUndoLog Log, string Checkpoint);
        private string _tracePath = "";

        /// <summary>访问 A 的 /api/sessions；界面不再自己跑 Agent。</summary>
        private WorkerApiClient _client;
        private readonly HashSet<string> _answeredPrompts = [];

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
        }

        internal void Initialize(Session session, Func<AppSettings> settings)
        {
            Session = session;
            _settings = settings;

            var current = settings();
            _model = string.IsNullOrWhiteSpace(current.Model) ? "openrouter/free" : current.Model;
            _effort = current.ReasoningEffort ?? "";
            _onlyFree = current.OnlyFreeModels;

            _lblTitle.Text = session.Title;
            _lblPath.Text = UiStyle.ShortPath(session.Workspace);
            _tips.SetToolTip(_lblPath, session.Workspace);
            _chat.PlaceholderText = Localization.T("NoMessages");
            //每轮都落过盘，但要在这里读回来，否则重启后会话是空的
            var saved = ChatStore.Load(session.Workspace, session.Id);
            if (saved.Count > 0) session.Book.ReplaceAll(saved, null);
            _chat.UndoRequested += OnChatUndo;
            _chat.CopyRequested += OnChatCopy;
            _chat.Bind(session.Book);
            SelectEffort(_effort);
            _freeSwitch.Checked = _onlyFree;
            FillModels();
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort);
            _pending.Bind(_queue);
            _pending.EditRequested += BeginEdit;
            _pending.ImmediateRequested += OnImmediateRequested;
            _pending.Removed += DropEdit;
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
            FitLoopLabels();
            UpdateEvolveButtonEnabled();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Session Session { get; private set; }

        internal event Action<Session> StateChanged;
        internal event Action<string> OpenLoopRequested;
        internal event Action<Session, string> ComplaintRequested;
        internal event Action<Session, string> ForkRequested;
        internal event Action<Session> TitleChanged;
        internal event Action<string, IReadOnlyList<ChatTurn>> SideWorkEvicted;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Workspace => Session?.Workspace ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Mode => _mode;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status => _lblStatus.Text;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Model => _model;

        /// <summary>A 面自检：运行中标志 / 最后一轮正文长度 / 步数。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RunStatus => $"{(_running ? "running" : "idle")}|{Session.Book.Turns.LastOrDefault()?.AssistantText?.Length ?? 0}|{Session.Steps}";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ModelOptionCount => _selModel.Items.Count;

        /// <summary>提供商下拉里的条目（插件贡献的提供商会排在内置之后），给 Puppet/诊断读。</summary>
        public string ProviderOptionNames => string.Join("\n",
            _selProvider.Items.OfType<AntdUI.SelectItem>().Select(i => i.Text));

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ReasoningText =>
            Session.Book.Turns.Count == 0 ? "" : Session.Book.Turns[^1].ReasoningText;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ChatDiagnostics => _chat.GetLayoutDiagnostics();

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int PendingCount => _queue.Count;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string PendingSummary => string.Join("\n",
            _queue.Items.Select(i => (i.Immediate ? "[立即] " : "[等本轮] ") + i.Text));

        /// <summary>A 面驱动：移除第 index 个排队请求。</summary>
        public void RemovePending(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            var item = _queue.Items[index];
            _queue.Remove(item);
            DropEdit(item);
            _pending.Sync();
        }

        /// <summary>A 面驱动：把第 index 个排队请求放回输入框编辑。</summary>
        public void RetractPending(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            BeginEdit(_queue.Items[index]);
        }

        /// <summary>A 面驱动：把第 index 个排队请求切换为「立即」。</summary>
        public void SendPendingNow(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            OnImmediateRequested(_queue.Toggle(_queue.Items[index]));
        }

        /// <summary>A 面驱动：恢复被 Stop 暂停的队列并按序发送。</summary>
        public void SendAllPending()
        {
            _queue.Paused = false;
            DrainQueue();
        }

        /// <summary>Puppet 用：只入队不发送（验证队列控件与两种模式）。</summary>
        public void EnqueueText(string text)
        {
            if (_queue.Add(text, _mode) == null) return;
            _queue.Paused = false;
            _pending.Sync();
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

        private void UpdateSideVisibility(int index)
        {
            _loopFlow.Visible = index == 0;
            _detailFlow.Visible = index == 1;
            _sideBtw.Visible = index == 2;
            if (index == 1) ApplyDetail();
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

        public void SendMode(string mode = "task")
        {
            SelectMode(mode);
            Submit();
            SetMode("task");
        }

        /// <summary>A 面驱动：点「演进」（走 I 的 /api/loop/evolve）。</summary>
        public void StartEvolve() => OnEvolve(this, EventArgs.Empty);

        public void Shutdown()
        {
            _cts?.Cancel();
            _sideBtw.Shutdown();
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id);
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
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void ApplyLayout()
        {
            if (IsDisposed || Width < Dip(800) || Height < Dip(300)) return;

            ApplySplit(_mainSplit, vertical: false,
                panel1Min: Dip(220), panel2Min: Dip(130), targetPanel2: Dip(196));
            ApplySplit(_upperSplit, vertical: true,
                panel1Min: Dip(400), panel2Min: Dip(280), targetPanel2: Dip(320));
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
            if (_cmdPopup.IsOpen)
            {
                if (e.KeyCode == Keys.Down) { _cmdPopup.MoveSelection(1); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Up) { _cmdPopup.MoveSelection(-1); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Escape) { _cmdPopup.Close(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Enter && !e.Control || e.KeyCode == Keys.Tab) { _cmdPopup.Accept(); e.SuppressKeyPress = true; return; }
            }

            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
        }

        private void OnInputTextChanged(object sender, EventArgs e)
        {
            UpdateSendButton();
            UpdateCommandPopup();
        }

        private static readonly (string Command, string Description)[] Commands =
        {
            ("/btw", "在右侧栏用当前上下文跑一个只读旁路任务"),
            ("/complain", "把对 Worker 表现的不满发给 Loop 立即处理"),
            ("/fork", "复制当前上下文开一个新会话（可继续对话）并跳转")
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
                SideWorkEvicted?.Invoke(Session.Workspace, previous);
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
            SideWorkEvicted?.Invoke(Session.Workspace, turns);
            _sideBtw.Reset();
        }

        /// <summary>用当前模型给会话起标题：首轮结束后自动生成一次，也可点「总结」重算覆盖。</summary>
        private async Task SummarizeTitleAsync(bool force)
        {
            if (!force && !string.IsNullOrWhiteSpace(Session.CustomTitle)) return;
            var turns = Session.Book.Turns;
            if (turns.Count == 0) return;

            string model = _model?.Trim() ?? "";
            var (baseUrl, _) = _settings().EndpointFor(model);
            if (baseUrl?.Trim().Length == 0 || model.Length == 0) return;

            var messages = new List<LlmMessage>
            {
                new("system", "你给对话起标题。只输出标题本身：不超过 12 个汉字，不加引号、不加句号、不解释。"),
                new("user", TitleInput(turns))
            };
            try
            {
                var reasoner = Reasoners.Create(_settings(), model, "", Session.Workspace);
                var completion = await reasoner.CompleteAsync(messages, CancellationToken.None);
                string title = CleanTitle(completion?.Content);
                if (title.Length == 0) return;
                Session.CustomTitle = title;
                _lblTitle.Text = title;
                TitleChanged?.Invoke(Session);
            }
            catch (Exception ex)
            {
                AppLog.Warn("生成标题失败：" + ex.Message);
                if (force) Notify("总结失败", ex.Message, error: true);
            }
        }

        /// <summary>给标题模型的输入：最近几轮问答，助手正文截断，够判断主题即可。</summary>
        private static string TitleInput(IReadOnlyList<ChatTurn> turns)
        {
            var sb = new StringBuilder();
            foreach (var turn in turns.Skip(Math.Max(0, turns.Count - 4)))
            {
                sb.Append("用户：").AppendLine(Trim(turn.UserText, 300));
                sb.Append("助手：").AppendLine(Trim(turn.AssistantText, 300));
            }
            return sb.ToString();
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string line = raw.Replace("\r", "\n").Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "";
            line = line.Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '#', '*', ' ');
            line = line.TrimEnd('。', '.', '：', ':');
            return line.Length <= 20 ? line : line[..20];
        }

        /// <summary>运行中：输入框有内容 = 发送（排队），清空 = 停止；编辑排队项时按钮是「保存」。</summary>
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

        private void Submit()
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
            if (IsCommand(text, "/btw") || IsCommand(text, "/complain") || IsCommand(text, "/fork")) return;

            if (_running)
            {
                if (text.Length == 0)
                {
                    _queue.Paused = true;
                    StopRun();
                    return;
                }
                Enqueue(text, _mode);
                _txtInput.Clear();
                SetMode("task");
                return;
            }

            if (text.Length == 0) return;
            _txtInput.Clear();
            SendTask(_mode, text);
            SetMode("task");
        }

        private void OnSendClick(object sender, EventArgs e) => Submit();

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
                ComplaintRequested?.Invoke(Session, ComplaintText(complaint));
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

            return false;
        }

        internal static bool IsCommand(string text, string command)
            => text.Equals(command, StringComparison.OrdinalIgnoreCase)
                || (text.StartsWith(command, StringComparison.OrdinalIgnoreCase)
                    && text.Length > command.Length
                    && char.IsWhiteSpace(text[command.Length]));

        /// <summary>转给 Loop 的用户反馈：带上最近一轮，Loop 才知道指的是哪次运行。</summary>
        private string ComplaintText(string complaint)
        {
            var sb = new StringBuilder();
            sb.Append("[用户反馈] 对 Worker 表现不满（会话「").Append(Session.Title).Append("」）：").AppendLine(complaint);
            var turns = Session.Book.Turns;
            if (turns.Count == 0) return sb.ToString().TrimEnd();
            var last = turns[^1];
            sb.Append("最近任务：").AppendLine(Trim(last.UserText, 300));
            sb.Append("最近回复：").AppendLine(Trim(last.AssistantText, 500));
            return sb.ToString().TrimEnd();
        }

        private static string Trim(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

        private void Enqueue(string text, string mode)
        {
            _queue.Add(text, mode);
            _queue.Paused = false;
            _pending.Sync();
        }

        /// <summary>「立即」：先让当前轮在下一个 step 前停下，停下后 DrainQueue 会先发这一条。</summary>
        private void OnImmediateRequested(PendingItem item)
        {
            if (item == null || !item.Immediate) return;
            _queue.Paused = false;
            _pending.Sync();
            if (!_running)
            {
                DrainQueue();
                return;
            }
            RequestInterrupt();
        }

        private async void RequestInterrupt()
        {
            try
            {
                await Client.InterruptAsync(Session.Id);
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
            _txtInput.Text = item.Text;
            _txtInput.SelectionStart = _txtInput.Text.Length;
            _txtInput.Focus();
            UpdateSendButton();
        }

        /// <summary>当前轮结束后调用：优先「立即」，其次按队列顺序。</summary>
        private void DrainQueue()
        {
            if (IsDisposed || _running) return;

            var next = _queue.TakeNext();
            if (next == null) return;
            _pending.Sync();
            SendTask(next.Tag, next.Text);
        }

        private void OnModeChanged(object sender, AntdUI.IntEventArgs e)
        {
            _mode = e.Value switch
            {
                1 => "criterion",
                2 => "correction",
                _ => "task"
            };
        }

        private void SetMode(string tag)
        {
            _mode = tag;
            int index = tag switch { "criterion" => 1, "correction" => 2, _ => 0 };
            if (_modeControl.SelectIndex != index) _modeControl.SelectIndex = index;
        }

        private void OnModelChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingModels) return;
            if (_selModel.SelectedValue is not string id || string.IsNullOrWhiteSpace(id)) return;
            _model = id;
            ApplyDetail();
            Persist(s => s.Model = id);
            UpdateEvolveButtonEnabled();
        }

        private void OnEffortChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingModels) return;
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

        private void FitLoopLabels()
        {
            int width = Math.Max(Dip(120), _loopFlow.ClientSize.Width - Dip(28));
            foreach (var label in new[] { _lblDef, _lblTasks, _lblCost, _lblStatus })
                label.MaximumSize = new Size(width, 0);

            _lblTrace.Width = width;
            int height = TextRenderer.MeasureText(
                _lblTrace.Text,
                _lblTrace.Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height;
            _lblTrace.Height = Math.Max(Dip(18), height + Dip(2));
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
                _selModel.Items.Clear();
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var visible = ModelChoices.FilterByProvider(
                    ModelChoices.Build(_settings(), _onlyFree), provider);

                foreach (var model in visible)
                    _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                    {
                        Tag = model.Id,
                        IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
                        SubText = model.IsFree ? "免费" : null
                    });

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
            }
            finally
            {
                _fillingModels = false;
            }
        }

        private void UpdateContext(int? tokens)
        {
            int limit = ModelChoices.ContextLimit(_model);
            float ratio = tokens.HasValue
                ? Math.Clamp(tokens.Value / (float)limit, 0F, 1F)
                : 0F;
            _ctx.Value = ratio;
            _ctx.Text = tokens.HasValue ? $"{ratio * 100F:0}%" : "—";
            _lblDCtx.Text = Localization.T("Context") + $": {(tokens.HasValue ? tokens.Value.ToString("N0") : "—")} / {limit:N0}";
            ApplyDetail();
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

        private async void SendTask(string tag, string text)
        {
            if (_running) return;

            var settings = _settings();
            if (string.IsNullOrWhiteSpace(text)) return;

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

            ChatTurn turn = null;

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var ct = cts.Token;

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

                snapshot = await client.SendAsync(Session.Id, tag, text, ct);
                if (snapshot == null) throw new InvalidOperationException("Worker 上找不到这个会话");
                ApplySnapshot(snapshot);
                //发出即落盘：进程被杀时最多丢流式中的半条回复，而不是整轮对话
                ChatStore.Save(workspace, Session.Book.Turns, Session.Id);

                while (snapshot.State == WorkerSessionHost.Running)
                {
                    await Task.Delay(250, ct);
                    snapshot = await client.GetAsync(Session.Id, ct);
                    if (snapshot == null) throw new InvalidOperationException("Worker 上找不到这个会话");
                    snapshot = await AnswerPromptsAsync(client, snapshot, ct);
                    ApplySnapshot(snapshot);
                }
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
            turn = Session.Book.Turns.Count > 0 ? Session.Book.Turns[^1] : null;
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
            _chat.ScrollToEnd();
            RefreshLedgerInfo();
            ChatStore.Save(workspace, Session.Book.Turns, Session.Id);
            // 首轮成功结束后由所选模型生成短标题（已有人工/历史标题就不再覆盖）
            if (outcome == "ok" && Session.Book.Turns.Count == 1)
                _ = SummarizeTitleAsync(force: false);
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(DrainQueue));
        }

        /// <summary>把 A 的快照映射到本地对话：新轮次建 turn，已有的更新正文/工具行/思考区。</summary>
        private void ApplySnapshot(WorkerSessionSnapshot snapshot)
        {
            if (snapshot == null) return;

            bool structure = false;
            while (Session.Book.Turns.Count < snapshot.Turns.Count)
            {
                var remote = snapshot.Turns[Session.Book.Turns.Count];
                var added = new ChatTurn
                {
                    User = new ChatMessage("user", remote.User ?? ""),
                    Assistant = new ChatMessage("assistant", remote.Assistant ?? ""),
                    Streaming = remote.Streaming,
                    ThinkingExpanded = _settings().ThinkingExpandedByDefault
                };
                Session.Book.AddTurn(added);
                _turnUndo[added] = new TurnUndo(null, remote.Checkpoint);
                structure = true;
            }

            for (int i = 0; i < snapshot.Turns.Count; i++)
            {
                var remote = snapshot.Turns[i];
                var turn = Session.Book.Turns[i];
                turn.ReasoningText = remote.Reasoning ?? "";
                turn.ToolCallLines = remote.ToolLines ?? "";
                turn.ToolTraceText = remote.ToolLines ?? "";
                turn.ToolStepCount = remote.Steps;
                turn.ToolTotalMs = remote.Ms;
                turn.Streaming = remote.Streaming;
                if (!string.IsNullOrEmpty(remote.Checkpoint))
                    _turnUndo[turn] = new TurnUndo(null, remote.Checkpoint);
                turn.AssistantText = LlmTextSanitizer.StripSafetyLines(remote.Assistant ?? "");
                if (remote.Transcript is { Count: > 0 })
                    ChatStore.AttachTranscript(turn, remote.Transcript);
            }

            if (structure) RefreshChat();
            var last = Session.Book.Turns.LastOrDefault();
            if (last != null) _chat.UpdateStreamingMessage(last, last.AssistantText, force: structure);
            if (!string.IsNullOrEmpty(snapshot.TraceDirectory)) SetTrace(snapshot.Workspace, snapshot.TraceDirectory);
            if (snapshot.ActivationVersion > 0)
                _lblDef.Text = $"def: v{snapshot.ActivationVersion} · {snapshot.FeatureCount} {Localization.T("Features")} · {snapshot.RuleCount} {Localization.T("Rules")}";
            if (structure) _chat.ScrollToEnd();
        }

        /// <summary>
        /// 这一轮不是壳发起的（会话是直接走 A 的 API / 网页 / 别的 agent 跑的）：把 A 的快照并进界面，
        /// 打开的页面上能看到实时进度，跑完也能看到定稿。壳自己正在跑时不插手。
        /// </summary>
        internal void SyncFromWorker(WorkerSessionSnapshot snapshot)
        {
            if (snapshot == null || _running || Session == null || IsDisposed || !IsHandleCreated) return;

            ApplySnapshot(snapshot);
            Session.Steps = snapshot.Turns?.LastOrDefault()?.Steps ?? Session.Steps;
            UpdateContext(snapshot.ContextTokens);
            _lblStatus.Text = Localization.T("Status") + (snapshot.State == WorkerSessionHost.Running
                ? ": 运行中…"
                : string.IsNullOrEmpty(snapshot.Outcome) ? "" : ": " + snapshot.Outcome);
            if (snapshot.Cost.HasValue)
                _lblCost.Text = Localization.T("Cost") + $": {snapshot.Cost.Value:0.0000} · {Session.Steps} {Localization.T("Steps")}";
        }

        /// <summary>回答 A 挂起的审批/确认；同一个 prompt 只答一次。</summary>
        private async Task<WorkerSessionSnapshot> AnswerPromptsAsync(WorkerApiClient client,
            WorkerSessionSnapshot snapshot, CancellationToken ct)
        {
            foreach (var prompt in snapshot.Prompts ?? [])
            {
                if (prompt?.Id == null || !_answeredPrompts.Add(prompt.Id)) continue;
                int decision = AnswerPrompt(prompt);
                var updated = await client.ApproveAsync(Session.Id, prompt.Id, decision, ct);
                if (updated != null) snapshot = updated;
            }
            return snapshot;
        }

        /// <summary>0 = 拒绝，1 = 允许一次，2 = 同类后续都允许（记进设置，A 下次直接放行）。</summary>
        private int AnswerPrompt(WorkerPromptView prompt)
        {
            if (prompt.Kind == "continue") return AskOnUi(prompt.Text) ? 1 : 0;

            var kind = prompt.Category == "outside" ? ApprovalKind.OutsideWorkspace : ApprovalKind.SensitiveAction;
            var decision = ApprovalGate.Ask(this, _settings,
                new ApprovalRequest(kind, prompt.Tool, prompt.Detail, prompt.Reason));
            if (decision == ApprovalDecision.AllowAlways)
            {
                var settings = _settings();
                if (kind == ApprovalKind.OutsideWorkspace) settings.AllowOutsideWorkspace = true;
                else settings.AllowSensitiveActions = true;
                settings.Save();
            }
            return (int)decision;
        }

        private void Notify(string title, string text, bool error = false)
        {
            var form = FindForm();
            if (form == null) return;
            if (error)
                AntdUI.Notification.error(form, title, text, AntdUI.TAlignFrom.TR, null, 5);
            else
                AntdUI.Notification.success(form, title, text, AntdUI.TAlignFrom.TR, null, 3);
        }

        private void RefreshLedgerInfo()
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                _lblDef.Text = "def: —";
                _lblTasks.Text = Localization.T("Tasks") + ": —";
                _btnDrafts.Text = Localization.T("TaskDrafts");
                _btnReport.Text = Localization.T("EvalReports");
                return;
            }
            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Activations().LastOrDefault();
                if (current == null)
                {
                    _lblDef.Text = "def: —（首次运行后初始化）";
                }
                else
                {
                    var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                    int rules = current.Features.Count(r => r?.Id != null
                        && (features.TryGetValue(r.Id, out var f)
                            ? f.Kind == "rule"
                            : r.Id.StartsWith("rule.", StringComparison.Ordinal)));
                    _lblDef.Text = $"def: v{current.Version} · {current.Features.Count} {Localization.T("Features")} · {rules} {Localization.T("Rules")}  ›";
                }
                _lblTasks.Text = Localization.T("Tasks") + $": {TaskSet.Load(ledger.EvalDir).Tasks.Count} {Localization.T("TasksUnit")}  ›";
                _tips.SetToolTip(_lblTasks, "点击查看考核题\n" + Path.Combine(ledger.EvalDir, "tasks"));

                int drafts = TaskDrafts.PendingCount(workspace);
                _btnDrafts.Text = Localization.T("TaskDrafts") + (drafts > 0 ? " " + drafts : "");
                int reports = EvalReportStore.List(workspace).Count;
                _btnReport.Text = Localization.T("EvalReports") + (reports > 0 ? " " + reports : "");
            }
            catch (Exception ex)
            {
                _lblDef.Text = "def: 读取失败：" + ex.Message;
                _lblTasks.Text = Localization.T("Tasks") + ": —";
                _btnDrafts.Text = Localization.T("TaskDrafts");
                _btnReport.Text = Localization.T("EvalReports");
            }
        }

        internal void SetTraceDebug(string path) => SetTrace(Session.Workspace, path);

        private void SetTrace(string workspace, string path)
        {
            _tracePath = path ?? "";
            _lblTrace.Text = "trace: " + RelativeToWorkspace(workspace, _tracePath);
            _tips.SetToolTip(_lblTrace, _tracePath);
            FitLoopLabels();
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
                contextLimit: ModelChoices.ContextLimit(model));
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
                    if (turns[j].AssistantText.StartsWith(FailurePrefix, StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrEmpty(turns[j].UserText)) list.Add(new LlmMessage("user", turns[j].UserText));
                    if (!string.IsNullOrEmpty(turns[j].AssistantText)) list.Add(new LlmMessage("assistant", turns[j].AssistantText));
                }
                return list;
            }

            var fallback = new List<LlmMessage>();
            foreach (var turn in turns)
            {
                //失败轮次的正文是接口报错，不是模型说过的话：别当上下文再喂回去
                if (turn.AssistantText.StartsWith(FailurePrefix, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(turn.UserText))
                    fallback.Add(new LlmMessage("user", turn.UserText));
                if (!string.IsNullOrEmpty(turn.AssistantText))
                    fallback.Add(new LlmMessage("assistant", turn.AssistantText));
            }
            return fallback;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            UpdateSendButton();
            _btnEvolve.Enabled = !running;
            _btnRollback.Enabled = !running;
            _btnRule.Enabled = !running;
            _btnDrafts.Enabled = !running;
            _btnReport.Enabled = !running;
            _modeControl.Enabled = !running;
            _selProvider.Enabled = !running;
            _selModel.Enabled = !running;
            _selEffort.Enabled = !running;
        }

        private void UpdateEvolveButtonEnabled()
        {
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

        private void OnChatUndo(object sender, ChatFlowActionEventArgs e)
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

            int restored = 0;
            var errors = new List<string>();
            for (int i = turns.Count - 1; i >= index; i--)
            {
                if (!_turnUndo.TryGetValue(turns[i], out var undo) || undo.Log == null) continue;
                var (n, errs) = undo.Log.UndoAll();
                restored += n;
                errors.AddRange(errs);
            }

            if (_turnUndo.TryGetValue(selected, out var selectedUndo)
                && !string.IsNullOrEmpty(selectedUndo.Checkpoint)
                && !Snapshots.Restore(Session.Workspace, selectedUndo.Checkpoint))
                errors.Add("git 快照恢复失败");

            var remove = new HashSet<ChatTurn>(turns.Skip(index));
            Session.Book.RemoveTurns(remove);
            foreach (var turn in remove) _turnUndo.Remove(turn);
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id);
            _chat.EnsureLastTurnVisible(true);

            string undone = selected.UserText?.Trim() ?? "";
            if (undone.Length > 0)
            {
                string draft = _txtInput.Text.Trim();
                _txtInput.Text = draft.Length == 0 ? undone : draft + "\n\n" + undone;
                _txtInput.SelectionStart = _txtInput.Text.Length;
                _txtInput.Focus();
            }

            string message = $"已撤回 {remove.Count} 组对话，恢复 {restored} 次文件写入。";
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
