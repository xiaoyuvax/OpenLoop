namespace OpenLoop.App
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Label _lblBaseUrl;
        private AntdUI.Input _txtBaseUrl;
        private AntdUI.Label _lblModel;
        private AntdUI.Select _cboModel;
        private AntdUI.Button _btnFetch;
        private AntdUI.Label _lblKey;
        private AntdUI.Input _txtKey;
        private AntdUI.Label _lblNote;
        private AntdUI.Label _lblStatus;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnTest;
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
            _lblBaseUrl = new AntdUI.Label();
            _txtBaseUrl = new AntdUI.Input();
            _lblModel = new AntdUI.Label();
            _cboModel = new AntdUI.Select();
            _btnFetch = new AntdUI.Button();
            _lblKey = new AntdUI.Label();
            _txtKey = new AntdUI.Input();
            _lblNote = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnTest = new AntdUI.Button();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 3;
            _root.RowCount = 6;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(16, 12, 16, 8);

            ConfigureLabel(_lblBaseUrl, "接口地址");
            ConfigureLabel(_lblModel, "模型");
            ConfigureLabel(_lblKey, "密钥");

            _txtBaseUrl.Dock = DockStyle.Fill;
            _txtBaseUrl.Radius = 8;
            _txtBaseUrl.BorderWidth = 1;
            _txtBaseUrl.Margin = new Padding(0, 6, 8, 6);

            _cboModel.Name = "cboModel";
            _cboModel.Dock = DockStyle.Fill;
            _cboModel.Radius = 8;
            _cboModel.Margin = new Padding(0, 6, 8, 6);
            _cboModel.Items.Add(new AntdUI.SelectItem("openrouter/free") { Tag = "openrouter/free" });

            _btnFetch.Name = "btnFetch";
            _btnFetch.Text = "免费模型";
            _btnFetch.Radius = 8;
            _btnFetch.IconSvg = UiIcons.Of(UiIcons.Refresh, UiStyle.Text);
            _btnFetch.Dock = DockStyle.Fill;
            _btnFetch.Margin = new Padding(0, 6, 0, 6);
            _btnFetch.Click += OnFetchModels;

            _txtKey.Name = "txtKey";
            _txtKey.Dock = DockStyle.Fill;
            _txtKey.Radius = 8;
            _txtKey.BorderWidth = 1;
            _txtKey.UseSystemPasswordChar = true;
            _txtKey.Margin = new Padding(0, 6, 8, 6);

            _lblNote.Dock = DockStyle.Fill;
            _lblNote.Text = "密钥用当前 Windows 用户 DPAPI 加密，只存 %APPDATA%\\OpenLoop\\settings.json；" +
                            "留空则读环境变量 OPENROUTER_API_KEY。";
            _lblNote.Font = new Font("Segoe UI", 8.5F);
            _lblNote.ForeColor = UiStyle.Muted;
            _lblNote.Margin = new Padding(0, 2, 0, 0);

            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Text = "";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.Margin = new Padding(0, 4, 0, 0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Name = "btnSave";
            _btnSave.Text = "保存";
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 0);
            _btnSave.Click += OnSave;

            _btnCancel.Name = "btnCancel";
            _btnCancel.Text = "取消";
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 0);
            _btnCancel.Click += OnCancel;

            _btnTest.Name = "btnTest";
            _btnTest.Text = "测试连接";
            _btnTest.Radius = 8;
            _btnTest.Size = new Size(104, 34);
            _btnTest.Margin = new Padding(8, 6, 0, 0);
            _btnTest.Click += OnTestProvider;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);
            _buttons.Controls.Add(_btnTest);

            _root.Controls.Add(_lblBaseUrl, 0, 0);
            _root.Controls.Add(_txtBaseUrl, 1, 0);
            _root.SetColumnSpan(_txtBaseUrl, 2);
            _root.Controls.Add(_lblModel, 0, 1);
            _root.Controls.Add(_cboModel, 1, 1);
            _root.Controls.Add(_btnFetch, 2, 1);
            _root.Controls.Add(_lblKey, 0, 2);
            _root.Controls.Add(_txtKey, 1, 2);
            _root.SetColumnSpan(_txtKey, 2);
            _root.Controls.Add(_lblNote, 0, 3);
            _root.SetColumnSpan(_lblNote, 3);
            _root.Controls.Add(_lblStatus, 0, 4);
            _root.SetColumnSpan(_lblStatus, 3);
            _root.Controls.Add(_buttons, 0, 5);
            _root.SetColumnSpan(_buttons, 3);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(660, 360);
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
