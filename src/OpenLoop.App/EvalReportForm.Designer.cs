namespace OpenLoop.App
{
    partial class EvalReportForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private ListBox _lstReports;
        private TableLayoutPanel _detail;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblStatus;
        private TextBox _txtPreview;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnOpenFolder;
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
            _lstReports = new ListBox();
            _detail = new TableLayoutPanel();
            _header = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _txtPreview = new TextBox();
            _buttons = new FlowLayoutPanel();
            _btnOpenFolder = new AntdUI.Button();
            _btnClose = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 1;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(12, 10, 12, 8);

            _lstReports.Dock = DockStyle.Fill;
            _lstReports.IntegralHeight = false;
            _lstReports.Font = new Font("Segoe UI", 9F);
            _lstReports.Margin = new Padding(0, 0, 10, 0);
            _lstReports.SelectedIndexChanged += OnReportSelected;

            _detail.Dock = DockStyle.Fill;
            _detail.ColumnCount = 1;
            _detail.RowCount = 3;
            _detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            _detail.Margin = new Padding(0);

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 1;
            _header.RowCount = 2;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _header.Margin = new Padding(0);

            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = Localization.T("EvalReports");
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.Margin = new Padding(0);

            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Margin = new Padding(0);

            _header.Controls.Add(_lblTitle, 0, 0);
            _header.Controls.Add(_lblStatus, 0, 1);

            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.ScrollBars = ScrollBars.Both;
            _txtPreview.WordWrap = false;
            _txtPreview.Font = new Font("Consolas", 9.5F);
            _txtPreview.BackColor = UiStyle.Page;
            _txtPreview.ForeColor = UiStyle.Text;
            _txtPreview.BorderStyle = BorderStyle.FixedSingle;
            _txtPreview.Margin = new Padding(0, 4, 0, 6);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnClose.Text = Localization.T("Close");
            _btnClose.Radius = 8;
            _btnClose.Size = new Size(80, 32);
            _btnClose.Margin = new Padding(8, 6, 0, 0);
            _btnClose.Click += OnClose;

            _btnOpenFolder.Text = Localization.T("OpenFolder");
            _btnOpenFolder.Radius = 8;
            _btnOpenFolder.Size = new Size(132, 32);
            _btnOpenFolder.Margin = new Padding(8, 6, 0, 0);
            _btnOpenFolder.Click += OnOpenFolder;

            _buttons.Controls.Add(_btnClose);
            _buttons.Controls.Add(_btnOpenFolder);

            _detail.Controls.Add(_header, 0, 0);
            _detail.Controls.Add(_txtPreview, 0, 1);
            _detail.Controls.Add(_buttons, 0, 2);

            _root.Controls.Add(_lstReports, 0, 0);
            _root.Controls.Add(_detail, 1, 0);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(1000, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("EvalReports");
            Controls.Add(_root);

            ResumeLayout(false);
        }
    }
}
