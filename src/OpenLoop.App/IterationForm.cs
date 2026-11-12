using OpenLoop.Service.Source;
using OpenLoop.Service.Web;
using Puppet.Core;

namespace OpenLoop.App
{
    /// <summary>
    /// 迭代确认与进度窗口：先摊开「谁、从哪个版本、升到哪个版本、改了什么」，用户点头才执行；
    /// 执行中把每一步写进进度框。A/I 换完才重启壳（界面会退出），重启后由启动流程再打开本窗口报告结果。
    /// </summary>
    public partial class IterationForm : Form, IPuppet
    {
        [PuppetIgnore] string IPuppet.AgentAccessKey => PuppetKeyVault.GlobalKey;
        [PuppetIgnore] Common.Logging.ILog IPuppet.AgentLog => AppLog.Log;
        string IPuppet.AgentInstanceName => "IterationForm";

        private readonly string _workspace;
        private readonly List<RolePlan> _plan;
        private CancellationTokenSource _cts;
        private bool _running;
        private bool _gitBusy;
        private bool _hasGitChanges;
        private readonly bool _reportMode;

        public IterationForm(string workspace, List<RolePlan> plan, string summary)
        {
            InitializeComponent();
            _workspace = workspace ?? "";
            _plan = plan ?? [];

            Text = "迭代确认";
            _txtSummary.Text = summary ?? "";
            // 行高用 Dip() 缩放，容器高度也用同一把尺子：否则高分屏上最后几个部件会被裁掉
            _root.RowStyles[0] = new RowStyle(SizeType.Absolute, Dip(222));
            RenderPlan();
            _lblStatus.Text = _plan.Any(p => p.Update)
                ? "确认后开始：A 与 I 不会同时换，壳最后重启。"
                : "没有部件需要更新：源码或依赖有变化才需要迭代。";
            SyncButtons();
        }

        /// <summary>壳重启过：打开时直接展示上一轮的报告。</summary>
        public IterationForm(IterationReport report)
        {
            InitializeComponent();
            _workspace = report?.Workspace ?? "";
            _plan = [];
            _reportMode = true;

            Text = report is { Ok: true } ? "迭代完成" : "迭代失败";
            _planScroll.Visible = false;
            _root.RowStyles[0] = new RowStyle(SizeType.Absolute, 0F);
            _lblSummary.Visible = false;
            _txtSummary.Visible = false;
            _txtSummary.Text = "";
            // 报告模式没有计划和摘要：把这两行的高度收掉，别留一大片空白
            _root.RowStyles[1] = new RowStyle(SizeType.Absolute, 0F);
            _root.RowStyles[2] = new RowStyle(SizeType.Absolute, 0F);
            _btnStart.Visible = false;
            _btnClose.Text = "关闭";
            _txtLog.Text = IterationRunner.ReportText(report);
            _lblStatus.Text = report is { Ok: true }
                ? "成功" + (report.ShellRestarted ? "（壳已重启）" : "")
                : "失败" + (report.RolledBack ? "，已回退到迭代前的版本" : "");
            SyncButtons();
        }

        /// <summary>壳重启交给 MainForm：写报告 → 起重启脚本 → 退出本进程。</summary>
        public Action<IterationReport> ShellRestart { get; set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PuppetRegistry.Register(this);   // 让 agent 能读计划/日志并截图确认
            _ = RefreshGitStateAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            PuppetRegistry.Unregister("IterationForm");
            base.OnFormClosed(e);
        }

        public string PlanText => string.Join("\n", _plan.Select(p =>
            $"{p.Label}\t{p.Current}\t{p.Target}\t{ActionOf(p)}"));

        public string LogText => _txtLog.Text;

        public string StatusText => _lblStatus.Text;

        public void StartFromPuppet() => OnStart(null, EventArgs.Empty);

        private void RenderPlan()
        {
            _planHost.Controls.Clear();
            _planHost.RowCount = _plan.Count + 1;
            _planHost.RowStyles.Clear();
            for (int i = 0; i <= _plan.Count; i++)
                _planHost.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(26)));

