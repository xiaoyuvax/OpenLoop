using OpenLoop.Service.Shell;
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
        private readonly bool _upstreamNewer;
        private readonly IReadOnlyDictionary<string, VersionEntry> _before;
        private bool _canRollback;
        private bool _rollingBack;
        private bool _rolledBack;

        public IterationForm(string workspace, List<RolePlan> plan, string summary, bool upstreamNewer = false)
        {
            InitializeComponent();
            _workspace = workspace ?? "";
            _plan = plan ?? [];
            _upstreamNewer = upstreamNewer;

            Text = Localization.T("IterTitleConfirm");
            _txtSummary.Text = summary ?? "";
            // 行高用 Dip() 缩放，容器高度也用同一把尺子：否则高分屏上最后几个部件会被裁掉
            _root.RowStyles[0] = new RowStyle(SizeType.Absolute, Dip(222));
            RenderPlan();
            _lblStatus.Text = Localization.T(upstreamNewer
                ? "IterUpstreamStatus"
                : _plan.Any(p => p.Update) ? "IterReadyHint" : "IterNoUpdates");
            SyncButtons();
        }

        /// <summary>壳重启过：打开时直接展示上一轮的报告。</summary>
        public IterationForm(IterationReport report)
        {
            InitializeComponent();
            _workspace = report?.Workspace ?? "";
            _plan = [];
            _reportMode = true;
            // 旧壳写报告时不认识 Before：成功且没自动回退时，previous.json 就是迭代前的版本
            _before = report?.Before is { Count: > 0 } before ? before : PreviousVersions(report);
            _canRollback = _before != null && RolesToRestore(
                new Dictionary<string, VersionEntry>(_before, StringComparer.OrdinalIgnoreCase)).Count > 0;
            // 报告模式没有可退回的版本时也留着按钮（灰掉），别让入口忽隐忽现
            _btnRollback.Visible = true;

            Text = Localization.T(report is { Ok: true } ? "IterTitleDone" : "IterTitleFailed");
            _planScroll.Visible = false;
            _root.RowStyles[0] = new RowStyle(SizeType.Absolute, 0F);
            _lblSummary.Visible = false;
            _txtSummary.Visible = false;
            _txtSummary.Text = "";
            // 报告模式没有计划和摘要：把这两行的高度收掉，别留一大片空白
            _root.RowStyles[1] = new RowStyle(SizeType.Absolute, 0F);
            _root.RowStyles[2] = new RowStyle(SizeType.Absolute, 0F);
            _btnStart.Visible = false;
            _modeRow.Visible = false;
            _root.RowStyles[5] = new RowStyle(SizeType.Absolute, 0F);
            _btnClose.Text = Localization.T("Close");
            _txtLog.Text = IterationRunner.ReportText(report);
            _lblStatus.Text = report is { Ok: true }
                ? Localization.T("IterOk") + (report.ShellRestarted ? Localization.T("IterShellRestarted") : "")
                : Localization.T("IterFailed") + (report.RolledBack ? Localization.T("IterRolledBackToBefore") : "");
            SyncButtons();
        }

        /// <summary>壳重启交给 MainForm：写报告 → 起重启脚本 → 退出本进程。返回 false = 这次先不重启。</summary>
        public Func<IterationReport, bool> ShellRestart { get; set; }

        /// <summary>壳已发布、用户选了先不重启：按钮改成「重启壳」，等会话跑完再点。</summary>
        private IterationReport _pendingRestart;

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

        public void StartFromPuppet()
        {
            if (_pendingRestart != null) { RestartShell(); return; }
            StartAsync(confirmUpstream: false);
        }

        private void RenderPlan()
        {
            _planHost.Controls.Clear();
            _planHost.RowCount = _plan.Count + 1;
            _planHost.RowStyles.Clear();
            for (int i = 0; i <= _plan.Count; i++)
                _planHost.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(26)));

            Cell(Localization.T("IterColPart"), 0, 0, bold: true);
            Cell(Localization.T("IterColCurrent"), 1, 0, bold: true);
            Cell(Localization.T("IterColTarget"), 2, 0, bold: true);
            Cell(Localization.T("IterColAction"), 3, 0, bold: true);

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
            => Localization.T(!plan.Update ? "IterActKeep" : plan.Current == plan.Target ? "IterActRebuild" : "IterActBump");

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

        private void OnStart(object sender, EventArgs e)
        {
            if (_pendingRestart != null) { RestartShell(); return; }
            StartAsync(confirmUpstream: true);
        }

        /// <summary>壳已发布、等用户点头再切：成功会退出进程；返回说明这次仍不重启，按钮留着。</summary>
        private void RestartShell()
        {
            if (_pendingRestart == null) return;
            if (ShellRestart?.Invoke(_pendingRestart) == true) _pendingRestart = null;
        }

        private async void StartAsync(bool confirmUpstream)
        {
            if (_running || _rollingBack || _reportMode || !_btnStart.Enabled) return;

            // 上游更新只做提醒，不禁止：用户可以选择仍用本地代码迭代
            if (confirmUpstream && _upstreamNewer &&
                MessageBox.Show(this,
                    Localization.T("IterUpstreamAsk"),
                    Localization.T("IterUpstreamTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _running = true;
            _btnStart.Enabled = false;
            _btnClose.Enabled = false;
            _btnClose.Text = Localization.T("IterBusy");
            SyncButtons();
            _lblStatus.Text = Localization.T("IterRunning");
            _cts = new CancellationTokenSource();

            try
            {
                // 迭代由 I 执行：这里只提交、轮询、显示；换 I 期间请求会短暂断开
                using var client = new LoopApiClient();
                string mode = _selMode.SelectedValue as string ?? Iteration.FrameworkMode;
                Log(Localization.T("IterModeLabel") + (mode == Iteration.SelfContainedMode ? Localization.T("IterSelfContained") : Localization.T("IterFrameworkDependent")));
                await client.StartIterationAsync(_workspace, true, mode, _cts.Token);
                Log(Localization.T("IterStartLog") + (_plan.Where(p => p.Update).Select(p => $"{p.Label} {p.Current}→{p.Target}")
                    .DefaultIfEmpty(Localization.T("IterRebuildOnly")).Aggregate((a, b) => a + Localization.T("ListSep") + b)));

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
                    _lblStatus.Text = Localization.T(report.RolledBack ? "IterFailedRolledBack" : "IterFailed");
                    Finish();
                    return;
                }

                if (report.Steps.Count > 0) Log(IterationRunner.ReportText(report));
                if (!string.IsNullOrWhiteSpace(report.ShellDir))
                {
                    _lblStatus.Text = Localization.T("IterRestarting");
                    Log(Localization.T("IterRestartLog"));
                    await Task.Delay(3000);
                    if (ShellRestart?.Invoke(report) == true) return;
                    // 用户选择先不重启（有会话在跑）：A/I 已是新版本、新壳已发布，留个入口稍后切换
                    _pendingRestart = report;
                    report.ShellRestarted = false;
                    _lblStatus.Text = Localization.T("IterOkNoRestart");
                    Log(Localization.T("IterNoRestartLog"));
                    Finish();
                    return;
                }

                _lblStatus.Text = Localization.T("IterOk");
                Finish();
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = Localization.T("IterCancelled");
                Log(Localization.T("IterCancelledLog"));
                Finish();
            }
            catch (Exception ex)
            {
                AppLog.Error("迭代异常", ex);
                _lblStatus.Text = Localization.T("IterFailed");
                Log(Localization.T("IterErrorLog") + ex.Message);
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
            _btnClose.Text = Localization.T("Close");
            SyncButtons();
            // 这一轮刚改过版本号/源码，工作树状态会变，重新问一次
            _ = RefreshGitStateAsync();
        }

        /// <summary>按钮可用性只有一处口径：没有要升/重编的部件不给开始，工作树干净不给提交。</summary>
        private void SyncButtons()
        {
            _btnStart.Text = Localization.T(_pendingRestart != null ? "IterRestartShell" : "IterStart");
            _btnStart.Enabled = !_reportMode && !_running
                && (_pendingRestart != null || _plan.Any(p => p.Update));
            _btnCommit.Enabled = _btnPush.Enabled = _hasGitChanges && !_running && !_gitBusy && !_rollingBack;
            _btnRollback.Enabled = _canRollback && !_rollingBack && !_rolledBack;
            _selMode.Enabled = !_reportMode && !_running;
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
            if (_gitBusy || _running || _rollingBack) return;
            _gitBusy = true;
            SyncButtons();
            _lblStatus.Text = Localization.T(push ? "IterCommittingPush" : "IterCommitting");

            try
            {
                using var client = new LoopApiClient(timeout: TimeSpan.FromMinutes(5));
                var status = await client.GitStatusAsync();
                foreach (var bad in (status?.Repos ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Error)))
                    Log(string.Format(Localization.T("IterSkipRepo"), bad.Id, bad.Error));

                var repos = (status?.Repos ?? []).Where(r => r.Files.Count > 0).ToList();
                if (repos.Count == 0)
                {
                    Log(Localization.T("IterNoChangesLog"));
                    _lblStatus.Text = Localization.T("IterNoChanges");
                    return;
                }

                foreach (var repo in repos)
                    Log(string.Format(Localization.T("IterPreviewRepo"), repo.Id, repo.Branch, repo.Files.Count)
                        + (string.IsNullOrWhiteSpace(repo.DiffStat) ? "" : " · " + repo.DiffStat));

                var tokens = repos.ToDictionary(r => r.Id, r => r.Token, StringComparer.OrdinalIgnoreCase);
                var result = await client.GitCommitAsync(
                    repos.Select(r => r.Id), null, push, tokens, force: true);

                foreach (var repo in result?.Repos ?? [])
                {
                    if (!repo.Ok)
                    {
                        Log(string.Format(Localization.T("IterRepoFail"), repo.Id, repo.Error));
                        continue;
                    }
                    string extra = push ? (repo.Pushed ? Localization.T("IterPushed") : string.Format(Localization.T("IterPushFailed"), repo.Error)) : "";
                    Log($"OK {repo.Id} {repo.Commit}{extra}");
                }

                bool allOk = result?.Repos is { Count: > 0 } list && list.All(r => r.Ok);
                _lblStatus.Text = Localization.T(allOk ? (push ? "IterCommittedPush" : "IterCommitted") : "IterCommitPartial");
            }
            catch (Exception ex)
            {
                AppLog.Error("提交失败", ex);
                Log(Localization.T("IterCommitErrorLog") + ex.Message);
                _lblStatus.Text = Localization.T("IterCommitFailed");
            }
            finally
            {
                _gitBusy = false;
                SyncButtons();
                // 提交后工作树多半干净了：按钮跟着关掉
                _ = RefreshGitStateAsync();
            }
        }

        /// <summary>
        /// 结果窗口的手动回退：按计划阶段记下的 A/I 版本退（不按 previous.json——自动回退后那里指向刚判坏的版本）。
        /// 只退 A/I，壳保持当前版本。
        /// </summary>
        private async void OnRollback(object sender, EventArgs e)
        {
            if (_rollingBack || _rolledBack || !_canRollback) return;

            var before = _before == null
                ? null
                : new Dictionary<string, VersionEntry>(_before, StringComparer.OrdinalIgnoreCase);
            var roles = RolesToRestore(before);
            if (roles.Count == 0)
            {
                _canRollback = false;
                _lblStatus.Text = Localization.T("IterAlreadyBefore");
                SyncButtons();
                return;
            }

            string target = string.Join(Localization.T("ListSep"), roles.Select(r => $"{r} {before[r].Version}"));
            if (MessageBox.Show(this, string.Format(Localization.T("IterRollbackAsk"), target),
                    Localization.T("IterRollback"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _rollingBack = true;
            SyncButtons();
            _btnClose.Enabled = false;
            _lblStatus.Text = Localization.T("IterRollingBack");
            Log(Localization.T("IterRollbackLog") + target);

            try
            {
                var steps = await IterationRunner.RollbackAsync(before, Log);
                if (steps.Count > 0 && steps.All(s => s.Ok))
                {
                    // 下一轮把这两个角色当「未发布」，重编当前源码版本；被退回的 I 可能是旧版，只能改共享基准
                    VersionState.Forget(roles);
                    _rolledBack = true;
                    _lblStatus.Text = Localization.T("IterRolledBack");
                    Log(Localization.T("IterRolledBackLog") + target);
                }
                else
                {
                    _lblStatus.Text = Localization.T("IterRollbackPartial");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("退回失败", ex);
                Log(Localization.T("IterRollbackErrorLog") + ex.Message);
                _lblStatus.Text = Localization.T("IterRollbackFailed");
            }
            finally
            {
                _rollingBack = false;
                _btnClose.Enabled = true;
                SyncButtons();
                _ = RefreshGitStateAsync();
            }
        }

        /// <summary>
        /// 报告里没有 Before 时的兜底：只有「成功且没自动回退」的迭代，previous.json 才一定是迭代前版本。
        /// 失败自动回退后 previous 可能指向刚判坏的版本，不能用。
        /// </summary>
        private static Dictionary<string, VersionEntry> PreviousVersions(IterationReport report)
        {
            if (report is not { Ok: true, RolledBack: false }) return null;
            var store = new VersionStore();
            var map = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string role in new[] { "a", "i" })
            {
                var entry = store.Previous(role);
                if (entry?.Dir is { Length: > 0 }) map[role] = entry;
            }
            return map.Count > 0 ? map : null;
        }

        /// <summary>当前跑的不是迭代前版本的角色（只算登记的 A/I）。</summary>
        private static List<string> RolesToRestore(IReadOnlyDictionary<string, VersionEntry> before)
        {
            var store = new VersionStore();
            var roles = new List<string>();
            foreach (string role in new[] { "a", "i" })
            {
                if (before == null || !before.TryGetValue(role, out var entry)) continue;
                if (string.IsNullOrWhiteSpace(entry?.Dir) || !Directory.Exists(entry.Dir)) continue;
                if (SameDir(store.Current(role)?.Dir, entry.Dir)) continue;
                roles.Add(role);
            }
            return roles;
        }

        private static bool SameDir(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
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
