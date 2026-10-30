namespace OpenLoop.App
{
    partial class SessionOverview
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel _root;
        private Panel _left;
        private TableLayoutPanel _leftLayout;
        private TableLayoutPanel _leftHeader;
        private AntdUI.Label _lblProjects;
        private AntdUI.Button _btnAddProject;
        private AntdUI.Button _btnRemoveProject;
        private AntdUI.Menu _menu;
        private FlowLayoutPanel _leftFooter;
        private AntdUI.Button _btnSettings;
        private Panel _right;
        private TableLayoutPanel _rightLayout;
        private TableLayoutPanel _rightBar;
        private AntdUI.Input _txtSearch;
        private AntdUI.Button _btnDelete;
        private AntdUI.Button _btnNewSession;
        private AntdUI.Label _lblProjectPath;
        private AntdUI.Table _table;
        private AntdUI.Column _colState;
        private AntdUI.Column _colTitle;
        private AntdUI.Column _colStarted;
        private AntdUI.Column _colSteps;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _root = new TableLayoutPanel();
            _left = new Panel();
            _leftLayout = new TableLayoutPanel();
            _leftHeader = new TableLayoutPanel();
            _lblProjects = new AntdUI.Label();
            _btnAddProject = new AntdUI.Button();
            _btnRemoveProject = new AntdUI.Button();
            _menu = new AntdUI.Menu();
            _leftFooter = new FlowLayoutPanel();
            _btnSettings = new AntdUI.Button();
            _right = new Panel();
            _rightLayout = new TableLayoutPanel();
            _rightBar = new TableLayoutPanel();
            _txtSearch = new AntdUI.Input();
            _btnDelete = new AntdUI.Button();
            _btnNewSession = new AntdUI.Button();
            _lblProjectPath = new AntdUI.Label();
            _table = new AntdUI.Table();
            _colState = new AntdUI.Column("State", Localization.T("ColState"));
            _colTitle = new AntdUI.Column("Title", Localization.T("ColSession"));
            _colStarted = new AntdUI.Column("Started", Localization.T("ColStarted"));
            _colSteps = new AntdUI.Column("StepsText", Localization.T("ColSteps"));

            SuspendLayout();

            // root: 项目栏 / 会话区
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 1;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.BackColor = UiStyle.Page;
            _root.Padding = new Padding(0);

            // left
            _left.Dock = DockStyle.Fill;
            _left.BackColor = UiStyle.Surface;
            _left.Padding = new Padding(10, 12, 10, 10);

            _leftLayout.Dock = DockStyle.Fill;
            _leftLayout.ColumnCount = 1;
            _leftLayout.RowCount = 3;
            _leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            _leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            _leftHeader.Dock = DockStyle.Fill;
            _leftHeader.ColumnCount = 3;
            _leftHeader.RowCount = 1;
            _leftHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _leftHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            _leftHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            _leftHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _leftHeader.Margin = new Padding(0);

            _lblProjects.Dock = DockStyle.Fill;
            _lblProjects.Text = Localization.T("Projects");
            _lblProjects.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblProjects.ForeColor = UiStyle.Muted;
            _lblProjects.TextAlign = ContentAlignment.MiddleLeft;
            _lblProjects.Margin = new Padding(6, 0, 0, 0);

            _btnAddProject.Name = "btnAddProject";
            _btnAddProject.Dock = DockStyle.Fill;
            _btnAddProject.Ghost = true;
            _btnAddProject.Radius = 6;
            _btnAddProject.IconSvg = UiIcons.Of(UiIcons.Plus, UiStyle.Text);
            _btnAddProject.Margin = new Padding(0, 2, 0, 2);
            _btnAddProject.Click += OnAddProject;

            _btnRemoveProject.Name = "btnRemoveProject";
            _btnRemoveProject.Dock = DockStyle.Fill;
            _btnRemoveProject.Ghost = true;
            _btnRemoveProject.Radius = 6;
            _btnRemoveProject.IconSvg = UiIcons.Of(UiIcons.Trash, UiStyle.Muted);
            _btnRemoveProject.Margin = new Padding(0, 2, 0, 2);
            _btnRemoveProject.Click += OnRemoveProject;

            _leftHeader.Controls.Add(_lblProjects, 0, 0);
            _leftHeader.Controls.Add(_btnAddProject, 1, 0);
            _leftHeader.Controls.Add(_btnRemoveProject, 2, 0);

            _menu.Name = "menuProjects";
            _menu.Dock = DockStyle.Fill;
            _menu.Mode = AntdUI.TMenuMode.Vertical;
            _menu.Indent = false;
            _menu.Gap = 2;
            _menu.itemMargin = 8;
            _menu.BackColor = UiStyle.Surface;
            _menu.Margin = new Padding(0, 6, 0, 0);
            _menu.SelectChanged += OnProjectSelect;

            _leftFooter.Dock = DockStyle.Fill;
            _leftFooter.FlowDirection = FlowDirection.TopDown;
            _leftFooter.WrapContents = false;
            _leftFooter.Margin = new Padding(0);

            _btnSettings.Name = "btnSettings";
            _btnSettings.Text = Localization.T("Settings");
            _btnSettings.Ghost = true;
            _btnSettings.Radius = 8;
            _btnSettings.Height = 34;
            _btnSettings.Width = 250;
            _btnSettings.IconSvg = UiIcons.Of(UiIcons.Settings, UiStyle.Muted);
            _btnSettings.ForeColor = UiStyle.Muted;
            _btnSettings.Margin = new Padding(0, 0, 0, 2);
            _btnSettings.Click += OnSettings;

            _leftFooter.Controls.Add(_btnSettings);

            _leftLayout.Controls.Add(_leftHeader, 0, 0);
            _leftLayout.Controls.Add(_menu, 0, 1);
            _leftLayout.Controls.Add(_leftFooter, 0, 2);
            _left.Controls.Add(_leftLayout);

            // right
            _right.Dock = DockStyle.Fill;
            _right.BackColor = UiStyle.Page;
            _right.Padding = new Padding(24, 14, 0, 0);

            _rightLayout.Dock = DockStyle.Fill;
            _rightLayout.ColumnCount = 1;
            _rightLayout.RowCount = 3;
            _rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _rightBar.Dock = DockStyle.Fill;
            _rightBar.ColumnCount = 4;
            _rightBar.RowCount = 1;
            _rightBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _rightBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F));
            _rightBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            _rightBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            _rightBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _rightBar.Margin = new Padding(0, 0, 0, 8);

            _txtSearch.Name = "txtSearch";
            _txtSearch.Dock = DockStyle.Fill;
            _txtSearch.Radius = 8;
            _txtSearch.PlaceholderText = Localization.T("SearchSessions");
            _txtSearch.PrefixSvg = UiIcons.Of(UiIcons.Search, UiStyle.Muted);
            _txtSearch.Margin = new Padding(0, 9, 12, 9);
            _txtSearch.TextChanged += OnSearchChanged;

            _btnDelete.Name = "btnDelete";
            _btnDelete.Text = "删除选中";
            _btnDelete.Ghost = true;
            _btnDelete.Type = AntdUI.TTypeMini.Error;
            _btnDelete.Radius = 8;
            _btnDelete.Dock = DockStyle.Fill;
            _btnDelete.IconSvg = UiIcons.Of(UiIcons.Trash, UiStyle.Error);
            _btnDelete.Margin = new Padding(0, 9, 12, 9);
            _btnDelete.Click += OnDelete;

            _btnNewSession.Name = "btnNewSession";
            _btnNewSession.Text = Localization.T("NewSession");
            _btnNewSession.Type = AntdUI.TTypeMini.Primary;
            _btnNewSession.Radius = 8;
            _btnNewSession.Dock = DockStyle.Fill;
            _btnNewSession.IconSvg = UiIcons.Of(UiIcons.Edit, Color.White);
            _btnNewSession.Margin = new Padding(0, 9, 0, 9);
            _btnNewSession.Click += OnNewSession;

            _rightBar.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);
            _rightBar.Controls.Add(_txtSearch, 1, 0);
            _rightBar.Controls.Add(_btnDelete, 2, 0);
            _rightBar.Controls.Add(_btnNewSession, 3, 0);

            _table.Name = "tableSessions";
            _table.Dock = DockStyle.Fill;
            _table.Bordered = false;
            _table.MultipleRows = true;
            _table.VisibleHeader = true;
            _table.RowHeight = 46;
            _table.EmptyText = Localization.T("EmptySessions");
            _table.Margin = new Padding(0);
            _table.CellClick += OnCellClick;
            _table.Resize += OnTableResize;

            _colTitle.Ellipsis = true;
            _table.Columns.Add(_colState);
            _table.Columns.Add(_colTitle);
            _table.Columns.Add(_colStarted);
            _table.Columns.Add(_colSteps);

            _lblProjectPath.Name = "lblProjectPath";
            _lblProjectPath.Dock = DockStyle.Fill;
            _lblProjectPath.Text = "";
            _lblProjectPath.Font = new Font("Segoe UI", 8.5F);
            _lblProjectPath.ForeColor = UiStyle.Muted;
            _lblProjectPath.PrefixSvg = UiIcons.Of(UiIcons.Folder, UiStyle.Muted);
            _lblProjectPath.TextAlign = ContentAlignment.MiddleLeft;
            _lblProjectPath.AutoEllipsis = true;
            _lblProjectPath.IconGap = 6;
            _lblProjectPath.Margin = new Padding(0, 0, 0, 8);

            _rightLayout.Controls.Add(_rightBar, 0, 0);
            _rightLayout.Controls.Add(_lblProjectPath, 0, 1);
            _rightLayout.Controls.Add(_table, 0, 2);
            _right.Controls.Add(_rightLayout);

            _root.Controls.Add(_left, 0, 0);
            _root.Controls.Add(_right, 1, 0);

            // overview
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Controls.Add(_root);
            Load += OnOverviewLoad;
            ResumeLayout(false);
        }
    }
}
