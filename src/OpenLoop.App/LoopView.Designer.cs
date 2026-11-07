namespace OpenLoop.App
{
    partial class LoopView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblSub;
        private AntdUI.Button _btnPanel;
        private Panel _line;
        private SplitContainer _upperSplit;
        private ChatFlow.ChatFlowControl _chat;
        private PendingQueueView _pending;
        private TableLayoutPanel _side;
        private AntdUI.Segmented _sideTabs;
        private Panel _sideHost;
        private FlowLayoutPanel _detailFlow;
        private SideWorkView _sideBtw;
        private AntdUI.Label _lblDModel;
        private AntdUI.Label _lblDEffort;
        private AntdUI.Label _lblDCtx;
        private AntdUI.Label _lblDCost;
        private AntdUI.Label _lblDStatus;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selProvider;
        private AntdUI.Select _selEffort;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Button _btnSend;
        private AntdUI.Button _btnIterate;
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
            _lblSub = new AntdUI.Label();
            _btnPanel = new AntdUI.Button();
            _line = new Panel();
            _upperSplit = new SplitContainer();
            _chat = new ChatFlow.ChatFlowControl();
            _pending = new PendingQueueView();
            _side = new TableLayoutPanel();
            _sideTabs = new AntdUI.Segmented();
            _sideHost = new Panel();
            _detailFlow = new FlowLayoutPanel();
            _sideBtw = new SideWorkView();
            _lblDModel = new AntdUI.Label();
            _lblDEffort = new AntdUI.Label();
            _lblDCtx = new AntdUI.Label();
            _lblDCost = new AntdUI.Label();
            _lblDStatus = new AntdUI.Label();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _selModel = new AntdUI.Select();
            _selProvider = new AntdUI.Select();
            _selEffort = new AntdUI.Select();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _btnSend = new AntdUI.Button();
            _btnIterate = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();

            SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_upperSplit).BeginInit();
            _upperSplit.Panel1.SuspendLayout();
            _upperSplit.Panel2.SuspendLayout();
            _upperSplit.SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            _root.BackColor = UiStyle.Page;

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 3;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 10, 0);

            _lblTitle.AutoSize = true;
            _lblTitle.Text = "Loop";
            _lblTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _lblTitle.ForeColor = Color.FromArgb(0x72, 0x2E, 0xD1);
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.Margin = new Padding(0, 0, 10, 0);

            _lblSub.Dock = DockStyle.Fill;
            _lblSub.Text = Localization.T("LoopSubtitle");
            _lblSub.Font = new Font("Segoe UI", 9F);
            _lblSub.ForeColor = UiStyle.Muted;
            _lblSub.TextAlign = ContentAlignment.MiddleLeft;
            _lblSub.AutoEllipsis = true;
            _lblSub.Margin = new Padding(0);

            _btnPanel.Name = "btnPanel";
            _btnPanel.Width = 36;
            _btnPanel.Height = 30;
            _btnPanel.Ghost = true;
            _btnPanel.Radius = 8;
            _btnPanel.IconSvg = UiIcons.Of(UiIcons.PanelClose, UiStyle.Text);
            _btnPanel.Margin = new Padding(0, 9, 0, 9);
            _btnPanel.Click += OnToggleSide;

            _header.Controls.Add(_lblTitle, 0, 0);
            _header.Controls.Add(_lblSub, 1, 0);
            _header.Controls.Add(_btnPanel, 2, 0);

            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            _upperSplit.Dock = DockStyle.Fill;
            _upperSplit.Orientation = Orientation.Vertical;
            _upperSplit.SplitterWidth = 8;
            _upperSplit.FixedPanel = FixedPanel.Panel2;
            _upperSplit.BackColor = UiStyle.Page;

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
            _sideTabs.Items.Add(new AntdUI.SegmentedItem { Text = "旁路" });
            _sideTabs.SelectIndex = 0;
            _sideTabs.SelectIndexChanged += OnSidePageChanged;

            _sideHost.Dock = DockStyle.Fill;
            _sideHost.Margin = new Padding(0, 8, 0, 0);

            _detailFlow.Dock = DockStyle.Fill;
            _detailFlow.FlowDirection = FlowDirection.TopDown;
            _detailFlow.WrapContents = false;
            _detailFlow.AutoScroll = true;

            _sideBtw = new SideWorkView { Dock = DockStyle.Fill, Visible = false };

            _lblDModel.AutoSize = true;
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

            _lblDStatus.AutoSize = true;
            _lblDStatus.MaximumSize = new Size(260, 0);
            _lblDStatus.Text = Localization.T("Status") + ": —";
            _lblDStatus.Font = new Font("Segoe UI", 9F);
            _lblDStatus.ForeColor = UiStyle.Text;
            _lblDStatus.Margin = new Padding(0, 0, 0, 8);

            _detailFlow.Controls.Add(_lblDModel);
            _detailFlow.Controls.Add(_lblDEffort);
            _detailFlow.Controls.Add(_lblDCtx);
            _detailFlow.Controls.Add(_lblDCost);
            _detailFlow.Controls.Add(_lblDStatus);

            _sideHost.Controls.Add(_detailFlow);
            _sideHost.Controls.Add(_sideBtw);
            _side.Controls.Add(_sideTabs, 0, 0);
            _side.Controls.Add(_sideHost, 0, 1);
            _upperSplit.Panel2.Controls.Add(_side);

            _inputArea.Dock = DockStyle.Fill;
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
            _selModel.PlaceholderText = "模型";
            _selModel.Margin = new Padding(0, 0, 8, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 30;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(0, 0, 8, 0);
            _selEffort.Items.Add(new AntdUI.SelectItem("自动") { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem("低") { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem("中") { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem("高") { Tag = "high" });
            _selEffort.SelectedIndex = 0;
            _selEffort.SelectedValueChanged += OnEffortChanged;

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
            _btnIterate.Text = "迭代";
            _btnIterate.Ghost = true;
            _btnIterate.Radius = 8;
            _btnIterate.Width = 76;
            _btnIterate.Height = 30;
            _btnIterate.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Muted);
            _btnIterate.Margin = new Padding(0, 0, 8, 0);
            _btnIterate.Click += OnIterateClick;

            _lblStatus.AutoSize = true;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Margin = new Padding(0, 9, 0, 0);

            _toolbarLeft.Controls.Add(_selProvider);
            _toolbarLeft.Controls.Add(_selModel);
            _toolbarLeft.Controls.Add(_freeSwitch);
            _toolbarLeft.Controls.Add(_lblFree);
            _toolbarLeft.Controls.Add(_selEffort);
            _toolbarLeft.Controls.Add(_btnIterate);
            _toolbarLeft.Controls.Add(_lblStatus);

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

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_line, 0, 1);
            _root.Controls.Add(_upperSplit, 0, 2);
            _root.Controls.Add(_inputArea, 0, 3);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            Load += OnViewLoad;

            _upperSplit.Panel1.ResumeLayout(false);
            _upperSplit.Panel2.ResumeLayout(false);
            _upperSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_upperSplit).EndInit();
            ResumeLayout(false);
        }
    }
}
