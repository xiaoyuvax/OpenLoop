namespace OpenLoop.App
{
    partial class SideWorkView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private Panel _header;
        private AntdUI.Label _lblTitle;
        private AntdUI.Label _lblStatus;
        private Panel _line;
        private ChatFlow.ChatFlowControl _chat;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _header = new Panel();
            _lblTitle = new AntdUI.Label();
            _lblStatus = new AntdUI.Label();
            _line = new Panel();
            _chat = new ChatFlow.ChatFlowControl();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Surface;

            _header.Dock = DockStyle.Fill;
            _header.BackColor = UiStyle.Surface;

            _lblTitle.Dock = DockStyle.Left;
            _lblTitle.Width = 60;
            _lblTitle.Text = "旁路";
            _lblTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblTitle.ForeColor = UiStyle.Text;
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;

            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Text = "只读";
            _lblStatus.Font = new Font("Segoe UI", 8.5F);
            _lblStatus.ForeColor = UiStyle.Muted;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblStatus.AutoEllipsis = true;

            _header.Controls.Add(_lblStatus);
            _header.Controls.Add(_lblTitle);

            _line.Dock = DockStyle.Fill;
            _line.BackColor = UiStyle.Border;
            _line.Margin = new Padding(0);

            _chat.Dock = DockStyle.Fill;
            _chat.BackColor = UiStyle.Surface;
            _chat.ChatFont = new Font("Microsoft YaHei UI", 9F);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_line, 0, 1);
            _root.Controls.Add(_chat, 0, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
