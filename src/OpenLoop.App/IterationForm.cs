using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;
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
        private readonly Dictionary<string, VersionEntry> _before;
        private CancellationTokenSource _cts;
        private bool _running;
        private readonly bool _reportMode;

        public IterationForm(string workspace, List<RolePlan> plan, string summary,
            Dictionary<string, VersionEntry> before)
        {
            InitializeComponent();
            _workspace = workspace ?? "";
            _plan = plan ?? [];
            _before = before ?? new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);

            Text = "迭代确认";
            _txtSummary.Text = summary ?? "";
            RenderPlan();
            _lblStatus.Text = _plan.Any(p => p.Update)
                ? "确认后开始：A 与 I 不会同时换，壳最后重启。"
                : "源码没有比运行版本新，仍可重编一次做检查。";
        }

        /// <summary>壳重启过：打开时直接展示上一轮的报告。</summary>
        public IterationForm(IterationReport report)
        {
            InitializeComponent();
            _workspace = report?.Workspace ?? "";
            _plan = [];
            _before = [];
            _reportMode = true;

            Text = report is { Ok: true } ? "迭代完成" : "迭代失败";
            _planHost.Visible = false;
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
        }

        /// <summary>壳重启交给 MainForm：写报告 → 起重启脚本 → 退出本进程。</summary>
        public Action<IterationReport> ShellRestart { get; set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PuppetRegistry.Register(this);   // 让 agent 能读计划/日志并截图确认
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
            if (_running || _reportMode) return;
            _running = true;
            _btnStart.Enabled = false;
            _btnClose.Enabled = false;
            _btnClose.Text = "执行中…";
            _lblStatus.Text = "正在执行…";
            _cts = new CancellationTokenSource();

            try
            {
                Log("开始：" + (_plan.Where(p => p.Update).Select(p => $"{p.Label} {p.Current}→{p.Target}")
                    .DefaultIfEmpty("仅重新编译").Aggregate((a, b) => a + "；" + b)));

                var report = await IterationRunner.RunAsync(_plan, _workspace, Log, _cts.Token);
                if (!report.Ok)
                {
                    Log("失败，回退到迭代前的版本…");
                    var rollback = await IterationRunner.RollbackAsync(_before, Log);
                    report.Steps.AddRange(rollback);
                    report.RolledBack = rollback.Count > 0 && rollback.All(s => s.Ok);
                    report.At = DateTimeOffset.Now.ToString("O");
                    Log(IterationRunner.ReportText(report));
                    _lblStatus.Text = report.RolledBack ? "失败，已回退" : "失败";
                    Finish();
                    return;
                }

                Log(IterationRunner.ReportText(report));
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
            _btnStart.Enabled = true;
            _btnClose.Enabled = true;
            _btnClose.Text = "关闭";
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
