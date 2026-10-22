using System.ComponentModel;
using System.Text;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Trace;

namespace OpenLoop.App
{
    /// <summary>
    /// Loop 的会话视图（阴面）：与 Worker 页相似的聊天界面，模型是同一个 Loop agent，
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
        private bool _running;
        private bool _filling;

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
        public string ComposerText
        {
            get => _txtInput.Text;
            set => _txtInput.Text = value;
        }

        internal void Initialize(string workspace, Func<AppSettings> settings)
        {
            _workspace = workspace ?? "";
            _settings = settings;

            var current = settings();
            _model = string.IsNullOrWhiteSpace(current.Model) ? "openrouter/free" : current.Model;
            _effort = current.ReasoningEffort ?? "";

            FillModels();
            SelectEffort(_effort);

            _book.ReplaceAll(ChatStore.Load(_workspace, "loop"), null);
            _chat.Bind(_book);
            _lblSub.Text = $"工作区 · {_workspace}";
            _chat.ScrollToEnd();

            ModelCatalog.Changed += OnCatalogChanged;
            Disposed += (_, _) => ModelCatalog.Changed -= OnCatalogChanged;
        }

        public void SetModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            _model = model.Trim();
            _selModel.SelectedValue = _model;
        }

        public void Send(string text = null)
        {
            if (!string.IsNullOrWhiteSpace(text)) _txtInput.Text = text;
            SendAsync();
        }

        public void Stop() => _cts?.Cancel();

        public void Shutdown()
        {
            _cts?.Cancel();
            ChatStore.Save(_workspace, _book.Turns, "loop");
        }

        private void OnViewLoad(object sender, EventArgs e)
        {
            if (_book.Turns.Count > 0) _chat.ScrollToEnd();
        }

        private async void SendAsync()
        {
            if (_running) return;

            var settings = _settings();
            string text = _txtInput.Text.Trim();
            if (text.Length == 0) return;
            if (!Directory.Exists(_workspace))
            {
                Notify("项目不可用", "工作区目录不存在：" + _workspace, error: true);
                return;
            }

            string baseUrl = settings.BaseUrl?.Trim() ?? "";
            string model = _model?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "请先在设置里填写接口地址、模型。", error: true);
                return;
            }

            var scope = new WorkspaceScope(_workspace);
            var tools = ToolRegistry.Workspace(scope);
            var reasoner = new OpenAiCompatibleReasoner(baseUrl, model, ResolveKey(), _effort);
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
                Streaming = true
            };
            _book.AddTurn(turn);
            _txtInput.Clear();
            SetRunning(true);
            _lblStatus.Text = "状态: 运行中";
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
                confirmContinue: AskOnUi);

            history.Insert(0, new LlmMessage("system",
                loop.BuildSystemPrompt(LoopPrompt.Role, LoopPrompt.Rules, AgentsInstructions.Load(_workspace))));

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string outcome = "error";

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, text, ct, AgentContext.DefaultContextLimit, () => ct.IsCancellationRequested, onDelta),
                    ct);

                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = CleanAnswer(result.FinalAnswer);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget" : result.StoppedByUser ? "stopped" : "ok";
                trace.Finish(outcome, turn.AssistantText, result.ChatCost);
                _lblStatus.Text = $"状态: {outcome} · {result.Steps.Count} 步 · 成本 {(result.ChatCost?.ToString("0.0000") ?? "—")}";
            }
            catch (OperationCanceledException)
            {
                outcome = "stopped";
                turn.Streaming = false;
                turn.ToolStatusText = null;
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
                ChatStore.Save(_workspace, _book.Turns, "loop");
            }
        }

        private static string CleanAnswer(string text)
        {
            string clean = LlmTextSanitizer.StripSafetyLines(text ?? "");
            return clean.Length == 0 && !string.IsNullOrWhiteSpace(text)
                ? "（模型只返回了安全判定，没有正文，请重试）"
                : clean;
        }

        private string ResolveKey() => AppSettings.ResolveKey(_settings());

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

        private void FillModels()
        {
            _filling = true;
            try
            {
                _selModel.Items.Clear();
                foreach (var model in ModelCatalog.All)
                    _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                    {
                        Tag = model.Id,
                        IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
                        SubText = model.IsFree ? "免费" : null
                    });

                if (ModelCatalog.All.Count == 0 || ModelCatalog.All.All(m => m.Id != _model))
                    _selModel.Items.Add(new AntdUI.SelectItem(_model) { Tag = _model });

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
            RunOnUi(FillModels);
        }

        private void OnModelChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            _model = _selModel.SelectedValue as string ?? _model;
        }

        private void OnEffortChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            _effort = _selEffort.SelectedValue as string ?? "";
            Persist(s => s.ReasoningEffort = _effort);
        }

        private void OnSendClick(object sender, EventArgs e) => SendAsync();

        private void OnStop(object sender, EventArgs e) => Stop();

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                SendAsync();
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
            _btnSend.Enabled = !running;
            _btnStop.Enabled = running;
            _txtInput.Enabled = !running;
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

        private void Notify(string title, string text, bool error = false)
        {
            var form = FindForm();
            if (form == null) return;
            if (error)
                AntdUI.Notification.error(form, title, text, AntdUI.TAlignFrom.TR, null, 5);
            else
                AntdUI.Notification.info(form, title, text, AntdUI.TAlignFrom.TR, null, 3);
        }

        private static bool LooksLikeToolCallText(string text)
            => text.TrimStart().StartsWith("tool-call", StringComparison.OrdinalIgnoreCase);
    }
}
