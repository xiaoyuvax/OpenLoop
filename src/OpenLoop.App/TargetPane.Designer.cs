namespace OpenLoop.App
{
    partial class TargetPane
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel _bar;
        private AntdUI.Label _lblTitle;
        private AntdUI.Segmented _segMode;
        private AntdUI.Button _btnUndock;
        private AntdUI.Button _btnClose;
        private FastColoredTextBoxNS.FastColoredTextBox _text;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _bar = new System.Windows.Forms.TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _segMode = new AntdUI.Segmented();
            _btnUndock = new AntdUI.Button();
            _btnClose = new AntdUI.Button();
            _text = new FastColoredTextBoxNS.FastColoredTextBox();
            _bar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_text).BeginInit();
            SuspendLayout();

            _bar.Name = "targetBar";
            _bar.Dock = System.Windows.Forms.DockStyle.Top;
            _bar.Height = 36;
            _bar.Margin = new System.Windows.Forms.Padding(0);
            _bar.Padding = new System.Windows.Forms.Padding(0);
            _bar.ColumnCount = 4;
            _bar.RowCount = 1;
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            _bar.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));

            _lblTitle.Name = "targetTitle";
            _lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            _lblTitle.AutoSize = false;
            _lblTitle.AutoEllipsis = true;
            _lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.Margin = new System.Windows.Forms.Padding(0);

            _segMode.Name = "targetMode";
            _segMode.Dock = System.Windows.Forms.DockStyle.Fill;
            _segMode.Height = 28;
            _segMode.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            _segMode.Items.Add(new AntdUI.SegmentedItem { Text = "模板" });
            _segMode.Items.Add(new AntdUI.SegmentedItem { Text = "实际" });
            _segMode.SelectIndex = 0;
            _segMode.SelectIndexChanged += OnModeChanged;

            _btnUndock.Name = "targetUndock";
            _btnUndock.Text = "独立窗口";
            _btnUndock.Height = 28;
            _btnUndock.Width = 88;
            _btnUndock.Radius = 6;
            _btnUndock.Margin = new System.Windows.Forms.Padding(0);
            _btnUndock.Click += OnUndock;

            _btnClose.Name = "targetClose";
            _btnClose.Text = "关闭";
            _btnClose.Height = 28;
            _btnClose.Width = 64;
            _btnClose.Radius = 6;
            _btnClose.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            _btnClose.Click += (_, _) => CloseRequested?.Invoke();

            _text.Name = "targetText";
            _text.Dock = System.Windows.Forms.DockStyle.Fill;
            _text.ReadOnly = true;
            _text.ShowLineNumbers = false;
            _text.WordWrap = true;
            _text.BorderStyle = System.Windows.Forms.BorderStyle.None;
            _text.BackColor = UiStyle.Surface;
            _text.ForeColor = UiStyle.Text;
            _text.Font = new System.Drawing.Font("Consolas", 10F);

            _bar.Controls.Add(_lblTitle, 0, 0);
            _bar.Controls.Add(_segMode, 1, 0);
            _bar.Controls.Add(_btnUndock, 2, 0);
            _bar.Controls.Add(_btnClose, 3, 0);

            Controls.Add(_text);
            Controls.Add(_bar);
            Name = "TargetPane";
            BackColor = UiStyle.Surface;

            _bar.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_text).EndInit();
            ResumeLayout(false);
        }
    }
}
