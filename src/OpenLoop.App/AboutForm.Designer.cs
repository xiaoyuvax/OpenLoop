namespace OpenLoop.App
{
    partial class AboutForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private Panel _side;
        private PictureBox _picLogo;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblSub;
        private TableLayoutPanel _right;
        private AntdUI.Label _lblVersions;
        private AntdUI.Input _txtVersions;
        private AntdUI.Button _btnRollback;
        private AntdUI.Label _lblStatus;
        private ToolTip _tips;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picLogo?.Image?.Dispose();
                _tips?.Dispose();
            }
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _tips = new ToolTip();

            _root = new TableLayoutPanel();
            _side = new Panel();
            _picLogo = new PictureBox();
            _lblTitle = new AntdUI.Label();
            _lblSub = new AntdUI.Label();
            _right = new TableLayoutPanel();
            _lblVersions = new AntdUI.Label();
            _txtVersions = new AntdUI.Input();
            _btnRollback = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();

            SuspendLayout();

            // 左栏（logo + 标题）固定宽，右侧版本与按钮占满
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 1;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.Padding = new Padding(20, 16, 20, 12);
            _root.BackColor = UiStyle.Page;

            _side.Dock = DockStyle.Fill;
            _side.Margin = new Padding(0);
            _side.BackColor = Color.Transparent;

            // 程序图标最大分辨率（OpenLoop.ico 带 256×256 帧）
            _picLogo.Dock = DockStyle.Top;
            _picLogo.Size = new Size(160, 160);
            _picLogo.SizeMode = PictureBoxSizeMode.Zoom;
            _picLogo.BackColor = Color.Transparent;
            _picLogo.Margin = new Padding(0);

            _lblTitle.AutoSize = true;
            _lblTitle.Dock = DockStyle.Top;
            _lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            _lblTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.Margin = new Padding(0, 12, 0, 0);
            _lblTitle.Padding = new Padding(0);

            _lblSub.AutoSize = true;
            _lblSub.Dock = DockStyle.Top;
            _lblSub.TextAlign = ContentAlignment.MiddleCenter;
            _lblSub.Font = new Font("Segoe UI", 9F);
            _lblSub.ForeColor = UiStyle.Muted;
            _lblSub.Margin = new Padding(0, 2, 0, 0);
            _lblSub.Padding = new Padding(0);

            _right.Dock = DockStyle.Fill;
            _right.ColumnCount = 1;
            _right.RowCount = 4;
            _right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _right.Margin = new Padding(16, 0, 0, 0);

            _lblVersions.AutoSize = true;
            _lblVersions.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblVersions.ForeColor = UiStyle.Text;
            _lblVersions.Margin = new Padding(0, 4, 0, 6);

            _txtVersions.Dock = DockStyle.Fill;
            _txtVersions.Multiline = true;
            _txtVersions.ReadOnly = true;
            _txtVersions.BorderWidth = 1;
            _txtVersions.Radius = 8;
            _txtVersions.Font = new Font("Consolas", 10F);
            _txtVersions.BackColor = UiStyle.Surface;
            _txtVersions.Margin = new Padding(0, 0, 0, 10);

            _btnRollback.IconSvg = UiIcons.Of(UiIcons.Undo, UiStyle.Text);
            _btnRollback.Ghost = true;
            _btnRollback.Radius = 8;
            _btnRollback.AutoSize = true;
            _btnRollback.MinimumSize = new Size(172, 32);
            _btnRollback.Margin = new Padding(0, 0, 0, 0);
            _btnRollback.Click += OnRollback;

            _lblStatus.AutoSize = true;
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.Margin = new Padding(0, 8, 0, 0);

            // 左栏从下往上叠：副标题在标题上面会被 Top 停靠顺序反转，这里按 状态→标题→logo 加
            _side.Controls.Add(_lblSub);
            _side.Controls.Add(_lblTitle);
            _side.Controls.Add(_picLogo);

            _right.Controls.Add(_lblVersions, 0, 0);
            _right.Controls.Add(_txtVersions, 0, 1);
            _right.Controls.Add(_btnRollback, 0, 2);
            _right.Controls.Add(_lblStatus, 0, 3);

            _root.Controls.Add(_side, 0, 0);
            _root.Controls.Add(_right, 1, 0);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            ClientSize = new Size(680, 420);
            MinimumSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Text = Localization.T("MenuAbout");
            Controls.Add(_root);
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>取 ico 里最大的帧（OpenLoop.ico 带 256×256 PNG 帧）；失败退回原始 Icon，界面不空。</summary>
        private static Image LargestIconFrame(Icon app)
        {
            try
            {
                foreach (int size in new[] { 256, 128, 96, 64, 48 })
                {
                    using var frame = new Icon(app, size, size);
                    if (frame.Width >= size) return new Bitmap(frame.ToBitmap());
                }
            }
            catch
            {
            }
            return app.ToBitmap();
        }
    }
}
