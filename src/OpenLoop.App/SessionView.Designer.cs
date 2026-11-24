namespace OpenLoop.App
{
    partial class SessionView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private FlowLayoutPanel _headerRight;
        private AntdUI.Checkbox _chkSelect;
        private AntdUI.Dropdown _selMenu;
        private AntdUI.Button _btnPanel;
        private AntdUI.Button _btnSummary;
        private Panel _headerLine;
        private SplitContainer _upperSplit;
        private SplitContainer _targetSplit;
        private TargetPane _targetPane;
        private ChatFlow.ChatFlowControl _chat;
        private TableLayoutPanel _side;
        private AntdUI.Segmented _sideTabs;
        private AntdUI.Button _btnPendingSurveys;
        private Panel _sideHost;
        private FlowLayoutPanel _loopFlow;
        private FlowLayoutPanel _detailFlow;
        private SideWorkView _sideBtw;
        private FilePreviewView _fileView;
        private PuppetActionView _puppetView;
        private LoopAppView _loopAppView;
        private BrowserView _browser;
        private AntdUI.Button _btnOpenWorkspace;
        private System.Windows.Forms.Label _lblDModel;
        private AntdUI.Label _lblDEffort;
        private AntdUI.Label _lblDCtx;
        private AntdUI.Label _lblDCost;
        private System.Windows.Forms.Label _lblDStatus;
        private AntdUI.Label _lblDef;
        private AntdUI.Label _lblTasks;
        private AntdUI.Label _lblTrace;
        private AntdUI.Label _lblCost;
        private AntdUI.Label _lblStatus;
        private AntdUI.Label _lblEvolveHint;
        private AntdUI.Button _btnDrafts;
        private AntdUI.Button _btnReport;
        private AntdUI.Button _btnEvolve;
        private AntdUI.Button _btnRollback;
        private AntdUI.Button _btnRule;
        private AntdUI.Button _btnPrompt;
        private AntdUI.Button _btnAgents;
        private AntdUI.Button _btnMode;
        private AntdUI.Label _lblCandidate;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarRight;
        private AntdUI.Dropdown _btnAttach;
        private AntdUI.Label _lblAttach;
        private AntdUI.Select _selImage;
        private AntdUI.Button _btnImage;
        private AntdUI.Select _selProvider;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selEffort;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Progress _ctx;
        private AntdUI.Button _btnSend;
        private PendingQueueView _pending;
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
            _chkSelect = new AntdUI.Checkbox();
            _selMenu = new AntdUI.Dropdown();
            _btnPanel = new AntdUI.Button();
            _btnSummary = new AntdUI.Button();
            _headerLine = new Panel();
            _upperSplit = new SplitContainer();
            _targetSplit = new SplitContainer();
            _targetPane = new TargetPane();
            _chat = new ChatFlow.ChatFlowControl();
            _side = new TableLayoutPanel();
            _sideTabs = new AntdUI.Segmented();
            _sideHost = new Panel();
            _loopFlow = new FlowLayoutPanel();
            _btnPendingSurveys = new AntdUI.Button();
            _detailFlow = new FlowLayoutPanel();
            _btnOpenWorkspace = new AntdUI.Button();
            _lblDModel = new System.Windows.Forms.Label();
            _lblDEffort = new AntdUI.Label();
            _lblDCtx = new AntdUI.Label();
            _lblDCost = new AntdUI.Label();
            _lblDStatus = new System.Windows.Forms.Label();
            _lblDef = new AntdUI.Label();
            _lblTasks = new AntdUI.Label();
            _lblTrace = new AntdUI.Label();
            _lblCost = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _lblEvolveHint = new AntdUI.Label();
            _btnDrafts = new AntdUI.Button();
            _btnReport = new AntdUI.Button();
            _btnEvolve = new AntdUI.Button();
            _btnRollback = new AntdUI.Button();
            _btnRule = new AntdUI.Button();
            _btnPrompt = new AntdUI.Button();
            _btnAgents = new AntdUI.Button();
            _btnMode = new AntdUI.Button();
            _lblCandidate = new AntdUI.Label();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarRight = new FlowLayoutPanel();
            _selProvider = new AntdUI.Select();
            _selModel = new AntdUI.Select();
            _btnAttach = new AntdUI.Dropdown();
            _lblAttach = new AntdUI.Label();
            _selImage = new AntdUI.Select();
            _btnImage = new AntdUI.Button();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _selEffort = new AntdUI.Select();
            _ctx = new AntdUI.Progress();
            _btnSend = new AntdUI.Button();
            _pending = new PendingQueueView();
            _lblPath = new AntdUI.Label();

            SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_upperSplit).BeginInit();
            _upperSplit.Panel1.SuspendLayout();
            _upperSplit.Panel2.SuspendLayout();
            _upperSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_targetSplit).BeginInit();
            _targetSplit.Panel1.SuspendLayout();
            _targetSplit.Panel2.SuspendLayout();
            _targetSplit.SuspendLayout();

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
            _header.ColumnCount = 3;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 10, 0);

            _lblTitle.Name = "lblTitle";
            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = Localization.T("会话");
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

            _chkSelect.Name = "chkSelect";
            _chkSelect.Text = Localization.T("选择");
            // AutoSize 会让勾选框比同排按钮高一截（250% DPI 下实测 78 vs 69），顶对齐就变成“下沉”；
            // 固定成和按钮一样的 30，整排才真正垂直居中。
            _chkSelect.AutoSize = false;
            _chkSelect.Width = 56;
            _chkSelect.Height = 30;
            _chkSelect.ForeColor = UiStyle.Muted;
            _chkSelect.Margin = new Padding(0, 5, 10, 5);
            _chkSelect.CheckedChanged += OnSelectModeChanged;

            _selMenu.Name = "selMenu";
            _selMenu.Text = Localization.T("操作");
            _selMenu.Width = 84;
            _selMenu.Height = 30;
            _selMenu.Ghost = true;
            _selMenu.Radius = 8;
            _selMenu.Margin = new Padding(0, 5, 8, 5);
            _selMenu.SelectedValueChanged += OnSelectAction;
            _selMenu.Items.Add(new AntdUI.SelectItem(Localization.T("复制")) { Tag = "copy" });
            _selMenu.Items.Add(new AntdUI.SelectItem(Localization.T("转给 Loop")) { Tag = "loop" });
            _selMenu.Items.Add(new AntdUI.SelectItem(Localization.T("删除")) { Tag = "delete" });

            _btnPanel.Name = "btnPanel";
            _btnPanel.Width = 36;
            _btnPanel.Height = 30;
            _btnPanel.Ghost = true;
            _btnPanel.Radius = 8;
            _btnPanel.IconSvg = UiIcons.Of(UiIcons.PanelOpen, UiStyle.Text);
            _btnPanel.Margin = new Padding(0, 5, 0, 5);
            _btnPanel.Click += OnToggleSide;

            _btnSummary.Name = "btnSummary";
            _btnSummary.Width = 30;
            _btnSummary.Height = 30;
            _btnSummary.Ghost = true;
            _btnSummary.Radius = 8;
            _btnSummary.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Muted);
            _btnSummary.Margin = new Padding(0, 5, 6, 5);
            _btnSummary.Click += OnSummarize;

            _header.Controls.Add(_btnSummary, 0, 0);
            _header.Controls.Add(_lblTitle, 1, 0);
            _header.Controls.Add(_headerRight, 2, 0);

            _headerLine.Dock = DockStyle.Fill;
            _headerLine.BackColor = UiStyle.Border;
            _headerLine.Margin = new Padding(0);

            // 一个纵向拆分：左栏 = 聊天流 + 队列 + 输入区，右栏 = 详情/旁路/Loop/文件
            _upperSplit.Dock = DockStyle.Fill;
            _upperSplit.Orientation = Orientation.Vertical;
            _upperSplit.SplitterWidth = 8;
            _upperSplit.FixedPanel = FixedPanel.Panel2;
            _upperSplit.BackColor = UiStyle.Page;
            // 右侧栏默认收起
            _upperSplit.Panel2Collapsed = true;

            // 目标中心会话：左侧目标文本（只读渲染、可独立成窗），右侧仍是聊天与侧栏
            _targetSplit.Dock = DockStyle.Fill;
            _targetSplit.Orientation = Orientation.Vertical;
            _targetSplit.SplitterWidth = 8;
            _targetSplit.FixedPanel = FixedPanel.Panel1;
            _targetSplit.BackColor = UiStyle.Page;
            _targetSplit.Panel1Collapsed = true;
            _targetSplit.Panel1MinSize = 240;
            _targetPane.Dock = DockStyle.Fill;
            _targetSplit.Panel1.Controls.Add(_targetPane);
            _targetSplit.Panel2.Controls.Add(_upperSplit);

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
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("TabDetail") });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = Localization.T("TabSideWork") });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "Harness" });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "Puppet" });
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "LoopApp" });
            // 「网页」页不预置：只有 Agent 打开浏览器时才插入，用户点不出一个空 WebView
            _sideTabs.SelectIndex = 0;
            _sideTabs.SelectIndexChanged += OnSidePageChanged;
            _sideTabs.MouseUp += OnSideTabsMouseUp;

            _sideHost.Dock = DockStyle.Fill;
            _sideHost.Margin = new Padding(0, 8, 0, 0);

            _loopFlow.Dock = DockStyle.Fill;
            _loopFlow.FlowDirection = FlowDirection.TopDown;
            _loopFlow.WrapContents = false;
            _loopFlow.AutoScroll = true;
            _loopFlow.Layout += OnFlowLayout;

            _detailFlow.Dock = DockStyle.Fill;
            _detailFlow.FlowDirection = FlowDirection.TopDown;
            _detailFlow.WrapContents = false;
            _detailFlow.AutoScroll = true;
            _detailFlow.Layout += OnFlowLayout;
            _detailFlow.Visible = false;

            _sideBtw = new SideWorkView { Dock = DockStyle.Fill, Visible = false };
            _fileView = new FilePreviewView { Dock = DockStyle.Fill, Visible = false };
            _puppetView = new PuppetActionView { Dock = DockStyle.Fill, Visible = false };
            _loopAppView = new LoopAppView { Dock = DockStyle.Fill, Visible = false };

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
            _lblTasks.Text = Localization.T("考核题: —");
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

            // 多行说明：AntdUI.Label 的 AutoSize 只按单行量高度，长句会被裁成一行；
            // 宽高改由 FitLoopLabels 按换行重算（与 trace 标签同一套做法）
            _lblEvolveHint.AutoSize = false;
            _lblEvolveHint.TextMultiLine = true;
            _lblEvolveHint.Width = 236;
            _lblEvolveHint.Height = 60;
            _lblEvolveHint.Text = Localization.T("Harness 只改当前工作区的可演进对象（Prompt / Skill / Rule / Limit / Guard）；")
                + Localization.T("系统提示词与 AGENTS.md 经目标会话由模型落盘；考核题 A/B 更好才采纳。");
            _lblEvolveHint.Font = new Font("Segoe UI", 8F);
            _lblEvolveHint.ForeColor = UiStyle.Muted;
            // 放在按钮组下方：与上方按钮留一段距离，别贴着
            _lblEvolveHint.Margin = new Padding(0, 12, 0, 0);

            _btnMode.Name = "btnMode";
            _btnMode.Text = Localization.T("HarnessModeDirect");
            _btnMode.Radius = 8;
            _btnMode.Height = 34;
            _btnMode.Width = 236;
            _btnMode.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnMode.Margin = new Padding(0, 0, 0, 8);
            _btnMode.Click += OnToggleMode;

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
            _btnRule.Text = Localization.T("新增规则");
            _btnRule.Radius = 8;
            _btnRule.Height = 34;
            _btnRule.Width = 236;
            _btnRule.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnRule.Margin = new Padding(0, 0, 0, 4);
            _btnRule.Click += OnAddRule;

            _btnPrompt.Name = "btnPrompt";
            _btnPrompt.Text = Localization.T("HarnessPromptObject");
            _btnPrompt.Radius = 8;
            _btnPrompt.Height = 34;
            _btnPrompt.Width = 236;
            _btnPrompt.IconSvg = UiIcons.Of(UiIcons.Edit, UiStyle.Text);
            _btnPrompt.Margin = new Padding(0, 0, 0, 8);
            _btnPrompt.Click += OnEditPrompt;

            _btnAgents.Name = "btnAgents";
            _btnAgents.Text = Localization.T("HarnessAgentsObject");
            _btnAgents.Radius = 8;
            _btnAgents.Height = 34;
            _btnAgents.Width = 236;
            _btnAgents.IconSvg = UiIcons.Of(UiIcons.Edit, UiStyle.Text);
            _btnAgents.Margin = new Padding(0, 0, 0, 4);
            _btnAgents.Click += OnEditAgents;

            _lblCandidate.AutoSize = false;
            _lblCandidate.TextMultiLine = true;
            _lblCandidate.Width = 236;
            _lblCandidate.Height = 20;
            _lblCandidate.Text = Localization.T("HarnessCandidateNone");
            _lblCandidate.Font = new Font("Segoe UI", 8F);
            _lblCandidate.ForeColor = UiStyle.Muted;
            _lblCandidate.Margin = new Padding(0, 2, 0, 6);

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
            _lblDCtx.Cursor = Cursors.Hand;
            _lblDCtx.Click += OnEditContext;

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

            _btnPendingSurveys.Text = Localization.T("待答 0");
            _btnPendingSurveys.Type = AntdUI.TTypeMini.Primary;
            _btnPendingSurveys.Ghost = true;
            _btnPendingSurveys.Radius = 8;
            _btnPendingSurveys.Width = 120;
            _btnPendingSurveys.Height = 30;
            _btnPendingSurveys.Margin = new Padding(0, 0, 0, 8);
            _btnPendingSurveys.Visible = false;
            _btnPendingSurveys.Click += OnOpenPendingSurveys;

            _btnOpenWorkspace.Name = "btnOpenWorkspace";
            _btnOpenWorkspace.Text = Localization.T("OpenWorkspace");
            _btnOpenWorkspace.Radius = 8;
            _btnOpenWorkspace.Height = 34;
            _btnOpenWorkspace.Width = 236;
            _btnOpenWorkspace.IconSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Text);
            _btnOpenWorkspace.Margin = new Padding(0, 4, 0, 8);
            _btnOpenWorkspace.Click += OnOpenWorkspace;

            _loopFlow.Controls.Add(_btnPendingSurveys);
            _loopFlow.Controls.Add(_lblDef);
            _loopFlow.Controls.Add(_lblTasks);
            _loopFlow.Controls.Add(_lblTrace);
            _loopFlow.Controls.Add(_lblCost);
            _loopFlow.Controls.Add(_lblStatus);
            _loopFlow.Controls.Add(_btnMode);
            _loopFlow.Controls.Add(_btnDrafts);
            _loopFlow.Controls.Add(_btnReport);
            _loopFlow.Controls.Add(_btnEvolve);
            _loopFlow.Controls.Add(_btnRollback);
            _loopFlow.Controls.Add(_btnRule);
            _loopFlow.Controls.Add(_btnPrompt);
            _loopFlow.Controls.Add(_btnAgents);
            _loopFlow.Controls.Add(_lblCandidate);
            // 说明文字放最后：所有按钮之下，占住底部空白区
            _loopFlow.Controls.Add(_lblEvolveHint);

            _detailFlow.Controls.Add(_lblDModel);
            _detailFlow.Controls.Add(_lblDEffort);
            _detailFlow.Controls.Add(_lblDCtx);
            _detailFlow.Controls.Add(_lblDCost);
            _detailFlow.Controls.Add(_lblDStatus);
            _detailFlow.Controls.Add(_btnOpenWorkspace);

            _sideHost.Controls.Add(_loopFlow);
            _sideHost.Controls.Add(_detailFlow);
            _sideHost.Controls.Add(_sideBtw);
            _sideHost.Controls.Add(_fileView);
            _sideHost.Controls.Add(_puppetView);
            _sideHost.Controls.Add(_loopAppView);
            _side.Controls.Add(_sideTabs, 0, 0);
            _side.Controls.Add(_sideHost, 0, 1);
            _upperSplit.Panel2.Controls.Add(_side);

            // input area
            _inputArea.Dock = DockStyle.Bottom;
            _inputArea.Height = 196;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 2;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 4, 16, 2);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.AcceptsTab = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = Localization.T("ComposerHint");
            _txtInput.KeyDown += OnInputKeyDown;
            _txtInput.TextChanged += OnInputTextChanged;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 3;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 4, 0, 0);

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
            _lblPath.Anchor = AnchorStyles.Left;
            _lblPath.Margin = new Padding(0, 0, 6, 0);
            _lblPath.Cursor = Cursors.Hand;
            _lblPath.Click += OnOpenWorkspace;

            _btnAttach.Name = "btnAttach";
            _btnAttach.Width = 30;
            _btnAttach.Height = 30;
            _btnAttach.Ghost = true;
            _btnAttach.Radius = 8;
            _btnAttach.IconSvg = UiIcons.Of(UiIcons.Attach, UiStyle.Muted);
            _btnAttach.Margin = new Padding(4, 0, 4, 0);
            _btnAttach.MaxCount = 20;
            _btnAttach.SelectedValueChanged += OnAttachMenu;

            _lblAttach.Name = "lblAttach";
            _lblAttach.AutoSize = false;
            _lblAttach.Width = 56;
            _lblAttach.Height = 30;
            _lblAttach.Text = "";
            _lblAttach.Font = new Font("Segoe UI", 8.5F);
            _lblAttach.ForeColor = UiStyle.Muted;
            _lblAttach.TextAlign = ContentAlignment.MiddleLeft;
            _lblAttach.AutoEllipsis = true;
            _lblAttach.Visible = false;
            _lblAttach.Margin = new Padding(0);

            _toolbarRight.Dock = DockStyle.Fill;
            _toolbarRight.FlowDirection = FlowDirection.RightToLeft;
            _toolbarRight.WrapContents = false;
            _toolbarRight.Margin = new Padding(0);

            _selProvider.Name = "selProvider";
            _selProvider.Width = 160;
            _selProvider.Height = 30;
            _selProvider.Radius = 8;
            _selProvider.ListAutoWidth = true;
            _selProvider.PlaceholderText = Localization.T("Provider");
            _selProvider.Margin = new Padding(6, 0, 0, 0);
            _selProvider.SelectedValueChanged += OnProviderChanged;

            _selModel.Name = "selModel";
            _selModel.Width = 260;
            _selModel.Height = 30;
            _selModel.Radius = 8;
            _selModel.ListAutoWidth = true;
            _selModel.PlaceholderText = Localization.T("模型");
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.Margin = new Padding(6, 0, 0, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _selImage.Name = "selImage";
            _selImage.Width = 200;
            _selImage.Height = 30;
            _selImage.Radius = 8;
            _selImage.ListAutoWidth = true;
            _selImage.PlaceholderText = Localization.T("生图模型");
            _selImage.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selImage.Margin = new Padding(6, 0, 0, 0);
            _selImage.Visible = false;
            _selImage.SelectedValueChanged += OnImageModelChanged;

            _btnImage.Name = "btnImage";
            _btnImage.Width = 30;
            _btnImage.Height = 30;
            _btnImage.Ghost = true;
            _btnImage.Radius = 8;
            _btnImage.IconSvg = UiIcons.Of(UiIcons.Image, UiStyle.Muted);
            _btnImage.Margin = new Padding(6, 0, 0, 0);
            _btnImage.Visible = false;
            _btnImage.Click += OnToggleImagePicker;

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
            _ctx.Width = 30;
            _ctx.Height = 30;
            _ctx.Shape = AntdUI.TShapeProgress.Circle;
            _ctx.TextUnit = "%";
            _ctx.Value = 0F;
            _ctx.Margin = new Padding(0, 5, 8, 5);
            _ctx.Cursor = Cursors.Hand;
            _ctx.Click += OnContextClick;

            // RightToLeft：先加的靠右。可见顺序（左→右）与 Loop 页一致：选择 | 操作 | 上下文 | 侧栏
            _headerRight.Controls.Add(_btnPanel);
            _headerRight.Controls.Add(_ctx);
            _headerRight.Controls.Add(_selMenu);
            _headerRight.Controls.Add(_chkSelect);

            _btnSend.Name = "btnSend";
            _btnSend.Text = Localization.T("Send");
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 96;
            _btnSend.Height = 30;
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(0);
            _btnSend.Click += OnSendClick;

            // pending drawer：贴在聊天区下方、输入框上方，有排队文本时才出现
            _pending.Dock = DockStyle.Bottom;

            // RightToLeft：先加的靠右。可见顺序（左→右）与 Loop 页一致，整条工具条贴右
            _toolbarRight.Controls.Add(_lblAttach);
            _toolbarRight.Controls.Add(_btnAttach);
            _toolbarRight.Controls.Add(_selEffort);
            _toolbarRight.Controls.Add(_lblFree);
            _toolbarRight.Controls.Add(_freeSwitch);
            _toolbarRight.Controls.Add(_selImage);
            _toolbarRight.Controls.Add(_btnImage);
            _toolbarRight.Controls.Add(_selModel);
            _toolbarRight.Controls.Add(_selProvider);

            _toolbar.Controls.Add(_lblPath, 0, 0);
            _toolbar.Controls.Add(_toolbarRight, 1, 0);
            _toolbar.Controls.Add(_btnSend, 2, 0);

            _inputArea.Controls.Add(_txtInput, 0, 0);
            _inputArea.Controls.Add(_toolbar, 0, 1);

            // 左栏：聊天流 + 队列抽屉 + 输入区（后加的先贴底，输入区在最下面；联想列表是浮层，最后加）
            _upperSplit.Panel1.Controls.Add(_pending);
            _upperSplit.Panel1.Controls.Add(_inputArea);
            _upperSplit.Panel1.Controls.Add(_cmdPopup);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_headerLine, 0, 1);
            _root.Controls.Add(_targetSplit, 0, 2);

            // “/” 命令联想：浮在输入区上方，不占布局（旧做法占一行，命令多了会把输入框压成一条线）
            _cmdPopup.Name = "cmdPopup";
            _cmdPopup.Dock = DockStyle.None;
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
            _targetSplit.Panel1.ResumeLayout(false);
            _targetSplit.Panel2.ResumeLayout(false);
            _targetSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_targetSplit).EndInit();
            ResumeLayout(false);
        }
    }
}
