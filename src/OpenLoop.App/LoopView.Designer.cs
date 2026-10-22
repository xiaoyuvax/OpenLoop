namespace OpenLoop.App
{
    partial class LoopView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblSub;
        private Panel _line;
        private ChatFlow.ChatFlowControl _chat;
        private TableLayoutPanel _inputArea;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Select _selModel;
        private AntdUI.Select _selEffort;
        private AntdUI.Button _btnStop;
        private AntdUI.Button _btnSend;
        private AntdUI.Label _lblStatus;

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
            _lblSub = new AntdUI.Label();
            _line = new Panel();
            _chat = new ChatFlow.ChatFlowControl();
            _inputArea = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _selModel = new AntdUI.Select();
            _selEffort = new AntdUI.Select();
            _btnStop = new AntdUI.Button();
            _btnSend = new AntdUI.Button();
            _lblStatus = new AntdUI.Label();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
            _root.BackColor = UiStyle.Page;

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 2;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.BackColor = UiStyle.Surface;
            _header.Padding = new Padding(16, 0, 16, 0);

            _lblTitle.AutoSize = true;
            _lblTitle.Text = "Loop";
            _lblTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _lblTitle.ForeColor = Color.FromArgb(0x72, 0x2E, 0xD1);
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.Margin = new Padding(0, 0, 10, 0);

            _lblSub.Dock = DockStyle.Fill;
            _lblSub.Text = "观察 Worker 与用户判定，积累下一版";
            _lblSub.Font = new Font("Segoe UI", 9F);
            _lblSub.ForeColor = UiStyle.Muted;
            _lblSub.TextAlign = ContentAlignment.MiddleLeft;
            _lblSub.AutoEllipsis = true;
            _lblSub.Margin = new Padding(0);

            _header.Controls.Add(_lblTitle, 0, 0);
            _header.Controls.Add(_lblSub, 1, 0);

            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            _chat.Dock = DockStyle.Fill;
            _chat.BackColor = UiStyle.Surface;

            _inputArea.Dock = DockStyle.Fill;
            _inputArea.ColumnCount = 1;
            _inputArea.RowCount = 2;
            _inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _inputArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _inputArea.BackColor = UiStyle.Page;
            _inputArea.Padding = new Padding(16, 8, 16, 8);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.BackColor = UiStyle.Surface;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = "和 Loop 对话：说出你的体验、不满或改进建议（Ctrl+Enter 发送）";
            _txtInput.KeyDown += OnInputKeyDown;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 2;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0, 6, 0, 0);

            _toolbarLeft.Dock = DockStyle.Fill;
            _toolbarLeft.WrapContents = false;
            _toolbarLeft.Margin = new Padding(0);

            _selModel.Name = "selModel";
            _selModel.Width = 300;
            _selModel.Height = 34;
            _selModel.Radius = 8;
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.PlaceholderText = "模型";
            _selModel.Margin = new Padding(0, 0, 8, 0);
            _selModel.SelectedValueChanged += OnModelChanged;

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 34;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(0, 0, 8, 0);
            _selEffort.Items.Add(new AntdUI.SelectItem("自动") { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem("低") { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem("中") { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem("高") { Tag = "high" });
            _selEffort.SelectedIndex = 0;
            _selEffort.SelectedValueChanged += OnEffortChanged;

            _lblStatus.AutoSize = true;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Margin = new Padding(0, 9, 0, 0);

            _toolbarLeft.Controls.Add(_selModel);
            _toolbarLeft.Controls.Add(_selEffort);
            _toolbarLeft.Controls.Add(_lblStatus);

            _btnStop.Name = "btnStop";
            _btnStop.Text = "停止";
            _btnStop.Type = AntdUI.TTypeMini.Error;
            _btnStop.Ghost = true;
            _btnStop.Radius = 8;
            _btnStop.Width = 76;
            _btnStop.Height = 34;
            _btnStop.IconSvg = UiIcons.Of(UiIcons.Stop, UiStyle.Error);
            _btnStop.Enabled = false;
            _btnStop.Margin = new Padding(0, 0, 8, 0);
            _btnStop.Click += OnStop;

            _btnSend.Name = "btnSend";
            _btnSend.Text = "发送";
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 104;
            _btnSend.Height = 34;
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
            _root.Controls.Add(_chat, 0, 2);
            _root.Controls.Add(_inputArea, 0, 3);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            Load += OnViewLoad;
            ResumeLayout(false);
        }
    }
}
