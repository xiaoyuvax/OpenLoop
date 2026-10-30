using System.ComponentModel;
using ChatFlow;
using ChatMessage = ChatFlow.ChatMessage;
using Puppet.Core;
using SurveyStore = OpenLoop.Core.Store.SurveyStore;

namespace OpenLoop.App
{
    /// <summary>
    /// 主窗体：顶栏（菜单/新建/设置）+ Antd Tabs。
    /// Tab0 是会话总览，其余每个 Tab 是一个会话；关闭 Tab 只移除页面，不停止会话。
    /// </summary>
    public partial class MainForm : Form, IPuppet
    {
        private readonly List<Session> _sessions = new();
        private AppSettings _settings = new();
        private bool _menuBusy;
        private AntdUI.TabPage _newPage;
        private AntdUI.TabPage _loopPage;
        private LoopView _loopView;
        private readonly Dictionary<string, (LoopView View, AntdUI.TabPage Page)> _loops = new(StringComparer.OrdinalIgnoreCase);
        private Session _lastSession;
        private bool _closing;

        public MainForm()
        {

            SurveyStore.Changed += OnSurveyStoreChanged;
            SurveyStore.Answered += OnSurveyAnswered;
            InitializeComponent();
            Icon = UiIcons.App;
        }

        [PuppetIgnore] string IPuppet.AgentAccessKey => PuppetKeyVault.GlobalKey;
        [PuppetIgnore] Common.Logging.ILog IPuppet.AgentLog => AppLog.Log;
        string IPuppet.AgentInstanceName => PuppetHost.InstanceName;

