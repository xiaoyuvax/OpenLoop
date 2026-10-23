using System.ComponentModel;
using Puppet.Core;

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
        private Session _lastSession;

        public MainForm()
        {
            InitializeComponent();
            Icon = UiIcons.App;
        }

        [PuppetIgnore] string IPuppet.AgentAccessKey => PuppetKeyVault.GlobalKey;
        [PuppetIgnore] Common.Logging.ILog IPuppet.AgentLog => AppLog.Log;
        string IPuppet.AgentInstanceName => PuppetHost.InstanceName;

        private Session Active => (_tabs.SelectedTab as AntdUI.TabPage)?.Tag as Session;

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
        public string Workspace => Active?.Workspace ?? "";

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

        public void SetModel(string model) => Active?.View.SetModel(model);

        public void SelectMode(string mode) => Active?.View.SelectMode(mode);

        public void Send(string mode = "task") => Active?.View.SendMode(mode);

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

        public void OpenLoop(string workspace = null)
        {
            if (_loopPage != null)
            {
                _tabs.SelectTab(_loopPage);
                return;
            }

            string target = workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _lastSession?.Workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _settings.Projects.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                AntdUI.Notification.warn(this, "Loop", "请先打开一个会话，Loop 需要工作区。", AntdUI.TAlignFrom.TR, null, 3);
                return;
            }

            _loopView = new LoopView { Dock = DockStyle.Fill };
            _loopView.Initialize(target, () => _settings);
            _loopPage = new AntdUI.TabPage
            {
                Text = Localization.T("TabLoop"),
                IconSvg = UiIcons.Dot(UiStyle.Loop),
                ReadOnly = false,
                Tag = _loopView,
                Dock = DockStyle.Fill
            };
            _loopPage.Controls.Add(_loopView);
            _tabs.Pages.Add(_loopPage);
            _tabs.SelectTab(_loopPage);
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

        private void OnFormLoad(object sender, EventArgs e)
        {
            _overview.Initialize();
            _overview.OpenRequested += ShowSession;
            _overview.NewSessionRequested += OpenNewSessionTab;
            _overview.SettingsRequested += OpenSettings;

            _settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(_settings.Workspace))
                _settings.Workspace = Directory.GetCurrentDirectory();
            _tabs.SelectTab(_overviewPage);
            if (_settings.Projects.Count == 0) OpenNewSessionTab("");
            else RefreshOverview();

            _ = RefreshCatalogAsync();
        }

        private void RefreshOverview() => _overview.RefreshSessions(_sessions, _settings.Projects);

        private void OnFormShown(object sender, EventArgs e)
        {
            PuppetHost.Start(this);
            if (PuppetHost.Endpoint != null)
                Text = $"OpenLoop · Puppet {PuppetHost.Endpoint}";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var session in _sessions) session.View?.Shutdown();
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

        private Session CreateSession(string workspace)
        {
            workspace = workspace ?? "";
            if (!string.IsNullOrWhiteSpace(workspace) &&
                !_settings.Projects.Contains(workspace, StringComparer.OrdinalIgnoreCase))
            {
                _settings.Projects.Add(workspace);
                _settings.Workspace = workspace;
                try { _settings.Save(); } catch (Exception ex) { AppLog.Warn("保存项目列表失败：" + ex.Message); }
            }

            var session = new Session { Workspace = workspace };
            session.View = new SessionView { Dock = DockStyle.Fill };
            session.View.Initialize(session, () => _settings);
            session.View.StateChanged += OnSessionStateChanged;
            session.View.OpenLoopRequested += OpenLoop;

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

        private void ShowSession(Session session)
        {
            if (session == null) return;
            _lastSession = session;
            if (session.Page.Parent == null) _tabs.Pages.Add(session.Page);
            _tabs.SelectTab(session.Page);
            RefreshOverview();
        }

        private void CloseTab(AntdUI.TabPage page)
        {
            bool selected = _tabs.SelectedTab == page;
            _tabs.Pages.Remove(page);
            if (page == _newPage) _newPage = null;
            if (page == _loopPage)
            {
                _loopView?.Shutdown();
                _loopPage = null;
                _loopView = null;
            }

            if (selected) _tabs.SelectTab(_overviewPage);
            RefreshOverview();
        }

        private bool OnClosingPage(object sender, AntdUI.ClosingPageEventArgs e)
        {
            if (e?.Value != null) CloseTab(e.Value);
            return false;
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
            var projects = _settings.Projects
                .Concat(_sessions.Select(s => s.Workspace))
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            view.Initialize(_settings, projects, workspace ?? "");
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
            using var dialog = new SettingsForm(_settings);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            _settings = dialog.Result;
            try
            {
                _settings.Save();
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
