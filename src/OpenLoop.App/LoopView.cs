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

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 的会话视图（观察者）：与 Worker 页相似的聊天界面，模型是同一个 Loop agent，
    /// 但提示词是观察者/改进者角色，历史单独存（ChatStore kind="loop"）。
    /// </summary>
    public partial class LoopView : UserControl
    {
        private readonly ChatTurnBook _book = new();
        private readonly StringBuilder _streamBuffer = new();
        private Func<AppSettings> _settings;
        private CancellationTokenSource _cts;
        private string _workspace = "";
        private string _model = "";
        private string _effort = "";
        private bool _onlyFree = true;
        private string _lastCost = "—";
        private int? _contextTokens;
        private bool _running;
        private bool _filling;
        private bool _fillingProviders;
        private bool _iterating;
        private bool _layoutReady;

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
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status => _lblStatus.Text;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Workspace => _workspace;

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

        internal void Initialize(string workspace, Func<AppSettings> settings)
        {
            _workspace = workspace ?? "";
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
            _txtInput.TextChanged += (_, _) => UpdateSendButton();

            _book.ReplaceAll(ChatStore.Load(_workspace, "loop"), null);
            LoopIndex.Touch(_workspace);
            _chat.PlaceholderText = Localization.T("NoMessages");
            _chat.Bind(_book);
            _lblSub.Text = "工作区 · " + UiStyle.ShortPath(_workspace);
            _tips.SetToolTip(_lblSub, _workspace);
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

        /// <summary>对话正文按「工作区|loop」存；顺带把项目记进 Loop 索引，供 [Loop] 页列出。</summary>
        private void SaveLoopChat()
        {
            ChatStore.Save(_workspace, _book.Turns, "loop");
            LoopIndex.Touch(_workspace);
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

            var skillCatalog = SkillCatalog.Load(_workspace, AppSettings.GlobalSkillsPath);
            var skills = ActiveSkills(skillCatalog, out var skillProblems);
            OpenLoop.Core.Safety.ApprovalHandler approve = AskApproval;
            // 章程 §2：Loop 读与 Worker 同宽，写只限配置/源码/安装路径
            var (readScope, writeScope) = OpenLoop.Service.Source.LoopScope.Create(approve);
            var loopTools = ToolRegistry.Loop(readScope, writeScope, _workspace, approve, skills,
                OpenCodeTools.For(writeScope.Root, approve, settings.OpenCodeAskPermissions)
                    .Concat(DshTools.For(settings, writeScope.Root, approve)));
            var tools = new ToolRegistry(loopTools.Tools.Concat(
                [new OpenLoop.Core.Tools.Builtin.AskUserTool(_workspace, () => SessionTarget?.Invoke() ?? ("", -1))]));
            string checkpoint = Snapshots.Create(writeScope.Root, "loop");
            if (checkpoint != null) AppLog.Info("snapshot " + checkpoint);
            var reasoner = Reasoners.Create(settings, model, _effort, writeScope.Root, approve);
            var policy = new AgentPolicy { MaxSteps = 12 };

            TraceWriter trace;
            try
            {
                trace = TraceWriter.Start(_workspace, "loop", text, model);
            }
            catch (Exception ex)
            {
                Notify("创建 trace 失败", ex.Message, error: true);
                return;
            }

            var history = BuildHistory();
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
            trace.SkillSet(skills, skillProblems);
            trace.Message("user", text);

            _streamBuffer.Clear();
            Action<string> onDelta = piece =>
            {
                lock (_streamBuffer) _streamBuffer.Append(piece);
                string snapshot;
                lock (_streamBuffer) snapshot = _streamBuffer.ToString();
                if (LooksLikeToolCallText(snapshot)) return;
                RunOnUi(() => _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(snapshot)));
            };

            //推理通道：推理模型只吐 reasoning 时也要有内容可看
            var reasoningBuffer = new StringBuilder();
            long lastReasoningUi = 0;
            Action<string> onReasoning = piece =>
            {
                lock (reasoningBuffer) reasoningBuffer.Append(piece);
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastReasoningUi) < 250) return;
                Interlocked.Exchange(ref lastReasoningUi, now);
                string snapshot;
                lock (reasoningBuffer) snapshot = reasoningBuffer.ToString();
                RunOnUi(() =>
                {
                    turn.ReasoningText = snapshot;
                    // 就地重绑最后一行：避免全量重建把高度打回估算值导致滚动条跳动
                    string text;
                    lock (_streamBuffer) text = _streamBuffer.ToString();
                    _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(text));
                });
            };

            // 断流整轮重发：清掉已显示的半截正文/思考，避免重发后出现两遍
            void ResetStream()
            {
                lock (_streamBuffer) _streamBuffer.Clear();
                lock (reasoningBuffer) reasoningBuffer.Clear();
                RunOnUi(() =>
                {
                    turn.ReasoningText = "";
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
                        string line = $"{step.Tool}({step.ArgsSummary}) · {step.DurationText}{(step.Ok ? "" : " · 失败")}";
                        turn.ToolCallLines = string.IsNullOrEmpty(turn.ToolCallLines)
                            ? line
                            : turn.ToolCallLines + "\n" + line;
                        turn.ToolTraceText = turn.ToolCallLines;
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

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _interrupt = false;
            string outcome = "error";

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, text, ct, ModelChoices.ContextLimit(model),
                        () => ct.IsCancellationRequested || _interrupt, onDelta, onReasoning, ResetStream),
                    ct);

                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                turn.AssistantText = CleanAnswer(result.FinalAnswer, result.FinishReason);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser || result.StoppedByGuard ? "stopped"
                    : "ok";
                trace.Finish(outcome, turn.AssistantText, result.ChatCost, result.Guards);
                _lastCost = result.ChatCost?.ToString("0.0000") ?? "—";
                _contextTokens = result.ContextTokens;
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

        private List<LlmMessage> BuildHistory()
        {
            var history = new List<LlmMessage>();
            foreach (var turn in _book.Turns)
            {
                string user = turn.UserText;
                string assistant = turn.AssistantText;
                if (!string.IsNullOrWhiteSpace(user)) history.Add(new LlmMessage("user", user));
                if (!string.IsNullOrWhiteSpace(assistant)) history.Add(new LlmMessage("assistant", assistant));
            }
            return history;
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

        private void OnSidePageChanged(object sender, AntdUI.IntEventArgs e)
            => UpdateSideVisibility(e?.Value ?? 0);

        private void UpdateSideVisibility(int index)
        {
            _detailFlow.Visible = index == 0;
            _sideBtw.Visible = index == 1;
            if (index == 0) ApplyDetail();
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

            _lblDModel.Text = Localization.T("Model") + ": " + (string.IsNullOrWhiteSpace(_model) ? "—" : _model);
            _lblDEffort.Text = Localization.T("Effort") + ": " + effort;
            _lblDCtx.Text = Localization.T("Context") + ": "
                + (_contextTokens.HasValue ? _contextTokens.Value.ToString("N0") : "—")
                + " / " + ModelChoices.ContextLimit(_model).ToString("N0");
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
            SessionView.ApplySplit(_upperSplit, vertical: true,
                panel1Min: Dip(400), panel2Min: Dip(280), targetPanel2: Dip(320));
            _layoutReady = true;
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

            if (!_running && SessionView.IsCommand(text, "/btw"))
            {
                string request = text[4..].Trim();
                if (request.Length == 0)
                {
                    Notify("缺少请求", "/btw 后面要写具体请求，例如：/btw 顺便看看还有哪些调用点。", error: true);
                    return;
                }
                _txtInput.Clear();
                RunSideWork(request);
                return;
            }

            if (_running)
            {
                if (text.Length == 0)
                {
                    Stop();
                    return;
                }
                _queue.Add(text);
                _txtInput.Clear();
                _pending.Sync();
                return;
            }

            if (text.Length == 0) return;
            SendAsync(null);
        }

        private void OnSendClick(object sender, EventArgs e) => Submit();

        /// <summary>壳注入的迭代动作：编译 + 发布 + 登记 A/I。Loop 的工具里没有这个入口。</summary>
        public Func<Task<string>> Iterate { get; set; }

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

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
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

        private void RefreshChat() => _book.RaiseTextChanged();

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

        private static bool LooksLikeToolCallText(string text)
            => text.TrimStart().StartsWith("tool-call", StringComparison.OrdinalIgnoreCase);
    }
}
