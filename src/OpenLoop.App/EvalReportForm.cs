using System.Diagnostics;
using System.Text;
using OpenLoop.Core.Loop;

namespace OpenLoop.App
{
    /// <summary>Gate 评估结果：历史上每次 A/B 的逐题对比与证据入口。</summary>
    internal sealed partial class EvalReportForm : Form
    {
        private readonly string _workspace;
        private List<EvalReport> _reports = [];

        public EvalReportForm(string workspace)
        {
            _workspace = workspace;
            InitializeComponent();
            Icon = UiIcons.App;
            RefreshList();
        }

        private EvalReport Selected => (_lstReports.SelectedItem as ReportItem)?.Report;

        private void RefreshList()
        {
            _reports = EvalReportStore.List(_workspace).ToList();
            _lstReports.Items.Clear();
            foreach (var report in _reports)
                _lstReports.Items.Add(new ReportItem(report));
            if (_lstReports.Items.Count > 0) _lstReports.SelectedIndex = 0;
            else
            {
                _txtPreview.Text = Localization.T("ReportsEmpty");
                _btnOpenFolder.Enabled = false;
            }
        }

        private void OnReportSelected(object sender, EventArgs e)
        {
            var report = Selected;
            _btnOpenFolder.Enabled = report != null;
            if (report == null) return;

            string dir = EvalReportStore.FullDirectory(_workspace, report);
            string markdown = Path.Combine(dir, "report.md");
            _txtPreview.Text = File.Exists(markdown)
                ? File.ReadAllText(markdown, Encoding.UTF8)
                : Format(report);
            _lblStatus.Text = report.DirectoryPath ?? "";
        }

        private static string Format(EvalReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Localization.T("报告: ") + report.Id);
            sb.AppendLine(Localization.T("时间: ") + report.At);
            sb.AppendLine(report.Adopted
                ? string.Format(Localization.T("EvalVerdictAdopted"), report.CandidateVersion)
                : string.Format(Localization.T("EvalVerdictKept"), report.BaselineVersion));
            sb.AppendLine(string.Format(Localization.T("EvalPassedLine"),
                report.BaselinePassed, report.Total, report.CandidatePassed, report.Total));
            sb.AppendLine(Localization.T("理由: ") + report.Reason);
            sb.AppendLine();
            foreach (var task in report.Tasks)
            {
                sb.Append("- ").Append(task.Id)
                  .Append(Localization.T("：基线 ")).Append(task.BaselinePassed ? Localization.T("通过") : Localization.T("失败"))
                  .Append(Localization.T("，候选 ")).Append(task.CandidatePassed ? Localization.T("通过") : Localization.T("失败"))
                  .AppendLine();
                if (!string.IsNullOrWhiteSpace(task.BaselineDetail)) sb.Append(Localization.T("  基线: ")).AppendLine(task.BaselineDetail);
                if (!string.IsNullOrWhiteSpace(task.CandidateDetail)) sb.Append(Localization.T("  候选: ")).AppendLine(task.CandidateDetail);
            }
            return sb.ToString();
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            var report = Selected;
            if (report == null) return;
            try
            {
                string dir = EvalReportStore.FullDirectory(_workspace, report);
                if (!Directory.Exists(dir)) return;
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _lblStatus.Text = ex.Message;
            }
        }

        private void OnClose(object sender, EventArgs e) => Close();

        private sealed class ReportItem
        {
            public ReportItem(EvalReport report) => Report = report;

            public EvalReport Report { get; }

            public override string ToString()
            {
                string verdict = Report.Adopted ? Localization.T("采纳") : Localization.T("保留");
                return $"{verdict} · v{Report.BaselineVersion}→v{Report.CandidateVersion?.ToString() ?? "-"}"
                     + $" · {Report.CandidatePassed}/{Report.Total} · {Short(Report.At)}";
            }

            private static string Short(string at)
                => string.IsNullOrEmpty(at) ? "" : (at.Length >= 16 ? at[..16].Replace('T', ' ') : at);
        }
    }
}
