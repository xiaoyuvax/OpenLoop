namespace OpenLoop.App
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _topBar;
        private AntdUI.Dropdown _btnMenu;
        private FlowLayoutPanel _titleFlow;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblSub;
        private FlowLayoutPanel _rightFlow;
        private AntdUI.Button _btnSettings;
        private AntdUI.Button _btnMin;
        private AntdUI.Button _btnMax;
        private AntdUI.Button _btnClose;
        private Panel _line;
        private AntdUI.Tabs _tabs;
        private AntdUI.TabPage _overviewPage;
        private SessionOverview _overview;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _topBar = new TableLayoutPanel();
            _btnMenu = new AntdUI.Dropdown();
            _titleFlow = new FlowLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _lblSub = new AntdUI.Label();
            _rightFlow = new FlowLayoutPanel();
            _btnSettings = new AntdUI.Button();
            _btnMin = new AntdUI.Button();
            _btnMax = new AntdUI.Button();
            _btnClose = new AntdUI.Button();
            _line = new Panel();
            _tabs = new AntdUI.Tabs();
            _overviewPage = new AntdUI.TabPage();
            _overview = new SessionOverview();

            SuspendLayout();

            // root
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Page;

            // top bar
            _topBar.Dock = DockStyle.Fill;
            _topBar.ColumnCount = 3;
            _topBar.RowCount = 1;
            _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46F));
            _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _topBar.BackColor = UiStyle.Surface;
            _topBar.Padding = new Padding(10, 0, 10, 0);

            _btnMenu.Name = "btnMenu";
            _btnMenu.Width = 38;
            _btnMenu.Height = 34;
            _btnMenu.Dock = DockStyle.Fill;
            _btnMenu.Margin = new Padding(0, 10, 6, 10);
            _btnMenu.Ghost = true;
            _btnMenu.Radius = 8;
            _btnMenu.MaxCount = 20;
            _btnMenu.IconSvg = UiIcons.Of(UiIcons.Menu, UiStyle.Text);
            _btnMenu.SelectedValueChanged += OnMenuSelect;
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuNewSession")) { Tag = "new" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuSessionList")) { Tag = "list" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuLoop")) { Tag = "loop" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuThemeLight")) { Tag = "theme-light" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuThemeDark")) { Tag = "theme-dark" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuThemeGrayBlue")) { Tag = "theme-grayblue" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuLanguageAuto")) { Tag = "lang-auto" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuLanguageZh")) { Tag = "lang-zh" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuLanguageEn")) { Tag = "lang-en" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuAbout")) { Tag = "about" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuSettings")) { Tag = "settings" });
            _btnMenu.Items.Add(new AntdUI.SelectItem(Localization.T("MenuExit")) { Tag = "exit" });

            _titleFlow.Dock = DockStyle.Fill;
            _titleFlow.FlowDirection = FlowDirection.LeftToRight;
            _titleFlow.WrapContents = false;
            _titleFlow.Margin = new Padding(0);

            _lblTitle.Name = "lblTitle";
            _lblTitle.AutoSize = true;
            _lblTitle.Text = Localization.T("AppTitle");
            _lblTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.Margin = new Padding(0, 14, 10, 0);

            _lblSub.Name = "lblSub";
            _lblSub.AutoSize = true;
            _lblSub.Text = Localization.T("AppSubtitle");
            _lblSub.Font = new Font("Segoe UI", 9F);
            _lblSub.ForeColor = UiStyle.Muted;
            _lblSub.Margin = new Padding(0, 18, 0, 0);

            _rightFlow.Dock = DockStyle.Fill;
            _rightFlow.FlowDirection = FlowDirection.RightToLeft;
            _rightFlow.WrapContents = false;
            _rightFlow.AutoSize = true;
            _rightFlow.Margin = new Padding(0);

            _btnSettings.Name = "btnSettings";
            _btnSettings.Width = 38;
            _btnSettings.Height = 34;
            _btnSettings.Ghost = true;
            _btnSettings.Radius = 8;
            _btnSettings.IconSvg = UiIcons.Of(UiIcons.Settings, UiStyle.Text);
            _btnSettings.Margin = new Padding(0, 10, 4, 10);
            _btnSettings.Click += OnSettings;

            _btnMin.Name = "btnMin";
            _btnMin.Width = 38;
            _btnMin.Height = 34;
            _btnMin.Ghost = true;
            _btnMin.Radius = 8;
            _btnMin.IconSvg = UiIcons.Of(UiIcons.Minimize, UiStyle.Text);
            _btnMin.Margin = new Padding(2, 10, 2, 10);
            _btnMin.Click += OnMinimize;

            _btnMax.Name = "btnMax";
            _btnMax.Width = 38;
            _btnMax.Height = 34;
            _btnMax.Ghost = true;
            _btnMax.Radius = 8;
            _btnMax.IconSvg = UiIcons.Of(UiIcons.Maximize, UiStyle.Text);
            _btnMax.Margin = new Padding(2, 10, 2, 10);
            _btnMax.Click += OnToggleMaximize;

            _btnClose.Name = "btnClose";
            _btnClose.Width = 38;
            _btnClose.Height = 34;
            _btnClose.Ghost = true;
            _btnClose.Radius = 8;
            _btnClose.IconSvg = UiIcons.Of(UiIcons.Close, UiStyle.Text);
            _btnClose.Margin = new Padding(2, 10, 0, 10);
            _btnClose.Click += OnCloseWindow;

            // 右到左：关闭在最右，其次最大化/最小化/设置
            _rightFlow.Controls.Add(_btnClose);
            _rightFlow.Controls.Add(_btnMax);
            _rightFlow.Controls.Add(_btnMin);
            _rightFlow.Controls.Add(_btnSettings);

            _topBar.Controls.Add(_btnMenu, 0, 0);
            _topBar.Controls.Add(_titleFlow, 1, 0);
            _topBar.Controls.Add(_rightFlow, 2, 0);
            _titleFlow.Controls.Add(_lblTitle);
            _titleFlow.Controls.Add(_lblSub);

            // 无标题栏：Banner（及标题文字）拖动整窗
            _topBar.MouseDown += OnBannerMouseDown;
            _titleFlow.MouseDown += OnBannerMouseDown;
            _lblTitle.MouseDown += OnBannerMouseDown;
            _lblSub.MouseDown += OnBannerMouseDown;

            // divider
            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            // tabs
            _tabs.Name = "tabs";
            _tabs.Dock = DockStyle.Fill;
            _tabs.Type = AntdUI.TabType.Card;
            _tabs.Style = new AntdUI.Tabs.StyleCard { Closable = true };
            _tabs.CloseDisposePage = false;
            _tabs.Margin = new Padding(0);
            _tabs.DragOrder = true;
            _tabs.ClosingPage += OnClosingPage;
            _tabs.MouseUp += OnTabMouseUp;
            _tabs.SelectedIndexChanged += OnSelectedTabChanged;

            _overviewPage.Name = "overviewPage";
            _overviewPage.Text = Localization.T("TabOverview");
            _overviewPage.ReadOnly = true;
            _overviewPage.Dock = DockStyle.Fill;
            _overviewPage.IconSvg = UiIcons.Of(UiIcons.List, UiStyle.Muted);

            _overview.Name = "overview";
            _overview.Dock = DockStyle.Fill;
            _overviewPage.Controls.Add(_overview);

            _tabs.Pages.Add(_overviewPage);

            _root.Controls.Add(_topBar, 0, 0);
            _root.Controls.Add(_line, 0, 1);
            _root.Controls.Add(_tabs, 0, 2);

            // form
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            ClientSize = new Size(1280, 840);
            MinimumSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "OpenLoop";
            Controls.Add(_root);
            Load += OnFormLoad;
            Shown += OnFormShown;
            Resize += OnWindowResize;
            FormClosing += OnFormClosing;

            ResumeLayout(false);
        }
    }
}
