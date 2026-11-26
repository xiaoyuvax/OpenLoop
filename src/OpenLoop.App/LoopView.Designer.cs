namespace OpenLoop.App
{
    partial class LoopView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private FlowLayoutPanel _headerRight;
        private AntdUI.Button _btnPanel;
        private AntdUI.Button _btnSummary;
        private Panel _line;
        private SplitContainer _upperSplit;
        private SplitContainer _targetSplit;
        private TargetPane _targetPane;
        private ChatFlow.ChatFlowControl _chat;
        private PendingQueueView _pending;
        private TableLayoutPanel _side;
        private AntdUI.Segmented _sideTabs;
        private Panel _sideHost;
        private FlowLayoutPanel _detailFlow;
        private HarnessView _harness;
        private SideWorkView _sideBtw;
        private BrowserView _browser;
        private FilePreviewView _fileView;
        private PuppetActionView _puppetView;
        private LoopAppView _loopAppView;
        private System.Windows.Forms.Label _lblDModel;
        private AntdUI.Label _lblDEffort;
        private AntdUI.Label _lblDCtx;
        private AntdUI.Label _lblDCost;
        private System.Windows.Forms.Label _lblDStatus;
        private AntdUI.Label _lblDPath;
        private AntdUI.Label _lblDOutside;
        private AntdUI.Switch _switchDOutside;
        private AntdUI.Label _lblDSensitive;
        private AntdUI.Switch _switchDSensitive;
        private FlowLayoutPanel _permBox;
        private TableLayoutPanel _permRowOutside;
        private TableLayoutPanel _permRowSensitive;
        private TableLayoutPanel _inputArea;
        private CommandPopup _cmdPopup;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selProvider;
        private AntdUI.Select _selEffort;
        private AntdUI.Dropdown _btnAttach;
        private AntdUI.Label _lblAttach;
        private AntdUI.Select _selImage;
        private AntdUI.Button _btnImage;
        private AntdUI.Progress _ctx;
        private AntdUI.Checkbox _chkSelect;
        private AntdUI.Dropdown _selMenu;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Button _btnSend;
        private AntdUI.Button _btnIterate;
        private AntdUI.Button _btnCommit;
        private AntdUI.Button _btnPush;
        private AntdUI.Button _btnRollback;
        private AntdUI.Button _btnOpenWorkspace;
        private AntdUI.Label _lblStatus;
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
            _header = new TableLayoutPanel();
            _lblTitle = new AntdUI.Label();
            _headerRight = new FlowLayoutPanel();
            _btnPanel = new AntdUI.Button();
            _btnSummary = new AntdUI.Button();
            _line = new Panel();
            _upperSplit = new SplitContainer();
            _targetSplit = new SplitContainer();
            _targetPane = new TargetPane();
            _chat = new ChatFlow.ChatFlowControl();
            _pending = new PendingQueueView();
            _side = new TableLayoutPanel();
            _sideTabs = new AntdUI.Segmented();
            _sideHost = new Panel();
            _detailFlow = new FlowLayoutPanel();
            _sideBtw = new SideWorkView();
            _fileView = new FilePreviewView();
            _lblDModel = new System.Windows.Forms.Label();
            _lblDEffort = new AntdUI.Label();
            _lblDCtx = new AntdUI.Label();
            _lblDCost = new AntdUI.Label();
            _lblDStatus = new System.Windows.Forms.Label();
            _lblDPath = new AntdUI.Label();
            _lblDOutside = new AntdUI.Label();
            _switchDOutside = new AntdUI.Switch();
            _lblDSensitive = new AntdUI.Label();
            _switchDSensitive = new AntdUI.Switch();
            _permBox = new FlowLayoutPanel();
            _permRowOutside = new TableLayoutPanel();
            _permRowSensitive = new TableLayoutPanel();
            _inputArea = new TableLayoutPanel();
            _cmdPopup = new CommandPopup();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _selModel = new AntdUI.Select();
            _selProvider = new AntdUI.Select();
            _selEffort = new AntdUI.Select();
            _btnAttach = new AntdUI.Dropdown();
            _lblAttach = new AntdUI.Label();
            _selImage = new AntdUI.Select();
            _btnImage = new AntdUI.Button();
            _ctx = new AntdUI.Progress();
            _chkSelect = new AntdUI.Checkbox();
            _selMenu = new AntdUI.Dropdown();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _btnSend = new AntdUI.Button();
            _btnIterate = new AntdUI.Button();
            _btnCommit = new AntdUI.Button();
            _btnPush = new AntdUI.Button();
            _btnRollback = new AntdUI.Button();
            _btnOpenWorkspace = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();

            SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_targetSplit).BeginInit();
            _targetSplit.Panel1.SuspendLayout();
            _targetSplit.Panel2.SuspendLayout();
            _targetSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_upperSplit).BeginInit();
            _upperSplit.Panel1.SuspendLayout();
            _upperSplit.Panel2.SuspendLayout();
            _upperSplit.SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Page;

            _header.Dock = DockStyle.Fill;
            // 与 Worker 的页头同构：按钮 · 标题（占满剩余宽度）· 右侧操作
            _header.ColumnCount = 3;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 10, 0);

            // 标题格式与 Worker 一致（同一字号/位置/省略号），只有颜色不同；Loop 身份靠前缀图标
            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Text = "Loop";
            _lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Loop;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.AutoEllipsis = true;
            _lblTitle.PrefixSvg = UiIcons.Of(UiIcons.Loop, UiStyle.Loop);
            _lblTitle.IconGap = 6;
            _lblTitle.Margin = new Padding(0);

            _headerRight.Dock = DockStyle.Fill;
            _headerRight.FlowDirection = FlowDirection.RightToLeft;
            _headerRight.WrapContents = false;
            _headerRight.AutoSize = true;
            _headerRight.Margin = new Padding(0);
            // FlowLayoutPanel 只按 Margin 贴顶：这里按行高把每个控件摆到中线（DPI 变化后重算）
            _headerRight.Layout += OnHeaderRightLayout;

            _btnPanel.Ghost = true;
            _btnPanel.Width = 36;
            _btnPanel.Height = 30;
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
            _headerRight.Controls.Add(_btnPanel);

            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            _upperSplit.Dock = DockStyle.Fill;
            _upperSplit.Orientation = Orientation.Vertical;
            _upperSplit.SplitterWidth = 8;
            _upperSplit.FixedPanel = FixedPanel.Panel2;
            _upperSplit.BackColor = UiStyle.Page;
            // 右侧栏默认收起：详情/旁路/文件按需打开
            _upperSplit.Panel2Collapsed = true;

            // 目标会话：左栏放可修订目标（系统提示词模板 / AGENTS.md），默认收起
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
            _chat.ChatFont = new Font("Microsoft YaHei UI", 9F);
            _upperSplit.Panel1.Controls.Add(_chat);
            _pending.Dock = DockStyle.Bottom;
            _upperSplit.Panel1.Controls.Add(_pending);

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

            _detailFlow.Dock = DockStyle.Fill;
            _detailFlow.FlowDirection = FlowDirection.TopDown;
            _detailFlow.WrapContents = false;
            _detailFlow.AutoScroll = true;
            _detailFlow.Layout += OnFlowLayout;

            _sideBtw = new SideWorkView { Dock = DockStyle.Fill, Visible = false };
            _harness = new HarnessView { Dock = DockStyle.Fill, Visible = false };
            _puppetView = new PuppetActionView { Dock = DockStyle.Fill, Visible = false };
            _loopAppView = new LoopAppView { Dock = DockStyle.Fill, Visible = false };
            _fileView.Dock = DockStyle.Fill;
            _fileView.Visible = false;

            // 多行说明：AutoSize 只按单行量高度，长模型名/状态会被裁成一行（F-20ed0f28）；
            // 高由 ApplyDetail 里的 FitWrapped 按换行重算
            // System.Windows.Forms.Label：没有 TextMultiLine，多行由 AutoSize=false + 显式高度决定
            _lblDModel.AutoSize = false;
            _lblDModel.Width = 260;
            _lblDModel.MaximumSize = new Size(260, 0);
            _lblDModel.Text = Localization.T("Model") + ": —";
            _lblDModel.Font = new Font("Segoe UI", 9F);
            _lblDModel.ForeColor = UiStyle.Text;
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

            // System.Windows.Forms.Label：没有 TextMultiLine，多行由 AutoSize=false + 显式高度决定
            _lblDStatus.AutoSize = false;
            _lblDStatus.Width = 260;
            _lblDStatus.MaximumSize = new Size(260, 0);
            _lblDStatus.Text = Localization.T("Status") + ": —";
            _lblDStatus.Font = new Font("Segoe UI", 9F);
            _lblDStatus.ForeColor = UiStyle.Text;
            _lblDStatus.Margin = new Padding(0, 0, 0, 8);

            _lblDPath.AutoSize = false;
            _lblDPath.Width = 260;
            _lblDPath.Height = 30;
            _lblDPath.Text = "";
            _lblDPath.Font = new Font("Segoe UI", 8.5F);
            _lblDPath.ForeColor = UiStyle.Muted;
            _lblDPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblDPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblDPath.AutoEllipsis = true;
            _lblDPath.IconGap = 6;
            _lblDPath.Margin = new Padding(0, 0, 0, 8);

            _switchDOutside.CheckedChanged += OnDetailPermChanged;
            _switchDSensitive.CheckedChanged += OnDetailPermChanged;

            PermissionRows.ConfigureLabel(_lblDOutside, "OutsideWrite");
            PermissionRows.ConfigureSwitch(_switchDOutside);
            PermissionRows.ConfigureLabel(_lblDSensitive, "Sensitive");
            PermissionRows.ConfigureSwitch(_switchDSensitive);
            PermissionRows.ConfigureBox(_permBox);
            PermissionRows.ConfigureRow(_permRowOutside);
            _permRowOutside.Controls.Add(_lblDOutside, 0, 0);
            _permRowOutside.Controls.Add(_switchDOutside, 1, 0);
            PermissionRows.ConfigureRow(_permRowSensitive);
            _permRowSensitive.Controls.Add(_lblDSensitive, 0, 0);
            _permRowSensitive.Controls.Add(_switchDSensitive, 1, 0);
            _permBox.Controls.Add(_permRowOutside);
            _permBox.Controls.Add(_permRowSensitive);

            _detailFlow.Controls.Add(_lblDModel);
            _detailFlow.Controls.Add(_lblDEffort);
            _detailFlow.Controls.Add(_lblDCtx);
            _detailFlow.Controls.Add(_lblDCost);
            _detailFlow.Controls.Add(_lblDStatus);
            _detailFlow.Controls.Add(_lblDPath);
            _detailFlow.Controls.Add(_btnIterate);
            _detailFlow.Controls.Add(_btnCommit);
            _detailFlow.Controls.Add(_btnPush);
            _detailFlow.Controls.Add(_btnRollback);
            _detailFlow.Controls.Add(_btnOpenWorkspace);
            // 审批豁免是全局设置、不是动作按钮：放在按钮组下方，单独一组
            _detailFlow.Controls.Add(_permBox);

            _sideHost.Controls.Add(_detailFlow);
            _sideHost.Controls.Add(_harness);
            _sideHost.Controls.Add(_sideBtw);
            _sideHost.Controls.Add(_fileView);
            _sideHost.Controls.Add(_puppetView);
            _sideHost.Controls.Add(_loopAppView);
            _side.Controls.Add(_sideTabs, 0, 0);
            _side.Controls.Add(_sideHost, 0, 1);
            _upperSplit.Panel2.Controls.Add(_side);

            _inputArea.Dock = DockStyle.Bottom;
            _inputArea.Height = 170;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 2;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 4, 16, 4);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.AcceptsTab = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = Localization.T("LoopHint");
            _txtInput.KeyDown += OnInputKeyDown;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 2;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 4, 0, 0);

            _toolbarLeft.Dock = DockStyle.Fill;
            _toolbarLeft.WrapContents = false;
            _toolbarLeft.Margin = new Padding(0);

            _selProvider.Name = "selProvider";
            _selProvider.Width = 150;
            _selProvider.Height = 30;
            _selProvider.Radius = 8;
            _selProvider.ListAutoWidth = true;
            _selProvider.PlaceholderText = Localization.T("Provider");
            _selProvider.Margin = new Padding(0, 0, 8, 0);
            _selProvider.SelectedValueChanged += OnProviderChanged;

            _selModel.Name = "selModel";
            _selModel.Width = 260;
            _selModel.Height = 30;
            _selModel.Radius = 8;
            _selModel.ListAutoWidth = true;
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.PlaceholderText = Localization.T("模型");
            _selModel.Margin = new Padding(0, 0, 8, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _selImage.Name = "selImage";
            _selImage.Width = 190;
            _selImage.Height = 30;
            _selImage.Radius = 8;
            _selImage.ListAutoWidth = true;
            _selImage.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selImage.PlaceholderText = Localization.T("生图模型");
            _selImage.Margin = new Padding(0, 0, 8, 0);
            _selImage.Visible = false;
            _selImage.SelectedValueChanged += OnImageModelChanged;

            _btnImage.Name = "btnImage";
            _btnImage.Width = 30;
            _btnImage.Height = 30;
            _btnImage.Ghost = true;
            _btnImage.Radius = 8;
            _btnImage.IconSvg = UiIcons.Of(UiIcons.Image, UiStyle.Muted);
            _btnImage.Margin = new Padding(0, 0, 8, 0);
            _btnImage.Visible = false;
            _btnImage.Click += OnToggleImagePicker;

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

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 30;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(0, 0, 8, 0);
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("自动")) { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("低")) { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("中")) { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem(Localization.T("高")) { Tag = "high" });
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

            _chkSelect.Name = "chkSelect";
            _chkSelect.Text = Localization.T("选择");
            // 与同排按钮统一 30 高：AutoSize 时勾选框更高，顶对齐会看起来下沉
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
            _selMenu.Items.Add(new AntdUI.SelectItem(Localization.T("转给 worker")) { Tag = "worker" });
            _selMenu.Items.Add(new AntdUI.SelectItem(Localization.T("删除")) { Tag = "delete" });

            // RightToLeft：先加的靠右，所以期望顺序是 [选择] [操作▾] 时倒着加
            _headerRight.Controls.Add(_selMenu);
            _headerRight.Controls.Add(_chkSelect);

            _freeSwitch.Name = "freeSwitch";
            _freeSwitch.Width = 40;
            _freeSwitch.Height = 22;
            _freeSwitch.Checked = true;
            _freeSwitch.Margin = new Padding(0, 4, 0, 0);
            _freeSwitch.CheckedChanged += OnFreeToggle;

            _lblFree.Name = "lblFree";
            _lblFree.AutoSize = true;
            _lblFree.Text = Localization.T("FreeOnly");
            _lblFree.Font = new Font("Segoe UI", 9F);
            _lblFree.ForeColor = UiStyle.Muted;
            _lblFree.Margin = new Padding(4, 7, 8, 0);

            _btnIterate.Name = "btnIterate";
            _btnIterate.Text = Localization.T("迭代");
            _btnIterate.Radius = 8;
            _btnIterate.Height = 34;
            _btnIterate.Width = 236;
            _btnIterate.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnIterate.Margin = new Padding(0, 0, 0, 8);
            _btnIterate.Click += OnIterateClick;

            // 仓库动作：没有改动/没有上一版本时灰着，同步见 SyncRepoButtons
            _btnCommit.Name = "btnCommit";
            _btnCommit.Text = Localization.T("提交");
            _btnCommit.Radius = 8;
            _btnCommit.Height = 34;
            _btnCommit.Width = 236;
            _btnCommit.IconSvg = UiIcons.Of(UiIcons.Go, UiStyle.Text);
            _btnCommit.Enabled = false;
            _btnCommit.Margin = new Padding(0, 0, 0, 8);
            _btnCommit.Click += OnCommitClick;

            _btnPush.Name = "btnPush";
            _btnPush.Text = Localization.T("提交并推送");
            _btnPush.Radius = 8;
            _btnPush.Height = 34;
            _btnPush.Width = 236;
            _btnPush.IconSvg = UiIcons.Of(UiIcons.Send, UiStyle.Text);
            _btnPush.Enabled = false;
            _btnPush.Margin = new Padding(0, 0, 0, 8);
            _btnPush.Click += OnPushClick;

            _btnRollback.Name = "btnRollback";
            _btnRollback.Text = Localization.T("退回上一个版本");
            _btnRollback.Radius = 8;
            _btnRollback.Height = 34;
            _btnRollback.Width = 236;
            _btnRollback.IconSvg = UiIcons.Of(UiIcons.Undo, UiStyle.Text);
            _btnRollback.Enabled = false;
            _btnRollback.Margin = new Padding(0, 0, 0, 8);
            _btnRollback.Click += OnRollbackClick;

            _btnOpenWorkspace.Name = "btnOpenWorkspace";
            _btnOpenWorkspace.Text = Localization.T("OpenWorkspace");
            _btnOpenWorkspace.Radius = 8;
            _btnOpenWorkspace.Height = 34;
            _btnOpenWorkspace.Width = 236;
            _btnOpenWorkspace.IconSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Text);
            _btnOpenWorkspace.Margin = new Padding(0, 4, 0, 8);
            _btnOpenWorkspace.Click += OnOpenWorkspace;

            _lblStatus.AutoSize = true;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Margin = new Padding(0, 9, 0, 0);

            _toolbarLeft.Controls.Add(_selProvider);
            _toolbarLeft.Controls.Add(_selModel);
            _toolbarLeft.Controls.Add(_btnImage);
            _toolbarLeft.Controls.Add(_selImage);
            _toolbarLeft.Controls.Add(_freeSwitch);
            _toolbarLeft.Controls.Add(_lblFree);
            _toolbarLeft.Controls.Add(_selEffort);
            _toolbarLeft.Controls.Add(_btnAttach);
            _toolbarLeft.Controls.Add(_lblAttach);
            // 状态标签已从底栏移除（控件保留：LoopView.Status 与右侧详情仍读它的文本）

            _btnSend.Name = "btnSend";
            _btnSend.Text = Localization.T("Send");
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 96;
            _btnSend.Height = 30;
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(0);
            _btnSend.Click += OnSendClick;

            _toolbar.Controls.Add(_toolbarLeft, 0, 0);
            _toolbar.Controls.Add(_btnSend, 1, 0);
            _toolbar.SetColumnSpan(_btnSend, 1);

            _inputArea.Controls.Add(_txtInput, 0, 0);
            _inputArea.Controls.Add(_toolbar, 0, 1);
            // 左栏：聊天流 + 待发送队列 + 输入区（后加的先贴底，输入区在最下面；联想列表是浮层，最后加）
            _upperSplit.Panel1.Controls.Add(_inputArea);
            _upperSplit.Panel1.Controls.Add(_cmdPopup);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_line, 0, 1);
            _root.Controls.Add(_targetSplit, 0, 2);

            // “/” 命令联想：浮在输入区上方，不占布局（旧做法占一行，命令多了会把输入框压成一条线）
            _cmdPopup.Name = "cmdPopup";
            _cmdPopup.Dock = DockStyle.None;
            _cmdPopup.Font = new Font("Microsoft YaHei UI", 9.5F);
            _cmdPopup.Accepted += OnCommandAccepted;

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            _targetSplit.Panel1.ResumeLayout(false);
            _targetSplit.Panel2.ResumeLayout(false);
            _targetSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(_targetSplit)).EndInit();
            _upperSplit.Panel1.ResumeLayout(false);
            _upperSplit.Panel2.ResumeLayout(false);
            _upperSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(_upperSplit)).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