            Cell("部件", 0, 0, bold: true);
            Cell("现版本", 1, 0, bold: true);
            Cell("目标版本", 2, 0, bold: true);
            Cell("动作", 3, 0, bold: true);

            for (int i = 0; i < _plan.Count; i++)
            {
                var p = _plan[i];
                int row = i + 1;
                Cell(p.Label, 0, row);
                Cell(string.IsNullOrWhiteSpace(p.Current) ? "—" : p.Current, 1, row);
                Cell(string.IsNullOrWhiteSpace(p.Target) ? "—" : p.Target, 2, row,
                    tone: p.Update ? UiStyle.Accent : UiStyle.Muted);
                Cell(ActionOf(p) + " · " + p.Note, 3, row,
                    tone: p.Update ? UiStyle.Text : UiStyle.Muted);
            }
        }

        private static string ActionOf(RolePlan plan)
            => !plan.Update ? "不变" : plan.Current == plan.Target ? "重编" : "升版";

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void Cell(string text, int column, int row, bool bold = false, Color? tone = null)
        {
            var label = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = text,
                Font = new Font("Segoe UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = tone ?? UiStyle.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            _planHost.Controls.Add(label, column, row);
        }

        private async void OnStart(object sender, EventArgs e)
        {
            if (_running || _reportMode || !_btnStart.Enabled) return;
            _running = true;
            _btnStart.Enabled = false;
            _btnClose.Enabled = false;
            _btnClose.Text = "执行中…";
            SyncButtons();
            _lblStatus.Text = "正在执行…";
            _cts = new CancellationTokenSource();

            try
            {
                // 迭代由 I 执行：这里只提交、轮询、显示；换 I 期间请求会短暂断开
                using var client = new LoopApiClient();
                await client.StartIterationAsync(_workspace, ct: _cts.Token);
                Log("开始：" + (_plan.Where(p => p.Update).Select(p => $"{p.Label} {p.Current}→{p.Target}")
                    .DefaultIfEmpty("仅重新编译").Aggregate((a, b) => a + "；" + b)));

                var state = await client.WaitIterationAsync(_workspace, Log, TimeSpan.FromMinutes(15), _cts.Token);
                var report = state.Report ?? new IterationReport { Workspace = _workspace };
                report.Ok = state.Ok;
                report.Version = state.Version;
                report.ShellDir = state.ShellDir;
                report.RolledBack = state.RolledBack;

                if (state.State != "done")
                {
                    Log(IterationRunner.ReportText(report));
                    if (!string.IsNullOrWhiteSpace(state.Error)) Log(state.Error);
                    _lblStatus.Text = report.RolledBack ? "失败，已回退" : "失败";
                    Finish();
                    return;
                }

                if (report.Steps.Count > 0) Log(IterationRunner.ReportText(report));
                if (!string.IsNullOrWhiteSpace(report.ShellDir))
                {
                    _lblStatus.Text = "即将重启壳：界面会退出，重启后自动回到这里";
                    Log("3 秒后重启壳（A/I 已完成，壳放到最后一步）。");
                    await Task.Delay(3000);
                    ShellRestart?.Invoke(report);
                    return;
                }

                _lblStatus.Text = "成功";
                Finish();
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "已取消";
                Log("已取消。");
                Finish();
            }
            catch (Exception ex)
            {
                AppLog.Error("迭代异常", ex);
                _lblStatus.Text = "失败";
                Log("异常：" + ex.Message);
                Finish();
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Finish()
        {
            _running = false;
            _btnClose.Enabled = true;
            _btnClose.Text = "关闭";
            SyncButtons();
            // 这一轮刚改过版本号/源码，工作树状态会变，重新问一次
            _ = RefreshGitStateAsync();
        }

        /// <summary>按钮可用性只有一处口径：没有要升/重编的部件不给开始，工作树干净不给提交。</summary>
        private void SyncButtons()
        {
            _btnStart.Enabled = !_reportMode && !_running && _plan.Any(p => p.Update);
            _btnCommit.Enabled = _btnPush.Enabled = _hasGitChanges && !_running && !_gitBusy;
        }

        /// <summary>问 I 一次工作树是否干净；I 不可达时保持禁用（提交本来也得走 I）。</summary>
        private async Task RefreshGitStateAsync()
        {
            try
            {
                using var client = new LoopApiClient();
                var status = await client.GitStatusAsync();
                _hasGitChanges = (status?.Repos ?? []).Any(r => r.Files.Count > 0);
            }
            catch
            {
                _hasGitChanges = false;
            }
            if (!IsDisposed) SyncButtons();
        }
        private async void OnCommit(object sender, EventArgs e) => await CommitAsync(push: false);

        private async void OnCommitPush(object sender, EventArgs e) => await CommitAsync(push: true);

        /// <summary>
        /// 迭代结束后提交本次改动：先取预览（每个仓库的文件清单 + token），原样带回再提交。
        /// force 恒为 true：点按钮本身就是显式确认，服务端那道「上一轮失败先别提交」的闸只拦自动调用。
        /// </summary>
        private async Task CommitAsync(bool push)
        {
            if (_gitBusy || _running) return;
            _gitBusy = true;
            SyncButtons();
            _lblStatus.Text = push ? "正在提交并推送…" : "正在提交…";

            try
            {
                using var client = new LoopApiClient(timeout: TimeSpan.FromMinutes(5));
                var status = await client.GitStatusAsync();
                foreach (var bad in (status?.Repos ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Error)))
                    Log($"跳过 {bad.Id}：{bad.Error}");

                var repos = (status?.Repos ?? []).Where(r => r.Files.Count > 0).ToList();
                if (repos.Count == 0)
                {
                    Log("工作树是干净的，没有可提交的改动。");
                    _lblStatus.Text = "没有改动";
                    return;
                }

                foreach (var repo in repos)
                    Log($"预览 {repo.Id}（{repo.Branch}，{repo.Files.Count} 个文件）"
                        + (string.IsNullOrWhiteSpace(repo.DiffStat) ? "" : " · " + repo.DiffStat));

                var tokens = repos.ToDictionary(r => r.Id, r => r.Token, StringComparer.OrdinalIgnoreCase);
                var result = await client.GitCommitAsync(
                    repos.Select(r => r.Id), null, push, tokens, force: true);

                foreach (var repo in result?.Repos ?? [])
                {
                    if (!repo.Ok)
                    {
                        Log($"FAIL {repo.Id}：{repo.Error}");
                        continue;
                    }
                    string extra = push ? (repo.Pushed ? "（已推送）" : "（推送失败：" + repo.Error + "）") : "";
                    Log($"OK {repo.Id} {repo.Commit}{extra}");
                }

                bool allOk = result?.Repos is { Count: > 0 } list && list.All(r => r.Ok);
                _lblStatus.Text = allOk ? (push ? "已提交并推送" : "已提交") : "提交未全部成功（见进度）";
            }
            catch (Exception ex)
            {
                AppLog.Error("提交失败", ex);
                Log("提交异常：" + ex.Message);
                _lblStatus.Text = "提交失败";
            }
            finally
            {
                _gitBusy = false;
                SyncButtons();
                // 提交后工作树多半干净了：按钮跟着关掉
                _ = RefreshGitStateAsync();
            }
        }

        private void OnClose(object sender, EventArgs e)
        {
            if (_running)
            {
                _cts?.Cancel();
                return;
            }
            Close();
        }

        private void Log(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), line);
                return;
            }
            _txtLog.Text = _txtLog.Text.Length == 0
                ? line
                : _txtLog.Text + Environment.NewLine + line;
            _txtLog.SelectionStart = _txtLog.Text.Length;
            _txtLog.ScrollToCaret();
        }
    }
}