        private Session Active => (_tabs.SelectedTab as AntdUI.TabPage)?.Tag as Session;
        private Session Current => Active ?? _lastSession;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SessionCount => _sessions.Count;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SessionSummary => string.Join("\n",
            _sessions.Select(s => $"{s.Id}\t{s.State}\t{s.Title}\t{s.Workspace}"));

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ActiveSession
        {
            get
            {
                var session = Active;
                return session == null ? "" : $"{session.Id}\t{session.State}\t{session.Title}";
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ActiveTabText => Active?.Page?.Text ?? "";

        /// <summary>已配置的提供商摘要（A 面自检用）。</summary>
        public string ProviderSummary => string.Join("; ",
            _settings.Providers.Select(p => $"{p.Name}|{p.Kind}|{p.BaseUrl}"));

        /// <summary>AntdUI 实际使用的字体（A 面自检用）。</summary>
        public string UiFont => $"{AntdUI.Config.Font?.Name}|{AntdUI.Config.Font?.SizeInPoints}|{Font.Name}|{Font.SizeInPoints}";

        public string Workspace => Active?.Workspace ?? "";

        public string AskDemo(string title)
        {
            var tool = new OpenLoop.Core.Tools.Builtin.AskUserTool(
                Current?.Workspace ?? Workspace, () => (Current?.Id ?? "", (Current?.Book.Turns.Count ?? 1) - 1));
            var args = new OpenLoop.Core.Tools.ToolArgs(new() { ["title"] = title, ["body"] = "它在没读文件的情况下直接改写了配置。", ["dimension"] = "tool_usage" });
            var r = tool.ExecuteAsync(args, default).GetAwaiter().GetResult();
            return r.Ok ? r.Data : r.Error;
        }

        public void AnswerLastSurvey(string summary) => Current?.View.AnswerLastSurvey(summary);

        public string AddDemoSurvey(string title, string body)
            => Current?.View.AddSurvey(title, body, "choice") ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Mode => Active?.View.Mode ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy => Active?.View.Busy ?? false;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status => Active?.View.Status ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Model => Active?.View.Model ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ModelOptionCount => Active?.View.ModelOptionCount ?? 0;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ReasoningText => Active?.View.ReasoningText ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ChatDiagnostics => Active?.View.ChatDiagnostics ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int PendingCount => Active?.View.PendingCount ?? 0;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string PendingSummary => Active?.View.PendingSummary ?? "";

        public void RemovePending(int index) => Active?.View.RemovePending(index);

        public void RetractPending(int index) => Active?.View.RetractPending(index);

        public void SendPendingNow(int index) => Active?.View.SendPendingNow(index);

        public void SendAllPending() => Active?.View.SendAllPending();

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EvolveEnabled => Active?.View.EvolveEnabled ?? false;

        public void ShowDefinitions() => Active?.View.ShowDefinitions();

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int NewSessionModelOptionCount => NewPageView?.ModelOptionCount ?? 0;

        public bool NewSessionOnlyFree
        {
            get => NewPageView?.OnlyFreeModels ?? true;
            set
            {
                if (NewPageView != null) NewPageView.OnlyFreeModels = value;
            }
        }

        public bool SelectNewSessionProvider(string name)
            => NewPageView?.SelectProvider(name) ?? false;

        private NewSessionView NewPageView => _newPage?.Tag as NewSessionView;

        public bool OnlyFreeModels
        {
            get => Active?.View.OnlyFreeModels ?? true;
            set
            {
                if (Active != null) Active.View.OnlyFreeModels = value;
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ComposerText
        {
            get => Active?.View.ComposerText ?? "";
            set
            {
                if (Active != null) Active.View.ComposerText = value;
            }
        }

        public void ShowSidePage(int index) => Active?.View.ShowSidePage(index);

        public string ProbeTrace(string path)
        {
            var view = Active?.View;
            if (view == null) return "no view";
            view.SetTraceDebug(path);
            return view.TraceDebug;
        }

        public void SetModel(string model) => Active?.View.SetModel(model);

        public void SetEffort(string effort) => Active?.View.SetEffort(effort);

        public void SelectMode(string mode) => Active?.View.SelectMode(mode);

        public void Send(string mode = "task") => Active?.View.SendMode(mode);

        /// <summary>把文字放进当前会话的输入框（不发送），A 面驱动用。</summary>
        public void SetComposerText(string text)
        {
            var view = Current?.View;
            if (view != null) view.ComposerText = text ?? "";
        }

        public void NewSession(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) workspace = _settings.Workspace;
            if (string.IsNullOrWhiteSpace(workspace)) workspace = Directory.GetCurrentDirectory();
            ShowSession(CreateSession(workspace));
        }

        public void OpenSession(string title)
        {
            var session = FindByTitle(title);
            if (session != null) ShowSession(session);
        }

        public void CloseSession(string title)
        {
            var session = FindByTitle(title);
            if (session?.Page != null) CloseTab(session.Page);
        }

        public void ShowOverview() => _tabs.SelectTab(_overviewPage);

        public bool LoopBusy => _loopView?.Busy ?? false;

        public string LoopStatus => _loopView?.Status ?? "";

        public string LoopModel => _loopView?.Model ?? "";

        public void SendLoop(string text) => _loopView?.Send(text);

        public void OpenLoop(string workspace = null) => EnsureLoop(workspace, select: true);

        /// <summary>Worker 的 /complain：投到该项目自己的 Loop 会话；忙则排队，且不抢当前标签页。</summary>
        private void OnComplaint(Session session, string text)
            => EnsureLoop(session.Workspace, select: false)?.Post(text);

        /// <summary>
        /// 取（必要时创建）某个项目的 Loop 会话。历史按项目分开存，
        /// 改进产物（修订提案、源码修改）仍落在全局。
        /// </summary>
        private LoopView EnsureLoop(string workspace, bool select)
        {
            string target = workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _lastSession?.Workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _settings.Projects.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                AntdUI.Notification.warn(this, "Loop", "请先打开一个会话，Loop 需要工作区。", AntdUI.TAlignFrom.TR, null, 3);
                return null;
            }

            if (_loops.TryGetValue(Paths.Normalize(target), out var existing))
            {
                if (select) _tabs.SelectTab(existing.Page);
                _loopView = existing.View;
                _loopPage = existing.Page;
                return existing.View;
            }

            var view = new LoopView { Dock = DockStyle.Fill };
            view.SessionTarget = () => (Current?.Id ?? "", (Current?.Book.Turns.Count ?? 1) - 1);
            view.Initialize(target, () => _settings);
            var page = new AntdUI.TabPage
            {
                Text = LoopTabText(target),
                IconSvg = UiIcons.Dot(UiStyle.Loop),
                ReadOnly = false,
                Tag = view,
                Dock = DockStyle.Fill
            };
            page.Controls.Add(view);
            _tabs.Pages.Add(page);
            // 新建页面不主动跳转：/complain 只让它出现在标签栏，用户自己去点
            if (select) _tabs.SelectTab(page);
            _loops[Paths.Normalize(target)] = (view, page);
            _loopView = view;
            _loopPage = page;
            return view;
        }

        private static string LoopTabText(string workspace)
        {
            string name = Path.GetFileName(Paths.Normalize(workspace));
            return string.IsNullOrEmpty(name) ? Localization.T("TabLoop") : Localization.T("TabLoop") + " · " + name;
        }

        public void SelectProject(string workspace) => _overview.FocusProject(workspace ?? "");

        public void OpenNewSession(string workspace = "") => OpenNewSessionTab(workspace ?? "");

        public string NewSessionDraft
        {
            set
            {
                if (_newPage?.Tag is NewSessionView view) view.Draft = value ?? "";
            }
        }

        public void SetNewSessionDraft(string text)
        {
            if (_newPage?.Tag is NewSessionView view) view.Draft = text ?? "";
        }

        public void SubmitNewSession() => (_newPage?.Tag as NewSessionView)?.SubmitDraft();

        private Session FindByTitle(string title)
            => _sessions.FirstOrDefault(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));

        private async void OnFormLoad(object sender, EventArgs e)
        {
            _overview.Initialize();
            _overview.OpenRequested += ShowSession;
            _overview.NewSessionRequested += OpenNewSessionTab;
            _overview.DeleteRequested += OnDeleteRequested;
            _overview.RemoveProjectRequested += OnRemoveProjectRequested;
            _overview.SettingsRequested += OpenSettings;

            _settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(_settings.Workspace))
                _settings.Workspace = Directory.GetCurrentDirectory();
            await OpenCodeHub.RefreshAsync();
            _tabs.SelectTab(_overviewPage);
            RestoreSessions();
            if (_settings.Projects.Count == 0 && _sessions.Count == 0) OpenNewSessionTab("");
            else RefreshOverview();

            RestoreWindowBounds();
            _ = RefreshCatalogAsync();
        }

