namespace OpenLoop.App
{
    partial class LoopAppView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Input _txtFilter;
        private AntdUI.Button _btnRefresh;
        private AntdUI.Table _table;
        private AntdUI.Label _lblHint;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _txtFilter = new AntdUI.Input();
            _btnRefresh = new AntdUI.Button();
            _table = new AntdUI.Table();
            _lblHint = new AntdUI.Label();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 3;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.Margin = new Padding(0);

            _txtFilter.Name = "txtLoopAppFilter";
            _txtFilter.Dock = DockStyle.Fill;
            _txtFilter.Radius = 8;
            _txtFilter.BorderWidth = 1;
            _txtFilter.PrefixSvg = UiIcons.Of(UiIcons.Search, UiStyle.Muted);
            _txtFilter.PlaceholderText = Localization.T("LoopAppFilterPlaceholder");
            _txtFilter.Margin = new Padding(0, 0, 8, 6);

            // AntdUI.Button 在 TableLayoutPanel 里必须给高度，否则算成 0（控件树里有、画面上没有）
            _btnRefresh.Name = "btnLoopAppRefresh";
            _btnRefresh.Text = Localization.T("Refresh");
            _btnRefresh.Radius = 8;
            _btnRefresh.Size = new Size(72, 28);
            _btnRefresh.Margin = new Padding(0, 0, 0, 6);

            _table.Name = "tblLoopApps";
            _table.Dock = DockStyle.Fill;
            _table.Font = new Font("Microsoft YaHei UI", 8F);   // 与 Puppet 页表格同一字号
            _table.Bordered = false;
            _table.VisibleHeader = true;
            _table.RowHeight = 36;
            _table.MultipleRows = false;
            _table.ShowTip = true;
            _table.EmptyText = Localization.T("LoopAppEmpty");
            _table.Margin = new Padding(0);
            _table.Columns.Add(new AntdUI.Column("Name", Localization.T("Name")) { Width = "110" });
            _table.Columns.Add(new AntdUI.Column("Usage", Localization.T("LoopAppUsageLabel")) { Width = "120" });
            _table.Columns.Add(new AntdUI.Column("Path", Localization.T("LoopAppColPath")));

            _lblHint.Name = "lblLoopAppHint";
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.AutoEllipsis = true;
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.MiddleLeft;
            _lblHint.Margin = new Padding(0, 6, 0, 0);
            _lblHint.Text = Localization.T("LoopAppPageHint");

            _root.Controls.Add(_txtFilter, 0, 0);
            _root.Controls.Add(_btnRefresh, 1, 0);
            _root.Controls.Add(_table, 0, 1);
            _root.SetColumnSpan(_table, 2);
            _root.Controls.Add(_lblHint, 0, 2);
            _root.SetColumnSpan(_lblHint, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
