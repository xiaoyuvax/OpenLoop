using System.Drawing;

namespace OpenLoop.App
{
    /// <summary>
    /// Tab0：项目 + 会话。左侧项目即工作区路径，选中项目后右侧只列该项目的会话。
    /// 新建会话入口只在这里出现：必须先有项目（路径）才能开会话。
    /// </summary>
    public partial class SessionOverview : UserControl
    {
        private static readonly Color[] AvatarColors =
        {
            Color.FromArgb(0x16, 0x77, 0xFF), Color.FromArgb(0x52, 0xC4, 0x1A),
            Color.FromArgb(0xFA, 0x8C, 0x16), Color.FromArgb(0x13, 0xC2, 0xC2),
            Color.FromArgb(0x72, 0x2E, 0xD1), Color.FromArgb(0xEB, 0x2F, 0x96),
            Color.FromArgb(0x2F, 0x54, 0xEB), Color.FromArgb(0xFA, 0xAD, 0x14)
        };

        private readonly List<Session> _all = new();
        private readonly List<string> _projects = new();
        private readonly List<Session> _rows = new();
        private string _project = "";
        private bool _ready;
        private bool _building;

        public SessionOverview()
        {
            InitializeComponent();
        }

        internal event Action<Session> OpenRequested;
        internal event Action<string> NewSessionRequested;
        internal event Action SettingsRequested;

        internal void Initialize()
        {
            if (_ready) return;
            _ready = true;
            _colState.Render = (row, value, index) => Badge(row as Session);
            _table.RowHeight = 44;
            SizeColumns();
            _table.DataSource = _rows;
            RebuildProjects();
        }

        internal void RefreshSessions(IEnumerable<Session> sessions, IEnumerable<string> projects)
        {
            if (!_ready) Initialize();

            _all.Clear();
            _all.AddRange(sessions.OrderByDescending(s => s.StartedAt));

            _projects.Clear();
            _projects.AddRange((projects ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            RebuildProjects();
        }

        internal void FocusProject(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            if (!_all.Any(s => string.Equals(s.Workspace, workspace, StringComparison.OrdinalIgnoreCase))) return;
            _project = workspace;
            RebuildProjects();
        }

        private void RebuildProjects()
        {
            var projects = _projects
                .Concat(_all.Select(s => s.Workspace))
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!projects.Contains(_project, StringComparer.OrdinalIgnoreCase))
                _project = projects.FirstOrDefault() ?? "";

            _building = true;
            try
            {
                _menu.Items.Clear();
                foreach (var path in projects)
                {
                    string name = ProjectName(path);
                    var item = new AntdUI.MenuItem(name, UiIcons.Avatar(name, AvatarColor(path), 28))
                    {
                        Tag = path
                    };
                    _menu.Items.Add(item);
                }

                var selected = _menu.Items.FirstOrDefault(i => string.Equals(i.Tag as string, _project, StringComparison.OrdinalIgnoreCase));
                if (selected != null) _menu.Select(selected, true);
            }
            finally
            {
                _building = false;
            }

            RefreshRows();
        }

        private void RefreshRows()
        {
            string q = _txtSearch.Text?.Trim() ?? "";
            _rows.Clear();
            _rows.AddRange(_all
                .Where(s => string.Equals(s.Workspace, _project, StringComparison.OrdinalIgnoreCase))
                .Where(s => q.Length == 0 || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.StartedAt));

            _lblProjectPath.Text = _project;
            _table.DataSource = null;
            _table.DataSource = _rows;
            _btnNewSession.Enabled = !string.IsNullOrEmpty(_project);
        }

        private static string ProjectName(string path)
        {
            try
            {
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch
            {
                return path;
            }
        }

        private static Color AvatarColor(string seed)
        {
            int hash = 17;
            foreach (char c in seed ?? "") hash = hash * 31 + char.ToLowerInvariant(c);
            return AvatarColors[Math.Abs(hash) % AvatarColors.Length];
        }

        private static AntdUI.CellBadge Badge(Session session) => session?.State switch
        {
            SessionState.Running => new AntdUI.CellBadge(AntdUI.TState.Success, Localization.T("StateRunning")),
            SessionState.Interrupted => new AntdUI.CellBadge(AntdUI.TState.Error, Localization.T("StateInterrupted")),
            SessionState.Done => new AntdUI.CellBadge(AntdUI.TState.Default, Localization.T("StateDone")),
            _ => new AntdUI.CellBadge(AntdUI.TState.Default, Localization.T("StateIdle"))
        };

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void SizeColumns()
        {
            float scale = DeviceDpi / 96f;
            int fixedDip = 96 + 150 + 70;
            int titleDip = (int)(_table.ClientSize.Width / scale) - fixedDip - 12;
            _colState.Width = "96";
            _colStarted.Width = "150";
            _colSteps.Width = "70";
            _colTitle.Width = Math.Max(240, titleDip).ToString();
        }

        private void OnTableResize(object sender, EventArgs e) => SizeColumns();

        private void OnOverviewLoad(object sender, EventArgs e) => Initialize();

        private void OnProjectSelect(object sender, AntdUI.MenuSelectEventArgs e)
        {
            if (_building || e?.Value == null) return;
            _project = e.Value.Tag as string ?? "";
            RefreshRows();
        }

        private void OnSearchChanged(object sender, EventArgs e) => RefreshRows();

        private void OnAddProject(object sender, EventArgs e) => NewSessionRequested?.Invoke("");

        private void OnNewSession(object sender, EventArgs e) => NewSessionRequested?.Invoke(_project);

        private void OnSettings(object sender, EventArgs e) => SettingsRequested?.Invoke();

        private void OnCellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            if (e?.Record is Session session) OpenRequested?.Invoke(session);
        }
    }
}
