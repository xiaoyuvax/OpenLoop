namespace OpenLoop.App
{
    /// <summary>provider 型插件的最小编辑器：编辑已有目录，或在插件根下新建一个。</summary>
    partial class PluginEditForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _grid;
        private AntdUI.Label _lblId;
        private AntdUI.Input _txtId;
        private AntdUI.Label _lblName;
        private AntdUI.Input _txtName;
        private AntdUI.Label _lblUrl;
        private AntdUI.Input _txtUrl;
        private AntdUI.Label _lblModelsUrl;
        private AntdUI.Input _txtModelsUrl;
        private AntdUI.Label _lblPrefix;
        private AntdUI.Input _txtPrefix;
        private AntdUI.Label _lblKey;
        private AntdUI.Input _txtKey;
        private AntdUI.Label _lblNote;
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

            _grid = new TableLayoutPanel();
            _lblId = new AntdUI.Label();
            _txtId = new AntdUI.Input();
            _lblName = new AntdUI.Label();
            _txtName = new AntdUI.Input();
            _lblUrl = new AntdUI.Label();
            _txtUrl = new AntdUI.Input();
            _lblModelsUrl = new AntdUI.Label();
            _txtModelsUrl = new AntdUI.Input();
            _lblPrefix = new AntdUI.Label();
            _txtPrefix = new AntdUI.Input();
            _lblKey = new AntdUI.Label();
            _txtKey = new AntdUI.Input();
            _lblNote = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();

            SuspendLayout();

            _grid.Dock = DockStyle.Fill;
            _grid.ColumnCount = 2;
            _grid.RowCount = 8;
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _grid.BackColor = UiStyle.Surface;
            _grid.Padding = new Padding(16, 12, 16, 8);

            ConfigureLabel(_lblId, Localization.T("PluginIdLabel"));
            ConfigureLabel(_lblName, Localization.T("Name"));
            ConfigureLabel(_lblUrl, "BaseUrl");
            ConfigureLabel(_lblModelsUrl, "ModelsUrl");
            ConfigureLabel(_lblPrefix, Localization.T("PluginPrefixLabel"));
            ConfigureLabel(_lblKey, Localization.T("SettingsKey"));

            _txtId.Dock = DockStyle.Fill;
            _txtId.Radius = 8;
            _txtId.BorderWidth = 1;
            _txtId.PlaceholderText = Localization.T("PluginIdPlaceholder");
            _txtId.Margin = new Padding(0, 5, 0, 5);

            _txtName.Dock = DockStyle.Fill;
            _txtName.Radius = 8;
            _txtName.BorderWidth = 1;
            _txtName.PlaceholderText = Localization.T("PluginNamePlaceholder");
            _txtName.Margin = new Padding(0, 5, 0, 5);

            _txtUrl.Dock = DockStyle.Fill;
            _txtUrl.Radius = 8;
            _txtUrl.BorderWidth = 1;
            _txtUrl.PlaceholderText = Localization.T("PluginUrlPlaceholder");
            _txtUrl.Margin = new Padding(0, 5, 0, 5);

            _txtModelsUrl.Dock = DockStyle.Fill;
            _txtModelsUrl.Radius = 8;
            _txtModelsUrl.BorderWidth = 1;
            _txtModelsUrl.PlaceholderText = Localization.T("PluginModelsUrlPlaceholder");
            _txtModelsUrl.Margin = new Padding(0, 5, 0, 5);

            _txtPrefix.Dock = DockStyle.Fill;
            _txtPrefix.Radius = 8;
            _txtPrefix.BorderWidth = 1;
            _txtPrefix.PlaceholderText = Localization.T("PluginPrefixPlaceholder");
            _txtPrefix.Margin = new Padding(0, 5, 0, 5);

            _txtKey.Dock = DockStyle.Fill;
            _txtKey.Radius = 8;
            _txtKey.BorderWidth = 1;
            _txtKey.UseSystemPasswordChar = true;
            _txtKey.PlaceholderText = Localization.T("PluginKeyPlaceholder");
            _txtKey.Margin = new Padding(0, 5, 0, 5);

            _lblNote.AutoSize = false;
            _lblNote.Dock = DockStyle.Fill;
            _lblNote.Font = new Font("Segoe UI", 8.5F);
            _lblNote.ForeColor = UiStyle.Muted;
            _lblNote.TextAlign = ContentAlignment.TopLeft;
            _lblNote.Margin = new Padding(0, 8, 0, 0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Text = Localization.T("SettingsSave");
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 0);
            _btnSave.Click += OnSave;

            _btnCancel.Text = Localization.T("SettingsCancel");
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 0);
            _btnCancel.Click += OnCancel;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);

            _grid.Controls.Add(_lblId, 0, 0);
            _grid.Controls.Add(_txtId, 1, 0);
            _grid.Controls.Add(_lblName, 0, 1);
            _grid.Controls.Add(_txtName, 1, 1);
            _grid.Controls.Add(_lblUrl, 0, 2);
            _grid.Controls.Add(_txtUrl, 1, 2);
            _grid.Controls.Add(_lblModelsUrl, 0, 3);
            _grid.Controls.Add(_txtModelsUrl, 1, 3);
            _grid.Controls.Add(_lblPrefix, 0, 4);
            _grid.Controls.Add(_txtPrefix, 1, 4);
            _grid.Controls.Add(_lblKey, 0, 5);
            _grid.Controls.Add(_txtKey, 1, 5);
            _grid.Controls.Add(_lblNote, 0, 6);
            _grid.SetColumnSpan(_lblNote, 2);
            _grid.Controls.Add(_buttons, 0, 7);
            _grid.SetColumnSpan(_buttons, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(600, 400);
            MinimumSize = new Size(560, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Localization.T("PluginEditTitle");
            Controls.Add(_grid);

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