        /// <summary>恢复上次窗口位置：原屏幕还在就原位启动，不在则回主屏居中（保留尺寸）。</summary>
        private void RestoreWindowBounds()
        {
            int width = _settings.WindowWidth;
            int height = _settings.WindowHeight;
            if (width < DipMin || height < DipMin) return;   // 首次运行：用设计器默认

            var saved = new Rectangle(_settings.WindowX, _settings.WindowY, width, height);
            StartPosition = FormStartPosition.Manual;

            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
            {
                Bounds = saved;
            }
            else
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                Size = new Size(Math.Min(width, area.Width), Math.Min(height, area.Height));
                Location = new Point(
                    area.X + (area.Width - Width) / 2,
                    area.Y + (area.Height - Height) / 2);
            }

            if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        private void SaveWindowBounds()
        {
            // 最大化时 Bounds 是屏幕尺寸，要存还原后的矩形
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.WindowX = bounds.X;
            _settings.WindowY = bounds.Y;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存窗口位置失败：" + ex.Message); }
        }

        private const int DipMin = 200;

        private void RefreshOverview() => _overview.RefreshSessions(_sessions, _settings.Projects);

        private void OnFormShown(object sender, EventArgs e)
        {
            PuppetHost.Start(this);
            if (PuppetHost.Endpoint != null)
                Text = $"OpenLoop · Puppet {PuppetHost.Endpoint}";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            SaveWindowBounds();
            foreach (var session in _sessions)
            {
                session.View?.FlushSideWork();
                session.View?.Shutdown();
            }
            foreach (var loop in _loops.Values) loop.View.Shutdown();
            PuppetHost.Stop();
        }

