namespace OpenLoop.App
{
    partial class PuppetActionView
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private AntdUI.Input _txtFilter;
        private AntdUI.Button _btnRefresh;
        private AntdUI.Table _table;
        private AntdUI.Label _lblHint;
        private AntdUI.Label _lblUsage;

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
            _lblUsage = new AntdUI.Label();

            SuspendLayout();

            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 4;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.Margin = new Padding(0);

            _txtFilter.Name = "txtPuppetActionFilter";
            _txtFilter.Dock = DockStyle.Fill;
            _txtFilter.Radius = 8;
            _txtFilter.BorderWidth = 1;
            _txtFilter.PrefixSvg = UiIcons.Of(UiIcons.Search, UiStyle.Muted);
            _txtFilter.PlaceholderText = Localization.T("PuppetFilterPlaceholder");
            _txtFilter.Margin = new Padding(0, 0, 8, 6);

            // AntdUI.Button 在 TableLayoutPanel 里必须给高度，否则算成 0（控件树里有、画面上没有）
            _btnRefresh.Name = "btnPuppetActionRefresh";
            _btnRefresh.Text = Localization.T("Refresh");
            _btnRefresh.Radius = 8;
            _btnRefresh.Size = new Size(72, 28);
            _btnRefresh.Margin = new Padding(0, 0, 0, 6);

            _table.Name = "tblPuppetActions";
            _table.Dock = DockStyle.Fill;
            _table.Font = new Font("Microsoft YaHei UI", 8F);   // 与思考区同一字号（ChatFont 9pt - 1）
            _table.Bordered = false;
            _table.VisibleHeader = true;
            _table.RowHeight = 36;
            _table.MultipleRows = false;
            _table.ShowTip = true;
            _table.EmptyText = Localization.T("PuppetEmpty");
            _table.Margin = new Padding(0);
            _table.Columns.Add(new AntdUI.Column("Name", "Action") { Width = "118" });
            _table.Columns.Add(new AntdUI.Column("ParamText", Localization.T("ColParams")) { Width = "104" });
            _table.Columns.Add(new AntdUI.Column("Desc", Localization.T("ColDescription")));

            _lblHint.Name = "lblPuppetActionHint";
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.AutoEllipsis = true;
            _lblHint.Font = new Font("Segoe UI", 8.5F);
            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.TextAlign = ContentAlignment.MiddleLeft;
            _lblHint.Margin = new Padding(0, 6, 0, 0);
            _lblHint.Text = Localization.T("PuppetListTitle");

            _lblUsage.Name = "lblPuppetActionUsage";
            _lblUsage.Dock = DockStyle.Fill;
            _lblUsage.Font = new Font("Segoe UI", 8.5F);
            _lblUsage.ForeColor = UiStyle.Muted;
            _lblUsage.TextAlign = ContentAlignment.TopLeft;
            _lblUsage.Margin = new Padding(0, 4, 0, 0);
            _lblUsage.Text = Localization.T("PuppetUsageShort");

            _root.Controls.Add(_txtFilter, 0, 0);
            _root.Controls.Add(_btnRefresh, 1, 0);
            _root.Controls.Add(_table, 0, 1);
            _root.SetColumnSpan(_table, 2);
            _root.Controls.Add(_lblHint, 0, 2);
            _root.SetColumnSpan(_lblHint, 2);
            _root.Controls.Add(_lblUsage, 0, 3);
            _root.SetColumnSpan(_lblUsage, 2);

            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Surface;
            Controls.Add(_root);
            ResumeLayout(false);
        }
    }
}
