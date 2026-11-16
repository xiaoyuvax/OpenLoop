namespace OpenLoop.App
{
    partial class BrowserView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _bar;
        private AntdUI.Button _btnBack;
        private AntdUI.Button _btnForward;
        private AntdUI.Button _btnReload;
        private TextBox _txtUrl;
        private AntdUI.Button _btnGo;
        private AntdUI.Button _btnInteract;
        private AntdUI.Button _btnUndock;
        private Panel _host;
        private Microsoft.Web.WebView2.WinForms.WebView2 _web;
        private Label _overlay;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _bar = new TableLayoutPanel();
            _btnBack = new AntdUI.Button();
            _btnForward = new AntdUI.Button();
            _btnReload = new AntdUI.Button();
            _txtUrl = new TextBox();
            _btnGo = new AntdUI.Button();
            _btnInteract = new AntdUI.Button();
            _btnUndock = new AntdUI.Button();
            _host = new Panel();
            _web = new Microsoft.Web.WebView2.WinForms.WebView2();
            _overlay = new Label();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 2;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.Margin = new Padding(0);

            // 仿常规浏览器：后退 / 前进 / 刷新 / 地址栏 / 前往 / 锁定 / 独立窗口
            _bar.Dock = DockStyle.Fill;
            _bar.ColumnCount = 7;
            _bar.RowCount = 1;
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            _bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _bar.Margin = new Padding(0);

            _btnBack.Name = "btnBack";
            _btnBack.Text = "←";
            _btnBack.Ghost = true;
            _btnBack.Radius = 6;
            _btnBack.Dock = DockStyle.Fill;
            _btnBack.Margin = new Padding(0, 1, 1, 3);
            _btnBack.Click += OnBack;

            _btnForward.Name = "btnForward";
            _btnForward.Text = "→";
            _btnForward.Ghost = true;
            _btnForward.Radius = 6;
            _btnForward.Dock = DockStyle.Fill;
            _btnForward.Margin = new Padding(0, 1, 1, 3);
            _btnForward.Click += OnForward;

            _btnReload.Name = "btnReload";
            _btnReload.Text = "⟳";
            _btnReload.Ghost = true;
            _btnReload.Radius = 6;
            _btnReload.Dock = DockStyle.Fill;
            _btnReload.Margin = new Padding(0, 1, 1, 3);
            _btnReload.Click += OnReload;

            _txtUrl.Name = "txtUrl";
            _txtUrl.Dock = DockStyle.Fill;
            _txtUrl.BorderStyle = BorderStyle.FixedSingle;
            _txtUrl.Font = new Font("Segoe UI", 9F);
            _txtUrl.Margin = new Padding(2, 3, 2, 5);
            _txtUrl.KeyDown += OnUrlKeyDown;

            _btnGo.Name = "btnGo";
            _btnGo.Text = "前往";
            _btnGo.Ghost = true;
            _btnGo.Radius = 6;
            _btnGo.Dock = DockStyle.Fill;
            _btnGo.Margin = new Padding(0, 1, 1, 3);
            _btnGo.Click += OnGo;

            _btnInteract.Name = "btnInteract";
            _btnInteract.Text = "解锁";
            _btnInteract.Ghost = true;
            _btnInteract.Radius = 6;
            _btnInteract.Dock = DockStyle.Fill;
            _btnInteract.Margin = new Padding(0, 1, 1, 3);
            _btnInteract.Click += OnToggleInteract;

            _btnUndock.Name = "btnUndock";
            _btnUndock.Text = "独立窗口";
            _btnUndock.Ghost = true;
            _btnUndock.Radius = 6;
            _btnUndock.Dock = DockStyle.Fill;
            _btnUndock.Margin = new Padding(0, 1, 0, 3);
            _btnUndock.Click += OnUndock;

            _bar.Controls.Add(_btnBack, 0, 0);
            _bar.Controls.Add(_btnForward, 1, 0);
            _bar.Controls.Add(_btnReload, 2, 0);
            _bar.Controls.Add(_txtUrl, 3, 0);
            _bar.Controls.Add(_btnGo, 4, 0);
            _bar.Controls.Add(_btnInteract, 5, 0);
            _bar.Controls.Add(_btnUndock, 6, 0);

            _host.Name = "browserHost";
            _host.Dock = DockStyle.Fill;
            _host.Margin = new Padding(0);

            _web.Name = "web";
            _web.Dock = DockStyle.Fill;
            _web.Margin = new Padding(0);
            // 默认锁定：用户点不动网页，只有 Agent 能导航/取内容
            _web.Enabled = false;

            // 锁定时盖在网页上：既不响应鼠标键盘，也明确告诉用户「当前由 Agent 控制」
            _overlay.Name = "browserOverlay";
            _overlay.Dock = DockStyle.Fill;
            _overlay.Text = "已锁定：网页由 Agent 控制\r\n点上方「解锁」后可自行操作";
            _overlay.TextAlign = ContentAlignment.MiddleCenter;
            _overlay.ForeColor = UiStyle.Muted;
            _overlay.BackColor = UiStyle.Surface;
            _overlay.Margin = new Padding(0);
            _overlay.Cursor = Cursors.Default;

            _host.Controls.Add(_web);
            _host.Controls.Add(_overlay);
            _root.Controls.Add(_bar, 0, 0);
            _root.Controls.Add(_host, 0, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Margin = new Padding(0);
            Name = "browserView";
            Controls.Add(_root);

            ResumeLayout(false);
        }
    }
}
