using System.ComponentModel;
using System.Text;
using System.Text.Json;
using OpenLoop.Core.Agent;
using OpenLoop.Core.Loop;
using OpenLoop.Core.Store;
using OpenLoop.Service.Config;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧栏的 Harness 页：管理本工作区的可演进参数（账本激活集、规则、考核题），
    /// 并触发演进 / 回滚。Worker 与 Loop 会话各挂一份，文案与行为保持同一套。
    /// </summary>
    public partial class HarnessView : UserControl
    {
        private Func<string> _workspace;
        private Func<AppSettings> _settings;
        private Func<string> _model;
        private Func<string> _effort;
        private Action<string, string, bool> _notify;
        private CancellationTokenSource _cts;
        private bool _busy;

        public HarnessView()
        {
            InitializeComponent();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EvolveEnabled => _btnEvolve.Enabled;

        /// <summary>「Prompt」「AGENTS.md」按钮：宿主打开目标会话让模型改（用户不直接编辑文本）。</summary>
        internal event Action<string> EditTargetRequested;

        internal void Initialize(Func<string> workspace, Func<AppSettings> settings,
            Func<string> model, Func<string> effort, Action<string, string, bool> notify)
        {
            _workspace = workspace;
            _settings = settings;
            _model = model;
            _effort = effort;
            _notify = notify;
            _flow.SizeChanged += (_, _) => FitHint();
            _tips.SetToolTip(_lblDef, Localization.T("HarnessTipDef"));
            _tips.SetToolTip(_lblTasks, Localization.T("HarnessTipTasks"));
            FitHint();
            Reload();
        }

        internal void SetBusy(bool busy)
        {
            _busy = busy;
            _btnDrafts.Enabled = _btnReport.Enabled = _btnRollback.Enabled = _btnRule.Enabled = !busy;
            _btnPrompt.Enabled = _btnAgents.Enabled = !busy;
            UpdateEvolveEnabled();
        }

        /// <summary>重新读账本，刷新「def / 考核题」两行与演进按钮可用性。</summary>
        internal void Reload()
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                _lblDef.Text = "def: —";
                _lblTasks.Text = Localization.T("Tasks") + ": —";
                UpdateEvolveEnabled();
                return;
            }

            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Activations().LastOrDefault();
                if (current == null)
                {
                    _lblDef.Text = "def: " + Localization.T("HarnessDefUninitialized");
                }
                else
                {
                    var known = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                    _lblDef.Text = $"def: v{current.Version} · {ObjectSummary(current, known)}  ›";
                }

                _lblTasks.Text = Localization.T("Tasks")
                    + $": {TaskSet.Load(ledger.EvalDir).Tasks.Count} {Localization.T("TasksUnit")}  ›";
            }
            catch (Exception ex)
            {
                _lblDef.Text = "def: " + ex.Message;
                _lblTasks.Text = Localization.T("Tasks") + ": —";
            }
            UpdateEvolveEnabled();
        }

        private string Workspace => _workspace?.Invoke()?.Trim() ?? "";
        private string Model => _model?.Invoke()?.Trim() ?? "";
        private string Effort => _effort?.Invoke() ?? "";

        private void Notify(string title, string text, bool error = false)
            => _notify?.Invoke(title, text, error);

        private void UpdateEvolveEnabled()
        {
            if (_busy) { _btnEvolve.Enabled = false; return; }
            if (!Directory.Exists(Workspace)) { _btnEvolve.Enabled = false; return; }
            if (Model.Length == 0) { _btnEvolve.Enabled = false; return; }
            var (baseUrl, _) = _settings().EndpointFor(Model);
            _btnEvolve.Enabled = !string.IsNullOrWhiteSpace(baseUrl);
        }

        private void OnFlowLayout(object sender, LayoutEventArgs e)
            => FlowButtons.Center(sender as FlowLayoutPanel);

        private void FitHint()
        {
            int width = Math.Max(120, _flow.ClientSize.Width - 28);
            _lblHint.MaximumSize = new Size(width, 0);
            _lblHint.Width = width;
            int height = TextRenderer.MeasureText(
                _lblHint.Text,
                _lblHint.Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height;
            _lblHint.Height = Math.Max(18, height + 2);
        }

        private void OnOpenDrafts(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            using var form = new TaskDraftsForm(workspace, SmokeBaselineAsync);
            form.Changed += Reload;
            form.ShowDialog(FindForm());
            Reload();
        }

        private void OnOpenReports(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            using var form = new EvalReportForm(workspace);
            form.ShowDialog(FindForm());
            Reload();
        }

        /// <summary>在某题上跑一次现役版本，判断题目是否有区分度（通过=可能过于简单）。</summary>
        private async Task<string> SmokeBaselineAsync(EvalTask task, CancellationToken ct)
        {
            string workspace = Workspace;
            var settings = _settings();
            string model = Model;
            var (baseUrl, _) = settings.EndpointFor(model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return Localization.T("HarnessNoEndpointBaseline");

            var ledger = Ledger.Open(workspace);
            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var activation = ledger.Current;
            if (activation == null) return Localization.T("HarnessNoActivationBaseline");

            var runner = new AgentTaskRunner(
                _ => Reasoners.Create(settings, model, Effort, workspace),
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
            if (_busy) return;

            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            string model = Model;
            if (model.Length == 0)
            {
                Notify(Localization.T("MissingConfig"), Localization.T("HarnessNoModel"), error: true);
                return;
            }

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            _lblStatus.Text = Localization.T("Status") + Localization.T("StatusPreparing");

            try
            {
                using var loop = new LoopApiClient();
                var state = await loop.EvolveAsync(workspace, model, Effort, cts.Token);
                while (state != null && state.State == "running")
                {
                    if (IsDisposed) return;
                    _lblStatus.Text = Localization.T("Status") + ": " + PhaseText(state);
                    await Task.Delay(2000, cts.Token);
                    state = await loop.EvolveStateAsync(workspace, cts.Token);
                }

                if (state == null) return;
                string score = string.Format(Localization.T("HarnessScore"), state.CandidatePassed, state.TaskCount, state.BaselinePassed, state.TaskCount);
                switch (state.State)
                {
                    case "done":
                        _lblStatus.Text = Localization.T("Status") +
                                          (state.Adopted ? string.Format(Localization.T("StatusAdopted"), state.CandidateVersion) : Localization.T("StatusKeptOld"));
                        Notify(state.Adopted ? Localization.T("HarnessAdopted") : Localization.T("HarnessKept"),
                            state.Detail == null ? score : score + "\n" + state.Detail, error: false);
                        break;
                    case "stopped":
                        _lblStatus.Text = Localization.T("Status") + Localization.T("StatusStopped");
                        break;
                    default:
                        _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                        Notify(Localization.T("EvolveFailed"), state.Error ?? score, error: true);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = Localization.T("Status") + Localization.T("StatusStopped");
            }
            catch (Exception ex)
            {
                AppLog.Error(Localization.T("EvolveFailed"), ex);
                _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                Notify(Localization.T("EvolveFailed"), ex.Message, error: true);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    Reload();
                }
            }
        }

        private static string PhaseText(LoopEvolveState state) => state.Phase switch
        {
            "evaluating" => string.Format(Localization.T("StatusReviewing"), state.TaskCount, state.TaskCount * 2),
            "proposing" => Localization.T("StatusProposingPhase"),
            _ => Localization.T("StatusPreparingPhase")
        };

        private async void OnRollback(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            int currentVersion;
            try
            {
                var current = Ledger.Open(workspace).Current;
                if (current == null || current.Version <= 1)
                {
                    Notify(Localization.T("NoEarlierVersion"), Localization.T("AlreadyFirstVersion"));
                    return;
                }
                currentVersion = current.Version;
            }
            catch (Exception ex)
            {
                Notify(Localization.T("LedgerReadFailed"), ex.Message, error: true);
                return;
            }

            int target = currentVersion - 1;
            if (MessageBox.Show(FindForm(),
                    string.Format(Localization.T("HarnessRollbackAsk"), target, currentVersion + 1),
                    Localization.T("HarnessRollbackTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            try
            {
                using var loop = new LoopApiClient();
                var version = await loop.RollbackAsync(workspace, target);
                Reload();
                _lblStatus.Text = Localization.T("Status") + string.Format(Localization.T("StatusRolledBackTo"), target, version);
                Notify(Localization.T("RolledBack"), string.Format(Localization.T("CurrentVersion"), version));
            }
            catch (Exception ex)
            {
                AppLog.Error(Localization.T("RollbackFailed"), ex);
                Notify(Localization.T("RollbackFailed"), ex.Message, error: true);
            }
        }

        private async void OnAddRule(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            var settings = _settings();
            string model = Model;
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify(Localization.T("MissingConfig"), Localization.T("HarnessNeedEndpointAndModel"), error: true);
                return;
            }
            using var form = new RuleForm((name, text, ct) =>
                RuleAssistant.ImproveAsync(
                    Reasoners.Create(settings, model, Effort, workspace), name, text, ct));
            if (form.ShowDialog(FindForm()) != DialogResult.OK) return;

            try
            {
                using var loop = new LoopApiClient();
                var id = await loop.AddRuleAsync(workspace, form.RuleName, form.RuleText);
                Reload();
                Notify(Localization.T("RuleRegistered"), string.Format(Localization.T("RuleRegisteredBody"), id));
            }
            catch (Exception ex)
            {
                AppLog.Error(Localization.T("RuleRegisterFailed"), ex);
                Notify(Localization.T("RuleRegisterFailed"), ex.Message, error: true);
            }
        }

        private void OnShowDefinitions(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            try
            {
                var ledger = Ledger.Open(workspace);
                var current = ledger.Current;
                if (current == null)
                {
                    Notify(Localization.T("HarnessNoActivation"), Localization.T("HarnessNoActivationBody"), error: true);
                    return;
                }

                var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                var active = current.Features.Select(r => r?.Id).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
                var sb = new StringBuilder();
                sb.AppendLine(string.Format(Localization.T("HarnessCurrentHeader"), current.Version, current.At, current.By));
                if (!string.IsNullOrWhiteSpace(current.Note)) sb.AppendLine(current.Note);
                sb.AppendLine();
                sb.AppendLine(string.Format(Localization.T("HarnessEnabled"), current.Features.Count));
                foreach (string kind in ObjectKinds.Append("other"))
                {
                    var group = current.Features
                        .Where(r => r?.Id != null
                            && (features.TryGetValue(r.Id, out var f) ? f.Kind : "other")
                            == (kind == "other" ? "other" : kind))
                        .ToList();
                    if (group.Count == 0) continue;
                    sb.AppendLine();
                    sb.AppendLine($"## {ObjectName(kind)}（{group.Count}）");
                    foreach (var reference in group)
                    {
                        features.TryGetValue(reference.Id, out var feature);
                        sb.AppendLine();
                        sb.AppendLine($"[{feature?.Kind ?? "?"}] {reference.Id}");
                        if (!string.IsNullOrWhiteSpace(feature?.Problem))
                            sb.AppendLine("  " + Localization.T("HarnessProblemLabel") + feature.Problem);
                        string payload = PayloadText(feature);
                        if (!string.IsNullOrWhiteSpace(payload))
                            sb.AppendLine("  " + Localization.T("HarnessContentLabel") + payload);
                        if (reference.Params is { Count: > 0 })
                            sb.AppendLine("  " + Localization.T("HarnessParamsLabel") + string.Join(", ",
                                reference.Params.Select(p => $"{p.Key}={p.Value}")));
                    }
                }

                var dormant = features.Values
                    .Where(f => !active.Contains(f.Id))
                    .OrderBy(f => f.Id, StringComparer.Ordinal)
                    .ToList();
                if (dormant.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(string.Format(Localization.T("HarnessDormant"), dormant.Count));
                    foreach (var feature in dormant)
                        sb.AppendLine($"[{feature.Kind}] {feature.Id}");
                }

                ShowText(Localization.T("HarnessDefsTitle"), sb.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Error(Localization.T("HarnessReadFeaturesFailed"), ex);
                Notify(Localization.T("HarnessReadFailed"), ex.Message, error: true);
            }
        }

        private void OnShowTasks(object sender, EventArgs e)
        {
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify(Localization.T("PickWorkspace"), Localization.T("WorkspaceMissing") + workspace, error: true);
                return;
            }

            try
            {
                var ledger = Ledger.Open(workspace);
                var tasks = TaskSet.Load(ledger.EvalDir);
                var sb = new StringBuilder();
                sb.AppendLine(Localization.T("HarnessDirLabel") + Path.Combine(ledger.EvalDir, "tasks"));
                sb.AppendLine(string.Format(Localization.T("HarnessTaskCount"), tasks.Tasks.Count));
                foreach (var task in tasks.Tasks)
                {
                    sb.AppendLine();
                    sb.AppendLine(string.Format(Localization.T("HarnessTaskLine"), task.Id, task.Source, task.Checks.Count));
                    if (!string.IsNullOrWhiteSpace(task.Problem)) sb.AppendLine(task.Problem);
                    if (!string.IsNullOrWhiteSpace(task.Prompt)) sb.AppendLine(Localization.T("HarnessPromptLabel") + task.Prompt);
                }

                ShowText(Localization.T("HarnessTasksTitle"), sb.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Error(Localization.T("HarnessReadTasksFailed"), ex);
                Notify(Localization.T("HarnessReadFailed"), ex.Message, error: true);
            }
        }

        private static readonly string[] ObjectKinds = ["prompt", "skill", "rule", "limit", "guard"];

        private static string ObjectName(string kind) => kind switch
        {
            "prompt" => "Prompt",
            "skill" => "Skill",
            "rule" => "Rule",
            "limit" => "Limit",
            "guard" => "Guard",
            _ => "Other"
        };

        /// <summary>激活集按五个对象计数：Prompt / Skill / Rule / Limit / Guard。</summary>
        private static string ObjectSummary(
            ActivationRecord activation, IReadOnlyDictionary<string, FeatureRecord> features)
        {
            var counts = ObjectKinds.ToDictionary(k => k, _ => 0);
            foreach (var reference in activation.Features)
            {
                if (reference?.Id == null) continue;
                string kind = features.TryGetValue(reference.Id, out var feature) ? feature.Kind : null;
                if (kind != null && counts.ContainsKey(kind)) counts[kind]++;
            }
            return string.Join(" · ", ObjectKinds.Select(k => $"{ObjectName(k)} {counts[k]}"));
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
                Text = text
            };
            // AntdUI Modal 会对其内容控件再乘一次 DPI（实测 250% 下 ×2.5），
            // 所以这里按 96-DPI 单位给尺寸，封顶值也换算到同一单位，最终物理尺寸才不会超出屏幕。
            var screen = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            float scale = DeviceDpi / 96f;
            int maxW96 = screen.Width > 0 ? (int)(screen.Width * 4 / 5 / scale) : 700;
            int maxH96 = screen.Height > 0 ? (int)(screen.Height * 4 / 5 / scale) : 500;
            box.Width = Math.Min(620, maxW96);
            box.Height = Math.Min(420, maxH96);
            var owner = FindForm();
            if (owner != null) AntdUI.Modal.open(owner, title, (object)box);
        }
    }
}
