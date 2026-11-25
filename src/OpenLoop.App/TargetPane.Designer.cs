namespace OpenLoop.App
{
    partial class TargetPane
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel _bar;
        private AntdUI.Label _lblTitle;
        private AntdUI.Segmented _segMode;
        private AntdUI.Button _btnSave;
        private AntdUI.Button _btnUndock;
        private AntdUI.Button _btnClose;
        private System.Windows.Forms.Label _banner;
        private FastColoredTextBoxNS.FastColoredTextBox _text;
        private System.Windows.Forms.ToolTip _tips;

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
            _btnSave = new AntdUI.Button();
            _btnUndock = new AntdUI.Button();
            _btnClose = new AntdUI.Button();
            _banner = new System.Windows.Forms.Label();
            _text = new FastColoredTextBoxNS.FastColoredTextBox();
            _tips = new System.Windows.Forms.ToolTip(components);
            _bar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_text).BeginInit();
            SuspendLayout();

            _bar.Name = "targetBar";
            _bar.Dock = System.Windows.Forms.DockStyle.Top;
            _bar.Height = 36;
            _bar.Margin = new System.Windows.Forms.Padding(0);
            _bar.Padding = new System.Windows.Forms.Padding(0);
            _bar.ColumnCount = 5;
            _bar.RowCount = 1;
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _bar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
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

            // 工具栏一律图标化（tooltip 说明），省掉多语种文案；保存按钮同时承载四态状态
            _btnSave.Name = "targetSave";
            _btnSave.Ghost = true;
            _btnSave.Radius = 6;
            _btnSave.IconSvg = UiIcons.Of(UiIcons.Save, UiStyle.Muted);
            _btnSave.Height = 28;
            _btnSave.Width = 28;
            _btnSave.Margin = new System.Windows.Forms.Padding(0);
            _btnSave.Click += OnSave;
            _tips.SetToolTip(_btnSave, "无改动");

            _btnUndock.Name = "targetUndock";
            _btnUndock.Ghost = true;
            _btnUndock.Radius = 6;
            _btnUndock.IconSvg = UiIcons.Of(UiIcons.Undock, UiStyle.Text);
            _btnUndock.Height = 28;
            _btnUndock.Width = 28;
            _btnUndock.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            _btnUndock.Click += OnUndock;
            _tips.SetToolTip(_btnUndock, "独立窗口：正文与工具栏一起搬出去");

            _btnClose.Name = "targetClose";
            _btnClose.Ghost = true;
            _btnClose.Radius = 6;
            _btnClose.IconSvg = UiIcons.Of(UiIcons.Close, UiStyle.Text);
            _btnClose.Height = 28;
            _btnClose.Width = 28;
            _btnClose.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            _btnClose.Click += OnClose;
            _tips.SetToolTip(_btnClose, "关闭目标面板（有未保存改动会先问保存/放弃）");

            _banner.Name = "targetBanner";
            _banner.Dock = System.Windows.Forms.DockStyle.Top;
            _banner.AutoSize = false;
            _banner.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _banner.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            _banner.Margin = new System.Windows.Forms.Padding(0);
            _banner.BackColor = UiStyle.AutoInsertBack;
            _banner.ForeColor = UiStyle.Text;
            _banner.Visible = false;

            _text.Name = "targetText";
            _text.Dock = System.Windows.Forms.DockStyle.Fill;
            _text.ReadOnly = true;
            _text.ShowLineNumbers = false;
            _text.WordWrap = true;
            _text.BorderStyle = System.Windows.Forms.BorderStyle.None;
            _text.BackColor = UiStyle.Surface;
            _text.ForeColor = UiStyle.Text;
            _text.AllowProportionalFont = true; // 目标正文用比例字体（FCTB 2.19.2 起必须显式开启，否则 SetFont 抛异常、整个会话页构造失败）
            _text.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            _text.Paddings = new System.Windows.Forms.Padding(2);

            _bar.Controls.Add(_lblTitle, 0, 0);
            _bar.Controls.Add(_segMode, 1, 0);
            _bar.Controls.Add(_btnSave, 2, 0);
            _bar.Controls.Add(_btnUndock, 3, 0);
            _bar.Controls.Add(_btnClose, 4, 0);

            Controls.Add(_text);
            Controls.Add(_banner);
            Controls.Add(_bar);
            Name = "TargetPane";
            BackColor = UiStyle.Surface;

            _bar.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_text).EndInit();
            ResumeLayout(false);
        }
    }
}
