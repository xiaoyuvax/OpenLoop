namespace OpenLoop.App
{
    partial class FilePreviewView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Label _lblPath;
        private Panel _host;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _lblPath = new AntdUI.Label();
            _host = new Panel();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 2;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.Margin = new Padding(0);

            _lblPath.Name = "lblFilePath";
            _lblPath.Dock = DockStyle.Fill;
            _lblPath.AutoEllipsis = true;
            _lblPath.Font = new Font("Segoe UI", 8.5F);
            _lblPath.ForeColor = UiStyle.Muted;
            _lblPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblPath.IconGap = 6;
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblPath.Margin = new Padding(0);

            _host.Name = "fileHost";
            _host.Dock = DockStyle.Fill;
            _host.Margin = new Padding(0);

            _root.Controls.Add(_lblPath, 0, 0);
            _root.Controls.Add(_host, 0, 1);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
