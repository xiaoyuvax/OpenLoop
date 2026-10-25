namespace OpenLoop.App
{
    partial class NewSessionView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _body;
        private AntdUI.Label _lblLogo;
        private AntdUI.Label _lblSub;
        private Panel _composer;
        private TableLayoutPanel _composerLayout;
        private AntdUI.Input _txtInput;
        private TableLayoutPanel _toolbar;
        private FlowLayoutPanel _toolbarLeft;
        private AntdUI.Select _selProvider;
        private AntdUI.Select _selModel;
        private AntdUI.Switch _freeSwitch;
        private AntdUI.Label _lblFree;
        private AntdUI.Select _selEffort;
        private AntdUI.Button _btnSend;
        private TableLayoutPanel _projectRow;
        private AntdUI.Label _lblProject;
        private AntdUI.Select _selProject;
        private AntdUI.Label _lblPath;
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
            _body = new TableLayoutPanel();
            _lblLogo = new AntdUI.Label();
            _lblSub = new AntdUI.Label();
            _composer = new Panel();
            _composerLayout = new TableLayoutPanel();
            _txtInput = new AntdUI.Input();
            _toolbar = new TableLayoutPanel();
            _toolbarLeft = new FlowLayoutPanel();
            _selProvider = new AntdUI.Select();
            _selModel = new AntdUI.Select();
            _freeSwitch = new AntdUI.Switch();
            _lblFree = new AntdUI.Label();
            _selEffort = new AntdUI.Select();
            _btnSend = new AntdUI.Button();
            _projectRow = new TableLayoutPanel();
            _lblProject = new AntdUI.Label();
            _selProject = new AntdUI.Select();
            _lblPath = new AntdUI.Label();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 3;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 760F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            _root.BackColor = UiStyle.Page;

            _body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _body.AutoSize = true;
            _body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _body.ColumnCount = 1;
            _body.RowCount = 6;
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblLogo.Dock = DockStyle.Top;
            _lblLogo.Height = 92;
            _lblLogo.Text = "OpenLoop";
            _lblLogo.Font = new Font("Segoe UI", 40F, FontStyle.Bold);
            _lblLogo.ForeColor = Color.FromArgb(0xDE, 0xDE, 0xDE);
            _lblLogo.TextAlign = ContentAlignment.MiddleCenter;
            _lblLogo.Margin = new Padding(0, 0, 0, 2);

            _lblSub.Dock = DockStyle.Top;
            _lblSub.Height = 28;
            _lblSub.Text = Localization.T("NewSessionSubtitle");
            _lblSub.Font = new Font("Segoe UI", 10F);
            _lblSub.ForeColor = UiStyle.Muted;
            _lblSub.TextAlign = ContentAlignment.MiddleCenter;
            _lblSub.Margin = new Padding(0);

            _composer.Dock = DockStyle.Top;
            _composer.Height = 236;
            _composer.BackColor = UiStyle.Surface;
            _composer.Padding = new Padding(16, 14, 16, 12);
            _composer.Margin = new Padding(0);

            _composerLayout.Dock = DockStyle.Fill;
            _composerLayout.ColumnCount = 1;
            _composerLayout.RowCount = 2;
            _composerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _composerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _composerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            _composerLayout.Margin = new Padding(0);

            _txtInput.Name = "txtInput";
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Multiline = true;
            _txtInput.Radius = 10;
            _txtInput.BorderWidth = 1;
            _txtInput.Font = new Font("Segoe UI", 10F);
            _txtInput.PlaceholderText = Localization.T("NewSessionHint");
            _txtInput.Margin = new Padding(0, 0, 0, 8);
            _txtInput.KeyDown += OnInputKeyDown;

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.ColumnCount = 2;
            _toolbar.RowCount = 1;
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _toolbar.Margin = new Padding(0);

            _toolbarLeft.Dock = DockStyle.Fill;
            _toolbarLeft.WrapContents = false;
            _toolbarLeft.Margin = new Padding(0);

            _selProvider.Name = "selProvider";
            _selProvider.Width = 140;
            _selProvider.Height = 34;
            _selProvider.Radius = 8;
            _selProvider.PlaceholderText = Localization.T("Provider");
            _selProvider.Margin = new Padding(0, 0, 8, 0);
            _selProvider.SelectedValueChanged += OnProviderChanged;

            _selModel.Name = "selModel";
            _selModel.Width = 220;
            _selModel.Height = 34;
            _selModel.Radius = 8;
            _selModel.PrefixSvg = UiIcons.Of(UiIcons.Model, UiStyle.Muted);
            _selModel.PlaceholderText = "模型";
            _selModel.Margin = new Padding(0, 0, 8, 0);

            _freeSwitch.Name = "freeSwitch";
            _freeSwitch.Width = 40;
            _freeSwitch.Height = 22;
            _freeSwitch.Checked = true;
            _freeSwitch.Margin = new Padding(4, 6, 0, 0);
            _freeSwitch.CheckedChanged += OnFreeToggle;

            _lblFree.Name = "lblFree";
            _lblFree.AutoSize = true;
            _lblFree.Text = Localization.T("FreeOnly");
            _lblFree.Font = new Font("Segoe UI", 9F);
            _lblFree.ForeColor = UiStyle.Muted;
            _lblFree.Margin = new Padding(0, 9, 8, 0);

            _selEffort.Name = "selEffort";
            _selEffort.Width = 128;
            _selEffort.Height = 34;
            _selEffort.Radius = 8;
            _selEffort.PrefixSvg = UiIcons.Of(UiIcons.Effort, UiStyle.Muted);
            _selEffort.Margin = new Padding(0);
            _selEffort.Items.Add(new AntdUI.SelectItem("自动") { Tag = "" });
            _selEffort.Items.Add(new AntdUI.SelectItem("低") { Tag = "low" });
            _selEffort.Items.Add(new AntdUI.SelectItem("中") { Tag = "medium" });
            _selEffort.Items.Add(new AntdUI.SelectItem("高") { Tag = "high" });
            _selEffort.SelectedIndex = 0;

            _toolbarLeft.Controls.Add(_selProvider);
            _toolbarLeft.Controls.Add(_selModel);
            _toolbarLeft.Controls.Add(_freeSwitch);
            _toolbarLeft.Controls.Add(_lblFree);
            _toolbarLeft.Controls.Add(_selEffort);

            _btnSend.Name = "btnSend";
            _btnSend.Type = AntdUI.TTypeMini.Primary;
            _btnSend.Radius = 8;
            _btnSend.Width = 96;
            _btnSend.Height = 36;
            _btnSend.Text = Localization.T("Start");
            _btnSend.IconSvg = UiIcons.Of(UiIcons.Send, Color.White);
            _btnSend.Margin = new Padding(8, 4, 0, 4);
            _btnSend.Click += OnSend;

            _toolbar.Controls.Add(_toolbarLeft, 0, 0);
            _toolbar.Controls.Add(_btnSend, 1, 0);

            _composerLayout.Controls.Add(_txtInput, 0, 0);
            _composerLayout.Controls.Add(_toolbar, 0, 1);
            _composer.Controls.Add(_composerLayout);

            _projectRow.Dock = DockStyle.Top;
            _projectRow.Height = 46;
            _projectRow.ColumnCount = 3;
            _projectRow.RowCount = 1;
            _projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
            _projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _projectRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _projectRow.Margin = new Padding(0);

            _lblProject.AutoSize = true;
            _lblProject.Text = Localization.T("Projects");
            _lblProject.Font = new Font("Segoe UI", 9F);
            _lblProject.ForeColor = UiStyle.Muted;
            _lblProject.Margin = new Padding(2, 9, 10, 0);

            _selProject.Name = "selProject";
            _selProject.Dock = DockStyle.Fill;
            _selProject.Height = 38;
            _selProject.Radius = 8;
            _selProject.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _selProject.PlaceholderText = Localization.T("PickProject");
            _selProject.Margin = new Padding(0, 3, 12, 3);
            _selProject.SelectedValueChanged += OnProjectChanged;

            _lblPath.Dock = DockStyle.Fill;
            _lblPath.Text = "";
            _lblPath.Font = new Font("Segoe UI", 8.5F);
            _lblPath.ForeColor = UiStyle.Muted;
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblPath.AutoEllipsis = true;
            _lblPath.Margin = new Padding(0);

            _projectRow.Controls.Add(_lblProject, 0, 0);
            _projectRow.Controls.Add(_selProject, 1, 0);
            _projectRow.Controls.Add(_lblPath, 2, 0);

            _body.Controls.Add(_lblLogo, 0, 0);
            _body.Controls.Add(_lblSub, 0, 1);
            _body.Controls.Add(new Panel { Margin = new Padding(0) }, 0, 2);
            _body.Controls.Add(_composer, 0, 3);
            _body.Controls.Add(new Panel { Margin = new Padding(0) }, 0, 4);
            _body.Controls.Add(_projectRow, 0, 5);

            _root.Controls.Add(_body, 1, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
