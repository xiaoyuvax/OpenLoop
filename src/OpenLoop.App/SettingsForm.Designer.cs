namespace OpenLoop.App
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Tabs _tabs;
        private AntdUI.TabPage _pageGeneral;
        private AntdUI.TabPage _pageModels;
        private TableLayoutPanel _tools;
        private AntdUI.Label _lblWebFetch;
        private AntdUI.Select _cboWebFetch;
        private AntdUI.Label _lblWebFetchHint;
        private AntdUI.Label _lblToolsNote;
        private AntdUI.TabPage _pageTools;
        private TableLayoutPanel _skills;
        private TableLayoutPanel _skillHeader;
        private AntdUI.Select _selSkillScope;
        private AntdUI.Label _lblSkillsPath;
        private AntdUI.Table _tblSkills;
        private AntdUI.Column _colSkillOn;
        private AntdUI.Column _colSkillName;
        private AntdUI.Column _colSkillFrom;
        private AntdUI.Column _colSkillDesc;
        private AntdUI.Label _lblSkillsHint;
        private FlowLayoutPanel _skillActions;
        private AntdUI.Button _btnSkillToggle;
        private AntdUI.Button _btnSkillInstall;
        private AntdUI.Button _btnSkillUninstall;
        private AntdUI.TabPage _pageSkills;
        private AntdUI.TabPage _pagePlugins;

        private TableLayoutPanel _general;
        private AntdUI.Label _lblLanguage;
        private AntdUI.Select _cboLanguage;
        private AntdUI.Label _lblTheme;
        private AntdUI.Select _cboTheme;
        private AntdUI.Label _lblFullAccess;
        private FlowLayoutPanel _fullFlow;
        private AntdUI.Switch _switchFullAccess;
        private AntdUI.Label _lblFullAccessHint;
        private AntdUI.Label _lblAutoApprove;
        private FlowLayoutPanel _autoApproveFlow;
        private AntdUI.Switch _switchAutoApprove;
        private AntdUI.Label _lblAutoApproveHint;
        private AntdUI.Label _lblAgents;
        private FlowLayoutPanel _agentsFlow;
        private AntdUI.Switch _switchAgents;
        private AntdUI.Label _lblAgentsHint;
        private AntdUI.Label _lblThinking;
        private FlowLayoutPanel _thinkingFlow;
        private AntdUI.Switch _switchThinking;
        private AntdUI.Label _lblThinkingHint;
        private AntdUI.Label _lblAutoEvolve;
        private FlowLayoutPanel _autoEvolveFlow;
        private AntdUI.Switch _switchAutoEvolve;
        private AntdUI.Label _lblAutoEvolveHint;
        private AntdUI.Label _lblAutoIterate;
        private FlowLayoutPanel _autoIterateFlow;
        private AntdUI.Switch _switchAutoIterate;
        private AntdUI.Label _lblAutoIterateHint;
        private AntdUI.Label _lblGeneralNote;
    private AntdUI.Label _lblOutsideWrite;
    private FlowLayoutPanel _outsideWriteFlow;
    private AntdUI.Switch _switchOutsideWrite;
    private AntdUI.Label _lblOutsideWriteHint;
    private AntdUI.Label _lblSensitive;
    private FlowLayoutPanel _sensitiveFlow;
    private AntdUI.Switch _switchSensitive;
    private AntdUI.Label _lblSensitiveHint;

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
        private AntdUI.Button _btnShowKey;
        private AntdUI.Label _lblStatus;
        private AntdUI.Label _lblModels;
        private FlowLayoutPanel _modelAdd;
        private AntdUI.Input _txtModelId;
        private AntdUI.Button _btnAddModel;
        private AntdUI.Button _btnClearModels;
        private AntdUI.Button _btnRemoveModel;
        private AntdUI.Button _btnModelProps;
        private FlowLayoutPanel _modelActions;
        private CheckedListBox _lstModels;

        private AntdUI.TabPage _pageCollab;
        private TableLayoutPanel _collab;
        private AntdUI.Label _lblEnableCollab;
        private FlowLayoutPanel _enableCollabFlow;
        private AntdUI.Switch _switchEnableCollab;
        private AntdUI.Label _lblEnableCollabHint;
        private AntdUI.Label _lblCollabPoll;
        private AntdUI.Select _cboCollabPoll;
        private AntdUI.Label _lblCollabReuse;
        private FlowLayoutPanel _collabReuseFlow;
        private AntdUI.Switch _switchCollabReuse;
        private AntdUI.Label _lblCollabReuseHint;
        private AntdUI.Label _lblCollabNote;

        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnCancel;
        private AntdUI.Button _btnSave;
        private AntdUI.Button _btnAiConfig;
        private ToolTip _tips;

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips?.Dispose();
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _tips = new ToolTip();

            _root = new TableLayoutPanel();
            _tabs = new AntdUI.Tabs();
            _pageGeneral = new AntdUI.TabPage();
            _pageModels = new AntdUI.TabPage();
            _pageTools = new AntdUI.TabPage();
            _pageSkills = new AntdUI.TabPage();
            _pagePlugins = new AntdUI.TabPage();
            _pageCollab = new AntdUI.TabPage();

            _skills = new TableLayoutPanel();
            _skillHeader = new TableLayoutPanel();
            _selSkillScope = new AntdUI.Select();
            _lblSkillsPath = new AntdUI.Label();
            _tblSkills = new AntdUI.Table();
            _colSkillOn = new AntdUI.Column("On", Localization.T("Status"));
            _colSkillName = new AntdUI.Column("Name", Localization.T("TabSkills"));
            _colSkillFrom = new AntdUI.Column("From", Localization.T("ColFrom"));
            _colSkillDesc = new AntdUI.Column("Description", Localization.T("ColDescription"));
            _lblSkillsHint = new AntdUI.Label();
            _skillActions = new FlowLayoutPanel();
            _btnSkillToggle = new AntdUI.Button();
            _btnSkillInstall = new AntdUI.Button();
            _btnSkillUninstall = new AntdUI.Button();

            _general = new TableLayoutPanel();
            _lblLanguage = new AntdUI.Label();
            _cboLanguage = new AntdUI.Select();
            _lblTheme = new AntdUI.Label();
            _cboTheme = new AntdUI.Select();
            _lblFullAccess = new AntdUI.Label();
            _fullFlow = new FlowLayoutPanel();
            _switchFullAccess = new AntdUI.Switch();
            _lblFullAccessHint = new AntdUI.Label();
            _lblAutoApprove = new AntdUI.Label();
            _autoApproveFlow = new FlowLayoutPanel();
            _switchAutoApprove = new AntdUI.Switch();
            _lblAutoApproveHint = new AntdUI.Label();
            _tools = new TableLayoutPanel();
            _lblWebFetch = new AntdUI.Label();
            _cboWebFetch = new AntdUI.Select();
            _lblWebFetchHint = new AntdUI.Label();
            _lblToolsNote = new AntdUI.Label();
            _lblAgents = new AntdUI.Label();
            _agentsFlow = new FlowLayoutPanel();
            _switchAgents = new AntdUI.Switch();
            _lblAgentsHint = new AntdUI.Label();
            _lblThinking = new AntdUI.Label();
            _thinkingFlow = new FlowLayoutPanel();
            _switchThinking = new AntdUI.Switch();
            _lblThinkingHint = new AntdUI.Label();
            _lblAutoEvolve = new AntdUI.Label();
            _autoEvolveFlow = new FlowLayoutPanel();
            _switchAutoEvolve = new AntdUI.Switch();
            _lblAutoEvolveHint = new AntdUI.Label();
            _lblAutoIterate = new AntdUI.Label();
            _autoIterateFlow = new FlowLayoutPanel();
            _switchAutoIterate = new AntdUI.Switch();
            _lblAutoIterateHint = new AntdUI.Label();
            _lblGeneralNote = new AntdUI.Label();
            _lblOutsideWrite = new AntdUI.Label();
            _outsideWriteFlow = new FlowLayoutPanel();
            _switchOutsideWrite = new AntdUI.Switch();
            _lblOutsideWriteHint = new AntdUI.Label();
            _lblSensitive = new AntdUI.Label();
            _sensitiveFlow = new FlowLayoutPanel();
            _switchSensitive = new AntdUI.Switch();
            _lblSensitiveHint = new AntdUI.Label();

            _collab = new TableLayoutPanel();
            _lblEnableCollab = new AntdUI.Label();
            _enableCollabFlow = new FlowLayoutPanel();
            _switchEnableCollab = new AntdUI.Switch();
            _lblEnableCollabHint = new AntdUI.Label();
            _lblCollabPoll = new AntdUI.Label();
            _cboCollabPoll = new AntdUI.Select();
            _lblCollabReuse = new AntdUI.Label();
            _collabReuseFlow = new FlowLayoutPanel();
            _switchCollabReuse = new AntdUI.Switch();
            _lblCollabReuseHint = new AntdUI.Label();
            _lblCollabNote = new AntdUI.Label();

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
            _btnShowKey = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();
            _lblModels = new AntdUI.Label();
            _modelAdd = new FlowLayoutPanel();
            _txtModelId = new AntdUI.Input();
            _btnAddModel = new AntdUI.Button();
            _btnClearModels = new AntdUI.Button();
            _btnRemoveModel = new AntdUI.Button();
            _btnModelProps = new AntdUI.Button();
            _modelActions = new FlowLayoutPanel();
            _lstModels = new CheckedListBox();

            _buttons = new FlowLayoutPanel();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();
            _btnAiConfig = new AntdUI.Button();

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

            _pageGeneral.Text = Localization.T("TabGeneral");
            _pageGeneral.ReadOnly = true;
            _pageGeneral.Dock = DockStyle.Fill;

            _pageModels.Text = Localization.T("TabModels");
            _pageModels.ReadOnly = true;
            _pageModels.Dock = DockStyle.Fill;

            _pageTools.Text = Localization.T("TabTools");
            _pageTools.ReadOnly = true;
            _pageTools.Dock = DockStyle.Fill;

            _pageSkills.Text = Localization.T("TabSkills");
            _pageSkills.ReadOnly = true;
            _pageSkills.Dock = DockStyle.Fill;

            _pagePlugins.Text = Localization.T("TabPlugins");
            _pagePlugins.ReadOnly = true;
            _pagePlugins.Dock = DockStyle.Fill;

            _pageCollab.Text = Localization.T("TabCollab");
            _pageCollab.ReadOnly = true;
            _pageCollab.Dock = DockStyle.Fill;

            // 普通
            _general.Dock = DockStyle.Fill;
            _general.ColumnCount = 2;
            _general.RowCount = 11;
            _general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _general.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _general.Padding = new Padding(8, 16, 8, 8);

            ConfigureLabel(_lblLanguage, Localization.T("Language"));
            ConfigureLabel(_lblTheme, Localization.T("ThemeAccent"));
            ConfigureLabel(_lblFullAccess, Localization.T("FullAccess"));
            ConfigureLabel(_lblAutoApprove, Localization.T("AutoApprove"));
            ConfigureLabel(_lblAgents, "AGENTS.md");
            ConfigureLabel(_lblThinking, Localization.T("Thinking"));
            ConfigureLabel(_lblAutoEvolve, Localization.T("AutoEvolve"));
            ConfigureLabel(_lblAutoIterate, Localization.T("AutoIterate"));
            ConfigureLabel(_lblOutsideWrite, Localization.T("OutsideWrite"));
            ConfigureLabel(_lblSensitive, Localization.T("Sensitive"));

            _cboLanguage.Dock = DockStyle.Fill;
            _cboLanguage.Radius = 8;
            _cboLanguage.Margin = new Padding(0, 6, 240, 6);
            _cboLanguage.Items.Add(new AntdUI.SelectItem(Localization.T("FollowSystem")) { Tag = "" });
            _cboLanguage.Items.Add(new AntdUI.SelectItem(Localization.T("Zh")) { Tag = "zh" });
            _cboLanguage.Items.Add(new AntdUI.SelectItem(Localization.T("En")) { Tag = "en" });

            _cboTheme.Dock = DockStyle.Fill;
            _cboTheme.Radius = 8;
            _cboTheme.Margin = new Padding(0, 6, 240, 6);
            _cboTheme.Items.Add(new AntdUI.SelectItem(Localization.T("ThemeLight")) { Tag = "light" });
            _cboTheme.Items.Add(new AntdUI.SelectItem(Localization.T("ThemeDark")) { Tag = "dark" });
            _cboTheme.Items.Add(new AntdUI.SelectItem(Localization.T("ThemeGrayBlue")) { Tag = "grayblue" });

            _fullFlow.Dock = DockStyle.Fill;
            _fullFlow.FlowDirection = FlowDirection.LeftToRight;
            _fullFlow.WrapContents = false;
            _fullFlow.Margin = new Padding(0);

            _switchFullAccess.Width = 44;
            _switchFullAccess.Height = 22;
            _switchFullAccess.Margin = new Padding(0, 9, 8, 0);

            _lblFullAccessHint.AutoSize = true;
            _lblFullAccessHint.Text = Localization.T("FullAccessHint");
            _lblFullAccessHint.Font = new Font("Segoe UI", 8.5F);
            _lblFullAccessHint.ForeColor = UiStyle.Error;
            _lblFullAccessHint.Margin = new Padding(0, 11, 0, 0);

            _fullFlow.Controls.Add(_switchFullAccess);
            _fullFlow.Controls.Add(_lblFullAccessHint);

            _autoApproveFlow.Dock = DockStyle.Fill;
            _autoApproveFlow.FlowDirection = FlowDirection.LeftToRight;
            _autoApproveFlow.WrapContents = false;
            _autoApproveFlow.Margin = new Padding(0);

            _switchAutoApprove.Width = 44;
            _switchAutoApprove.Height = 22;
            _switchAutoApprove.Margin = new Padding(0, 9, 8, 0);

            _lblAutoApproveHint.AutoSize = true;
            _lblAutoApproveHint.Text = Localization.T("AutoApproveHint");
            _lblAutoApproveHint.Font = new Font("Segoe UI", 8.5F);
            _lblAutoApproveHint.ForeColor = UiStyle.Muted;
            _lblAutoApproveHint.Margin = new Padding(0, 11, 0, 0);

            _autoApproveFlow.Controls.Add(_switchAutoApprove);
            _autoApproveFlow.Controls.Add(_lblAutoApproveHint);

            _agentsFlow.Dock = DockStyle.Fill;
            _agentsFlow.FlowDirection = FlowDirection.LeftToRight;
            _agentsFlow.WrapContents = false;
            _agentsFlow.Margin = new Padding(0);

            _switchAgents.Width = 44;
            _switchAgents.Height = 22;
            _switchAgents.Checked = true;
            _switchAgents.Margin = new Padding(0, 9, 8, 0);

            _lblAgentsHint.AutoSize = true;
            _lblAgentsHint.Text = Localization.T("AgentsHint");
            _lblAgentsHint.Font = new Font("Segoe UI", 8.5F);
            _lblAgentsHint.ForeColor = UiStyle.Muted;
            _lblAgentsHint.Margin = new Padding(0, 11, 0, 0);

            _agentsFlow.Controls.Add(_switchAgents);
            _agentsFlow.Controls.Add(_lblAgentsHint);

            _thinkingFlow.Dock = DockStyle.Fill;
            _thinkingFlow.FlowDirection = FlowDirection.LeftToRight;
            _thinkingFlow.WrapContents = false;
            _thinkingFlow.Margin = new Padding(0);

            _switchThinking.Width = 44;
            _switchThinking.Height = 22;
            _switchThinking.Margin = new Padding(0, 9, 8, 0);

            _lblThinkingHint.AutoSize = true;
            _lblThinkingHint.Text = Localization.T("ThinkingHint");
            _lblThinkingHint.Font = new Font("Segoe UI", 8.5F);
            _lblThinkingHint.ForeColor = UiStyle.Muted;
            _lblThinkingHint.Margin = new Padding(0, 11, 0, 0);

            _thinkingFlow.Controls.Add(_switchThinking);
            _thinkingFlow.Controls.Add(_lblThinkingHint);

            _autoEvolveFlow.Dock = DockStyle.Fill;
            _autoEvolveFlow.FlowDirection = FlowDirection.LeftToRight;
            _autoEvolveFlow.WrapContents = false;
            _autoEvolveFlow.Margin = new Padding(0);

            _switchAutoEvolve.Width = 44;
            _switchAutoEvolve.Height = 22;
            _switchAutoEvolve.Margin = new Padding(0, 9, 8, 0);

            _lblAutoEvolveHint.AutoSize = true;
            _lblAutoEvolveHint.Text = Localization.T("AutoEvolveHint");
            _lblAutoEvolveHint.Font = new Font("Segoe UI", 8.5F);
            _lblAutoEvolveHint.ForeColor = UiStyle.Muted;
            _lblAutoEvolveHint.Margin = new Padding(0, 11, 0, 0);

            _autoEvolveFlow.Controls.Add(_switchAutoEvolve);
            _autoEvolveFlow.Controls.Add(_lblAutoEvolveHint);

            _autoIterateFlow.Dock = DockStyle.Fill;
            _autoIterateFlow.FlowDirection = FlowDirection.LeftToRight;
            _autoIterateFlow.WrapContents = false;
            _autoIterateFlow.Margin = new Padding(0);

            _switchAutoIterate.Width = 44;
            _switchAutoIterate.Height = 22;
            _switchAutoIterate.Margin = new Padding(0, 9, 8, 0);

            _lblAutoIterateHint.AutoSize = true;
            _lblAutoIterateHint.Text = Localization.T("AutoIterateHint");
            _lblAutoIterateHint.Font = new Font("Segoe UI", 8.5F);
            _lblAutoIterateHint.ForeColor = UiStyle.Muted;
            _lblAutoIterateHint.Margin = new Padding(0, 11, 0, 0);

            _autoIterateFlow.Controls.Add(_switchAutoIterate);
            _autoIterateFlow.Controls.Add(_lblAutoIterateHint);

            _lblGeneralNote.AutoSize = false;
            _lblGeneralNote.Dock = DockStyle.Fill;
            _lblGeneralNote.Text = Localization.T("GeneralNote");
            _lblGeneralNote.Font = new Font("Segoe UI", 8.5F);
            _lblGeneralNote.ForeColor = UiStyle.Muted;
            _lblGeneralNote.TextAlign = ContentAlignment.TopLeft;
            _lblGeneralNote.Margin = new Padding(0, 10, 0, 0);

            _outsideWriteFlow.Dock = DockStyle.Fill;
            _outsideWriteFlow.FlowDirection = FlowDirection.LeftToRight;
            _outsideWriteFlow.WrapContents = false;
            _outsideWriteFlow.Margin = new Padding(0);

            _switchOutsideWrite.Width = 44;
            _switchOutsideWrite.Height = 22;
            _switchOutsideWrite.Margin = new Padding(0, 9, 8, 0);

            _lblOutsideWriteHint.AutoSize = true;
            _lblOutsideWriteHint.Text = Localization.T("OutsideWriteHint");
            _lblOutsideWriteHint.Font = new Font("Segoe UI", 8.5F);
            _lblOutsideWriteHint.ForeColor = UiStyle.Error;
            _lblOutsideWriteHint.Margin = new Padding(0, 11, 0, 0);

            _outsideWriteFlow.Controls.Add(_switchOutsideWrite);
            _outsideWriteFlow.Controls.Add(_lblOutsideWriteHint);

            _sensitiveFlow.Dock = DockStyle.Fill;
            _sensitiveFlow.FlowDirection = FlowDirection.LeftToRight;
            _sensitiveFlow.WrapContents = false;
            _sensitiveFlow.Margin = new Padding(0);

            _switchSensitive.Width = 44;
            _switchSensitive.Height = 22;
            _switchSensitive.Margin = new Padding(0, 9, 8, 0);

            _lblSensitiveHint.AutoSize = true;
            _lblSensitiveHint.Text = Localization.T("SensitiveHint");
            _lblSensitiveHint.Font = new Font("Segoe UI", 8.5F);
            _lblSensitiveHint.ForeColor = UiStyle.Muted;
            _lblSensitiveHint.Margin = new Padding(0, 11, 0, 0);

            _sensitiveFlow.Controls.Add(_switchSensitive);
            _sensitiveFlow.Controls.Add(_lblSensitiveHint);

            _general.Controls.Add(_lblLanguage, 0, 0);
            _general.Controls.Add(_cboLanguage, 1, 0);
            _general.Controls.Add(_lblTheme, 0, 1);
            _general.Controls.Add(_cboTheme, 1, 1);
            _general.Controls.Add(_lblFullAccess, 0, 2);
            _general.Controls.Add(_fullFlow, 1, 2);
            _general.Controls.Add(_lblAutoApprove, 0, 3);
            _general.Controls.Add(_autoApproveFlow, 1, 3);
            _general.Controls.Add(_lblAgents, 0, 4);
            _general.Controls.Add(_agentsFlow, 1, 4);
            _general.Controls.Add(_lblThinking, 0, 5);
            _general.Controls.Add(_thinkingFlow, 1, 5);
            _general.Controls.Add(_lblAutoEvolve, 0, 6);
            _general.Controls.Add(_autoEvolveFlow, 1, 6);
            _general.Controls.Add(_lblAutoIterate, 0, 7);
            _general.Controls.Add(_autoIterateFlow, 1, 7);
            _general.Controls.Add(_lblOutsideWrite, 0, 8);
            _general.Controls.Add(_outsideWriteFlow, 1, 8);
            _general.Controls.Add(_lblSensitive, 0, 9);
            _general.Controls.Add(_sensitiveFlow, 1, 9);
            _general.Controls.Add(_lblGeneralNote, 0, 10);
            _general.SetColumnSpan(_lblGeneralNote, 2);
            _pageGeneral.Controls.Add(_general);

            // 工具
            _tools.Dock = DockStyle.Fill;
            _tools.ColumnCount = 2;
            _tools.RowCount = 3;
            _tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tools.Padding = new Padding(8, 16, 8, 8);

            ConfigureLabel(_lblWebFetch, "WebFetch");

            _cboWebFetch.Dock = DockStyle.Fill;
            _cboWebFetch.Radius = 8;
            _cboWebFetch.Margin = new Padding(0, 6, 240, 6);
            _cboWebFetch.Items.Add(new AntdUI.SelectItem(Localization.T("WebFetchAllow")) { Tag = "allow" });
            _cboWebFetch.Items.Add(new AntdUI.SelectItem(Localization.T("WebFetchAsk")) { Tag = "ask" });
            _cboWebFetch.Items.Add(new AntdUI.SelectItem(Localization.T("WebFetchOff")) { Tag = "off" });

            _lblWebFetchHint.AutoSize = true;
            _lblWebFetchHint.Text = Localization.T("WebFetchHint");
            _lblWebFetchHint.Font = new Font("Segoe UI", 8.5F);
            _lblWebFetchHint.ForeColor = UiStyle.Muted;
            _lblWebFetchHint.Margin = new Padding(0, 11, 0, 0);

            _lblToolsNote.AutoSize = false;
            _lblToolsNote.Dock = DockStyle.Fill;
            _lblToolsNote.Text = Localization.T("ToolsNote");
            _lblToolsNote.Font = new Font("Segoe UI", 8.5F);
            _lblToolsNote.ForeColor = UiStyle.Muted;
            _lblToolsNote.TextAlign = ContentAlignment.TopLeft;
            _lblToolsNote.Margin = new Padding(0, 10, 0, 0);

            _tools.Controls.Add(_lblWebFetch, 0, 0);
            _tools.Controls.Add(_cboWebFetch, 1, 0);
            _tools.Controls.Add(_lblWebFetchHint, 1, 1);
            _tools.Controls.Add(_lblToolsNote, 0, 2);
            _tools.SetColumnSpan(_lblToolsNote, 2);
            _pageTools.Controls.Add(_tools);

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

            ConfigureLabel(_lblProvider, Localization.T("Provider"));
            ConfigureLabel(_lblName, Localization.T("Name"));
            ConfigureLabel(_lblUrl, Localization.T("SettingsBaseUrl"));
            ConfigureLabel(_lblKey, Localization.T("SettingsKey"));
            ConfigureLabel(_lblModels, Localization.T("SettingsModel"));

            _cboProvider.Dock = DockStyle.Fill;
            _cboProvider.Radius = 8;
            _cboProvider.Margin = new Padding(0, 4, 8, 4);
            _cboProvider.SelectedValueChanged += OnProviderChanged;

            _providerButtons.Dock = DockStyle.Fill;
            _providerButtons.FlowDirection = FlowDirection.LeftToRight;
            _providerButtons.WrapContents = false;
            _providerButtons.AutoSize = true;
            _providerButtons.Margin = new Padding(0, 4, 0, 4);

            _btnAddProvider.Text = Localization.T("Add");
            _btnAddProvider.Radius = 8;
            _btnAddProvider.Size = new Size(72, 30);
            _btnAddProvider.Margin = new Padding(0, 0, 6, 0);
            _btnAddProvider.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnAddProvider.Click += OnAddProvider;

            _btnRemoveProvider.Text = Localization.T("Remove");
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

            _btnShowKey.Text = Localization.T("ShowKey");
            _btnShowKey.Ghost = true;
            _btnShowKey.Radius = 8;
            _btnShowKey.Size = new Size(84, 30);
            _btnShowKey.IconSvg = UiIcons.Of(UiIcons.Eye, UiStyle.Text);
            _btnShowKey.Margin = new Padding(0, 4, 0, 4);
            _btnShowKey.MouseDown += OnShowKeyMouseDown;
            _btnShowKey.MouseUp += OnShowKeyEnd;
            _btnShowKey.MouseLeave += OnShowKeyEnd;

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

            _btnFetch.Text = Localization.T("FetchModels");
            _btnFetch.Radius = 8;
            _btnFetch.Size = new Size(100, 30);
            _btnFetch.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnFetch.Margin = new Padding(0, 0, 6, 0);
            _btnFetch.Click += OnFetchModels;

            _btnTest.Text = Localization.T("SettingsTest");
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
            _txtModelId.PlaceholderText = Localization.T("ModelSearchHint");
            _txtModelId.Margin = new Padding(0, 4, 8, 4);
            _txtModelId.KeyDown += OnModelKeyDown;
            _txtModelId.TextChanged += OnModelSearchChanged;

            _btnAddModel.Text = Localization.T("Add");
            _btnAddModel.Radius = 8;
            _btnAddModel.Size = new Size(72, 30);
            _btnAddModel.Margin = new Padding(0, 0, 6, 0);
            _btnAddModel.Click += OnAddModel;

            _btnClearModels.Text = Localization.T("ClearModels");
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

            _btnRemoveModel.Text = Localization.T("RemoveSelected");
            _btnRemoveModel.Radius = 8;
            _btnRemoveModel.Size = new Size(100, 30);
            _btnRemoveModel.Margin = new Padding(0);
            _btnRemoveModel.Click += OnRemoveModel;

            _btnModelProps.Text = Localization.T("Properties");
            _btnModelProps.Radius = 8;
            _btnModelProps.Size = new Size(72, 30);
            _btnModelProps.Margin = new Padding(0, 0, 6, 0);
            _btnModelProps.Click += OnModelProps;

            _modelActions.Dock = DockStyle.Fill;
            _modelActions.FlowDirection = FlowDirection.LeftToRight;
            _modelActions.WrapContents = false;
            _modelActions.AutoSize = true;
            _modelActions.Margin = new Padding(0, 4, 0, 4);
            _modelActions.Controls.Add(_btnModelProps);
            _modelActions.Controls.Add(_btnRemoveModel);

            _lstModels.Dock = DockStyle.Fill;
            _lstModels.BorderStyle = BorderStyle.FixedSingle;
            _lstModels.IntegralHeight = false;
            _lstModels.Font = new Font("Segoe UI", 9F);
            _lstModels.BackColor = UiStyle.Surface;
            _lstModels.ForeColor = UiStyle.Text;
            _lstModels.Margin = new Padding(0, 2, 0, 0);
            _lstModels.CheckOnClick = true;
            _lstModels.ItemCheck += OnModelChecked;
            _lstModels.DoubleClick += OnModelProps;

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
            _models.Controls.Add(_btnShowKey, 2, 3);
            _models.Controls.Add(_lblHint, 0, 4);
            _models.SetColumnSpan(_lblHint, 3);
            _models.Controls.Add(_providerActions, 0, 5);
            _models.SetColumnSpan(_providerActions, 2);
            _models.Controls.Add(_lblStatus, 2, 5);
            _models.Controls.Add(_lblModels, 0, 6);
            _models.Controls.Add(_modelAdd, 1, 6);
            _models.Controls.Add(_modelActions, 2, 6);
            _models.Controls.Add(_lstModels, 0, 7);
            _models.SetColumnSpan(_lstModels, 3);
            _pageModels.Controls.Add(_models);

            // 技能：全局 / 项目两套目录、同一套界面管理
            _skills.Dock = DockStyle.Fill;
            _skills.ColumnCount = 1;
            _skills.RowCount = 4;
            _skills.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _skills.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _skills.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _skills.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _skills.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _skills.Padding = new Padding(8, 12, 8, 8);

            _skillHeader.Dock = DockStyle.Fill;
            _skillHeader.ColumnCount = 2;
            _skillHeader.RowCount = 1;
            _skillHeader.Margin = new Padding(0);
            _skillHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            _skillHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _selSkillScope.Dock = DockStyle.Fill;
            _selSkillScope.Radius = 8;
            _selSkillScope.Margin = new Padding(0, 6, 8, 6);
            _selSkillScope.Items.Add(new AntdUI.SelectItem(Localization.T("ScopeGlobal")) { Tag = "global" });
            _selSkillScope.Items.Add(new AntdUI.SelectItem(Localization.T("ScopeProject")) { Tag = "project" });
            _selSkillScope.SelectedValueChanged += OnSkillScopeChanged;

            _lblSkillsPath.Dock = DockStyle.Fill;
            _lblSkillsPath.Font = new Font("Segoe UI", 9F);
            _lblSkillsPath.ForeColor = UiStyle.Muted;
            _lblSkillsPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblSkillsPath.IconGap = 6;
            _lblSkillsPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblSkillsPath.AutoEllipsis = true;
            _lblSkillsPath.Margin = new Padding(0);

            _tblSkills.Dock = DockStyle.Fill;
            _tblSkills.Bordered = false;
            _tblSkills.VisibleHeader = true;
            _tblSkills.RowHeight = 40;
            _tblSkills.MultipleRows = true;
            _tblSkills.SelectIndexChanged += OnSkillSelectionChanged;
            _tblSkills.EmptyText = Localization.T("SkillsEmptyProject");
            _tblSkills.Margin = new Padding(0, 4, 0, 4);
            _colSkillOn.Width = "90";
            _colSkillName.Width = "180";
            _colSkillFrom.Width = "90";
            _colSkillDesc.Width = "360";
            _colSkillDesc.Ellipsis = true;
            _tblSkills.Columns.Add(_colSkillOn);
            _tblSkills.Columns.Add(_colSkillName);
            _tblSkills.Columns.Add(_colSkillFrom);
            _tblSkills.Columns.Add(_colSkillDesc);

            _lblSkillsHint.Dock = DockStyle.Fill;
            _lblSkillsHint.Font = new Font("Segoe UI", 8.5F);
            _lblSkillsHint.ForeColor = UiStyle.Muted;
            _lblSkillsHint.AutoEllipsis = true;
            _lblSkillsHint.Margin = new Padding(0, 4, 0, 0);
            _lblSkillsHint.Text = Localization.T("SkillsHint");

            _skillActions.Dock = DockStyle.Fill;
            _skillActions.FlowDirection = FlowDirection.LeftToRight;
            _skillActions.WrapContents = false;
            _skillActions.Margin = new Padding(0);

            _btnSkillToggle.Text = Localization.T("SkillToggle");
            _btnSkillToggle.Type = AntdUI.TTypeMini.Primary;
            _btnSkillToggle.Radius = 8;
            _btnSkillToggle.Size = new Size(112, 30);
            _btnSkillToggle.Margin = new Padding(0, 2, 8, 0);
            _btnSkillToggle.Click += OnSkillToggle;

            _btnSkillInstall.Text = Localization.T("Install");
            _btnSkillInstall.Radius = 8;
            _btnSkillInstall.Size = new Size(88, 30);
            _btnSkillInstall.Margin = new Padding(0, 2, 8, 0);
            _btnSkillInstall.Click += OnSkillInstall;

            _btnSkillUninstall.Text = Localization.T("Uninstall");
            _btnSkillUninstall.Radius = 8;
            _btnSkillUninstall.Size = new Size(88, 30);
            _btnSkillUninstall.Margin = new Padding(0, 2, 0, 0);
            _btnSkillUninstall.Click += OnSkillUninstall;

            _skillActions.Controls.Add(_btnSkillToggle);
            _skillActions.Controls.Add(_btnSkillInstall);
            _skillActions.Controls.Add(_btnSkillUninstall);

            _skillHeader.Controls.Add(_selSkillScope, 0, 0);
            _skillHeader.Controls.Add(_lblSkillsPath, 1, 0);
            _skills.Controls.Add(_skillHeader, 0, 0);
            _skills.Controls.Add(_tblSkills, 0, 1);
            _skills.Controls.Add(_skillActions, 0, 2);
            _skills.Controls.Add(_lblSkillsHint, 0, 3);
            _pageSkills.Controls.Add(_skills);

            // 协作：发起方分解任务、起同工作区的平级会话，并把监测汇报插进它的队列
            _collab.Dock = DockStyle.Fill;
            _collab.ColumnCount = 2;
            _collab.RowCount = 4;
            _collab.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _collab.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _collab.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _collab.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _collab.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _collab.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _collab.Padding = new Padding(8, 16, 8, 8);

            ConfigureLabel(_lblEnableCollab, Localization.T("EnableCollab"));
            ConfigureLabel(_lblCollabPoll, Localization.T("CollabPoll"));
            ConfigureLabel(_lblCollabReuse, Localization.T("CollabReuse"));

            _enableCollabFlow.Dock = DockStyle.Fill;
            _enableCollabFlow.FlowDirection = FlowDirection.LeftToRight;
            _enableCollabFlow.WrapContents = false;
            _enableCollabFlow.Margin = new Padding(0);

            _switchEnableCollab.Width = 44;
            _switchEnableCollab.Height = 22;
            _switchEnableCollab.Margin = new Padding(0, 9, 8, 0);

            _lblEnableCollabHint.AutoSize = true;
            _lblEnableCollabHint.Text = Localization.T("EnableCollabHint");
            _lblEnableCollabHint.Font = new Font("Segoe UI", 8.5F);
            _lblEnableCollabHint.ForeColor = UiStyle.Muted;
            _lblEnableCollabHint.Margin = new Padding(0, 11, 0, 0);

            _enableCollabFlow.Controls.Add(_switchEnableCollab);
            _enableCollabFlow.Controls.Add(_lblEnableCollabHint);

            _cboCollabPoll.Dock = DockStyle.Fill;
            _cboCollabPoll.Radius = 8;
            _cboCollabPoll.Margin = new Padding(0, 6, 240, 6);
            _cboCollabPoll.Items.Add(new AntdUI.SelectItem(string.Format(Localization.T("IntervalSeconds"), 10)) { Tag = 10 });
            _cboCollabPoll.Items.Add(new AntdUI.SelectItem(string.Format(Localization.T("IntervalSeconds"), 30)) { Tag = 30 });
            _cboCollabPoll.Items.Add(new AntdUI.SelectItem(string.Format(Localization.T("IntervalMinutes"), 1)) { Tag = 60 });
            _cboCollabPoll.Items.Add(new AntdUI.SelectItem(string.Format(Localization.T("IntervalMinutes"), 2)) { Tag = 120 });
            _cboCollabPoll.Items.Add(new AntdUI.SelectItem(string.Format(Localization.T("IntervalMinutes"), 5)) { Tag = 300 });

            _collabReuseFlow.Dock = DockStyle.Fill;
            _collabReuseFlow.FlowDirection = FlowDirection.LeftToRight;
            _collabReuseFlow.WrapContents = false;
            _collabReuseFlow.Margin = new Padding(0);

            _switchCollabReuse.Width = 44;
            _switchCollabReuse.Height = 22;
            _switchCollabReuse.Checked = true;
            _switchCollabReuse.Margin = new Padding(0, 9, 8, 0);

            _lblCollabReuseHint.AutoSize = true;
            _lblCollabReuseHint.Text = Localization.T("CollabReuseHint");
            _lblCollabReuseHint.Font = new Font("Segoe UI", 8.5F);
            _lblCollabReuseHint.ForeColor = UiStyle.Muted;
            _lblCollabReuseHint.Margin = new Padding(0, 11, 0, 0);

            _collabReuseFlow.Controls.Add(_switchCollabReuse);
            _collabReuseFlow.Controls.Add(_lblCollabReuseHint);

            _lblCollabNote.AutoSize = false;
            _lblCollabNote.Dock = DockStyle.Fill;
            _lblCollabNote.Text = Localization.T("CollabNote");
            _lblCollabNote.Font = new Font("Segoe UI", 8.5F);
            _lblCollabNote.ForeColor = UiStyle.Muted;
            _lblCollabNote.TextAlign = ContentAlignment.TopLeft;
            _lblCollabNote.Margin = new Padding(0, 10, 0, 0);

            _collab.Controls.Add(_lblEnableCollab, 0, 0);
            _collab.Controls.Add(_enableCollabFlow, 1, 0);
            _collab.Controls.Add(_lblCollabPoll, 0, 1);
            _collab.Controls.Add(_cboCollabPoll, 1, 1);
            _collab.Controls.Add(_lblCollabReuse, 0, 2);
            _collab.Controls.Add(_collabReuseFlow, 1, 2);
            _collab.Controls.Add(_lblCollabNote, 0, 3);
            _collab.SetColumnSpan(_lblCollabNote, 2);
            _pageCollab.Controls.Add(_collab);

            _tabs.Pages.Add(_pageGeneral);
            _tabs.Pages.Add(_pageTools);
            _tabs.Pages.Add(_pageModels);
            _tabs.Pages.Add(_pageSkills);
            _tabs.Pages.Add(_pagePlugins);
            _tabs.Pages.Add(_pageCollab);
            InitPluginsPage(_pagePlugins);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Text = Localization.T("SettingsSave");
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 6);
            _btnSave.Click += OnSave;

            _btnCancel.Text = Localization.T("SettingsCancel");
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 6);
            _btnCancel.Click += OnCancel;

            //「让 AI 帮我配置」：关了设置页，把当前页交给 Loop 会话逐项问、逐项改
            _btnAiConfig.Text = Localization.T("AiConfig");
            _btnAiConfig.Radius = 8;
            _btnAiConfig.Size = new Size(148, 34);
            _btnAiConfig.Margin = new Padding(0, 6, 8, 6);
            _btnAiConfig.Click += OnAiConfig;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);
            _buttons.Controls.Add(_btnAiConfig);

            _root.Controls.Add(_tabs, 0, 0);
            _root.Controls.Add(_buttons, 0, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(780, 600);
            MinimumSize = new Size(740, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("SettingsTitle");
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
