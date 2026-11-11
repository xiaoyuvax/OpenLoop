using System.Drawing;
using OpenLoop.Service.Source;

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

        /// <summary>项目列表底部的固定项 [Loop]：列出全部项目的 Loop 会话。</summary>
        internal const string LoopTag = "\u0001loop";

        public SessionOverview()
        {
            InitializeComponent();
        }

        internal event Action<Session> OpenRequested;

        /// <summary>(工作区, Loop 会话 id)；id 为空 = 新建一个新会话。</summary>
        internal event Action<string, string> LoopRequested;
        internal event Action<string> NewSessionRequested;
        internal event Action<IReadOnlyList<Session>> DeleteRequested;
        internal event Action<string> RemoveProjectRequested;
        internal event Action SettingsRequested;

        internal void Initialize()
        {
            if (_ready) return;
            _ready = true;
            _colState.Render = (value, row, index) => Badge(row as Session);
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
            _projects.AddRange(Paths.Dedup(projects));
            RebuildProjects();
        }

        internal void FocusProject(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;
            if (!_all.Any(s => Paths.Same(s.Workspace, workspace))) return;
            _project = workspace;
            RebuildProjects();
        }

        private void RebuildProjects()
        {
            var projects = Paths.Dedup(_projects.Concat(_all.Select(s => s.Workspace)))
                .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!projects.Any(p => Paths.Same(p, _project)) && _project != LoopTag)
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

                // 固定项 [Loop] 不在列表里：它是控件底部的按钮，固定在设置按钮上方
                var selected = _menu.Items.FirstOrDefault(i =>
                    i.Tag is string tag && Paths.Same(tag, _project));
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

            if (_project == LoopTag)
            {
                foreach (var loop in LoopIndex.List()
                             .Where(l => q.Length == 0 ||
                                         ProjectName(l.Workspace).Contains(q, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(l => l.UpdatedAt))
                {
                    string name = ProjectName(loop.Workspace);
                    _rows.Add(new Session
                    {
                        Id = "loop:" + loop.SessionId,
                        Workspace = loop.Workspace,
                        CustomTitle = "[Loop] " + name,
                        StartedAt = loop.UpdatedAt.LocalDateTime
                    });
                }
            }
            else
            {
                _rows.AddRange(_all
                    .Where(s => Paths.Same(s.Workspace, _project))
                    .Where(s => q.Length == 0 || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.StartedAt));
            }

            bool loops = _project == LoopTag;
            _lblProjectPath.Text = loops ? "Loop 会话（全部项目）" : _project;
            _table.DataSource = null;
            _table.DataSource = _rows;
            // [Loop] 页也能开会话：没有项目可依附，工作区用安装目录
            _btnNewSession.Enabled = loops || !string.IsNullOrEmpty(_project);
            _btnRemoveProject.Enabled = !loops && !string.IsNullOrEmpty(_project);
        }

        /// <summary>勾选（复选列 / Ctrl+Shift 多选）后删除；没勾选什么都不做。</summary>
        private void OnDelete(object sender, EventArgs e)
        {
            var picked = _rows.Where(r => r.Selected)
                .Concat((_table.SelectedIndexs ?? [])
                    .Where(i => i >= 1 && i <= _rows.Count)
                    .Select(i => _rows[i - 1]))
                .Distinct()
                .ToList();
            if (picked.Count == 0) return;
            DeleteRequested?.Invoke(picked);
            foreach (var row in _rows) row.Selected = false;
            _table.SelectedIndexs = [];
            RefreshRows();
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
            int fixedDip = 48 + 96 + 150 + 70;
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

        private void OnLoopOverview(object sender, EventArgs e) => SelectLoop();

        private void OnRemoveProject(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_project)) return;
            RemoveProjectRequested?.Invoke(_project);
        }

        private void OnNewSession(object sender, EventArgs e)
        {
            // [Loop] 页没有项目：新建的 Loop 会话工作区取安装目录；空 id = 开一个新的会话，不续用同工作区旧对话
            if (_project == LoopTag) LoopRequested?.Invoke(SourceSync.InstallRoot, "");
            else NewSessionRequested?.Invoke(_project);
        }

        private void OnSettings(object sender, EventArgs e) => SettingsRequested?.Invoke();

        private void OnCellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            if (e?.Record is not Session session) return;
            // 复选列只勾选，Ctrl/Shift 多选也不打开会话（AntdUI 在触发 CellClick 前已经处理完选择）
            if (e.Column is AntdUI.ColumnCheck) return;
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0) return;
            if (session.Id.StartsWith("loop:", StringComparison.Ordinal))
                LoopRequested?.Invoke(session.Workspace, session.Id["loop:".Length..]);
            else OpenRequested?.Invoke(session);
        }

        // ---- Puppet 检查面：只读 + 选中，不改业务语义 ----

        internal string[] MenuItems() => _menu.Items.Select(i => i.Text ?? "").ToArray();

        internal string Selection => _project == LoopTag ? "[Loop]" : _project;

        internal string[] RowTitles() => _rows.Select(r => r.Title).ToArray();

        internal string[] CheckedRows() => _rows.Where(r => r.Selected).Select(r => r.Title).ToArray();

        internal bool SelectLoop()
        {
            _project = LoopTag;
            _building = true;
            try
            {
                _menu.USelect();
            }
            finally
            {
                _building = false;
            }
            RefreshRows();
            return _project == LoopTag;
        }

        internal string LoopEntryText => _btnLoop.Text;
    }
}
