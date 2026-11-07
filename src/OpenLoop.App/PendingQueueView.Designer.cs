namespace OpenLoop.App
{
    partial class PendingQueueView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private TableLayoutPanel _header;
        private AntdUI.Label _lblCount;
        private AntdUI.Label _lblHint;
        private FlowLayoutPanel _list;
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
            _lblCount = new AntdUI.Label();
            _lblHint = new AntdUI.Label();
            _list = new FlowLayoutPanel();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 2;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.Margin = new Padding(0);

            _header.Dock = DockStyle.Fill;
            _header.ColumnCount = 2;
            _header.RowCount = 1;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _header.Margin = new Padding(0);

            _lblCount.AutoSize = true;
            _lblCount.Text = "待发送 0";
            _lblCount.Font = new Font("Segoe UI", 9F);
            _lblCount.ForeColor = UiStyle.Muted;
            _lblCount.TextAlign = ContentAlignment.MiddleLeft;
            _lblCount.Margin = new Padding(0);

            _lblHint.AutoSize = true;
            _lblHint.Text = "排队默认等本轮返回，点「立即」插到下一步";
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.MiddleRight;
            _lblHint.Margin = new Padding(0);

            _header.Controls.Add(_lblCount, 0, 0);
            _header.Controls.Add(_lblHint, 1, 0);

            _list.Dock = DockStyle.Fill;
            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoScroll = true;
            _list.Margin = new Padding(0);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_list, 0, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.SecondaryBack;
            Padding = new Padding(16, 4, 16, 6);
            Visible = false;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
