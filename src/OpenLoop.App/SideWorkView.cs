using System.ComponentModel;
using System.Text;
using ChatFlow;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Safety;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;
using OpenLoop.Core.Tools;
using OpenLoop.Core.Trace;
using OpenLoop.Service.Worker;

namespace OpenLoop.App
{
    /// <summary>
    /// 旁路（/btw）：拿当前会话的上下文，在工作区里跑一个一次性的 agent 任务，
    /// 结果显示在会话右侧栏。只读——不并入会话历史，也没有输入框，不能继续对话。
    /// </summary>
    public partial class SideWorkView : UserControl
    {
        private readonly ChatTurnBook _book = new();
        private readonly StringBuilder _streamBuffer = new();
        private Func<AppSettings> _settings;
        private CancellationTokenSource _cts;
        private string _workspace = "";
        private string _model = "";
        private string _effort = "";
        private bool _running;

        /// <summary>用户动作计数（跑新旁路、停、换模型、清空）：免费额度自愈据此判断用户动没动过这个旁路。</summary>
        private int _userEpoch;
        private bool _closed;

        /// <summary>自愈重试表的键：同一个 SideWorkView 实例同一时刻只留一个重试。</summary>
        private readonly string _recoveryKey = "btw-" + Guid.NewGuid().ToString("N")[..8];

        public SideWorkView()
        {
            InitializeComponent();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => _running;

        internal void Initialize(string workspace, Func<AppSettings> settings)
        {
            _workspace = workspace ?? "";
            _settings = settings;
            var current = settings();
            _model = current.Model ?? "";
            _effort = current.ReasoningEffort ?? "";
            _lblStatus.Text = "只读 · 尚未运行";
            _chat.PlaceholderText = "在输入框里用 /btw <请求> 跑一个只读的旁路任务";
            _chat.Bind(_book);
        }

        public void SetModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            if (!string.Equals(model.Trim(), _model, StringComparison.Ordinal)) _userEpoch++;
            _model = model.Trim();
        }

        public void SetEffort(string effort) => _effort = effort ?? "";

        public void Stop()
        {
            _userEpoch++;
            _cts?.Cancel();
        }

        /// <summary>当前旁路的历史快照（供挤走时转成普通会话；克隆一份，旁路清空后仍可用）。</summary>
        public IReadOnlyList<ChatTurn> Snapshot() =>
            _book.Turns.Select(t =>
            {
                var copy = new ChatTurn
                {
                    User = new ChatMessage("user", t.UserText),
                    Assistant = new ChatMessage("assistant", t.AssistantText),
                    ReasoningText = t.ReasoningText,
                    ToolTraceText = t.ToolTraceText,
                    ToolCallLines = t.ToolCallLines,
                    TimelineText = t.TimelineText,
                    ToolStepCount = t.ToolStepCount,
                    ToolTotalMs = t.ToolTotalMs
                };
                copy.Phases.AddRange(t.Phases);
                return copy;
            }).ToList();

        /// <summary>清空当前旁路，给新旁路让位。</summary>
        public void Reset()
        {
            _userEpoch++;
            _book.Clear();
            _lblStatus.Text = "只读 · 尚未运行";
        }

        public void Shutdown()
        {
            _userEpoch++;
            _closed = true;
            _cts?.Cancel();
        }

        /// <summary>免费额度自愈读的用户动作计数；这个旁路已关/已销毁返回 null（自愈据此收工）。</summary>
        private int? UserEpochOf() => _closed || IsDisposed ? null : _userEpoch;

        /// <summary>用给定上下文跑一次旁路任务；结果只落在本视图。</summary>
        public async void Run(string text, IReadOnlyList<LlmMessage> context)
        {
            if (_running)
            {
                Notify("旁路任务进行中", "等当前旁路任务结束再发。", error: true);
                return;
            }

            text = text?.Trim() ?? "";
            if (text.Length == 0) return;
            _userEpoch++;

            var settings = _settings();
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
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
                return;
            }

            bool fullAccess = settings.FullAccess;
            var scope = new WorkspaceScope(_workspace, fullAccess ? [] : null, fullAccess) { Undo = new FileUndoLog() };
            var skillCatalog = SkillCatalog.Load(_workspace, AppSettings.GlobalSkillsPath);

            Ledger ledger;
            ActivationRecord activation;
            AgentPolicy policy;
            IReadOnlyList<string> rules;
            IReadOnlyList<SkillInfo> skills;
            IReadOnlyList<string> skillProblems;
            try
            {
                ledger = Ledger.Open(_workspace);
                ledger.EnsureInitialized();
                skills = SkillCatalog.Sync(ledger, skillCatalog, out skillProblems,
                    SkillStore.LoadDisabled(AppSettings.GlobalSkillsStatePath));
                activation = ledger.Current;
                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                policy = FeatureCatalog.PolicyFrom(activation, features);
                rules = FeatureCatalog.RulesFrom(activation, features);
            }
            catch (Exception ex)
            {
                AppLog.Error("旁路：读取账本失败", ex);
                Notify("读取账本失败", ex.Message, error: true);
                return;
            }

            OpenLoop.Core.Safety.ApprovalHandler approve =
                fullAccess ? _ => OpenLoop.Core.Safety.ApprovalDecision.AllowOnce : AskApproval;
            scope.Approve = approve;
            var tools = ToolRegistry.Workspace(scope, approve, skills, fullAccess,
                OpenCodeTools.For(_workspace, approve, settings.OpenCodeAskPermissions)
                    .Concat(DshTools.For(settings, _workspace, approve)),
                settings.WebFetchMode);
            TraceWriter trace;
            try
            {
                trace = TraceWriter.Start(_workspace, "btw", text, model);
            }
            catch (Exception ex)
            {
                Notify("创建 trace 失败", ex.Message, error: true);
                return;
            }

