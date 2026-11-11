namespace OpenLoop.App
{
    /// <summary>单个模型的属性页：id / 显示名称 / 上下文上限（保存到设置的逐模型覆盖）。</summary>
    partial class ModelPropsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Label _lblId;
        private AntdUI.Input _txtId;
        private AntdUI.Label _lblName;
        private AntdUI.Input _txtName;
        private AntdUI.Label _lblCtx;
        private AntdUI.Select _selCtx;
        private AntdUI.Label _lblHint;
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

            _root = new TableLayoutPanel();
            _lblId = new AntdUI.Label();
            _txtId = new AntdUI.Input();
            _lblName = new AntdUI.Label();
            _txtName = new AntdUI.Input();
            _lblCtx = new AntdUI.Label();
            _selCtx = new AntdUI.Select();
            _lblHint = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 5;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(16, 12, 16, 8);

            ConfigureLabel(_lblId, "模型 id");
            ConfigureLabel(_lblName, "显示名称");
            ConfigureLabel(_lblCtx, "上下文");

            _txtId.Dock = DockStyle.Fill;
            _txtId.Radius = 8;
            _txtId.BorderWidth = 1;
            _txtId.PlaceholderText = "发给服务商的模型 id，例如 deepseek-flash-1017";
            _txtId.Margin = new Padding(0, 6, 0, 6);

            _txtName.Dock = DockStyle.Fill;
            _txtName.Radius = 8;
            _txtName.BorderWidth = 1;
            _txtName.PlaceholderText = "留空用目录里的名字或模型 id";
            _txtName.Margin = new Padding(0, 6, 0, 6);

            _selCtx.Dock = DockStyle.Fill;
            _selCtx.Radius = 8;
            _selCtx.Margin = new Padding(0, 6, 0, 6);

            _lblHint.AutoSize = false;
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.TopLeft;
            _lblHint.Margin = new Padding(0, 8, 0, 0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Text = "保存";
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 0);
            _btnSave.Click += OnSave;

            _btnCancel.Text = "取消";
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 0);
            _btnCancel.Click += OnCancel;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);

            _root.Controls.Add(_lblId, 0, 0);
            _root.Controls.Add(_txtId, 1, 0);
            _root.Controls.Add(_lblName, 0, 1);
            _root.Controls.Add(_txtName, 1, 1);
            _root.Controls.Add(_lblCtx, 0, 2);
            _root.Controls.Add(_selCtx, 1, 2);
            _root.Controls.Add(_lblHint, 0, 3);
            _root.SetColumnSpan(_lblHint, 2);
            _root.Controls.Add(_buttons, 0, 4);
            _root.SetColumnSpan(_buttons, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(620, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "模型属性";
            Controls.Add(_root);

            ResumeLayout(false);
        }

        private static void ConfigureLabel(AntdUI.Label label, string text)
        {
            label.Dock = DockStyle.Fill;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = UiStyle.Text;
            label.Margin = new Padding(0, 0, 8, 0);
        }
    }
}
