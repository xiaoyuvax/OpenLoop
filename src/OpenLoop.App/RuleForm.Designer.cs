namespace OpenLoop.App
{
    partial class RuleForm
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Label _lblName;
        private AntdUI.Input _txtName;
        private AntdUI.Label _lblRule;
        private AntdUI.Input _txtRule;
        private AntdUI.Label _lblHint;
        private FlowLayoutPanel _buttons;
        private AntdUI.Button _btnOptimize;
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
            _lblName = new AntdUI.Label();
            _txtName = new AntdUI.Input();
            _lblRule = new AntdUI.Label();
            _txtRule = new AntdUI.Input();
            _lblHint = new AntdUI.Label();
            _buttons = new FlowLayoutPanel();
            _btnOptimize = new AntdUI.Button();
            _btnCancel = new AntdUI.Button();
            _btnSave = new AntdUI.Button();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 4;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _root.BackColor = UiStyle.Surface;
            _root.Padding = new Padding(16, 12, 16, 8);

            ConfigureLabel(_lblName, "规则名");
            ConfigureLabel(_lblRule, "规则正文");

            _txtName.Name = "txtName";
            _txtName.Dock = DockStyle.Fill;
            _txtName.Radius = 8;
            _txtName.BorderWidth = 1;
            _txtName.PlaceholderText = "可选，例如：先读再写";
            _txtName.Margin = new Padding(0, 6, 0, 6);

            _txtRule.Name = "txtRule";
            _txtRule.Dock = DockStyle.Fill;
            _txtRule.Radius = 8;
            _txtRule.BorderWidth = 1;
            _txtRule.Multiline = true;
            _txtRule.PlaceholderText = "写给 Worker 的一条规则，例如：修改文件前先用 read_file 确认内容。";
            _txtRule.Margin = new Padding(0, 4, 0, 4);

            _lblHint.AutoSize = false;
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.Text = "登记后是休眠特征，下一轮演进可被启用；id 由名字 + 正文哈希生成，改字即新 id。";
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.MiddleLeft;
            _lblHint.Margin = new Padding(0, 8, 0, 0);

            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.RightToLeft;
            _buttons.WrapContents = false;
            _buttons.Margin = new Padding(0);

            _btnSave.Name = "btnSave";
            _btnSave.Text = "登记";
            _btnSave.Type = AntdUI.TTypeMini.Primary;
            _btnSave.Radius = 8;
            _btnSave.Size = new Size(92, 34);
            _btnSave.Margin = new Padding(8, 6, 0, 0);
            _btnSave.Click += OnSave;

            _btnOptimize.Name = "btnOptimize";
            _btnOptimize.Text = "AI 优化";
            _btnOptimize.Radius = 8;
            _btnOptimize.Size = new Size(100, 34);
            _btnOptimize.IconSvg = UiIcons.Of(UiIcons.Edit, UiStyle.Text);
            _btnOptimize.Margin = new Padding(8, 6, 0, 0);
            _btnOptimize.Click += OnOptimize;

            _btnCancel.Name = "btnCancel";
            _btnCancel.Text = "取消";
            _btnCancel.Radius = 8;
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.Margin = new Padding(8, 6, 0, 0);
            _btnCancel.Click += OnCancel;

            _buttons.Controls.Add(_btnSave);
            _buttons.Controls.Add(_btnCancel);
            _buttons.Controls.Add(_btnOptimize);

            _root.Controls.Add(_lblName, 0, 0);
            _root.Controls.Add(_txtName, 1, 0);
            _root.Controls.Add(_lblRule, 0, 1);
            _root.Controls.Add(_txtRule, 1, 1);
            _root.Controls.Add(_lblHint, 0, 2);
            _root.SetColumnSpan(_lblHint, 2);
            _root.Controls.Add(_buttons, 0, 3);
            _root.SetColumnSpan(_buttons, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            ClientSize = new Size(620, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "新增规则";
            Controls.Add(_root);

            ResumeLayout(false);
        }

        private static void ConfigureLabel(AntdUI.Label label, string text)
        {
            label.Dock = DockStyle.Fill;
            label.Text = text;
            label.TextAlign = ContentAlignment.TopLeft;
            label.ForeColor = UiStyle.Text;
            label.Margin = new Padding(0, 10, 8, 0);
        }
    }
}
