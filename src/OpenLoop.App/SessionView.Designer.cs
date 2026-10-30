namespace OpenLoop.App
{
    partial class SessionView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private FlowLayoutPanel _headerRight;
        private AntdUI.Button _btnPanel;
        private Panel _headerLine;
        private SplitContainer _mainSplit;
        private SplitContainer _upperSplit;
        private ChatFlow.ChatFlowControl _chat;
        private TableLayoutPanel _side;
        private AntdUI.Segmented _sideTabs;
        private AntdUI.Button _btnPendingSurveys;
        private Panel _sideHost;
        private FlowLayoutPanel _loopFlow;
        private FlowLayoutPanel _detailFlow;
        private SideWorkView _sideBtw;
        private AntdUI.Button _btnOpenLoop;
        private AntdUI.Label _lblDModel;
        private AntdUI.Label _lblDEffort;
        private AntdUI.Label _lblDCtx;
        private AntdUI.Label _lblDCost;
        private AntdUI.Label _lblDStatus;
        private AntdUI.Label _lblDef;
        private AntdUI.Label _lblTasks;
        private AntdUI.Label _lblTrace;
        private AntdUI.Label _lblCost;
        private AntdUI.Label _lblStatus;
        private AntdUI.Button _btnDrafts;
        private AntdUI.Button _btnReport;
        private AntdUI.Button _btnEvolve;
        private AntdUI.Button _btnRollback;
        private AntdUI.Button _btnRule;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Segmented _modeControl;
        private FlowLayoutPanel _toolbarRight;
        private AntdUI.Select _selProvider;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selEffort;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Progress _ctx;
        private AntdUI.Button _btnSend;
        private Panel _queueHost;
        private TableLayoutPanel _queueHeader;
        private AntdUI.Label _lblQueue;
        private AntdUI.Button _btnQueueSendAll;
        private FlowLayoutPanel _queueList;
        private AntdUI.Label _lblPath;
        private ToolTip _tips;
        private CommandPopup _cmdPopup;

        internal void DetachSurveyEvents() => OpenLoop.Core.Store.SurveyStore.Changed -= OnSurveyChanged;

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
            _cmdPopup = new CommandPopup();

            _root = new TableLayoutPanel();
            _header = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _headerRight = new FlowLayoutPanel();
            _btnPanel = new AntdUI.Button();
            _headerLine = new Panel();
            _mainSplit = new SplitContainer();
            _upperSplit = new SplitContainer();
            _chat = new ChatFlow.ChatFlowControl();
            _side = new TableLayoutPanel();
            _sideTabs = new AntdUI.Segmented();
            _sideHost = new Panel();
            _loopFlow = new FlowLayoutPanel();
            _btnPendingSurveys = new AntdUI.Button();
            _detailFlow = new FlowLayoutPanel();
            _btnOpenLoop = new AntdUI.Button();
            _lblDModel = new AntdUI.Label();
            _lblDEffort = new AntdUI.Label();
            _lblDCtx = new AntdUI.Label();
            _lblDCost = new AntdUI.Label();
            _lblDStatus = new AntdUI.Label();
            _lblDef = new AntdUI.Label();
            _lblTasks = new AntdUI.Label();
            _lblTrace = new AntdUI.Label();
            _lblCost = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _btnDrafts = new AntdUI.Button();
            _btnReport = new AntdUI.Button();
            _btnEvolve = new AntdUI.Button();
            _btnRollback = new AntdUI.Button();
            _btnRule = new AntdUI.Button();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _modeControl = new AntdUI.Segmented();
            _toolbarRight = new FlowLayoutPanel();
            _selProvider = new AntdUI.Select();
            _selModel = new AntdUI.Select();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _selEffort = new AntdUI.Select();
            _ctx = new AntdUI.Progress();
            _btnSend = new AntdUI.Button();
            _queueHost = new Panel();
            _queueHeader = new TableLayoutPanel();
            _lblQueue = new AntdUI.Label();
            _btnQueueSendAll = new AntdUI.Button();
            _queueList = new FlowLayoutPanel();
            _lblPath = new AntdUI.Label();

            SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_mainSplit).BeginInit();
            _mainSplit.Panel1.SuspendLayout();
            _mainSplit.Panel2.SuspendLayout();
            _mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_upperSplit).BeginInit();
            _upperSplit.Panel1.SuspendLayout();
            _upperSplit.Panel2.SuspendLayout();
            _upperSplit.SuspendLayout();

            // root
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Page;

            // session header
            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 2;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 10, 0);

            _lblTitle.Name = "lblTitle";
            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = "会话";
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.AutoEllipsis = true;
            _lblTitle.Margin = new Padding(0);

            _headerRight.Dock = DockStyle.Fill;
            _headerRight.FlowDirection = FlowDirection.RightToLeft;
            _headerRight.WrapContents = false;
            _headerRight.AutoSize = true;
            _headerRight.Margin = new Padding(0);

            _btnPanel.Name = "btnPanel";
            _btnPanel.Width = 36;
            _btnPanel.Height = 30;
            _btnPanel.Ghost = true;
            _btnPanel.Radius = 8;
            _btnPanel.IconSvg = UiIcons.Of(UiIcons.PanelClose, UiStyle.Text);
            _btnPanel.Margin = new Padding(0, 5, 0, 5);
            _btnPanel.Click += OnToggleSide;

            _header.Controls.Add(_lblTitle, 0, 0);
            _header.Controls.Add(_headerRight, 1, 0);
            _headerRight.Controls.Add(_btnPanel);

            _headerLine.Dock = DockStyle.Fill;
            _headerLine.BackColor = UiStyle.Border;
            _headerLine.Margin = new Padding(0);

            // main split (chat+side / input)
            _mainSplit.Dock = DockStyle.Fill;
            _mainSplit.Orientation = Orientation.Horizontal;
            _mainSplit.SplitterWidth = 8;
            _mainSplit.FixedPanel = FixedPanel.Panel2;
            _mainSplit.BackColor = UiStyle.Page;

            _upperSplit.Dock = DockStyle.Fill;
            _upperSplit.Orientation = Orientation.Vertical;
            _upperSplit.SplitterWidth = 8;
            _upperSplit.FixedPanel = FixedPanel.Panel2;
            _upperSplit.BackColor = UiStyle.Page;

            _chat.Dock = DockStyle.Fill;
            _chat.BackColor = UiStyle.Surface;
            _chat.Margin = new Padding(0);
            _chat.ChatFont = new Font("Microsoft YaHei UI", 9F);
            _upperSplit.Panel1.Controls.Add(_chat);

            _side.Dock = DockStyle.Fill;
            _side.ColumnCount = 1;
            _side.RowCount = 2;
            _side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _side.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _side.BackColor = UiStyle.Surface;
            _side.Padding = new Padding(14, 12, 10, 10);

            _sideTabs.Name = "sideTabs";
            _sideTabs.Dock = DockStyle.Fill;
            _sideTabs.Height = 32;
            _sideTabs.Margin = new Padding(0);
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "Loop" });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("TabDetail") });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "旁路" });
            _sideTabs.SelectIndex = 0;
            _sideTabs.SelectIndexChanged += OnSidePageChanged;

            _sideHost.Dock = DockStyle.Fill;
            _sideHost.Margin = new Padding(0, 8, 0, 0);

            _loopFlow.Dock = DockStyle.Fill;
            _loopFlow.FlowDirection = FlowDirection.TopDown;
            _loopFlow.WrapContents = false;
            _loopFlow.AutoScroll = true;

            _detailFlow.Dock = DockStyle.Fill;
            _detailFlow.FlowDirection = FlowDirection.TopDown;
            _detailFlow.WrapContents = false;
            _detailFlow.AutoScroll = true;
            _detailFlow.Visible = false;

            _sideBtw = new SideWorkView { Dock = DockStyle.Fill, Visible = false };

            _lblDef.AutoSize = true;
            _lblDef.MaximumSize = new Size(260, 0);
            _lblDef.Text = "def: —";
            _lblDef.Font = new Font("Segoe UI", 9F);
            _lblDef.ForeColor = UiStyle.Muted;
            _lblDef.Margin = new Padding(0, 0, 0, 8);
            _lblDef.Cursor = Cursors.Hand;
            _lblDef.Click += OnShowDefinitions;

            _lblTasks.AutoSize = true;
            _lblTasks.MaximumSize = new Size(260, 0);
            _lblTasks.Text = "考核题: —";
            _lblTasks.Font = new Font("Segoe UI", 9F);
            _lblTasks.ForeColor = UiStyle.Muted;
            _lblTasks.Margin = new Padding(0, 0, 0, 8);
            _lblTasks.Cursor = Cursors.Hand;
            _lblTasks.Click += OnShowTasks;

            _lblTrace.AutoSize = false;
            _lblTrace.Width = 236;
            _lblTrace.Height = 18;
            _lblTrace.TextMultiLine = true;
            _lblTrace.ShowTooltip = false;
            _lblTrace.Text = "trace: —";
            _lblTrace.Font = new Font("Segoe UI", 9F);
            _lblTrace.ForeColor = UiStyle.Muted;
            _lblTrace.Margin = new Padding(0, 0, 0, 8);
            _lblTrace.Cursor = Cursors.Hand;
            _lblTrace.Click += OnOpenTrace;

            _lblCost.AutoSize = true;
            _lblCost.Text = Localization.T("Cost") + ": —";
            _lblCost.Font = new Font("Segoe UI", 9F);
            _lblCost.ForeColor = UiStyle.Muted;
            _lblCost.Margin = new Padding(0, 0, 0, 8);

            _lblStatus.AutoSize = true;
            _lblStatus.MaximumSize = new Size(260, 0);
            _lblStatus.Text = Localization.T("Status") + ": —";
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = UiStyle.Text;
            _lblStatus.Margin = new Padding(0, 0, 0, 16);

            _btnDrafts.Name = "btnDrafts";
            _btnDrafts.Text = Localization.T("TaskDrafts");
            _btnDrafts.Radius = 8;
            _btnDrafts.Height = 34;
            _btnDrafts.Width = 236;
            _btnDrafts.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnDrafts.Margin = new Padding(0, 0, 0, 8);
            _btnDrafts.Click += OnOpenDrafts;

            _btnReport.Name = "btnReport";
            _btnReport.Text = Localization.T("EvalReports");
            _btnReport.Radius = 8;
            _btnReport.Height = 34;
            _btnReport.Width = 236;
            _btnReport.IconSvg = UiIcons.Of(UiIcons.List, UiStyle.Text);
            _btnReport.Margin = new Padding(0, 0, 0, 8);
            _btnReport.Click += OnOpenReports;

            _btnEvolve.Name = "btnEvolve";
            _btnEvolve.Text = Localization.T("Evolve");
            _btnEvolve.Radius = 8;
            _btnEvolve.Height = 34;
            _btnEvolve.Width = 236;
            _btnEvolve.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnEvolve.Margin = new Padding(0, 0, 0, 8);
            _btnEvolve.Click += OnEvolve;

            _btnRollback.Name = "btnRollback";
            _btnRollback.Text = Localization.T("Rollback");
            _btnRollback.Radius = 8;
            _btnRollback.Height = 34;
            _btnRollback.Width = 236;
            _btnRollback.IconSvg = UiIcons.Of(UiIcons.Undo, UiStyle.Text);
            _btnRollback.Margin = new Padding(0, 0, 0, 4);
            _btnRollback.Click += OnRollback;

            _btnRule.Name = "btnRule";
            _btnRule.Text = "新增规则";
            _btnRule.Radius = 8;
            _btnRule.Height = 34;
            _btnRule.Width = 236;
            _btnRule.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnRule.Margin = new Padding(0, 0, 0, 4);
            _btnRule.Click += OnAddRule;

            _btnOpenLoop.Name = "btnOpenLoop";
            _btnOpenLoop.Text = Localization.T("OpenLoopChat");
            _btnOpenLoop.Radius = 8;
            _btnOpenLoop.Height = 34;
            _btnOpenLoop.Width = 236;
            _btnOpenLoop.IconSvg = UiIcons.Dot(UiStyle.Loop);
            _btnOpenLoop.Margin = new Padding(0, 12, 0, 4);
            _btnOpenLoop.Click += OnOpenLoop;

            _lblDModel.AutoSize = true;
            _lblDModel.Text = Localization.T("Model") + ": —";
            _lblDModel.Font = new Font("Segoe UI", 9F);
            _lblDModel.ForeColor = UiStyle.Text;
            _lblDModel.MaximumSize = new Size(260, 0);
            _lblDModel.Margin = new Padding(0, 2, 0, 8);

            _lblDEffort.AutoSize = true;
            _lblDEffort.Text = Localization.T("Effort") + ": —";
            _lblDEffort.Font = new Font("Segoe UI", 9F);
            _lblDEffort.ForeColor = UiStyle.Muted;
            _lblDEffort.Margin = new Padding(0, 0, 0, 8);

            _lblDCtx.AutoSize = true;
            _lblDCtx.Text = Localization.T("Context") + ": —";
            _lblDCtx.Font = new Font("Segoe UI", 9F);
            _lblDCtx.ForeColor = UiStyle.Muted;
            _lblDCtx.Margin = new Padding(0, 0, 0, 8);

            _lblDCost.AutoSize = true;
            _lblDCost.Text = Localization.T("Cost") + ": —";
            _lblDCost.Font = new Font("Segoe UI", 9F);
            _lblDCost.ForeColor = UiStyle.Muted;
            _lblDCost.Margin = new Padding(0, 0, 0, 8);

            _lblDStatus.AutoSize = true;
            _lblDStatus.Text = Localization.T("Status") + ": —";
            _lblDStatus.Font = new Font("Segoe UI", 9F);
            _lblDStatus.ForeColor = UiStyle.Text;
            _lblDStatus.MaximumSize = new Size(260, 0);
            _lblDStatus.Margin = new Padding(0, 0, 0, 8);

            _btnPendingSurveys.Text = "待答 0";
            _btnPendingSurveys.Type = AntdUI.TTypeMini.Primary;
            _btnPendingSurveys.Ghost = true;
            _btnPendingSurveys.Radius = 8;
            _btnPendingSurveys.Width = 120;
            _btnPendingSurveys.Height = 30;
            _btnPendingSurveys.Margin = new Padding(0, 0, 0, 8);
            _btnPendingSurveys.Visible = false;
            _btnPendingSurveys.Click += OnOpenPendingSurveys;

            _loopFlow.Controls.Add(_btnPendingSurveys);
            _loopFlow.Controls.Add(_lblDef);
            _loopFlow.Controls.Add(_lblTasks);
            _loopFlow.Controls.Add(_lblTrace);
            _loopFlow.Controls.Add(_lblCost);
            _loopFlow.Controls.Add(_lblStatus);
            _loopFlow.Controls.Add(_btnDrafts);
            _loopFlow.Controls.Add(_btnReport);
            _loopFlow.Controls.Add(_btnEvolve);
            _loopFlow.Controls.Add(_btnRollback);
            _loopFlow.Controls.Add(_btnRule);
            _loopFlow.Controls.Add(_btnOpenLoop);

            _detailFlow.Controls.Add(_lblDModel);
            _detailFlow.Controls.Add(_lblDEffort);
            _detailFlow.Controls.Add(_lblDCtx);
            _detailFlow.Controls.Add(_lblDCost);
            _detailFlow.Controls.Add(_lblDStatus);

            _sideHost.Controls.Add(_loopFlow);
            _sideHost.Controls.Add(_detailFlow);
            _sideHost.Controls.Add(_sideBtw);
            _side.Controls.Add(_sideTabs, 0, 0);
            _side.Controls.Add(_sideHost, 0, 1);
            _upperSplit.Panel2.Controls.Add(_side);

            // input area
            _inputArea.Dock = DockStyle.Fill;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 3;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 4, 16, 2);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = Localization.T("ComposerHint");
            _txtInput.KeyDown += OnInputKeyDown;
            _txtInput.TextChanged += OnInputTextChanged;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 2;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 4, 0, 0);

            _toolbarLeft.Dock = DockStyle.Fill;
            _toolbarLeft.FlowDirection = FlowDirection.LeftToRight;
            _toolbarLeft.WrapContents = false;
            _toolbarLeft.Margin = new Padding(0);

            _modeControl.Name = "mode";
            _modeControl.Height = 30;
            _modeControl.Width = 300;
            _modeControl.Margin = new Padding(0, 0, 8, 0);
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("ModeTask") });
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("ModeCriterion") });
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("ModeCorrection") });
            _modeControl.SelectIndex = 0;
            _modeControl.SelectIndexChanged += OnModeChanged;

            _toolbarLeft.Controls.Add(_modeControl);

            _lblPath.Name = "lblPath";
            _lblPath.AutoSize = false;
            _lblPath.Width = 220;
            _lblPath.Height = 30;
            _lblPath.Text = "";
            _lblPath.Font = new Font("Segoe UI", 8.5F);
            _lblPath.ForeColor = UiStyle.Muted;
            _lblPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblPath.AutoEllipsis = true;
            _lblPath.IconGap = 6;
            _lblPath.Margin = new Padding(6, 0, 0, 0);
            _toolbarLeft.Controls.Add(_lblPath);

            _toolbarRight.Dock = DockStyle.Fill;
            _toolbarRight.FlowDirection = FlowDirection.RightToLeft;
            _toolbarRight.WrapContents = false;
            _toolbarRight.AutoSize = true;
            _toolbarRight.Margin = new Padding(0);

            _selProvider.Name = "selProvider";
            _selProvider.Width = 160;
            _selProvider.Height = 30;
            _selProvider.Radius = 8;
            _selProvider.PlaceholderText = Localization.T("Provider");
            _selProvider.Margin = new Padding(6, 0, 0, 0);
            _selProvider.SelectedValueChanged += OnProviderChanged;

            _selModel.Name = "selModel";
            _selModel.Width = 260;
            _selModel.Height = 30;
            _selModel.Radius = 8;
            _selModel.PlaceholderText = "模型";
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.Margin = new Padding(6, 0, 0, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _freeSwitch.Name = "freeSwitch";
            _freeSwitch.Width = 40;
            _freeSwitch.Height = 22;
            _freeSwitch.Checked = true;
            _freeSwitch.Margin = new Padding(10, 4, 0, 0);
            _freeSwitch.CheckedChanged += OnFreeToggle;

            _lblFree.Name = "lblFree";
            _lblFree.AutoSize = true;
            _lblFree.Text = Localization.T("FreeOnly");
            _lblFree.Font = new Font("Segoe UI", 9F);
            _lblFree.ForeColor = UiStyle.Muted;
            _lblFree.Margin = new Padding(0, 7, 4, 0);

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 30;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(6, 0, 0, 0);
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("EffortAuto")) { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("EffortLow")) { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("EffortMedium")) { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("EffortHigh")) { Tag = "high" });
            _selEffort.SelectedIndex = 0;
            _selEffort.SelectedValueChanged += OnEffortChanged;

            _ctx.Name = "ctx";
            _ctx.Width = 32;
            _ctx.Height = 32;
            _ctx.Shape = AntdUI.TShapeProgress.Circle;
            _ctx.TextUnit = "%";
            _ctx.Value = 0F;
            _ctx.Margin = new Padding(0, 5, 8, 5);
            _ctx.Cursor = Cursors.Hand;
            _ctx.Click += OnContextClick;
            _headerRight.Controls.Add(_ctx);

            _btnSend.Name = "btnSend";
            _btnSend.Text = Localization.T("Send");
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 96;
            _btnSend.Height = 30;
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(0);
            _btnSend.Click += OnSendClick;

            // pending request drawer：贴在聊天区下方、输入框上方，有排队请求时才出现
            _queueHost.Dock = DockStyle.Bottom;
            _queueHost.Height = 0;
            _queueHost.Visible = false;
            _queueHost.BackColor = UiStyle.SecondaryBack;
            _queueHost.Padding = new Padding(16, 6, 16, 6);

            _queueHeader.Dock = DockStyle.Top;
            _queueHeader.Height = 24;
            _queueHeader.ColumnCount = 2;
            _queueHeader.RowCount = 1;
            _queueHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _queueHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _queueHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _queueHeader.Margin = new Padding(0);

            _lblQueue.AutoSize = true;
            _lblQueue.Text = "待发送 0";
            _lblQueue.Font = new Font("Segoe UI", 9F);
            _lblQueue.ForeColor = UiStyle.Muted;
            _lblQueue.TextAlign = ContentAlignment.MiddleLeft;
            _lblQueue.Margin = new Padding(0);

            _btnQueueSendAll.Text = "全部发送";
            _btnQueueSendAll.Ghost = true;
            _btnQueueSendAll.Radius = 6;
            _btnQueueSendAll.Height = 24;
            _btnQueueSendAll.Width = 80;
            _btnQueueSendAll.Margin = new Padding(0);
            _btnQueueSendAll.Click += OnQueueSendAll;

            _queueHeader.Controls.Add(_lblQueue, 0, 0);
            _queueHeader.Controls.Add(_btnQueueSendAll, 1, 0);

            _queueList.Dock = DockStyle.Fill;
            _queueList.FlowDirection = FlowDirection.TopDown;
            _queueList.WrapContents = false;
            _queueList.AutoScroll = true;
            _queueList.Margin = new Padding(0);
            _queueList.SizeChanged += (_, _) => FitQueueRows();

            _queueHost.Controls.Add(_queueList);
            _queueHost.Controls.Add(_queueHeader);

            _toolbarRight.Controls.Add(_btnSend);
            _toolbarRight.Controls.Add(_selEffort);
            _toolbarRight.Controls.Add(_selModel);
            _toolbarRight.Controls.Add(_selProvider);
            _toolbarRight.Controls.Add(_freeSwitch);
            _toolbarRight.Controls.Add(_lblFree);

            _toolbar.Controls.Add(_toolbarLeft, 0, 0);
            _toolbar.Controls.Add(_toolbarRight, 1, 0);

            _inputArea.Controls.Add(_cmdPopup, 0, 0);
            _inputArea.Controls.Add(_txtInput, 0, 1);
            _inputArea.Controls.Add(_toolbar, 0, 2);
            _mainSplit.Panel2.Controls.Add(_inputArea);

            // 队列抽屉在 Panel1 内 Dock=Bottom；先加 Fill 再加 Bottom，Bottom 先占位
            _mainSplit.Panel1.Controls.Add(_upperSplit);
            _mainSplit.Panel1.Controls.Add(_queueHost);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_headerLine, 0, 1);
            _root.Controls.Add(_mainSplit, 0, 2);

            // “/” 命令联想下拉：占用输入框上方的一行，关闭时行高收成 0
            _cmdPopup.Name = "cmdPopup";
            _cmdPopup.Dock = DockStyle.Fill;
            _cmdPopup.Font = new Font("Microsoft YaHei UI", 9.5F);
            _cmdPopup.Accepted += OnCommandAccepted;

            // view
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            Load += OnViewLoad;

            _upperSplit.Panel1.ResumeLayout(false);
            _upperSplit.Panel2.ResumeLayout(false);
            _upperSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_upperSplit).EndInit();
            _mainSplit.Panel1.ResumeLayout(false);
            _mainSplit.Panel2.ResumeLayout(false);
            _mainSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_mainSplit).EndInit();
            ResumeLayout(false);
        }
    }
}
