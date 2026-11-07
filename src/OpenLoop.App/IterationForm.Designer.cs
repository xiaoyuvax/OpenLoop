namespace OpenLoop.App
{
    partial class IterationForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _planHost;
        private AntdUI.Label _lblSummary;
        private AntdUI.Input _txtSummary;
        private AntdUI.Label _lblLog;
        private AntdUI.Input _txtLog;
        private AntdUI.Label _lblStatus;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnStart;
        private AntdUI.Button _btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _planHost = new TableLayoutPanel();
            _lblSummary = new AntdUI.Label();
            _txtSummary = new AntdUI.Input();
            _lblLog = new AntdUI.Label();
            _txtLog = new AntdUI.Input();
            _lblStatus = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnStart = new AntdUI.Button();
            _btnClose = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 7;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _root.Padding = new Padding(16, 12, 16, 8);
            _root.Margin = new Padding(0);

            _planHost.Dock = DockStyle.Fill;
            _planHost.ColumnCount = 4;
            _planHost.RowCount = 1;
            _planHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            _planHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            _planHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            _planHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            _planHost.AutoSize = true;
            _planHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _planHost.Margin = new Padding(0, 0, 0, 8);

            _lblSummary.AutoSize = true;
            _lblSummary.Text = "变更内容";
            _lblSummary.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblSummary.ForeColor = UiStyle.Text;
            _lblSummary.Margin = new Padding(0, 2, 0, 4);

            _txtSummary.Dock = DockStyle.Fill;
            _txtSummary.Multiline = true;
            _txtSummary.ReadOnly = true;
            _txtSummary.BorderWidth = 1;
            _txtSummary.Radius = 8;
            _txtSummary.Font = new Font("Consolas", 9F);
            _txtSummary.BackColor = UiStyle.Surface;
            _txtSummary.Margin = new Padding(0, 0, 0, 8);

            _lblLog.AutoSize = true;
            _lblLog.Text = "进度";
            _lblLog.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblLog.ForeColor = UiStyle.Text;
            _lblLog.Margin = new Padding(0, 2, 0, 4);

            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.BorderWidth = 1;
            _txtLog.Radius = 8;
            _txtLog.Font = new Font("Consolas", 9F);
            _txtLog.BackColor = UiStyle.Surface;
            _txtLog.Margin = new Padding(0, 0, 0, 6);

            _lblStatus.AutoSize = true;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.Margin = new Padding(0, 2, 0, 6);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnStart.Text = "开始迭代";
            _btnStart.Type = AntdUI.TTypeMini.Primary;
            _btnStart.Radius = 8;
            _btnStart.Width = 108;
            _btnStart.Height = 32;
            _btnStart.Margin = new Padding(8, 0, 0, 0);
            _btnStart.Click += OnStart;

            _btnClose.Text = "取消";
            _btnClose.Ghost = true;
            _btnClose.Radius = 8;
            _btnClose.Width = 96;
            _btnClose.Height = 32;
            _btnClose.Margin = new Padding(0);
            _btnClose.Click += OnClose;

            _buttons.Controls.Add(_btnStart);
            _buttons.Controls.Add(_btnClose);

            _root.Controls.Add(_planHost, 0, 0);
            _root.Controls.Add(_lblSummary, 0, 1);
            _root.Controls.Add(_txtSummary, 0, 2);
            _root.Controls.Add(_lblLog, 0, 3);
            _root.Controls.Add(_txtLog, 0, 4);
            _root.Controls.Add(_lblStatus, 0, 5);
            _root.Controls.Add(_buttons, 0, 6);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Text = "迭代";
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
