namespace OpenLoop.App
{
    partial class HarnessView
    {
        private System.ComponentModel.IContainer components = null;

        private FlowLayoutPanel _flow;
        private AntdUI.Label _lblDef;
        private AntdUI.Label _lblTasks;
        private AntdUI.Label _lblStatus;
        private AntdUI.Button _btnDrafts;
        private AntdUI.Button _btnReport;
        private AntdUI.Button _btnEvolve;
        private AntdUI.Button _btnRollback;
        private AntdUI.Button _btnRule;
        private AntdUI.Label _lblHint;
        private ToolTip _tips;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _tips?.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _tips = new ToolTip();

            _flow = new FlowLayoutPanel();
            _lblDef = new AntdUI.Label();
            _lblTasks = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _btnDrafts = new AntdUI.Button();
            _btnReport = new AntdUI.Button();
            _btnEvolve = new AntdUI.Button();
            _btnRollback = new AntdUI.Button();
            _btnRule = new AntdUI.Button();
            _lblHint = new AntdUI.Label();

            _flow.Dock = DockStyle.Fill;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.AutoScroll = true;
            _flow.Layout += OnFlowLayout;

            _lblDef.AutoSize = true;
            _lblDef.MaximumSize = new Size(260, 0);
            _lblDef.Text = "def: —";
            _lblDef.Font = new Font("Segoe UI", 9F);
            _lblDef.ForeColor = UiStyle.Muted;
            _lblDef.Margin = new Padding(0, 0, 0, 8);
            _lblDef.Cursor = Cursors.Hand;
            _lblDef.Click += OnShowDefinitions;

            _lblTasks.AutoSize = true;
            _lblTasks.MaximumSize = new Size(260, 0);
            _lblTasks.Text = Localization.T("Tasks") + ": —";
            _lblTasks.Font = new Font("Segoe UI", 9F);
            _lblTasks.ForeColor = UiStyle.Muted;
            _lblTasks.Margin = new Padding(0, 0, 0, 8);
            _lblTasks.Cursor = Cursors.Hand;
            _lblTasks.Click += OnShowTasks;

            _lblStatus.AutoSize = true;
            _lblStatus.MaximumSize = new Size(260, 0);
            _lblStatus.Text = Localization.T("Status") + ": —";
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = UiStyle.Text;
            _lblStatus.Margin = new Padding(0, 0, 0, 16);

            _btnDrafts.Name = "btnDrafts";
            _btnDrafts.Text = Localization.T("TaskDrafts");
            _btnDrafts.Radius = 8;
            _btnDrafts.Height = 34;
            _btnDrafts.Width = 236;
            _btnDrafts.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnDrafts.Margin = new Padding(0, 0, 0, 8);
            _btnDrafts.Click += OnOpenDrafts;

            _btnReport.Name = "btnReport";
            _btnReport.Text = Localization.T("EvalReports");
            _btnReport.Radius = 8;
            _btnReport.Height = 34;
            _btnReport.Width = 236;
            _btnReport.IconSvg = UiIcons.Of(UiIcons.List, UiStyle.Text);
            _btnReport.Margin = new Padding(0, 0, 0, 8);
            _btnReport.Click += OnOpenReports;

            _btnEvolve.Name = "btnEvolve";
            _btnEvolve.Text = Localization.T("Evolve");
            _btnEvolve.Radius = 8;
            _btnEvolve.Height = 34;
            _btnEvolve.Width = 236;
            _btnEvolve.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnEvolve.Margin = new Padding(0, 0, 0, 8);
            _btnEvolve.Click += OnEvolve;

            _btnRollback.Name = "btnRollback";
            _btnRollback.Text = Localization.T("Rollback");
            _btnRollback.Radius = 8;
            _btnRollback.Height = 34;
            _btnRollback.Width = 236;
            _btnRollback.IconSvg = UiIcons.Of(UiIcons.Undo, UiStyle.Text);
            _btnRollback.Margin = new Padding(0, 0, 0, 4);
            _btnRollback.Click += OnRollback;

            _btnRule.Name = "btnRule";
            _btnRule.Text = "新增规则";
            _btnRule.Radius = 8;
            _btnRule.Height = 34;
            _btnRule.Width = 236;
            _btnRule.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnRule.Margin = new Padding(0, 0, 0, 4);
            _btnRule.Click += OnAddRule;

            _lblHint.AutoSize = false;
            _lblHint.TextMultiLine = true;
            _lblHint.Width = 236;
            _lblHint.Height = 60;
            _lblHint.Text = "Harness 只改当前工作区的可演进参数（激活集：护栏开关、阈值、规则、"
                + "技能顺序、模型参数）；不改代码/工具/提示词，其它工作区不受影响；考核题 A/B 更好才采纳。";
            _lblHint.Font = new Font("Segoe UI", 8F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.Margin = new Padding(0, 12, 0, 0);

            _flow.Controls.Add(_lblDef);
            _flow.Controls.Add(_lblTasks);
            _flow.Controls.Add(_lblStatus);
            _flow.Controls.Add(_btnDrafts);
            _flow.Controls.Add(_btnReport);
            _flow.Controls.Add(_btnEvolve);
            _flow.Controls.Add(_btnRollback);
            _flow.Controls.Add(_btnRule);
            _flow.Controls.Add(_lblHint);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Controls.Add(_flow);
        }
    }
}
