using System.ComponentModel;
using System.Text;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
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
        private bool _layoutReady;

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
            _lblPath.Text = session.Workspace;
            _chat.PlaceholderText = Localization.T("NoMessages");
            _chat.Bind(session.Book);
            SelectEffort(_effort);
            _freeSwitch.Checked = _onlyFree;
            FillModels();
            SetMode("task");
            RefreshLedgerInfo();
            UpdateContext(null);

            _lblStatus.TextChanged += OnStatusTextChanged;
            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) => ModelCatalog.Changed -= OnCatalogChanged;
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
                SendTask(_mode);
                SetMode("task");
            }
        }

        private void OnSendClick(object sender, EventArgs e)
        {
            SendTask(_mode);
            SetMode("task");
        }

        private void OnStop(object sender, EventArgs e) => _cts?.Cancel();

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

        private void FillModels()
        {
            if (IsDisposed) return;

            _fillingModels = true;
            try
            {
                _selModel.Items.Clear();
                var all = ModelCatalog.All;
                if (all.Count == 0)
                {
                    _selModel.Items.Add(new AntdUI.SelectItem(_model) { Tag = _model });
                }
                else
                {
                    var visible = _onlyFree
                        ? all.Where(m => m.IsFree).ToList()
                        : all.ToList();
                    if (visible.Count == 0) visible = all.ToList();

                    foreach (var model in visible)
                        _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                        {
                            Tag = model.Id,
                            IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
                            SubText = model.IsFree ? "免费" : null
                        });

                    if (visible.All(m => m.Id != _model))
                    {
                        var currentModel = all.FirstOrDefault(m => m.Id == _model);
                        _selModel.Items.Add(new AntdUI.SelectItem(currentModel?.Name ?? _model)
                        {
                            Tag = _model,
                            IconSvg = currentModel?.IsFree == true ? UiIcons.Dot(UiStyle.Success) : null
                        });
                    }
                }

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

        private static string CleanAnswer(string text)
        {
            string clean = LlmTextSanitizer.StripSafetyLines(text ?? "");
            return clean.Length == 0 && !string.IsNullOrWhiteSpace(text)
                ? "（模型只返回了安全判定，没有正文，请重试）"
                : clean;
        }

        private string ResolveKey() => AppSettings.ResolveKey(_settings());

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

            string baseUrl = settings.BaseUrl?.Trim() ?? "";
            string model = _model?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }

            var scope = new WorkspaceScope(workspace);
            var tools = ToolRegistry.Workspace(scope);
            var reasoner = new OpenAiCompatibleReasoner(baseUrl, model, ResolveKey(), _effort);

            Ledger ledger;
            ActivationRecord activation;
            AgentPolicy policy;
            IReadOnlyList<string> rules;
            try
            {
                ledger = Ledger.Open(workspace);
                ledger.EnsureInitialized();
                activation = ledger.Current;
                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                policy = FeatureCatalog.PolicyFrom(activation, features);
                rules = FeatureCatalog.RulesFrom(activation, features);
            }
            catch (Exception ex)
            {
                AppLog.Error("读取账本失败", ex);
                Notify("读取账本失败", ex.Message, error: true);
                return;
            }

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
            _txtInput.Clear();
            SetRunning(true);
            SetState(SessionState.Running);
            _lblTrace.Text = "trace: " + trace.DirectoryPath;
            _lblStatus.Text = Localization.T("Status") + ": 运行中…";
            _chat.ScrollToEnd();
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
                confirmContinue: AskOnUi);

            history.Insert(0, new LlmMessage("system", loop.BuildSystemPrompt(null, rules, AgentsInstructions.Load(workspace))));
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
                turn.AssistantText = CleanAnswer(result.FinalAnswer);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser ? "stopped"
                    : "ok";
                stepCount = result.Steps.Count;
                cost = result.ChatCost;
                trace.Finish(outcome, turn.AssistantText, result.ChatCost);

                _lblCost.Text = Localization.T("Cost") + $": {(result.ChatCost.HasValue ? result.ChatCost.Value.ToString("0.0000") : "—")} · {result.Steps.Count} 步";
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
                return;
            }
            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Activations().LastOrDefault();
                _lblDef.Text = current == null
                    ? "def: —（首次运行后初始化）"
                    : $"def: v{current.Version} · {current.Features.Count} {Localization.T("Features")}";
                _lblTasks.Text = Localization.T("Tasks") + $": {TaskSet.Load(ledger.EvalDir).Tasks.Count} {Localization.T("TasksUnit")}";
            }
            catch (Exception ex)
            {
                _lblDef.Text = "def: 读取失败：" + ex.Message;
                _lblTasks.Text = Localization.T("Tasks") + ": —";
            }
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

            string baseUrl = settings.BaseUrl?.Trim() ?? "";
            string model = _model?.Trim() ?? "";
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

                _lblStatus.Text = Localization.T("Status") + $": 评审 {tasks.Tasks.Count} 道题（{tasks.Tasks.Count * 2} 次运行）…";
                var runner = new AgentTaskRunner(
                    _ => new OpenAiCompatibleReasoner(baseUrl, model, ResolveKey(), _effort),
                    features, workspace, ledger.EvalDir);
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

        private IReasoner CreateLoopReasoner()
        {
            var settings = _settings();
            var command = settings.LoopCommand;
            if (command is { Count: > 0 })
                return new ProcessReasoner(command[0], command.Skip(1));
            return new OpenAiCompatibleReasoner(
                settings.BaseUrl, _model, ResolveKey(), _effort);
        }

        private static string Short(string text)
            => string.IsNullOrEmpty(text) || text.Length <= 8 ? text ?? "" : text[..8];

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
            _btnSend.Enabled = !running;
            _btnStop.Enabled = running;
            _btnEvolve.Enabled = !running;
            _btnRollback.Enabled = !running;
            _txtInput.Enabled = !running;
            _modeControl.Enabled = !running;
            _selModel.Enabled = !running;
            _selEffort.Enabled = !running;
        }

        private void RefreshChat() => Session.Book.RaiseTextChanged();

        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) Invoke(action);
            else action();
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
