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
            _chat.UndoRequested += OnChatUndo;
            _chat.CopyRequested += OnChatCopy;
            _agentChat.CopyRequested += OnChatCopy;
            _chat.Bind(session.Book);
            SelectEffort(_effort);
            _freeSwitch.Checked = _onlyFree;
            FillModels();
            SetMode("task");
            RefreshLedgerInfo();
            UpdateContext(null);

            _lblStatus.TextChanged += OnStatusTextChanged;
            _loopFlow.SizeChanged += (_, _) => FitLoopLabels();
            _chat.SurveySubmitted += OnSurveySubmitted;
            _chat.LinkActivated += OnAgentLink;
            OpenLoop.Core.Store.SurveyStore.Changed += OnSurveyChanged;
            _chat.SurveySkipped += (_, _) => { };
            LoadSurveys();
            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) => ModelCatalog.Changed -= OnCatalogChanged;
            _tips.SetToolTip(_lblDef, "点击查看特征与规则");
            _tips.SetToolTip(_lblTasks, "点击查看考核题");
            FitLoopLabels();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Session Session { get; private set; }

        internal event Action<Session> StateChanged;
        internal event Action<string> OpenLoopRequested;

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

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ModelOptionCount => _selModel.Items.Count;

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
            _loopFlow.Visible = index != 1;
            _detailFlow.Visible = index == 1;
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
            SendTask(_mode);
            SetMode("task");
        }

        public void Shutdown()
        {
            _cts?.Cancel();
            ChatStore.Save(Session.Workspace, Session.Book.Turns, Session.Id);
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

        private static void ApplySplit(
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

        private void OnSidePageChanged(object sender, AntdUI.IntEventArgs e)
        {
            bool detail = e?.Value == 1;
            _loopFlow.Visible = !detail;
            _detailFlow.Visible = detail;
            if (detail) ApplyDetail();
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
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
        }

        private void Submit()
        {
            if (_running)
            {
                _cts?.Cancel();
                return;
            }
            SendTask(_mode);
            SetMode("task");
        }

        private void OnSendClick(object sender, EventArgs e) => Submit();

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
                foreach (var provider in _settings?.Invoke()?.Providers ?? [])
                {
                    _selProvider.Items.Add(new AntdUI.SelectItem(provider.Name)
                    {
                        Tag = provider,
                        SubText = provider.Kind
                    });
                }

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
        }

        private void OnProviderChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_fillingProviders || _fillingModels) return;
            FillModels();
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
            int limit = AgentContext.DefaultContextLimit;
            float ratio = tokens.HasValue
                ? Math.Clamp(tokens.Value / (float)limit, 0F, 1F)
                : 0F;
            _ctx.Value = ratio;
            _ctx.Text = tokens.HasValue ? $"{ratio * 100F:0}%" : "—";
            _lblDCtx.Text = Localization.T("Context") + $": {(tokens.HasValue ? tokens.Value.ToString("N0") : "—")} / {limit:N0}";
            ApplyDetail();
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

        private async void SendTask(string tag)
        {
            if (_running) return;

            var settings = _settings();
            string text = _txtInput.Text.Trim();
            if (text.Length == 0) return;

            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            var (baseUrl, apiKey) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }

            bool fullAccess = settings.FullAccess;
            var undoLog = new FileUndoLog();
            var scope = new WorkspaceScope(workspace, fullAccess ? [] : null, fullAccess) { Undo = undoLog };
            var skillCatalog = SkillCatalog.Load(workspace);
            string checkpoint = Snapshots.Create(workspace, "worker");
            if (checkpoint != null) AppLog.Info("snapshot " + checkpoint);
            var reasoner = new OpenAiCompatibleReasoner(baseUrl, model, apiKey, _effort);

            Ledger ledger;
            ActivationRecord activation;
            AgentPolicy policy;
            IReadOnlyList<string> rules;
            IReadOnlyList<SkillInfo> skills = [];
            IReadOnlyList<string> skillProblems = [];
            try
            {
                ledger = Ledger.Open(workspace);
                ledger.EnsureInitialized();
                SkillCatalog.RegisterFeatures(ledger, skillCatalog);
                activation = ledger.Current;
                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                policy = FeatureCatalog.PolicyFrom(activation, features);
                rules = FeatureCatalog.RulesFrom(activation, features);
                skills = SkillCatalog.Active(activation, features, skillCatalog, out skillProblems);
            }
            catch (Exception ex)
            {
                AppLog.Error("读取账本失败", ex);
                Notify("读取账本失败", ex.Message, error: true);
                return;
            }

            if (skillProblems.Count > 0)
                AppLog.Warn("技能不可用：" + string.Join("；", skillProblems));
            Func<OpenLoop.Core.Tools.Builtin.CommandRequest, bool> approve =
                fullAccess ? _ => true : ApproveCommand;
            var tools = new ToolRegistry(ToolRegistry.Workspace(scope, approve, skills, fullAccess).Tools.Concat(
            [
                new OpenLoop.Core.Tools.Builtin.SpawnAgentTool(
                    () => new OpenAiCompatibleReasoner(baseUrl, model, apiKey, _effort),
                    names => ChildRegistry(scope, names),
                    workspace,
                    () => (Session.Id, Session.Book.Turns.Count - 1))
            ]));

            TraceWriter trace;
            try
            {
                trace = TraceWriter.Start(workspace, tag, text, model);
            }
            catch (Exception ex)
            {
                AppLog.Error("创建 trace 失败", ex);
                Notify("创建 trace 失败", ex.Message, error: true);
                return;
            }

            var history = BuildHistory();
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", text),
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true
            };

            Session.Book.AddTurn(turn);
            _turnUndo[turn] = new TurnUndo(undoLog, checkpoint);
            _txtInput.Clear();
            SetRunning(true);
            SetState(SessionState.Running);
            SetTrace(workspace, trace.DirectoryPath);
            _lblStatus.Text = Localization.T("Status") + ": 运行中…";
            _chat.ScrollToEnd();
            trace.SkillSet(skills, skillProblems);
            trace.Message("user", $"[{tag}] {text}");

            var streamBuffer = new StringBuilder();
            Action<string> onDelta = piece =>
            {
                lock (streamBuffer) streamBuffer.Append(piece);
                string snapshot;
                lock (streamBuffer) snapshot = streamBuffer.ToString();
                if (LooksLikeToolCallText(snapshot)) return;
                RunOnUi(() => _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(snapshot)));
            };

            var loop = new AgentLoop(tools, reasoner, policy,
                onStep: step =>
                {
                    RunOnUi(() =>
                    {
                        turn.ToolStepCount++;
                        turn.ToolTotalMs += step.DurationMs;
                        string line = $"{(step.Ok ? "✓" : "✗")} {step.Tool}({step.ArgsSummary}) · {step.DurationText}";
                        turn.ToolCallLines = string.IsNullOrEmpty(turn.ToolCallLines)
                            ? line
                            : turn.ToolCallLines + "\n" + line;
                        turn.ToolTraceText = string.IsNullOrEmpty(turn.ToolTraceText)
                            ? line
                            : turn.ToolTraceText + "\n" + line;
                        turn.ToolStatusText = null;
                        RefreshChat();
                        _chat.ScrollToEnd();
                    });
                    trace.Step(step);
                },
                onToolStart: (name, _) =>
                {
                    lock (streamBuffer) streamBuffer.Clear();
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
                null, rules,
                settings.UseAgentsMd
                    ? AgentsInstructions.Load(workspace, AppSettings.GlobalAgentsPath)
                    : null);
            history.Insert(0, new LlmMessage("system", systemPrompt));
            trace.SystemPrompt(systemPrompt);
            _lblDef.Text = $"def: v{activation.Version} · {activation.Features.Count} {Localization.T("Features")} · {rules.Count} {Localization.T("Rules")}";

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            string outcome = "error";
            int stepCount = 0;
            decimal? cost = null;

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, text, ct, AgentContext.DefaultContextLimit, () => ct.IsCancellationRequested, onDelta),
                    ct);

                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = CleanAnswer(result.FinalAnswer, result.FinishReason);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser ? "stopped"
                    : "ok";
                stepCount = result.Steps.Count;
                cost = result.ChatCost;
                trace.Finish(outcome, turn.AssistantText, result.ChatCost, result.Guards);

                _lblCost.Text = Localization.T("Cost") + $": {(result.ChatCost.HasValue ? result.ChatCost.Value.ToString("0.0000") : "—")} · {result.Steps.Count} {Localization.T("Steps")}";
                _lblStatus.Text = Localization.T("Status") + $": {outcome}" + (result.Compressed ? "（上下文已压缩）" : "");
                UpdateContext(result.ContextTokens);

                if (outcome == "ok")
                    Notify("任务完成", $"{result.Steps.Count} 步 · 成本 {(result.ChatCost?.ToString("0.0000") ?? "—")}");
                else if (outcome == "budget")
                    Notify("预算用尽", "本次执行到达步数上限。", error: true);
            }
            catch (OperationCanceledException)
            {
                outcome = "stopped";
                turn.Streaming = false;
                turn.ToolStatusText = null;
                if (string.IsNullOrEmpty(turn.AssistantText)) turn.AssistantText = "（已停止）";
                trace.Message("assistant", turn.AssistantText);
                trace.Finish("stopped", turn.AssistantText);
                _lblStatus.Text = Localization.T("Status") + ": stopped";
            }
            catch (Exception ex)
            {
                AppLog.Error("任务执行失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = "执行失败：" + ex.Message;
                trace.Finish("error", turn.AssistantText);
                _lblStatus.Text = Localization.T("Status") + ": error";
                Notify("执行失败", ex.Message, error: true);
            }
            finally
            {
                try
                {
                    ledger.AppendRun(new RunRecord
                    {
                        Id = Ledger.NewId("R"),
                        ActivationVersion = activation.Version,
                        Task = text.Length <= 200 ? text : text[..200],
                        Outcome = outcome,
                        Steps = stepCount,
                        Cost = cost,
                        TraceDir = trace.DirectoryPath,
                        At = DateTimeOffset.UtcNow.ToString("O")
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("账本写入失败", ex);
                    _lblStatus.Text += $"（账本写入失败：{ex.Message}）";
                }

                trace.Dispose();
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                Session.Steps = stepCount;
                SetState(outcome is "ok" or "budget" ? SessionState.Done : SessionState.Interrupted);
                RefreshChat();
                _chat.ScrollToEnd();
                RefreshLedgerInfo();
                ChatStore.Save(workspace, Session.Book.Turns, Session.Id);
            }
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
                Width = Dip(620),
                Height = Dip(420)
            };
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
            var (baseUrl, apiKey) = settings.EndpointFor(model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return "缺少接口配置，未试跑基线。";

            var ledger = Ledger.Open(workspace);
            ledger.EnsureInitialized();
            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var activation = ledger.Current;
            if (activation == null) return "没有激活版本，未试跑基线。";

            var runner = new AgentTaskRunner(
                _ => new OpenAiCompatibleReasoner(baseUrl, model, apiKey, _effort),
                features, workspace, ledger.EvalDir, AppSettings.GlobalAgentsPath);
            var outcome = await runner.RunAsync(task, activation, ct);
            return outcome.Passed
                ? Localization.T("BaselinePassed")
                : Localization.T("BaselineFailed");
        }

        private async void OnEvolve(object sender, EventArgs e)
        {
            if (_running) return;

            var settings = _settings();
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            string model = _model?.Trim() ?? "";
            var (baseUrl, apiKey) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }

            Ledger ledger;
            TaskSet tasks;
            Dictionary<string, FeatureRecord> features;
            ActivationRecord baseline;
            try
            {
                ledger = Ledger.Open(workspace);
                ledger.EnsureInitialized();
                SkillCatalog.RegisterFeatures(ledger, SkillCatalog.Load(workspace));
                tasks = TaskSet.Load(ledger.EvalDir);
                if (tasks.Tasks.Count == 0)
                {
                    Notify("没有考核题", "先把考核题放进 " + Path.Combine(ledger.EvalDir, "tasks"), error: true);
                    return;
                }
                features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                baseline = ledger.Current;
            }
            catch (Exception ex)
            {
                AppLog.Error("读取账本失败", ex);
                Notify("读取账本失败", ex.Message, error: true);
                return;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            SetRunning(true);
            SetState(SessionState.Running);
            _lblStatus.Text = Localization.T("Status") + ": 构思中…";

            try
            {
                var loopAgent = new LoopAgent(CreateLoopReasoner());
                var proposal = await loopAgent.ProposeAsync(loopAgent.BuildPacket(ledger), baseline, features, ct);
                if (proposal.Error != null)
                {
                    _lblStatus.Text = Localization.T("Status") + ": 候选被拒";
                    string problems = proposal.Problems.Count == 0 ? "" : "\n" + string.Join("\n", proposal.Problems);
                    Notify("候选被拒", proposal.Error + problems, error: true);
                    return;
                }

                _lblStatus.Text = Localization.T("Status") + ": " + string.Format(Localization.T("StatusReviewing"), tasks.Tasks.Count, tasks.Tasks.Count * 2);
                var runner = new AgentTaskRunner(
                    _ => new OpenAiCompatibleReasoner(baseUrl, model, apiKey, _effort),
                    features, workspace, ledger.EvalDir, AppSettings.GlobalAgentsPath, settings.UseAgentsMd);
                var gate = new Gate(ledger, tasks, features, runner);
                var report = await gate.EvaluateAsync(baseline, proposal.Candidate, ct);

                _lblStatus.Text = report.Adopted
                    ? Localization.T("Status") + $": 已采纳 v{report.CandidateVersion}"
                    : Localization.T("Status") + ": 保留旧版";
                Notify(report.Adopted ? "已更替" : "未更替",
                    $"候选 {report.CandidatePassed}/{report.Tasks.Count}，基线 {report.BaselinePassed}/{report.Tasks.Count}");
                SetState(SessionState.Done);
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
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                RefreshLedgerInfo();
            }
        }

        private void OnRollback(object sender, EventArgs e)
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
                if (current == null || current.Version <= 1)
                {
                    Notify("没有更早的版本", "当前已是最早版本。");
                    return;
                }

                int target = current.Version - 1;
                if (MessageBox.Show(FindForm(),
                        $"退回 v{target}？会写成 v{current.Version + 1}，旧版本文件不动。",
                        "回滚", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;

                var restored = ledger.Rollback(target, "user", $"手动回滚到 v{target}");
                RefreshLedgerInfo();
                _lblStatus.Text = Localization.T("Status") + $": 已退回 v{target}（新版本 v{restored.Version}）";
                Notify("已回滚", $"当前版本 v{restored.Version}");
            }
            catch (Exception ex)
            {
                AppLog.Error("回滚失败", ex);
                Notify("回滚失败", ex.Message, error: true);
            }
        }

        private void OnAddRule(object sender, EventArgs e)
        {
            string workspace = Session.Workspace.Trim();
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            var settings = _settings();
            string model = _model?.Trim() ?? "";
            var (baseUrl, apiKey) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }
            using var form = new RuleForm((name, text, ct) =>
                RuleAssistant.ImproveAsync(
                    new OpenAiCompatibleReasoner(baseUrl, model, apiKey, _effort), name, text, ct));
            if (form.ShowDialog(FindForm()) != DialogResult.OK) return;

            try
            {
                var ledger = Ledger.Open(workspace);
                ledger.EnsureInitialized();
                var feature = ledger.AddRule(form.RuleName, form.RuleText);
                RefreshLedgerInfo();
                Notify("规则已登记", $"{feature.Id}\n当前版本不变；下一轮演进时可被启用。");
            }
            catch (Exception ex)
            {
                AppLog.Error("登记规则失败", ex);
                Notify("登记规则失败", ex.Message, error: true);
            }
        }

        private IReasoner CreateLoopReasoner()
        {
            var settings = _settings();
            var command = settings.LoopCommand;
            if (command is { Count: > 0 })
                return new ProcessReasoner(command[0], command.Skip(1));
            var (baseUrl, apiKey) = settings.EndpointFor(_model);
            return new OpenAiCompatibleReasoner(baseUrl, _model, apiKey, _effort);
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

        private static bool LooksLikeToolCallText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("tool-call:", StringComparison.OrdinalIgnoreCase)) return true;
            return text.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }

        private List<LlmMessage> BuildHistory()
        {
            var list = new List<LlmMessage>();
            foreach (var turn in Session.Book.Turns)
            {
                if (!string.IsNullOrEmpty(turn.UserText))
                    list.Add(new LlmMessage("user", turn.UserText));
                if (!string.IsNullOrEmpty(turn.AssistantText))
                    list.Add(new LlmMessage("assistant", turn.AssistantText));
            }
            return list;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _btnSend.Text = Localization.T(running ? "Stop" : "Send");
            _btnSend.IconSvg = UiIcons.Of(running ? UiIcons.Stop : UiIcons.Send,
                running ? UiStyle.Error : Color.White);
            _btnSend.Type = running ? AntdUI.TTypeMini.Error : AntdUI.TTypeMini.Primary;
            _btnSend.Ghost = running;
            _btnEvolve.Enabled = !running;
            _btnRollback.Enabled = !running;
            _btnRule.Enabled = !running;
            _btnDrafts.Enabled = !running;
            _btnReport.Enabled = !running;
            _txtInput.Enabled = !running;
            _modeControl.Enabled = !running;
            _selProvider.Enabled = !running;
            _selModel.Enabled = !running;
            _selEffort.Enabled = !running;
        }

        private static ToolRegistry ChildRegistry(WorkspaceScope scope, IReadOnlyList<string> names)
        {
            var tools = new List<OpenLoop.Core.Tools.ITool>();
            foreach (string name in names)
            {
                switch (name)
                {
                    case "read_file": tools.Add(new OpenLoop.Core.Tools.Builtin.ReadFileTool(scope)); break;
                    case "write_file": tools.Add(new OpenLoop.Core.Tools.Builtin.WriteFileTool(scope)); break;
                    case "list_dir": tools.Add(new OpenLoop.Core.Tools.Builtin.ListDirTool(scope)); break;
                    case "grep": tools.Add(new OpenLoop.Core.Tools.Builtin.GrepTool(scope)); break;
                }
            }
            return new ToolRegistry(tools);
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

        private void LoadAgentLinks()
        {
            if (Session == null) return;
            var links = OpenLoop.Core.Store.AgentTranscriptStore.List(Session.Workspace, Session.Id)
                .Select(t => new ChatLink
                {
                    Id = t.Id,
                    AnchorTurnIndex = t.AnchorTurn,
                    Title = t.Title,
                    Subtitle = "子 agent",
                    AccentHex = "#2F54EB"
                }).ToList();
            _chat.SetLinks(links);
        }

        public string SeedAgentDemo()
        {
            var transcript = OpenLoop.Core.Store.AgentTranscriptStore.Add(Session.Workspace,
                new OpenLoop.Core.Store.AgentTranscript
                {
                    SessionId = Session.Id,
                    AnchorTurn = Session.Book.Turns.Count - 1,
                    Task = "扫描 src 目录并总结模块",
                    Answer = "子 agent 结论：共 3 个模块，入口在 Program.cs。",
                    Steps =
                    [
                        new OpenLoop.Core.Store.AgentStepRecord { Tool = "list_dir", Args = "src", Ok = true, Ms = 12 },
                        new OpenLoop.Core.Store.AgentStepRecord { Tool = "grep", Args = "class Program", Ok = true, Ms = 30 }
                    ]
                });
            LoadAgentLinks();
            return transcript?.Id;
        }

        public string TraceDebug => null; // removed temporary diagnostic

        public bool AgentViewVisible => _agentHost.Visible;

        public string AgentDebug => $"host={_agentHost.Visible}/{_agentHost.Bounds}/idx={_agentHost.Parent?.Controls.GetChildIndex(_agentHost)}; chatParent={(_chat.Parent == _agentHost.Parent)}; chatIdx={_chat.Parent?.Controls.GetChildIndex(_chat)}; hostChildren={_agentHost.Controls.Count}; chatVisible={_chat.Visible}";

        public string AgentViewBounds => _agentHost.Bounds + " parent=" + (_agentHost.Parent?.GetType().Name ?? "null") + " idx=" + (_agentHost.Parent?.Controls.GetChildIndex(_agentHost) ?? -1);

        public string OpenAgentView(string id)
        {
            try { ShowAgent(id); return "ok"; } catch (Exception ex) { return "ERR: " + ex; }
        }

        private void ShowAgent(string id)
        {
            var transcript = OpenLoop.Core.Store.AgentTranscriptStore.Load(Session.Workspace, id);
            if (transcript == null) return;

            var book = new ChatTurnBook();
            book.AddTurn(new ChatTurn
            {
                User = new ChatMessage("user", transcript.Task),
                Assistant = new ChatMessage("assistant", transcript.Answer),
                ToolTraceText = string.Join("\n", transcript.Steps.Select(s => $"{s.Tool}  {(s.Ok ? "ok" : "fail")}  {s.Ms}ms"))
            });
            _agentBack = true;
            _chat.Bind(book);
            _chat.SetLinks([new ChatLink { Id = "__back", AnchorTurnIndex = -1, Title = Localization.T("AgentBack"), AccentHex = "#8C8C8C" }]);
            _lblTitle.Text = "子 agent · " + transcript.Title;
            _txtInput.Enabled = false;
            _btnSend.Enabled = false;
            _chat.ScrollToEnd();
        }

        private void OnAgentLink(object sender, ChatLinkEventArgs e)
        {
            if (e?.Link == null) return;
            if (e.Link.Id == "__back")
            {
                _agentBack = false;
                _chat.Bind(Session.Book);
                LoadAgentLinks();
                _lblTitle.Text = Session.Title;
                _txtInput.Enabled = true;
                _btnSend.Enabled = true;
                return;
            }
            var transcript = OpenLoop.Core.Store.AgentTranscriptStore.Load(Session.Workspace, e.Link.Id);
            if (transcript == null) return;

            var book = new ChatTurnBook();
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", transcript.Task),
                Assistant = new ChatMessage("assistant", transcript.Answer),
                ToolTraceText = string.Join("\n", transcript.Steps.Select(s => $"{s.Tool}  {(s.Ok ? "ok" : "fail")}  {s.Ms}ms"))
            };
            book.AddTurn(turn);
            _agentChat.Bind(book);
            _lblAgentTitle.Text = "子 agent · " + transcript.Title;
            _agentHost.Visible = true;
            _agentHost.BringToFront();
        }

        private void OnAgentBack(object sender, EventArgs e) => _agentHost.Visible = false;

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
        private bool _agentBack;

        private void OnSurveySubmitted(object sender, ChatSurveyEventArgs e)
        {
            string summary = e.Answers.Count == 0
                ? "已提交"
                : string.Join(" · ", e.Answers.Select(kv => kv.Value));
            OpenLoop.Core.Store.SurveyStore.Answer(Session.Workspace, Session.Id, e.Survey.Id, summary);
        }


        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) Invoke(action);
            else action();
        }

        private bool ApproveCommand(OpenLoop.Core.Tools.Builtin.CommandRequest request)
            => AskOnUi(string.Format(Localization.T("ApproveCommand"), request.Reason, request.Line));

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