        private async Task RefreshCatalogAsync()
        {
            try
            {
                var models = await OpenRouterCatalog.FetchModelsAsync(AppSettings.ResolveKey(_settings));
                if (models.Count == 0) return;
                ModelCatalog.Set(models);
                AppLog.Info($"模型目录已刷新：{models.Count} 个可用");
            }
            catch (Exception ex)
            {
                AppLog.Warn("模型目录刷新失败：" + ex.Message);
            }
        }

        private Session CreateSession(string workspace, string sessionId = null, string title = null)
        {
            workspace = Paths.Normalize(workspace);
            //同一目录的不同写法（盘符大小写、斜杠、尾分隔符）算同一个项目：复用既有写法，不再新增一条
            string existingProject = _settings.Projects.FirstOrDefault(p => Paths.Same(p, workspace));
            if (existingProject != null)
            {
                workspace = existingProject;
            }
            else if (workspace.Length > 0)
            {
                if (!_settings.Projects.Contains(workspace, StringComparer.OrdinalIgnoreCase))
                    _settings.Projects.Add(workspace);
                _settings.Workspace = workspace;
                try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存项目列表失败：" + ex.Message); }
            }
            else
            {
                workspace = "";
            }

            var session = new Session { Workspace = workspace, Id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N")[..8] : sessionId };
            session.CustomTitle = title;
            session.View = new SessionView { Dock = DockStyle.Fill };
            session.View.Initialize(session, () => _settings);
            session.View.StateChanged += OnSessionStateChanged;
            session.View.OpenLoopRequested += OpenLoop;
            session.View.ComplaintRequested += OnComplaint;
            session.View.ForkRequested += ForkSession;
            session.View.TitleChanged += OnSessionTitleChanged;
            session.View.SideWorkEvicted += OnSideWorkEvicted;

            session.Page = new AntdUI.TabPage
            {
                Text = session.Title,
                IconSvg = UiIcons.Dot(UiStyle.Muted),
                ReadOnly = false,
                Tag = session,
                Dock = DockStyle.Fill
            };
            session.Page.Controls.Add(session.View);
            _sessions.Add(session);
            return session;
        }

        private void OnSurveyStoreChanged(string workspace, string sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session?.Page == null) return;
            void Apply()
            {
                int pending = SurveyStore.Load(workspace, sessionId).Count(r => r.State == "pending");
                session.Page.Text = pending > 0 ? session.Title + "  ●" : session.Title;
            }
            if (InvokeRequired) BeginInvoke(new Action(Apply)); else Apply();
        }

        private void OnSurveyAnswered(string workspace, string sessionId, string surveyId, string summary)
        {
            var record = SurveyStore.Load(workspace, sessionId).FirstOrDefault(r => r.Id == surveyId);
            _loopView?.NoteSurveyAnswer(workspace, record?.Title ?? surveyId, summary);
            OnSurveyStoreChanged(workspace, sessionId);
        }

