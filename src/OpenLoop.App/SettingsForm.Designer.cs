namespace OpenLoop.App
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Tabs _tabs;
        private AntdUI.TabPage _pageGeneral;
        private AntdUI.TabPage _pageModels;

        private TableLayoutPanel _general;
        private AntdUI.Label _lblLanguage;
        private AntdUI.Select _cboLanguage;
        private AntdUI.Label _lblTheme;
        private AntdUI.Select _cboTheme;
        private AntdUI.Label _lblFullAccess;
        private FlowLayoutPanel _fullFlow;
        private AntdUI.Switch _switchFullAccess;
        private AntdUI.Label _lblFullAccessHint;
        private AntdUI.Label _lblAgents;
        private FlowLayoutPanel _agentsFlow;
        private AntdUI.Switch _switchAgents;
        private AntdUI.Label _lblAgentsHint;
        private AntdUI.Label _lblGeneralNote;

        private TableLayoutPanel _models;
        private AntdUI.Label _lblProvider;
        private AntdUI.Select _cboProvider;
        private FlowLayoutPanel _providerButtons;
        private AntdUI.Button _btnAddProvider;
        private AntdUI.Button _btnRemoveProvider;
        private AntdUI.Label _lblName;
        private AntdUI.Input _txtName;
        private AntdUI.Label _lblUrl;
        private AntdUI.Input _txtUrl;
        private AntdUI.Label _lblKey;
        private AntdUI.Input _txtKey;
        private AntdUI.Label _lblHint;
        private FlowLayoutPanel _providerActions;
        private AntdUI.Button _btnFetch;
        private AntdUI.Button _btnTest;
        private AntdUI.Label _lblStatus;
        private AntdUI.Label _lblModels;
        private FlowLayoutPanel _modelAdd;
        private AntdUI.Input _txtModelId;
        private AntdUI.Button _btnAddModel;
        private AntdUI.Button _btnClearModels;
        private AntdUI.Button _btnRemoveModel;
        private ListBox _lstModels;

        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnCancel;
        private AntdUI.Button _btnSave;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _tabs = new AntdUI.Tabs();
            _pageGeneral = new AntdUI.TabPage();
            _pageModels = new AntdUI.TabPage();

            _general = new TableLayoutPanel();
            _lblLanguage = new AntdUI.Label();
            _cboLanguage = new AntdUI.Select();
            _lblTheme = new AntdUI.Label();
            _cboTheme = new AntdUI.Select();
            _lblFullAccess = new AntdUI.Label();
            _fullFlow = new FlowLayoutPanel();
            _switchFullAccess = new AntdUI.Switch();
            _lblFullAccessHint = new AntdUI.Label();
            _lblAgents = new AntdUI.Label();
            _agentsFlow = new FlowLayoutPanel();
            _switchAgents = new AntdUI.Switch();
            _lblAgentsHint = new AntdUI.Label();
            _lblGeneralNote = new AntdUI.Label();

            _models = new TableLayoutPanel();
            _lblProvider = new AntdUI.Label();
            _cboProvider = new AntdUI.Select();
            _providerButtons = new FlowLayoutPanel();
            _btnAddProvider = new AntdUI.Button();
            _btnRemoveProvider = new AntdUI.Button();
            _lblName = new AntdUI.Label();
            _txtName = new AntdUI.Input();
            _lblUrl = new AntdUI.Label();
            _txtUrl = new AntdUI.Input();
            _lblKey = new AntdUI.Label();
            _txtKey = new AntdUI.Input();
            _lblHint = new AntdUI.Label();
            _providerActions = new FlowLayoutPanel();
            _btnFetch = new AntdUI.Button();
            _btnTest = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();
            _lblModels = new AntdUI.Label();
            _modelAdd = new FlowLayoutPanel();
            _txtModelId = new AntdUI.Input();
            _btnAddModel = new AntdUI.Button();
            _btnClearModels = new AntdUI.Button();
            _btnRemoveModel = new AntdUI.Button();
            _lstModels = new ListBox();

            _buttons = new FlowLayoutPanel();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 2;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(12, 8, 12, 8);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Type = AntdUI.TabType.Line;
            _tabs.Margin = new Padding(0);
            _tabs.Gap = 18;

            _pageGeneral.Text = "普通";
            _pageGeneral.ReadOnly = true;
            _pageGeneral.Dock = DockStyle.Fill;

            _pageModels.Text = "模型";
            _pageModels.ReadOnly = true;
            _pageModels.Dock = DockStyle.Fill;

            // 普通
            _general.Dock = DockStyle.Fill;
            _general.ColumnCount = 2;
            _general.RowCount = 5;
            _general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _general.Padding = new Padding(8, 16, 8, 8);

            ConfigureLabel(_lblLanguage, "语言");
            ConfigureLabel(_lblTheme, "主题色");
            ConfigureLabel(_lblFullAccess, "完全许可");
            ConfigureLabel(_lblAgents, "AGENTS.md");

            _cboLanguage.Dock = DockStyle.Fill;
            _cboLanguage.Radius = 8;
            _cboLanguage.Margin = new Padding(0, 6, 240, 6);
            _cboLanguage.Items.Add(new AntdUI.SelectItem("跟随系统") { Tag = "" });
            _cboLanguage.Items.Add(new AntdUI.SelectItem("中文") { Tag = "zh" });
            _cboLanguage.Items.Add(new AntdUI.SelectItem("English") { Tag = "en" });

            _cboTheme.Dock = DockStyle.Fill;
            _cboTheme.Radius = 8;
            _cboTheme.Margin = new Padding(0, 6, 240, 6);
            _cboTheme.Items.Add(new AntdUI.SelectItem("浅色") { Tag = "light" });
            _cboTheme.Items.Add(new AntdUI.SelectItem("深色") { Tag = "dark" });
            _cboTheme.Items.Add(new AntdUI.SelectItem("灰蓝") { Tag = "grayblue" });

            _fullFlow.Dock = DockStyle.Fill;
            _fullFlow.FlowDirection = FlowDirection.LeftToRight;
            _fullFlow.WrapContents = false;
            _fullFlow.Margin = new Padding(0);

            _switchFullAccess.Width = 44;
            _switchFullAccess.Height = 22;
            _switchFullAccess.Margin = new Padding(0, 9, 8, 0);

            _lblFullAccessHint.AutoSize = true;
            _lblFullAccessHint.Text = "关闭路径边界、命令审批与环境隔离，可直连网络";
            _lblFullAccessHint.Font = new Font("Segoe UI", 8.5F);
            _lblFullAccessHint.ForeColor = UiStyle.Error;
            _lblFullAccessHint.Margin = new Padding(0, 11, 0, 0);

            _fullFlow.Controls.Add(_switchFullAccess);
            _fullFlow.Controls.Add(_lblFullAccessHint);

            _agentsFlow.Dock = DockStyle.Fill;
            _agentsFlow.FlowDirection = FlowDirection.LeftToRight;
            _agentsFlow.WrapContents = false;
            _agentsFlow.Margin = new Padding(0);

            _switchAgents.Width = 44;
            _switchAgents.Height = 22;
            _switchAgents.Checked = true;
            _switchAgents.Margin = new Padding(0, 9, 8, 0);

            _lblAgentsHint.AutoSize = true;
            _lblAgentsHint.Text = "把全局与项目 AGENTS.md 注入系统提示词";
            _lblAgentsHint.Font = new Font("Segoe UI", 8.5F);
            _lblAgentsHint.ForeColor = UiStyle.Muted;
            _lblAgentsHint.Margin = new Padding(0, 11, 0, 0);

            _agentsFlow.Controls.Add(_switchAgents);
            _agentsFlow.Controls.Add(_lblAgentsHint);

            _lblGeneralNote.AutoSize = false;
            _lblGeneralNote.Dock = DockStyle.Fill;
            _lblGeneralNote.Text = "主题与语言在重启 OpenLoop 后生效；完全许可立即生效。";
            _lblGeneralNote.Font = new Font("Segoe UI", 8.5F);
            _lblGeneralNote.ForeColor = UiStyle.Muted;
            _lblGeneralNote.TextAlign = ContentAlignment.TopLeft;
            _lblGeneralNote.Margin = new Padding(0, 10, 0, 0);

            _general.Controls.Add(_lblLanguage, 0, 0);
            _general.Controls.Add(_cboLanguage, 1, 0);
            _general.Controls.Add(_lblTheme, 0, 1);
            _general.Controls.Add(_cboTheme, 1, 1);
            _general.Controls.Add(_lblFullAccess, 0, 2);
            _general.Controls.Add(_fullFlow, 1, 2);
            _general.Controls.Add(_lblAgents, 0, 3);
            _general.Controls.Add(_agentsFlow, 1, 3);
            _general.Controls.Add(_lblGeneralNote, 0, 4);
            _general.SetColumnSpan(_lblGeneralNote, 2);
            _pageGeneral.Controls.Add(_general);

            // 模型
            _models.Dock = DockStyle.Fill;
            _models.ColumnCount = 3;
            _models.RowCount = 8;
            _models.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _models.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _models.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _models.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _models.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _models.Padding = new Padding(8, 10, 8, 4);

            ConfigureLabel(_lblProvider, "提供商");
            ConfigureLabel(_lblName, "名称");
            ConfigureLabel(_lblUrl, "接口地址");
            ConfigureLabel(_lblKey, "密钥");
            ConfigureLabel(_lblModels, "模型");

            _cboProvider.Dock = DockStyle.Fill;
            _cboProvider.Radius = 8;
            _cboProvider.Margin = new Padding(0, 4, 8, 4);
            _cboProvider.SelectedValueChanged += OnProviderChanged;

            _providerButtons.Dock = DockStyle.Fill;
            _providerButtons.FlowDirection = FlowDirection.LeftToRight;
            _providerButtons.WrapContents = false;
            _providerButtons.AutoSize = true;
            _providerButtons.Margin = new Padding(0, 4, 0, 4);

            _btnAddProvider.Text = "添加";
            _btnAddProvider.Radius = 8;
            _btnAddProvider.Size = new Size(72, 30);
            _btnAddProvider.Margin = new Padding(0, 0, 6, 0);
            _btnAddProvider.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnAddProvider.Click += OnAddProvider;

            _btnRemoveProvider.Text = "删除";
            _btnRemoveProvider.Radius = 8;
            _btnRemoveProvider.Size = new Size(72, 30);
            _btnRemoveProvider.Margin = new Padding(0);
            _btnRemoveProvider.Click += OnRemoveProvider;

            _providerButtons.Controls.Add(_btnAddProvider);
            _providerButtons.Controls.Add(_btnRemoveProvider);

            _txtName.Dock = DockStyle.Fill;
            _txtName.Radius = 8;
            _txtName.Margin = new Padding(0, 4, 8, 4);
            _txtUrl.Dock = DockStyle.Fill;
            _txtUrl.Radius = 8;
            _txtUrl.Margin = new Padding(0, 4, 8, 4);
            _txtKey.Dock = DockStyle.Fill;
            _txtKey.Radius = 8;
            _txtKey.UseSystemPasswordChar = true;
            _txtKey.Margin = new Padding(0, 4, 8, 4);

            _lblHint.AutoSize = false;
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.MiddleLeft;
            _lblHint.Margin = new Padding(0);

            _providerActions.Dock = DockStyle.Fill;
            _providerActions.FlowDirection = FlowDirection.LeftToRight;
            _providerActions.WrapContents = false;
            _providerActions.AutoSize = true;
            _providerActions.Margin = new Padding(0, 3, 0, 3);

            _btnFetch.Text = "拉取模型";
            _btnFetch.Radius = 8;
            _btnFetch.Size = new Size(100, 30);
            _btnFetch.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnFetch.Margin = new Padding(0, 0, 6, 0);
            _btnFetch.Click += OnFetchModels;

            _btnTest.Text = "测试连接";
            _btnTest.Radius = 8;
            _btnTest.Size = new Size(100, 30);
            _btnTest.Margin = new Padding(0);
            _btnTest.Click += OnTestProvider;

            _providerActions.Controls.Add(_btnFetch);
            _providerActions.Controls.Add(_btnTest);

            _lblStatus.AutoSize = true;
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.Margin = new Padding(12, 10, 0, 0);

            _txtModelId.Width = 300;
            _txtModelId.Height = 30;
            _txtModelId.Radius = 8;
            _txtModelId.PlaceholderText = "输入搜索模型；点添加加入列表";
            _txtModelId.Margin = new Padding(0, 4, 8, 4);
            _txtModelId.KeyDown += OnModelKeyDown;
            _txtModelId.TextChanged += OnModelSearchChanged;

            _btnAddModel.Text = "添加";
            _btnAddModel.Radius = 8;
            _btnAddModel.Size = new Size(72, 30);
            _btnAddModel.Margin = new Padding(0, 0, 6, 0);
            _btnAddModel.Click += OnAddModel;

            _btnClearModels.Text = "全部清除";
            _btnClearModels.Radius = 8;
            _btnClearModels.Size = new Size(96, 30);
            _btnClearModels.Margin = new Padding(0);
            _btnClearModels.Click += OnClearModels;

            _modelAdd.Dock = DockStyle.Fill;
            _modelAdd.FlowDirection = FlowDirection.LeftToRight;
            _modelAdd.WrapContents = false;
            _modelAdd.Margin = new Padding(0, 4, 8, 4);
            _modelAdd.Controls.Add(_txtModelId);
            _modelAdd.Controls.Add(_btnAddModel);
            _modelAdd.Controls.Add(_btnClearModels);

            _btnRemoveModel.Text = "删除选中";
            _btnRemoveModel.Radius = 8;
            _btnRemoveModel.Size = new Size(100, 30);
            _btnRemoveModel.Margin = new Padding(0, 4, 0, 4);
            _btnRemoveModel.Click += OnRemoveModel;

            _lstModels.Dock = DockStyle.Fill;
            _lstModels.BorderStyle = BorderStyle.FixedSingle;
            _lstModels.IntegralHeight = false;
            _lstModels.Font = new Font("Segoe UI", 9F);
            _lstModels.BackColor = UiStyle.Surface;
            _lstModels.ForeColor = UiStyle.Text;
            _lstModels.Margin = new Padding(0, 2, 0, 0);

            _models.Controls.Add(_lblProvider, 0, 0);
            _models.Controls.Add(_cboProvider, 1, 0);
            _models.Controls.Add(_providerButtons, 2, 0);
            _models.Controls.Add(_lblName, 0, 1);
            _models.Controls.Add(_txtName, 1, 1);
            _models.SetColumnSpan(_txtName, 2);
            _models.Controls.Add(_lblUrl, 0, 2);
            _models.Controls.Add(_txtUrl, 1, 2);
            _models.SetColumnSpan(_txtUrl, 2);
            _models.Controls.Add(_lblKey, 0, 3);
            _models.Controls.Add(_txtKey, 1, 3);
            _models.SetColumnSpan(_txtKey, 2);
            _models.Controls.Add(_lblHint, 0, 4);
            _models.SetColumnSpan(_lblHint, 3);
            _models.Controls.Add(_providerActions, 0, 5);
            _models.SetColumnSpan(_providerActions, 2);
            _models.Controls.Add(_lblStatus, 2, 5);
            _models.Controls.Add(_lblModels, 0, 6);
            _models.Controls.Add(_modelAdd, 1, 6);
            _models.Controls.Add(_btnRemoveModel, 2, 6);
            _models.Controls.Add(_lstModels, 0, 7);
            _models.SetColumnSpan(_lstModels, 3);
            _pageModels.Controls.Add(_models);

            _tabs.Pages.Add(_pageGeneral);
            _tabs.Pages.Add(_pageModels);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Text = "保存";
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 6);
            _btnSave.Click += OnSave;

            _btnCancel.Text = "取消";
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 6);
            _btnCancel.Click += OnCancel;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);

            _root.Controls.Add(_tabs, 0, 0);
            _root.Controls.Add(_buttons, 0, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(780, 540);
            MinimumSize = new Size(740, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "设置";
            Controls.Add(_root);

            ResumeLayout(false);
        }

        private static void ConfigureLabel(AntdUI.Label label, string text)
        {
            label.Dock = DockStyle.Fill;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = UiStyle.Text;
            label.Margin = new Padding(0);
        }
    }
}
