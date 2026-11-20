using System.Diagnostics;
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

        /// <summary>上一次真正重建的项目菜单与表格行；相同就不动控件（切标签会连续触发两次刷新）。</summary>
        private List<string> _builtProjects = [];
        private readonly List<(string Id, SessionState State, string Title, int Steps)> _shownRows = [];

        /// <summary>搜索框防抖：每敲一个字就整表重置重绘代价太大。</summary>
        private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 250 };

        /// <summary>[Puppet] 页每 3 秒刷新一次：留言板上的会话由各 agent 随时加入/退出。</summary>
        private readonly System.Windows.Forms.Timer _puppetRefresh = new() { Interval = 3000 };

        /// <summary>项目列表底部的固定项 [Loop]：列出全部项目的 Loop 会话。</summary>
        internal const string LoopTag = "\u0001loop";

        /// <summary>固定项 [Puppet]：列出 @A / @I 留言板上的会话（没有主 agent 驱动）。</summary>
        internal const string PuppetTag = "\u0001puppet";

        public SessionOverview()
        {
            InitializeComponent();
            _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); RefreshRows(); };
            _puppetRefresh.Tick += (_, _) =>
            {
                if (_project == PuppetTag) RefreshRows();
                else _puppetRefresh.Stop();
            };
            Disposed += (_, _) => { _searchDelay.Dispose(); _puppetRefresh.Dispose(); };
        }

        internal event Action<Session> OpenRequested;

        /// <summary>(工作区, Loop 会话 id)；id 为空 = 新建一个新会话。</summary>
        internal event Action<string, string> LoopRequested;

        /// <summary>[Loop] 行的运行状态来自本进程里还活着的 Loop 会话（关掉标签页也在跑）。</summary>
        internal Func<string, string, SessionState> LoopState;

        /// <summary>(宿主 @A/@I, 会话 id)：在 [Puppet] 页点开会话。</summary>
        internal event Action<string, string> PuppetRequested;
        internal event Action<string> NewSessionRequested;
        internal event Action<IReadOnlyList<Session>> DeleteRequested;
        internal event Action<string> RemoveProjectRequested;
        internal event Action SettingsRequested;

        internal void Initialize()
        {
            if (_ready) return;
            _ready = true;
            _colState.Render = (value, row, index) => Badge(row as Session);
            // 留言板会话的标题用青色 + 列表图标标出来，和 Worker/Loop 行区分
            _colTitle.Render = (value, row, index) => row is Session { IsPuppet: true }
                ? PuppetTitle(value as string)
                : value;
            _table.RowHeight = 44;
            SizeColumns();
            _table.DataSource = _rows;
            RebuildProjects();
        }

        internal void RefreshSessions(IEnumerable<Session> sessions, IEnumerable<string> projects)
        {
            if (!_ready) Initialize();

            _all.Clear();
            _all.AddRange(sessions.OrderByDescending(s => s.LastActiveAt));

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

            if (!projects.Any(p => Paths.Same(p, _project)) && _project != LoopTag && _project != PuppetTag)
                _project = projects.FirstOrDefault() ?? "";

            if (!projects.SequenceEqual(_builtProjects, StringComparer.OrdinalIgnoreCase))
            {
                _builtProjects = projects;
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
                }
                finally
                {
                    _building = false;
                }
            }

            _building = true;
            try
            {
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

            if (_project == PuppetTag)
            {
                foreach (var s in OpenLoop.Service.Web.PuppetBoard.Sessions())
                {
                    string title = string.IsNullOrWhiteSpace(s.LastText) ? "" : OneLine(s.LastText);
                    if (q.Length > 0 && !(s.Host + " " + title).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                    _rows.Add(new Session
                    {
                        Id = "puppet:" + s.Host + "|" + s.Id,
                        Workspace = s.Host,
                        IsPuppet = true,
                        CustomTitle = title,
                        StartedAt = s.LastAt,
                        LastActiveAt = s.LastAt,
                        State = s.Live ? SessionState.Running : SessionState.Idle,
                        Steps = s.Steps?.Length ?? 0
                    });
                }
            }
            else if (_project == LoopTag)
            {
                foreach (var loop in LoopIndex.List()
                             .Where(l => q.Length == 0 ||
                                         ProjectName(l.Workspace).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         (l.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(l => l.UpdatedAt))
                {
                    _rows.Add(new Session
                    {
                        Id = "loop:" + loop.SessionId,
                        Workspace = loop.Workspace,
                        IsLoop = true,
                        CustomTitle = loop.Title,
                        StartedAt = loop.UpdatedAt.LocalDateTime,
                        LastActiveAt = loop.UpdatedAt.LocalDateTime,
                        State = LoopState?.Invoke(loop.Workspace, loop.SessionId) ?? SessionState.Idle
                    });
                }
            }
            else
            {
                _rows.AddRange(_all
                    .Where(s => Paths.Same(s.Workspace, _project))
                    .Where(s => q.Length == 0 || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.LastActiveAt));
            }

            bool loops = _project == LoopTag;
            bool puppets = _project == PuppetTag;
            _lblProjectPath.Text = loops
                ? "Loop 会话（全部项目）· 关于OpenLoop自身迭代和演进的所有会话"
                : puppets
                    ? "Puppet 是 OpenLoop 各组件内嵌自调试机制。OpenLoop 的主驱动服务：A(Worker) 和 I(Loop) 两个实例各有一个 Puppet 实例，均允许 Agent 连接交流。Puppet 留言板 · 各会话的 Agent 在同一块板上自行对话（默认 @A，A 不可达时转 @I），没有主 Agent 驱动"
                    : _project;
            // 行没变就别重置数据源：整表重绘还会把滚动位置拉回顶部
            bool same = _shownRows.Count == _rows.Count;
            if (same)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    var shown = _shownRows[i];
                    if (shown.Id != row.Id || shown.State != row.State || shown.Steps != row.Steps
                        || !string.Equals(shown.Title, row.Title, StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }
            }
            if (!same)
            {
                _shownRows.Clear();
                foreach (var row in _rows) _shownRows.Add((row.Id, row.State, row.Title, row.Steps));
                _table.DataSource = null;
                _table.DataSource = _rows;
            }
            // [Loop] 页也能开会话：没有项目可依附，工作区用安装目录；[Puppet] 页的会话由 agent 自己出现，不能新建
            _btnNewSession.Enabled = loops || (!puppets && !string.IsNullOrEmpty(_project));
            _btnRemoveProject.Enabled = !loops && !puppets && !string.IsNullOrEmpty(_project);
            // 留言板会话不是本地会话，删除要由各自的 agent 收场，这里不给删
            _btnDelete.Enabled = !puppets;
            // [Puppet] 页每 3 秒刷新一次：代理会话由各 agent 随时加入/退出
            if (puppets)
            {
                if (!_puppetRefresh.Enabled) _puppetRefresh.Start();
            }
            else _puppetRefresh.Stop();
        }

        /// <summary>勾选（复选列 / Ctrl+Shift 多选）后删除；没勾选什么都不做。</summary>
        private void OnDelete(object sender, EventArgs e)
        {
            if (_project == PuppetTag) return;
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

        /// <summary>留言摘要压成一行：换行/连续空白折成空格，超长截断。</summary>
        private static string OneLine(string text)
        {
            string joined = string.Join(" ", (text ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return joined.Length <= 80 ? joined : joined[..80] + "…";
        }
        private static Color AvatarColor(string seed)
        {
            int hash = 17;
            foreach (char c in seed ?? "") hash = hash * 31 + char.ToLowerInvariant(c);
            return AvatarColors[Math.Abs(hash) % AvatarColors.Length];
        }

        private static AntdUI.CellBadge Badge(Session session) => session?.State switch
        {
            SessionState.Running => new AntdUI.CellBadge(AntdUI.TState.Processing),
            SessionState.Interrupted => new AntdUI.CellBadge(AntdUI.TState.Error),
            SessionState.Done => new AntdUI.CellBadge(AntdUI.TState.Success),
            _ => new AntdUI.CellBadge(AntdUI.TState.Default)
        };

        /// <summary>[Puppet] 行标题：青色文字 + 列表图标（与 [Puppet] 页图标一致）。</summary>
        private static AntdUI.CellButton PuppetTitle(string title) => new(title ?? "")
        {
            IconSvg = UiIcons.Of(UiIcons.List, UiStyle.Puppet),
            IconGap = 6,
            Fore = UiStyle.Puppet,
            Type = AntdUI.TTypeMini.Default,
            Ghost = true,
            BorderWidth = 0,
            Radius = 8
        };

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void SizeColumns()
        {
            float scale = DeviceDpi / 96f;
            int fixedDip = 48 + 56 + 150 + 70;
            int titleDip = (int)(_table.ClientSize.Width / scale) - fixedDip - 12;
            _colState.Width = "56";
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

        private void OnSearchChanged(object sender, EventArgs e)
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        }

        private void OnAddProject(object sender, EventArgs e) => NewSessionRequested?.Invoke("");

        private void OnLoopOverview(object sender, EventArgs e) => SelectLoop();

        private void OnPuppetOverview(object sender, EventArgs e) => SelectPuppet();

        /// <summary>项目行右键：打开工作区路径 / 复制路径 / 新建会话 / 删除项目。</summary>
        private void OnProjectItemClick(object sender, AntdUI.MenuItemEventArgs e)
        {
            if (e?.Button != MouseButtons.Right || e.Item?.Tag is not string path) return;

            var items = new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem("打开工作区路径") { ID = "open" },
                new AntdUI.ContextMenuStripItem("复制路径") { ID = "copy" },
                new AntdUI.ContextMenuStripItem("新建会话") { ID = "new" },
                new AntdUI.ContextMenuStripItemDivider(),
                new AntdUI.ContextMenuStripItem("删除项目") { ID = "delete" }
            };
            AntdUI.ContextMenuStrip.open(_menu, selected => OnProjectMenu(path, selected?.ID), items);
        }

        private void OnProjectMenu(string path, string id)
        {
            switch (id)
            {
                case "open":
                    if (!Directory.Exists(path)) { AppLog.Warn("打开项目目录失败：" + path); return; }
                    try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                    catch (Exception ex) { AppLog.Warn("打开项目目录失败：" + ex.Message); }
                    break;
                case "copy":
                    try { Clipboard.SetText(path); } catch (Exception ex) { AppLog.Warn("复制路径失败：" + ex.Message); }
                    break;
                case "new": NewSessionRequested?.Invoke(path); break;
                case "delete": RemoveProjectRequested?.Invoke(path); break;
            }
        }

        private void OnRemoveProject(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_project)) return;
            RemoveProjectRequested?.Invoke(_project);
        }

        private void OnNewSession(object sender, EventArgs e)
        {
            if (_project == PuppetTag) return;
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
            if (session.Id.StartsWith("puppet:", StringComparison.Ordinal))
            {
                string rest = session.Id["puppet:".Length..];
                int bar = rest.IndexOf('|');
                if (bar > 0) PuppetRequested?.Invoke(rest[..bar], rest[(bar + 1)..]);
                return;
            }
            if (session.Id.StartsWith("loop:", StringComparison.Ordinal))
                LoopRequested?.Invoke(session.Workspace, session.Id["loop:".Length..]);
            else OpenRequested?.Invoke(session);
        }

        // ---- Puppet 检查面：只读 + 选中，不改业务语义 ----

        internal string[] MenuItems() => _menu.Items.Select(i => i.Text ?? "").ToArray();

        internal string Selection => _project == LoopTag ? "[Loop]" : _project == PuppetTag ? "[Puppet]" : _project;

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

        internal string PuppetEntryText => _btnPuppet.Text;

        internal bool SelectPuppet()
        {
            _project = PuppetTag;
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
            return _project == PuppetTag;
        }
    }
}
