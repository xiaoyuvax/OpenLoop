namespace OpenLoop.App
{
    partial class WorkspaceHarnessForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Tabs _tabs;
        private AntdUI.TabPage _pageSkills;
        private AntdUI.TabPage _pageRules;
        private AntdUI.TabPage _pageLimits;
        private AntdUI.TabPage _pageGuards;
        private AntdUI.Label _lblPath;
        private AntdUI.Label _lblStatus;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnAiConfig;
        private AntdUI.Button _btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _tabs = new AntdUI.Tabs();
            _pageSkills = new AntdUI.TabPage();
            _pageRules = new AntdUI.TabPage();
            _pageLimits = new AntdUI.TabPage();
            _pageGuards = new AntdUI.TabPage();
            _lblPath = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnAiConfig = new AntdUI.Button();
            _btnClose = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(12, 8, 12, 6);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Type = AntdUI.TabType.Line;
            _tabs.Gap = 18;
            _tabs.Font = new Font("Microsoft YaHei UI", 8F);
            _tabs.Margin = new Padding(0);

            _pageSkills.Text = Localization.T("TabSkills");
            _pageSkills.Dock = DockStyle.Fill;
            _pageRules.Text = Localization.T("HarnessTabRules");
            _pageRules.Dock = DockStyle.Fill;
            _pageLimits.Text = Localization.T("HarnessTabLimits");
            _pageLimits.Dock = DockStyle.Fill;
            _pageGuards.Text = Localization.T("HarnessTabGuards");
            _pageGuards.Dock = DockStyle.Fill;

            _lblPath.AutoSize = false;
            _lblPath.Dock = DockStyle.Fill;
            _lblPath.Font = new Font("Microsoft YaHei UI", 8F);
            _lblPath.ForeColor = UiStyle.Muted;
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblPath.AutoEllipsis = true;
            _lblPath.Padding = new Padding(2, 0, 0, 0);
            _lblPath.Margin = new Padding(0);

            _lblStatus.AutoSize = false;
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Font = new Font("Microsoft YaHei UI", 8F);
            _lblStatus.ForeColor = UiStyle.Text;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Margin = new Padding(0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnClose.Text = Localization.T("Close");
            _btnClose.Radius = 8;
            _btnClose.Font = new Font("Microsoft YaHei UI", 8F);
            _btnClose.Size = new Size(88, 34);
            _btnClose.Margin = new Padding(8, 6, 0, 6);
            _btnClose.Click += OnClose;

            //「让Agent辅助我配置」：和设置页同名同义——关了本窗体，把当前页交给 Loop 会话逐项问、逐项改
            _btnAiConfig.Text = Localization.T("AiConfig");
            _btnAiConfig.Radius = 8;
            _btnAiConfig.Font = new Font("Microsoft YaHei UI", 8F);
            _btnAiConfig.Size = new Size(148, 34);
            _btnAiConfig.Margin = new Padding(0, 6, 8, 6);
            _btnAiConfig.Click += OnAiConfig;

            _buttons.Controls.Add(_btnClose);
            _buttons.Controls.Add(_btnAiConfig);

            // 页面必须显式登记到 Tabs.Pages：只 new 出来不 Add，窗体上就一个页签都没有
            _tabs.Pages.Add(_pageSkills);
            _tabs.Pages.Add(_pageRules);
            _tabs.Pages.Add(_pageLimits);
            _tabs.Pages.Add(_pageGuards);

            _root.Controls.Add(_tabs, 0, 0);
            _root.Controls.Add(_lblStatus, 0, 1);
            _root.Controls.Add(_buttons, 0, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(860, 600);
            MinimumSize = new Size(780, 520);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("HarnessConfigTitle");
            Controls.Add(_root);

            ResumeLayout(false);
        }
    }
}
