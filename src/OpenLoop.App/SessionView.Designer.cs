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
        private Panel _sideHost;
        private FlowLayoutPanel _loopFlow;
        private FlowLayoutPanel _detailFlow;
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
        private AntdUI.Button _btnEvolve;
        private AntdUI.Button _btnRollback;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Segmented _modeControl;
        private AntdUI.Button _btnStop;
        private FlowLayoutPanel _toolbarRight;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selEffort;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Progress _ctx;
        private AntdUI.Button _btnSend;
        private AntdUI.Label _lblPath;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

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
            _btnEvolve = new AntdUI.Button();
            _btnRollback = new AntdUI.Button();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _modeControl = new AntdUI.Segmented();
            _btnStop = new AntdUI.Button();
            _toolbarRight = new FlowLayoutPanel();
            _selModel = new AntdUI.Select();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _selEffort = new AntdUI.Select();
            _ctx = new AntdUI.Progress();
            _btnSend = new AntdUI.Button();
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
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "详情" });
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

            _lblDef.AutoSize = true;
            _lblDef.MaximumSize = new Size(260, 0);
            _lblDef.Text = "def: —";
            _lblDef.Font = new Font("Segoe UI", 9F);
            _lblDef.ForeColor = UiStyle.Muted;
            _lblDef.Margin = new Padding(0, 0, 0, 8);

            _lblTasks.AutoSize = true;
            _lblTasks.MaximumSize = new Size(260, 0);
            _lblTasks.Text = "考核题: —";
            _lblTasks.Font = new Font("Segoe UI", 9F);
            _lblTasks.ForeColor = UiStyle.Muted;
            _lblTasks.Margin = new Padding(0, 0, 0, 8);

            _lblTrace.AutoSize = true;
            _lblTrace.MaximumSize = new Size(260, 0);
            _lblTrace.Text = "trace: —";
            _lblTrace.Font = new Font("Segoe UI", 9F);
            _lblTrace.ForeColor = UiStyle.Muted;
            _lblTrace.Margin = new Padding(0, 0, 0, 8);

            _lblCost.AutoSize = true;
            _lblCost.Text = "成本: —";
            _lblCost.Font = new Font("Segoe UI", 9F);
            _lblCost.ForeColor = UiStyle.Muted;
            _lblCost.Margin = new Padding(0, 0, 0, 8);

            _lblStatus.AutoSize = true;
            _lblStatus.MaximumSize = new Size(260, 0);
            _lblStatus.Text = "状态: 就绪";
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.ForeColor = UiStyle.Text;
            _lblStatus.Margin = new Padding(0, 0, 0, 16);

            _btnEvolve.Name = "btnEvolve";
            _btnEvolve.Text = "演进一轮";
            _btnEvolve.Radius = 8;
            _btnEvolve.Height = 34;
            _btnEvolve.Width = 236;
            _btnEvolve.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnEvolve.Margin = new Padding(0, 0, 0, 8);
            _btnEvolve.Click += OnEvolve;

            _btnRollback.Name = "btnRollback";
            _btnRollback.Text = "回滚上一版";
            _btnRollback.Radius = 8;
            _btnRollback.Height = 34;
            _btnRollback.Width = 236;
            _btnRollback.IconSvg = UiIcons.Of(UiIcons.Undo, UiStyle.Text);
            _btnRollback.Margin = new Padding(0, 0, 0, 4);
            _btnRollback.Click += OnRollback;

            _btnOpenLoop.Name = "btnOpenLoop";
            _btnOpenLoop.Text = "打开 Loop 会话";
            _btnOpenLoop.Radius = 8;
            _btnOpenLoop.Height = 34;
            _btnOpenLoop.Width = 236;
            _btnOpenLoop.IconSvg = UiIcons.Dot(UiStyle.Loop);
            _btnOpenLoop.Margin = new Padding(0, 12, 0, 4);
            _btnOpenLoop.Click += OnOpenLoop;

            _lblDModel.AutoSize = true;
            _lblDModel.Text = "模型: —";
            _lblDModel.Font = new Font("Segoe UI", 9F);
            _lblDModel.ForeColor = UiStyle.Text;
            _lblDModel.MaximumSize = new Size(260, 0);
            _lblDModel.Margin = new Padding(0, 2, 0, 8);

            _lblDEffort.AutoSize = true;
            _lblDEffort.Text = "强度: 自动";
            _lblDEffort.Font = new Font("Segoe UI", 9F);
            _lblDEffort.ForeColor = UiStyle.Muted;
            _lblDEffort.Margin = new Padding(0, 0, 0, 8);

            _lblDCtx.AutoSize = true;
            _lblDCtx.Text = "上下文: —";
            _lblDCtx.Font = new Font("Segoe UI", 9F);
            _lblDCtx.ForeColor = UiStyle.Muted;
            _lblDCtx.Margin = new Padding(0, 0, 0, 8);

            _lblDCost.AutoSize = true;
            _lblDCost.Text = "成本: —";
            _lblDCost.Font = new Font("Segoe UI", 9F);
            _lblDCost.ForeColor = UiStyle.Muted;
            _lblDCost.Margin = new Padding(0, 0, 0, 8);

            _lblDStatus.AutoSize = true;
            _lblDStatus.Text = "状态: 就绪";
            _lblDStatus.Font = new Font("Segoe UI", 9F);
            _lblDStatus.ForeColor = UiStyle.Text;
            _lblDStatus.MaximumSize = new Size(260, 0);
            _lblDStatus.Margin = new Padding(0, 0, 0, 8);

            _loopFlow.Controls.Add(_lblDef);
            _loopFlow.Controls.Add(_lblTasks);
            _loopFlow.Controls.Add(_lblTrace);
            _loopFlow.Controls.Add(_lblCost);
            _loopFlow.Controls.Add(_lblStatus);
            _loopFlow.Controls.Add(_btnEvolve);
            _loopFlow.Controls.Add(_btnRollback);
            _loopFlow.Controls.Add(_btnOpenLoop);

            _detailFlow.Controls.Add(_lblDModel);
            _detailFlow.Controls.Add(_lblDEffort);
            _detailFlow.Controls.Add(_lblDCtx);
            _detailFlow.Controls.Add(_lblDCost);
            _detailFlow.Controls.Add(_lblDStatus);

            _sideHost.Controls.Add(_loopFlow);
            _sideHost.Controls.Add(_detailFlow);
            _side.Controls.Add(_sideTabs, 0, 0);
            _side.Controls.Add(_sideHost, 0, 1);
            _upperSplit.Panel2.Controls.Add(_side);

            _mainSplit.Panel1.Controls.Add(_upperSplit);

            // input area
            _inputArea.Dock = DockStyle.Fill;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 3;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 8, 16, 6);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = "描述任务，或声明判据 / 纠正（Ctrl+Enter 发送）";
            _txtInput.KeyDown += OnInputKeyDown;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 2;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 6, 0, 0);

            _toolbarLeft.Dock = DockStyle.Fill;
            _toolbarLeft.FlowDirection = FlowDirection.LeftToRight;
            _toolbarLeft.WrapContents = false;
            _toolbarLeft.Margin = new Padding(0);

            _modeControl.Name = "mode";
            _modeControl.Height = 34;
            _modeControl.Width = 300;
            _modeControl.Margin = new Padding(0, 0, 8, 0);
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = "任务" });
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = "声明判据" });
            _modeControl.Items.Add(new AntdUI.SegmentedItem { Text = "这是纠正" });
            _modeControl.SelectIndex = 0;
            _modeControl.SelectIndexChanged += OnModeChanged;

            _btnStop.Name = "btnStop";
            _btnStop.Text = "停止";
            _btnStop.Type = AntdUI.TTypeMini.Error;
            _btnStop.Ghost = true;
            _btnStop.Radius = 8;
            _btnStop.Height = 34;
            _btnStop.Width = 76;
            _btnStop.IconSvg = UiIcons.Of(UiIcons.Stop, UiStyle.Error);
            _btnStop.Enabled = false;
            _btnStop.Margin = new Padding(0);
            _btnStop.Click += OnStop;

            _toolbarLeft.Controls.Add(_modeControl);
            _toolbarLeft.Controls.Add(_btnStop);

            _toolbarRight.Dock = DockStyle.Fill;
            _toolbarRight.FlowDirection = FlowDirection.RightToLeft;
            _toolbarRight.WrapContents = false;
            _toolbarRight.AutoSize = true;
            _toolbarRight.Margin = new Padding(0);

            _selModel.Name = "selModel";
            _selModel.Width = 290;
            _selModel.Height = 34;
            _selModel.Radius = 8;
            _selModel.PlaceholderText = "模型";
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.Margin = new Padding(6, 0, 0, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _freeSwitch.Name = "freeSwitch";
            _freeSwitch.Width = 40;
            _freeSwitch.Height = 22;
            _freeSwitch.Checked = true;
            _freeSwitch.Margin = new Padding(10, 6, 0, 0);
            _freeSwitch.CheckedChanged += OnFreeToggle;

            _lblFree.Name = "lblFree";
            _lblFree.AutoSize = true;
            _lblFree.Text = "只看免费";
            _lblFree.Font = new Font("Segoe UI", 9F);
            _lblFree.ForeColor = UiStyle.Muted;
            _lblFree.Margin = new Padding(0, 9, 4, 0);

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 34;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(6, 0, 0, 0);
            _selEffort.Items.Add(new AntdUI.SelectItem("自动") { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem("低") { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem("中") { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem("高") { Tag = "high" });
            _selEffort.SelectedIndex = 0;
            _selEffort.SelectedValueChanged += OnEffortChanged;

            _ctx.Name = "ctx";
            _ctx.Width = 32;
            _ctx.Height = 32;
            _ctx.Shape = AntdUI.TShapeProgress.Circle;
            _ctx.TextUnit = "%";
            _ctx.Value = 0F;
            _ctx.Margin = new Padding(0, 5, 8, 5);
            _headerRight.Controls.Add(_ctx);

            _btnSend.Name = "btnSend";
            _btnSend.Text = "发送";
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 104;
            _btnSend.Height = 34;
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(0);
            _btnSend.Click += OnSendClick;

            _toolbarRight.Controls.Add(_btnSend);
            _toolbarRight.Controls.Add(_selEffort);
            _toolbarRight.Controls.Add(_selModel);
            _toolbarRight.Controls.Add(_freeSwitch);
            _toolbarRight.Controls.Add(_lblFree);

            _toolbar.Controls.Add(_toolbarLeft, 0, 0);
            _toolbar.Controls.Add(_toolbarRight, 1, 0);

            _lblPath.Name = "lblPath";
            _lblPath.Dock = DockStyle.Fill;
            _lblPath.Text = "";
            _lblPath.Font = new Font("Segoe UI", 8.5F);
            _lblPath.ForeColor = UiStyle.Muted;
            _lblPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblPath.AutoEllipsis = true;
            _lblPath.IconGap = 6;
            _lblPath.Margin = new Padding(2, 0, 0, 0);

            _inputArea.Controls.Add(_txtInput, 0, 0);
            _inputArea.Controls.Add(_toolbar, 0, 1);
            _inputArea.Controls.Add(_lblPath, 0, 2);
            _mainSplit.Panel2.Controls.Add(_inputArea);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_headerLine, 0, 1);
            _root.Controls.Add(_mainSplit, 0, 2);

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