            var history = new List<LlmMessage>(context ?? []);
            var reasoner = Reasoners.Create(settings, model, _effort, _workspace, approve);
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", text),
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true
            };

            _book.AddTurn(turn);
            SetRunning(true);
            trace.SkillSet(skills, skillProblems);
            trace.Message("user", $"[btw] {text}");

            _streamBuffer.Clear();
            var timeline = new RunTimeline();
            // 正文喂进时间线的进度：正文出现即分段，正文之后的思维另起一段折叠区
            int answerLen = 0;
            long lastDeltaUi = 0;
            Action<string> onDelta = piece =>
            {
                lock (_streamBuffer) _streamBuffer.Append(piece);
                // 逐 token 整段 ToString + 清洗是 O(n²)：250ms 合并一次，收尾用最终正文
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastDeltaUi) < 250) return;
                Interlocked.Exchange(ref lastDeltaUi, now);
                string snapshot;
                lock (_streamBuffer) snapshot = _streamBuffer.ToString();
                string visible = AgentToolCallParser.CutToolCallText(snapshot);
                if (visible.Length == 0) return;
                if (visible.Length > answerLen)
                {
                    lock (timeline) timeline.Answer(visible[answerLen..]);
                    answerLen = visible.Length;
                }
                RunOnUi(() =>
                {
                    lock (timeline) SyncPhases(turn, timeline);
                    _chat.UpdateStreamingMessage(turn, LlmTextSanitizer.StripSafetyLines(visible));
                });
            };

            var reasoningBuffer = new StringBuilder();
            long lastReasoningUi = 0;
            Action<string> onReasoning = piece =>
            {
                lock (reasoningBuffer) reasoningBuffer.Append(piece);
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
                    // 与 Worker 一致：正文只由 onDelta 写（已过滤协议原文），推理增量只刷新思考区
                    _chat.UpdateStreamingMessage(turn, null);
                });
            };

            void ResetStream()
            {
                lock (_streamBuffer) _streamBuffer.Clear();
                lock (reasoningBuffer) reasoningBuffer.Clear();
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
                },
                onToolStart: (name, args) =>
                {
                    lock (_streamBuffer) _streamBuffer.Clear();
                    answerLen = 0;
                    string hint = AgentStepRecord.InFlightLine(name, args);
                    RunOnUi(() =>
                    {
                        turn.AssistantText = "";
                        turn.ToolStarted(hint);
                        _chat.UpdateStreamingMessage(turn, "", true);
                        RefreshChat();
                    });
                },
                confirmContinue: AskOnUi,
                skills: skills);

            string systemPrompt = loop.BuildSystemPrompt(
                null, rules,
                settings.UseAgentsMd
                    ? AgentsInstructions.Load(_workspace, AppSettings.GlobalAgentsPath)
                    : null,
                _workspace);
            history.Insert(0, new LlmMessage("system", systemPrompt));
            trace.SystemPrompt(systemPrompt);

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string outcome = "error";

            try
            {
                var result = await Task.Run(
                    () => loop.RunAsync(history, text, ct, ModelChoices.ContextLimit(model, settings),
                        () => ct.IsCancellationRequested, onDelta, onReasoning, ResetStream),
                    ct);

                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                lock (timeline) turn.TimelineText = timeline.Render();
                turn.AssistantText = CleanAnswer(result.FinalAnswer, result.FinishReason);
                lock (timeline) timeline.FeedAnswerTail(turn.AssistantText);
                lock (timeline) SyncPhases(turn, timeline);
                trace.Message("assistant", turn.AssistantText);

                outcome = result.BudgetExhausted ? "budget"
                    : result.StoppedByUser || result.StoppedByGuard ? "stopped"
                    : "ok";
                trace.Finish(outcome, turn.AssistantText, result.ChatCost, result.Guards);
                _lblStatus.Text = $"只读 · {outcome} · {result.Steps.Count} 步";
            }
            catch (OperationCanceledException)
            {
                outcome = "stopped";
                turn.Streaming = false;
                turn.ToolStatusText = null;
                lock (reasoningBuffer) turn.ReasoningText = reasoningBuffer.ToString();
                if (string.IsNullOrEmpty(turn.AssistantText)) turn.AssistantText = "（已停止）";
                trace.Finish("stopped", turn.AssistantText);
                _lblStatus.Text = "只读 · stopped";
            }
            catch (Exception ex)
            {
                AppLog.Error("旁路任务失败", ex);
                turn.Streaming = false;
                turn.ToolStatusText = null;
                turn.AssistantText = "执行失败：" + ex.Message;
                trace.Finish("error", turn.AssistantText);
                _lblStatus.Text = "只读 · error";
                Notify("旁路执行失败", ex.Message, error: true);
                // 免费额度耗尽：让 ConnMan 换节点，就绪后重跑同一条旁路任务（旁路没有「继续」的会话，
                // 只能把这次请求原样再发一遍）。回调在后台线程，Run 会碰控件，必须回 UI 线程。
                if (ServerNotice.IsQuotaFailure(turn.AssistantText))
                {
                    FreeLimitRecovery.TryStart(_recoveryKey, model, UserEpochOf, _ =>
                    {
                        if (UserEpochOf() == null) return false;
                        RunOnUi(() => Run(text, context));
                        return true;
                    }, _recoveryKey);
                }
            }
            finally
            {
                trace.Dispose();
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
                RefreshChat();
            }
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

        private void SetRunning(bool running)
        {
            _running = running;
            if (running) _lblStatus.Text = "只读 · 运行中…";
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
    }
}