        /// <summary>重启后把上次的会话摆回会话页：会话文件按 工作目录+id 分桶，靠索引才找得回来。</summary>
        private void RestoreSessions()
        {
            try
            {
                foreach (var (id, workspace, title) in ChatStore.SavedSessions())
                {
                    if (_sessions.Any(s => s.Id == id)) continue;
                    if (!Directory.Exists(workspace)) continue;
                    CreateSession(workspace, id, title);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("恢复会话失败", ex);
            }
        }

        private void ShowSession(Session session)
        {
            if (session == null) return;
            _lastSession = session;
            if (session.Page.Parent == null) _tabs.Pages.Add(session.Page);
            _tabs.SelectTab(session.Page);
            RefreshOverview();
            _overview.FocusProject(session.Workspace);
        }

        /// <summary>/fork：复制当前会话的上下文开一个新会话，并把新请求发出去。</summary>
        private void ForkSession(Session source, string request)
        {
            if (source == null || string.IsNullOrWhiteSpace(request)) return;
            var session = CreateSession(source.Workspace);
            //克隆可见历史：新会话既是「带着上下文」起步，后续追问也仍带上下文
            session.Book.ReplaceAll(source.Book.Turns.Select(t => new ChatTurn
            {
                User = new ChatMessage("user", t.UserText),
                Assistant = new ChatMessage("assistant", t.AssistantText),
                ReasoningText = t.ReasoningText,
                ToolTraceText = t.ToolTraceText,
                ToolCallLines = t.ToolCallLines,
                ToolStepCount = t.ToolStepCount,
                ToolTotalMs = t.ToolTotalMs
            }), null);
            ShowSession(session);
            session.View.ComposerText = request;
            session.View.SendMode("task");
        }

        /// <summary>界面上删除会话：先确认再执行。</summary>
        private void OnDeleteRequested(IReadOnlyList<Session> sessions)
        {
            if (sessions == null || sessions.Count == 0) return;
            if (MessageBox.Show(this,
                    $"删除 {sessions.Count} 个会话？对话记录会一并删除，不能恢复。",
                    "删除会话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            DeleteSessions(sessions.Select(s => s.Id));
        }

        /// <summary>界面上移除项目：项目下的会话（含磁盘对话）一并删除，否则下次启动会把项目带回来。</summary>
        private void OnRemoveProjectRequested(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return;

            int count = _sessions.Count(s => Paths.Same(s.Workspace, workspace));
            string name = Path.GetFileName(workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = workspace;

            string prompt = count == 0
                ? $"移除项目“{name}”？"
                : $"移除项目“{name}”？\n该项目下 {count} 个会话及其对话记录会一并删除，不能恢复。";
            if (MessageBox.Show(this, prompt, "移除项目", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            RemoveProject(workspace);
        }

        /// <summary>移除项目：连同该项目下的会话与对话记录一起删掉（否则重启时会被会话带回来）。</summary>
        public int RemoveProject(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return 0;

            int removed = DeleteSessions(
                _sessions.Where(s => Paths.Same(s.Workspace, workspace)).Select(s => s.Id));
            _settings.Projects.RemoveAll(p => Paths.Same(p, workspace));
            if (Paths.Same(_settings.Workspace, workspace)) _settings.Workspace = "";
            try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存项目列表失败：" + ex.Message); }
            RefreshOverview();
            if (_newPage?.Tag is NewSessionView view) view.SetProjects(ProjectChoices());
            return removed;
        }

        private IEnumerable<string> ProjectChoices()
            => _settings.Projects
                .Concat(_sessions.Select(s => s.Workspace))
                .Where(w => !string.IsNullOrWhiteSpace(w));

        /// <summary>删除会话（含磁盘上的对话与索引），A 面与界面共用同一个入口。</summary>
        public int DeleteSessions(IEnumerable<string> sessionIds)
        {
            var ids = new HashSet<string>(sessionIds ?? [], StringComparer.Ordinal);
            var targets = _sessions.Where(s => ids.Contains(s.Id)).ToList();
            foreach (var session in targets)
            {
                session.View?.Shutdown();
                if (session.Page != null) CloseTab(session.Page);
                ChatStore.Delete(session.Workspace, session.Id);
                _sessions.Remove(session);
                if (_lastSession == session) _lastSession = null;
            }
            if (targets.Count > 0) RefreshOverview();
            return targets.Count;
        }

        private void CloseTab(AntdUI.TabPage page)
        {
            bool selected = _tabs.SelectedTab == page;
            _tabs.Pages.Remove(page);
            if (page == _newPage) _newPage = null;
            if (page.Tag is LoopView)
            {
                var entry = _loops.FirstOrDefault(p => p.Value.Page == page);
                if (!string.IsNullOrEmpty(entry.Key)) _loops.Remove(entry.Key);
                ((LoopView)page.Tag).Shutdown();
                if (_loopPage == page) { _loopPage = null; _loopView = null; }
            }

            if (selected) _tabs.SelectTab(_overviewPage);
            RefreshOverview();
        }

        private bool OnClosingPage(object sender, AntdUI.ClosingPageEventArgs e)
        {
            if (e?.Value != null) CloseTab(e.Value);
            return true;
        }

        private void OnTabMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var page = _tabs.ContainsTabPage(e.X, e.Y);
            if (page == null || page == _overviewPage) return;

            var items = new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem("重命名") { ID = "rename" },
                new AntdUI.ContextMenuStripItemDivider(),
                new AntdUI.ContextMenuStripItem("关闭右侧所有页面") { ID = "right" },
                new AntdUI.ContextMenuStripItem("关闭左侧所有页面") { ID = "left" },
                new AntdUI.ContextMenuStripItemDivider(),
                new AntdUI.ContextMenuStripItem("关闭其他页面") { ID = "others" },
                new AntdUI.ContextMenuStripItem("关闭全部页面") { ID = "all" }
            };

            AntdUI.ContextMenuStrip.open(_tabs, selected => OnTabMenu(page, selected?.ID), items);
        }

        private void OnTabMenu(AntdUI.TabPage target, string id)
        {
            switch (id)
            {
                case "rename": RenameTab(target); break;
                case "right": CloseRange(target, 1); break;
                case "left": CloseRange(target, -1); break;
                case "others": CloseOthers(target); break;
                case "all": CloseAll(); break;
            }
        }

        private void RenameTab(AntdUI.TabPage page)
        {
            var input = new AntdUI.Input
            {
                Text = page.Text,
                Width = 320,
                Height = 34,
                Radius = 8,
                BorderWidth = 1
            };
            if (AntdUI.Modal.open(this, "重命名", input) != DialogResult.OK) return;

            string name = input.Text?.Trim() ?? "";
            if (name.Length == 0) return;

            if (page.Tag is Session session)
            {
                session.CustomTitle = name;
                ChatStore.Touch(session.Workspace, session.Id, name);
            }
            page.Text = name;
            RefreshOverview();
        }

        private void CloseRange(AntdUI.TabPage target, int direction)
        {
            var pages = _tabs.Pages.ToList();
            int index = pages.IndexOf(target);
            if (index < 0) return;

            for (int i = index + direction; i >= 0 && i < pages.Count; i += direction)
                if (pages[i] != _overviewPage) CloseTab(pages[i]);
        }

        private void CloseOthers(AntdUI.TabPage target)
        {
            foreach (var page in _tabs.Pages.ToList())
                if (page != target && page != _overviewPage) CloseTab(page);
        }

        private void CloseAll()
        {
            foreach (var page in _tabs.Pages.ToList())
                if (page != _overviewPage) CloseTab(page);
        }

        private void OnSelectedTabChanged(object sender, AntdUI.IntEventArgs e)
        {
            if (_tabs.SelectedTab == _overviewPage) _overview.FocusProject(_lastSession?.Workspace);
            RefreshOverview();
        }

        private void OnSessionStateChanged(Session session)
        {
            if (session.Page != null)
                session.Page.IconSvg = UiIcons.Dot(StateColor(session.State));
            RefreshOverview();
        }

        private void OnSessionTitleChanged(Session session)
        {
            if (session.Page != null) session.Page.Text = session.Title;
            ChatStore.Touch(session.Workspace, session.Id, session.CustomTitle);
            RefreshOverview();
        }

        /// <summary>旁路被新旁路挤走：旧结果落成一个普通会话，之后可从会话历史恢复继续聊。</summary>
        private void OnSideWorkEvicted(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (turns == null || turns.Count == 0) return;
            if (_closing)
            {
                // 关窗时只落盘，不建视图、不再请求模型
                string id = Guid.NewGuid().ToString("N")[..8];
                ChatStore.Save(workspace, turns, id);
                ChatStore.Touch(workspace, id);
                return;
            }
            var session = CreateSession(workspace);
            session.Book.ReplaceAll(turns, null);
            ChatStore.Save(workspace, turns, session.Id);
            ChatStore.Touch(workspace, session.Id);
            session.View.RequestTitle();
            RefreshOverview();
        }

        private static Color StateColor(SessionState state) => state switch
        {
            SessionState.Running => UiStyle.Success,
            SessionState.Interrupted => UiStyle.Error,
            _ => UiStyle.Muted
        };

        private void OpenNewSessionTab(string workspace)
        {
            if (_newPage != null)
            {
                _tabs.SelectTab(_newPage);
                return;
            }

            var view = new NewSessionView { Dock = DockStyle.Fill };
            view.Initialize(_settings, ProjectChoices(), workspace ?? "");
            view.Submit += request =>
            {
                var page = _newPage;
                if (page != null) CloseTab(page);

                var session = CreateSession(request.Workspace);
                session.View.SetModel(request.Model);
                session.View.SetEffort(request.Effort);
                ShowSession(session);
                session.View.ComposerText = request.Text;
                session.View.SendMode("task");
            };

            _newPage = new AntdUI.TabPage
            {
                Text = Localization.T("NewSession"),
                IconSvg = UiIcons.Of(UiIcons.Edit, UiStyle.Muted),
                ReadOnly = false,
                Tag = view,
                Dock = DockStyle.Fill
            };
            _newPage.Controls.Add(view);
            _tabs.Pages.Add(_newPage);
            _tabs.SelectTab(_newPage);
        }

        private void OnMenuSelect(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_menuBusy) return;
            _menuBusy = true;
            try
            {
                string key = _btnMenu.SelectedValue as string;
                _btnMenu.SelectedValue = null;

                switch (key)
                {
                    case "new": OpenNewSessionTab(""); break;
                    case "list": ShowOverview(); break;
                    case "loop": OpenLoop(); break;
                    case "theme-light": ApplyTheme("light"); break;
                    case "theme-dark": ApplyTheme("dark"); break;
                    case "theme-grayblue": ApplyTheme("grayblue"); break;
                    case "lang-auto": ApplyLanguage(""); break;
                    case "lang-zh": ApplyLanguage("zh"); break;
                    case "lang-en": ApplyLanguage("en"); break;
                    case "settings": OpenSettings(); break;
                    case "exit": Close(); break;
                }
            }
            finally
            {
                _menuBusy = false;
            }
        }

        private void OnSettings(object sender, EventArgs e) => OpenSettings();

        private void ApplyLanguage(string language)
        {
            _settings.Language = language;
            try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存语言失败：" + ex.Message); }
            AntdUI.Notification.info(this, "Language", Localization.T("LanguageChanged"), AntdUI.TAlignFrom.TR, null, 3);
        }

        private void ApplyTheme(string theme)
        {
            _settings.Theme = theme;
            try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存主题失败：" + ex.Message); }
            AntdUI.Notification.info(this, "主题", "已保存，重启 OpenLoop 后生效。", AntdUI.TAlignFrom.TR, null, 3);
        }

        private void OpenSettings()
        {
            SettingsForm dialog;
            try
            {
                dialog = new SettingsForm(_settings, Current?.Workspace ?? _lastSession?.Workspace ?? _settings.Projects.FirstOrDefault() ?? "");
            }
            catch (Exception ex)
            {
                AppLog.Error("打开设置失败", ex);
                AntdUI.Notification.error(this, "设置失败", ex.Message, AntdUI.TAlignFrom.TR, null, 6);
                return;
            }

            using (dialog)
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings = dialog.Result;
                try
                {
                    _settings.Save();
                    ModelCatalog.Set(ModelCatalog.All.ToList());
                    AntdUI.Notification.success(this, "设置已保存", "配置写入本地。",
                        AntdUI.TAlignFrom.TR, null, 2);
                }
                catch (Exception ex)
                {
                    AppLog.Error("保存设置失败", ex);
                    AntdUI.Notification.error(this, "保存失败", ex.Message, AntdUI.TAlignFrom.TR, null, 4);
                }
            }
        }
    }
}
