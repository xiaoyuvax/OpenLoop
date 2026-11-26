namespace OpenLoop.App
{
    partial class NotificationsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Label _lblTitle;
        private TextBox _txtLog;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnClear;
        private AntdUI.Button _btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NotificationLog.Changed -= RefreshLog;
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _txtLog = new TextBox();
            _buttons = new FlowLayoutPanel();
            _btnClear = new AntdUI.Button();
            _btnClose = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _root.BackColor = UiStyle.Page;
            _root.Padding = new Padding(12, 10, 12, 8);

            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = Localization.T("NotificationLogTitle");
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.Margin = new Padding(0);

            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.WordWrap = true;
            _txtLog.Font = new Font("Segoe UI", 9.5F);
            _txtLog.BackColor = UiStyle.Surface;
            _txtLog.ForeColor = UiStyle.Text;
            _txtLog.BorderStyle = BorderStyle.FixedSingle;
            _txtLog.Margin = new Padding(0, 4, 0, 6);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnClose.Text = Localization.T("Close");
            _btnClose.Radius = 8;
            _btnClose.Size = new Size(80, 32);
            _btnClose.Margin = new Padding(8, 6, 0, 0);
            _btnClose.Click += OnClose;

            _btnClear.Text = Localization.T("NotificationClear");
            _btnClear.Radius = 8;
            _btnClear.Size = new Size(80, 32);
            _btnClear.Margin = new Padding(8, 6, 0, 0);
            _btnClear.Click += OnClear;

            _buttons.Controls.Add(_btnClose);
            _buttons.Controls.Add(_btnClear);

            _root.Controls.Add(_lblTitle, 0, 0);
            _root.Controls.Add(_txtLog, 0, 1);
            _root.Controls.Add(_buttons, 0, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("NotificationLogTitle");
            Controls.Add(_root);

            ResumeLayout(false);
        }
    }
}
