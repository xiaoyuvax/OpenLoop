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

        internal void Initialize(Func<string> workspace, Func<AppSettings> settings,
            Func<string> model, Func<string> effort, Action<string, string, bool> notify)
        {
            _workspace = workspace;
            _settings = settings;
            _model = model;
            _effort = effort;
            _notify = notify;
            _flow.SizeChanged += (_, _) => FitHint();
            _tips.SetToolTip(_lblDef, "点击查看特征与规则");
            _tips.SetToolTip(_lblTasks, "点击查看考核题");
            FitHint();
            Reload();
        }

        internal void SetBusy(bool busy)
        {
            _busy = busy;
            _btnDrafts.Enabled = _btnReport.Enabled = _btnRollback.Enabled = _btnRule.Enabled = !busy;
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
                    _lblDef.Text = "def: —（首次运行后初始化）";
                }
                else
                {
                    var known = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
                    int ruleCount = current.Features.Count(r => r?.Id != null
                        && (known.TryGetValue(r.Id, out var f)
                            ? f.Kind == "rule"
                            : r.Id.StartsWith("rule.", StringComparison.Ordinal)));
                    _lblDef.Text = $"def: v{current.Version} · {current.Features.Count} {Localization.T("Features")}"
                        + $" · {ruleCount} {Localization.T("Rules")}  ›";
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
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
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
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
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
                return "缺少接口配置，未试跑基线。";

            var ledger = Ledger.Open(workspace);
            var features = ledger.Features().ToDictionary(f => f.Id, StringComparer.Ordinal);
            var activation = ledger.Current;
            if (activation == null) return "没有激活版本，未试跑基线。";

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
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            string model = Model;
            if (model.Length == 0)
            {
                Notify("缺少配置", "先在设置里选择模型。", error: true);
                return;
            }

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            _lblStatus.Text = Localization.T("Status") + ": 准备中…";

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
                string score = $"候选 {state.CandidatePassed}/{state.TaskCount}，基线 {state.BaselinePassed}/{state.TaskCount}";
                switch (state.State)
                {
                    case "done":
                        _lblStatus.Text = Localization.T("Status") +
                                          (state.Adopted ? $": 已采纳 v{state.CandidateVersion}" : ": 保留旧版");
                        Notify(state.Adopted ? "已更替" : "未更替",
                            state.Detail == null ? score : score + "\n" + state.Detail, error: false);
                        break;
                    case "stopped":
                        _lblStatus.Text = Localization.T("Status") + ": 已停止";
                        break;
                    default:
                        _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                        Notify("演进失败", state.Error ?? score, error: true);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = Localization.T("Status") + ": 已停止";
            }
            catch (Exception ex)
            {
                AppLog.Error("演进失败", ex);
                _lblStatus.Text = Localization.T("Status") + ": " + Localization.T("StatusEvolveFailed");
                Notify("演进失败", ex.Message, error: true);
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
            "proposing" => "构思中…",
            _ => "准备中…"
        };

        private async void OnRollback(object sender, EventArgs e)
        {
            string workspace = Workspace;
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
                using var loop = new LoopApiClient();
                var version = await loop.RollbackAsync(workspace, target);
                Reload();
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
            string workspace = Workspace;
            if (!Directory.Exists(workspace))
            {
                Notify("请选择工作目录", "工作目录不存在：" + workspace, error: true);
                return;
            }

            var settings = _settings();
            string model = Model;
            var (baseUrl, _) = settings.EndpointFor(model);
            baseUrl = baseUrl?.Trim() ?? "";
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                Notify("缺少配置", "先在设置里填写接口地址与模型。", error: true);
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
                Notify("规则已登记", $"{id}\n当前版本不变；下一轮演进时可被启用。");
            }
            catch (Exception ex)
            {
                AppLog.Error("登记规则失败", ex);
                Notify("登记规则失败", ex.Message, error: true);
            }
        }

        private void OnShowDefinitions(object sender, EventArgs e)
        {
            string workspace = Workspace;
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
            string workspace = Workspace;
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
