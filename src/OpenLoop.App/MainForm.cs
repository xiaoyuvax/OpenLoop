using System.ComponentModel;
using ChatFlow;
using ChatMessage = ChatFlow.ChatMessage;
using OpenLoop.Service.Loop;
using OpenLoop.Service.Shell;
using OpenLoop.Service.Source;
using OpenLoop.Service.Web;
using OpenLoop.Service.Worker;
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
        /// <summary>定时把 A 那边（API / 网页 / 别的 agent 建的）会话并进界面。</summary>
        private readonly System.Windows.Forms.Timer _workerSync = new() { Interval = 3000 };
        private bool _syncingRemote;
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

        /// <summary>Puppet 用：当前选中的标签页文字（Loop 页不算 Session，ActiveTabText 会是空的）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedTabText => (_tabs.SelectedTab as AntdUI.TabPage)?.Text ?? "";

        /// <summary>A 面自检：当前会话用的模型。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ActiveModel => Active?.View?.Model ?? "";

        /// <summary>A 面自检：当前会话的运行状态（running/idle | 正文长度 | 步数）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ActiveRunStatus => Active?.View?.RunStatus ?? "";

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
        public string ProviderOptionNames => Active?.View.ProviderOptionNames ?? "";

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

        /// <summary>Puppet 用：只入队不发送，验证队列控件与两种插入模式。</summary>
        public void EnqueuePending(string text) => Active?.View.EnqueueText(text);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ComposerSendText => Active?.View.SendText ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EvolveEnabled => Active?.View.EvolveEnabled ?? false;

        public void StartEvolve() => Active?.View.StartEvolve();

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

        /// <summary>按会话 id 打开某个 Loop 会话（Puppet 用：[Loop] 页的一行）。</summary>
        public void OpenLoopSession(string workspace, string sessionId) => EnsureLoop(workspace, select: true, sessionId);

        private TestSurface _test;

        /// <summary>
        /// Puppet 用：AntdUI 等自绘控件的统一调试动作（临时测试面，见 TestSurface）。
        /// 整体移除测试入口时：删 PuppetTestSurface.cs + 这几行（字段 / 方法）。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object Test(string verb, string arg1 = "", string arg2 = "")
            => (_test ??= new TestSurface(this)).Run(verb, arg1, arg2);

        // ---- Puppet 检查面：项目 / [Loop] / provider 的状态与选中 ----

        public string[] OverviewMenu() => _overview.MenuItems();

        public string LoopEntry => _overview.LoopEntryText;

        public string OverviewSelection => _overview.Selection;

        public string[] OverviewRows() => _overview.RowTitles();

        /// <summary>Puppet 用：会话列表里勾选了的行。</summary>
        public string[] OverviewChecked() => _overview.CheckedRows();

        public bool SelectLoopOverview()
        {
            ShowOverview();
            return _overview.SelectLoop();
        }

        public string[] LoopWorkspaces() => LoopIndex.List().Select(l => l.Workspace)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>Puppet 用：[Loop] 页的行（会话 id + 工作区）。</summary>
        public string[] LoopSessions() => LoopIndex.List()
            .Select(l => l.SessionId + "\t" + l.Workspace).ToArray();

        public int LoopTabs => _loops.Count;

        public string LoopIterateEntry => _loopView?.IterateEntry ?? "";

        public string LoopSideTabs => _loopView?.SideTabNames ?? "";

        public int LoopModelOptionCount => _loopView?.ModelOptionCount ?? 0;

        public bool LoopOnlyFree => _loopView?.OnlyFreeModels ?? false;

        public string[] Providers() => ProviderList.All(_settings).Select(p => p.Name).ToArray();

        public string[] ModelsOf(string provider)
        {
            foreach (var (name, tag) in ProviderList.All(_settings))
                if (string.Equals(name, provider, StringComparison.OrdinalIgnoreCase))
                    return (tag.Models ?? []).ToArray();
            return [];
        }

        /// <summary>Puppet 用：不落盘，只看一个路径在 Loop 的读/写边界里是否放行。</summary>
        public string LoopScopeProbe(string path)
        {
            var (read, write) = global::OpenLoop.Service.Source.LoopScope.Create();
            bool canRead = read.TryResolve(path, out _, out _);
            bool canWrite = write.TryResolve(path, out _, out _);
            return (canRead ? "read=ok" : "read=denied") + " " + (canWrite ? "write=ok" : "write=denied");
        }

        /// <summary>Worker 的 /complain：投到该项目自己的 Loop 会话；忙则排队，且不抢当前标签页。</summary>
        private void OnComplaint(Session session, string text)
            => EnsureLoop(session.Workspace, select: false)?.Post(text);

        /// <summary>
        /// 取（必要时创建）某个项目的 Loop 会话。历史按「项目 + 会话 id」分开存，
        /// 改进产物（修订提案、源码修改）仍落在全局。
        /// </summary>
        /// <param name="sessionId">null = 该项目最近的 Loop 会话（没有就新建）；"" = 总是新建。</param>
        private LoopView EnsureLoop(string workspace, bool select, string sessionId = null)
        {
            string target = workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _lastSession?.Workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _settings.Projects.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                AntdUI.Notification.warn(this, "Loop", "请先打开一个会话，Loop 需要工作区。", AntdUI.TAlignFrom.TR, null, 3);
                return null;
            }

            string id = sessionId;
            if (id == null)
                id = LoopIndex.List()
                    .FirstOrDefault(l => Paths.Same(l.Workspace, target))?.SessionId
                    ?? LoopRef.LegacyId;
            else if (id.Length == 0)
                id = "loop-" + Guid.NewGuid().ToString("N")[..8];

            string key = LoopKey(target, id);
            if (_loops.TryGetValue(key, out var existing))
            {
                if (select) _tabs.SelectTab(existing.Page);
                _loopView = existing.View;
                _loopPage = existing.Page;
                return existing.View;
            }

            var view = new LoopView { Dock = DockStyle.Fill };
            view.SessionTarget = () => (Current?.Id ?? "", (Current?.Book.Turns.Count ?? 1) - 1);
            view.Iterate = () => ShowIterationAsync(target);
            view.TransferToWorker = turns => TransferToWorker(target, turns);
            view.ForkRequested += ForkLoop;
            view.NewSessionRequested += NewLoopSession;
            view.Initialize(target, () => _settings, id);
            var page = new AntdUI.TabPage
            {
                Text = LoopTabText(target, id),
                IconSvg = UiIcons.Dot(UiStyle.Loop),
                ReadOnly = false,
                Tag = view,
                Dock = DockStyle.Fill
            };
            page.Controls.Add(view);
            _tabs.Pages.Add(page);
            // 新建页面不主动跳转：/complain 只让它出现在标签栏，用户自己去点
            if (select) _tabs.SelectTab(page);
            _loops[key] = (view, page);
            _loopView = view;
            _loopPage = page;
            return view;
        }

        private static string LoopKey(string workspace, string sessionId)
            => Paths.Normalize(workspace) + "|" + sessionId;

        private static string LoopTabText(string workspace, string sessionId)
        {
            string name = Path.GetFileName(Paths.Normalize(workspace));
            string text = string.IsNullOrEmpty(name) ? Localization.T("TabLoop") : Localization.T("TabLoop") + " · " + name;
            // 同一项目可以有多个 Loop 会话：非默认会话补 id 尾段，免得标签页文本一模一样
            if (!string.Equals(sessionId, LoopRef.LegacyId, StringComparison.Ordinal))
                text += " · " + (sessionId?.StartsWith("loop-", StringComparison.Ordinal) == true
                    ? sessionId["loop-".Length..]
                    : sessionId);
            return text;
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
            // 重启脚本靠这个标记判断新壳已经进到界面（比只看进程存活可靠）
            IterationRunner.MarkShellStarted();
            _overview.Initialize();
            _overview.OpenRequested += ShowSession;
            _overview.LoopRequested += (workspace, sessionId) => EnsureLoop(workspace, true, sessionId);
            _overview.NewSessionRequested += OpenNewSessionTab;
            _overview.DeleteRequested += OnDeleteRequested;
            _overview.RemoveProjectRequested += OnRemoveProjectRequested;
            _overview.SettingsRequested += OpenSettings;

            _settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(_settings.Workspace))
                _settings.Workspace = TryGetDevRepoWorkspace() ?? Directory.GetCurrentDirectory();
            await OpenCodeHub.RefreshAsync();
            _tabs.SelectTab(_overviewPage);
            RestoreSessions();
            if (_settings.Projects.Count == 0 && _sessions.Count == 0) OpenNewSessionTab("");
            else RefreshOverview();

            RestoreWindowBounds();
            _ = RefreshCatalogAsync();
            _ = CheckSourcesAsync();

            // 壳刚被迭代重启过：回到原来的 Loop 会话，再把结果报告弹出来
            if (!string.IsNullOrWhiteSpace(Startup.Loop)) OpenLoop(Startup.Loop);
            ShowIterationReportOnStartup();

            _workerSync.Tick += OnWorkerSyncTick;
            _workerSync.Start();
        }

        private void OnWorkerSyncTick(object sender, EventArgs e) => _ = SyncWorkerSessionsAsync();

        /// <summary>
        /// 启动时对比源码与运行版本（章程 §3）。这里只报状态：
        /// 迭代轮次由 Loop 执行，不在这里偷偷编译/替换。
        /// </summary>
        private async Task CheckSourcesAsync()
        {
            try
            {
                var check = await global::OpenLoop.Service.Source.SourceSync.CheckAsync();
                AppLog.Info("源码同步：" + check);

                if (check.State == global::OpenLoop.Service.Source.SourceState.Newer)
                    AntdUI.Notification.info(this, "源码有更新",
                        $"源码 {check.SourceVersion} 高于运行版本 {check.RunningVersion}，可以迭代。",
                        AntdUI.TAlignFrom.TR, null, 5);
                else if (check.State == global::OpenLoop.Service.Source.SourceState.Older)
                    AntdUI.Notification.warn(this, "源码过旧",
                        $"源码 {check.SourceVersion} 低于运行版本 {check.RunningVersion}：" +
                        "更新源码、放弃迭代，或用 Puppet 自愈。",
                        AntdUI.TAlignFrom.TR, null, 6);

                var puppet = await global::OpenLoop.Service.Source.SourceSync.CheckPuppetCoreAsync();
                AppLog.Info("PuppetCore 同步：" + puppet);

                var chatflow = await global::OpenLoop.Service.Source.SourceSync.CheckChatFlowAsync();
                AppLog.Info("ChatFlow 同步：" + chatflow);
            }
            catch (Exception ex)
            {
                AppLog.Warn("源码同步失败：" + ex.Message);
            }
        }

        private readonly List<string> _iterationSteps = new();
        private string _iterationStatus = "idle";
        private IterationForm _iterationForm;

        /// <summary>Puppet 用：上一轮迭代状态（idle / running / ok / staged / failed）与逐步结果。</summary>
        public string IterationStatus => _iterationStatus;

        public string IterationSteps => string.Join("\n", _iterationSteps);

        /// <summary>Puppet 用：打开迭代确认窗口（只出计划，不自动开始）。</summary>
        public void OpenIterationDialog() => _ = ShowIterationAsync(null);

        /// <summary>Puppet 用：在确认窗口里点「开始迭代」。</summary>
        public bool StartIterationDialog()
        {
            if (_iterationForm is not { IsDisposed: false }) return false;
            _iterationForm.StartFromPuppet();
            return true;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IterationPlanText => _iterationForm?.PlanText ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IterationDialogLog => _iterationForm?.LogText ?? "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IterationDialogStatus => _iterationForm?.StatusText ?? "";

        /// <summary>Puppet 用：不开窗口跑一轮（编译/发布/登记 + 换 A/I，不动壳），轮询 IterationStatus。</summary>
        public void StartIteration() => _ = RunIterationAsync();

        /// <summary>Puppet 用：重启 A/I 到已登记版本。</summary>
        public void ApplyStaged() => _ = ApplyStagedAsync();

        private async Task<string> ApplyStagedAsync()
        {
            _iterationStatus = "restarting";
            bool applied = true;
            foreach (string role in new[] { global::OpenLoop.Service.Bus.BusRoles.A, global::OpenLoop.Service.Bus.BusRoles.I })
            {
                var step = await global::OpenLoop.Service.Source.Iteration.RestartAsync(role);
                _iterationSteps.Add(step.ToString());
                applied &= step.Ok;
                await Task.Delay(1500);   // A 先回来，再换 I
            }
            AppLog.Info("迭代换版：" + string.Join(" | ", _iterationSteps.TakeLast(2)));
            _iterationStatus = applied ? "ok" : "staged";
            return applied ? "已请求重启" : "重启被拒（忙），稍后再试";
        }

        /// <summary>
        /// Loop 页「迭代」：先弹确认框（谁、从哪个版本到哪个版本、改了什么），用户点头才编译与换版。
        /// 无窗口版本留给 Puppet：跳过壳，只换 A/I。
        /// </summary>
        private async Task<string> ShowIterationAsync(string workspace)
        {
            if (_iterationForm is { IsDisposed: false })
            {
                _iterationForm.Activate();
                return "迭代窗口已经打开";
            }

            IterationPlanResult planned;
            try
            {
                using var client = new LoopApiClient();
                planned = await client.IterationPlanAsync(workspace ?? "");
            }
            catch (Exception ex)
            {
                return "无法连接 Loop（I）：" + ex.Message;
            }

            var plan = planned?.Plan ?? [];
            string summary = planned?.Summary ?? "";

            string target = workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _loopView?.Workspace ?? "";
            if (string.IsNullOrWhiteSpace(target)) target = Workspace;
            if (string.IsNullOrWhiteSpace(target)) target = _lastSession?.Workspace ?? "";
            if (string.IsNullOrWhiteSpace(target)) target = _settings.Workspace ?? "";
            var form = new IterationForm(target, plan, summary);
            form.ShellRestart = report =>
            {
                IterationRunner.WriteReport(report);
                IterationRunner.LaunchRestarter(new ShellStage(report.ShellDir, report.Version), report.Workspace);
                Application.Exit();
            };
            form.FormClosed += (_, _) => _iterationForm = null;
            _iterationForm = form;
            form.Show(this);
            form.Activate();

            return "已打开迭代确认：" + string.Join("；",
                plan.Where(p => p.Update).Select(p => $"{p.Label} {p.Current}→{p.Target}")
                    .DefaultIfEmpty("无需升版"));
        }

        /// <summary>无窗口的一轮（Puppet）：不碰壳，由 I 执行，失败由 I 按迭代前的版本回退。</summary>
        private async Task<string> RunIterationAsync()
        {
            if (_iterationStatus is "running" or "restarting") return "已有迭代在跑";
            _iterationStatus = "running";
            _iterationSteps.Clear();
            try
            {
                using var client = new LoopApiClient();
                var planned = await client.IterationPlanAsync(Workspace);
                _iterationSteps.Add("plan " + string.Join(" | ", (planned?.Plan ?? [])
                    .Select(p => $"{p.Label} {p.Current}→{p.Target} {(p.Update ? "升版" : "不变")}")));

                await client.StartIterationAsync(Workspace, shell: false);
                var state = await client.WaitIterationAsync(Workspace, line => _iterationSteps.Add(line));
                if (state.State != "done")
                {
                    _iterationStatus = "failed";
                    return "迭代失败：" + (state.Error ?? _iterationSteps.LastOrDefault() ?? "");
                }

                _iterationStatus = "ok";
                return "迭代完成：" + state.Version;
            }
            catch (Exception ex)
            {
                AppLog.Error("迭代失败", ex);
                _iterationSteps.Add("FAIL " + ex.Message);
                _iterationStatus = "failed";
                return "迭代失败：" + ex.Message;
            }
        }

        /// <summary>壳刚被迭代重启过：回到原来的 Loop 会话，并把结果/回退情况弹出来。</summary>
        private void ShowIterationReportOnStartup()
        {
            var report = IterationRunner.TakeReport();
            if (report == null) return;

            if (IterationRunner.TakeShellFailure())
            {
                report.Ok = false;
                report.Steps.Add(new Iteration.Step("restart shell", false, "新壳没起来，已回退旧版本"));
            }
            report.ShellRestarted = report.Ok;

            if (!string.IsNullOrWhiteSpace(report.Workspace)) EnsureLoop(report.Workspace, select: true);
            var form = new IterationForm(report);
            form.FormClosed += (_, _) => _iterationForm = null;
            _iterationForm = form;
            form.Show(this);
            form.Activate();
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
            _workerSync.Stop();
            _workerSync.Dispose();
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
            session.View.NewSessionRequested += NewSessionFrom;
            session.View.TransferToLoop = turns => TransferToLoop(session.Workspace, turns);
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

        /// <summary>
        /// 壳只认识自己建的会话；直接走 A 的 API / 网页 / 别的 agent 建的会话靠这里补进列表。
        /// 打开着的页面顺带同步进度；没打开的只更新状态，不碰控件。
        /// </summary>
        private async Task SyncWorkerSessionsAsync()
        {
            if (_syncingRemote || _closing || WindowState == FormWindowState.Minimized) return;
            _syncingRemote = true;
            try
            {
                bool created = false;
                string newestWorkspace = null;
                foreach (var (id, workspace, title) in ChatStore.SavedSessions())
                {
                    if (_sessions.Any(s => s.Id == id) || !Directory.Exists(workspace)) continue;
                    CreateSession(workspace, id, title);
                    created = true;
                }

                using var client = new WorkerApiClient();
                if (!await client.PingAsync()) return;
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var brief in await client.ListAsync(summary: true))
                {
                    if (brief?.Id == null) continue;
                    known.Add(brief.Id);
                    var session = _sessions.FirstOrDefault(s => s.Id == brief.Id);
                    if (session == null)
                    {
                        if (!Directory.Exists(brief.Workspace)) continue;
                        session = CreateSession(brief.Workspace, brief.Id);
                        created = true;
                        newestWorkspace ??= brief.Workspace;
                    }

                    //跑着的时候每 3 秒并一次实时进度，刚跑完再补一次定稿
                    if (session.Page?.Parent != null
                        && (brief.State == WorkerSessionHost.Running || session.State == SessionState.Running))
                        session.View.SyncFromWorker(await client.GetAsync(brief.Id) ?? brief);

                    var state = StateOf(brief.State);
                    if (session.State != state)
                    {
                        session.State = state;
                        OnSessionStateChanged(session);
                    }
                }

                //A 重启或忘了这个会话：本地别一直挂着「运行中」
                foreach (var stale in _sessions.Where(s => s.State == SessionState.Running
                                                           && s.View?.Busy != true
                                                           && !known.Contains(s.Id)))
                {
                    stale.State = SessionState.Idle;
                    OnSessionStateChanged(stale);
                }

                if (created) RefreshOverview();
                //新会话可能在别的项目下；列表是按项目过滤的，不跳过去就等于「看不见」
                if (newestWorkspace != null) _overview.FocusProject(newestWorkspace);
            }
            catch (Exception ex)
            {
                AppLog.Warn("同步 A 的会话失败：" + ex.Message);
            }
            finally
            {
                _syncingRemote = false;
            }
        }

        /// <summary>打开一条会话时先对一次 A：它可能在别处又跑过几轮，界面还是旧的。</summary>
        private async Task SyncSessionAsync(Session session)
        {
            if (session == null) return;
            try
            {
                using var client = new WorkerApiClient();
                var snapshot = await client.GetAsync(session.Id);
                if (snapshot == null) return;
                session.View.SyncFromWorker(snapshot);
                var state = StateOf(snapshot.State);
                if (session.State != state)
                {
                    session.State = state;
                    OnSessionStateChanged(session);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取会话状态失败：" + ex.Message);
            }
        }

        /// <summary>删除一条会话时也让 A 忘掉它，否则同步定时器 3 秒后又把行拾回来。</summary>
        private static async Task ForgetOnWorkerAsync(string id)
        {
            try
            {
                using var client = new WorkerApiClient();
                await client.DeleteAsync(id);
            }
            catch (Exception ex)
            {
                AppLog.Warn("通知 A 删除会话失败：" + ex.Message);
            }
        }

        private static SessionState StateOf(string workerState) => workerState switch
        {
            WorkerSessionHost.Running => SessionState.Running,
            WorkerSessionHost.Done => SessionState.Done,
            WorkerSessionHost.Interrupted => SessionState.Interrupted,
            _ => SessionState.Idle
        };

        private void ShowSession(Session session)
        {
            if (session == null) return;
            _lastSession = session;
            if (session.Page.Parent == null) _tabs.Pages.Add(session.Page);
            _tabs.SelectTab(session.Page);
            RefreshOverview();
            _overview.FocusProject(session.Workspace);
            _ = SyncSessionAsync(session);
        }

        /// <summary>/fork：复制当前会话的上下文开一个新会话，并把新请求发出去。</summary>
        private void ForkSession(Session source, string request)
        {
            if (source == null || string.IsNullOrWhiteSpace(request)) return;
            var session = CreateSession(source.Workspace);
            //克隆可见历史：新会话既是「带着上下文」起步，后续追问也仍带上下文
            session.Book.ReplaceAll(source.Book.Turns.Select(CloneTurn), null);
            ShowSession(session);
            session.View.ComposerText = request;
            session.View.SendMode("task");
        }

        /// <summary>Loop 的 /fork：复制当前 Loop 对话开一个新 Loop 会话，并把新请求发出去。</summary>
        private void ForkLoop(LoopView source, string request)
        {
            if (source == null || string.IsNullOrWhiteSpace(request)) return;
            string workspace = source.Workspace;
            if (string.IsNullOrWhiteSpace(workspace) || source.Turns.Count == 0)
            {
                AntdUI.Notification.warn(this, "Loop", "没有可复制的对话。", AntdUI.TAlignFrom.TR, null, 3);
                return;
            }

            string id = "loop-" + Guid.NewGuid().ToString("N")[..8];
            ChatStore.Save(workspace, source.Turns.Select(CloneTurn).ToList(), id);
            ChatStore.Touch(workspace, id, "分叉 " + DateTime.Now.ToString("HH:mm"));
            EnsureLoop(workspace, select: true, sessionId: id)?.Send(request);
        }

        /// <summary>/new：开一个空会话，沿用来源会话的模型与强度；带请求就顺手发出去。</summary>
        private void NewSessionFrom(Session source, string request)
        {
            if (source == null) return;
            var session = CreateSession(source.Workspace);
            session.View.SetModel(source.View.Model);
            session.View.SetEffort(source.View.Effort);
            ShowSession(session);
            if (string.IsNullOrWhiteSpace(request)) return;
            session.View.ComposerText = request;
            session.View.SendMode("task");
        }

        /// <summary>Loop 的 /new：开一个空 Loop 会话，沿用当前模型与强度；带请求就顺手发出去。</summary>
        private void NewLoopSession(LoopView source, string request)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Workspace)) return;
            var view = EnsureLoop(source.Workspace, select: true, sessionId: "");
            if (view == null) return;
            view.SetModel(source.Model);
            view.SetEffort(source.Effort);
            if (!string.IsNullOrWhiteSpace(request)) view.Send(request);
        }

        /// <summary>
        /// Loop 页「转给 worker」：先把选中的对话落成新会话的历史，
        /// Worker 首次 Create 时会读这份历史当初始上下文；再开这个会话让用户接着追问。
        /// </summary>
        private bool TransferToWorker(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            var cloned = turns.Select(CloneTurn).ToList();
            ChatStore.Save(workspace, cloned, id);
            ChatStore.Touch(workspace, id, title);
            ShowSession(CreateSession(workspace, id, title));
            return true;
        }

        /// <summary>
        /// Worker 页「转给 Loop」：把选中的对话落成新 Loop 会话的历史，
        /// Loop 页打开时会读这份历史当初始上下文。
        /// </summary>
        private bool TransferToLoop(string workspace, IReadOnlyList<ChatTurn> turns)
        {
            if (string.IsNullOrWhiteSpace(workspace) || turns == null || turns.Count == 0) return false;
            workspace = Paths.Normalize(workspace);
            string id = "loop-" + Guid.NewGuid().ToString("N")[..8];
            string title = "转交 " + DateTime.Now.ToString("HH:mm");
            ChatStore.Save(workspace, turns.Select(CloneTurn).ToList(), id);
            ChatStore.Touch(workspace, id, title);
            return EnsureLoop(workspace, true, id) != null;
        }

        private static ChatTurn CloneTurn(ChatTurn t) => new()
        {
            User = new ChatMessage("user", t.UserText),
            Assistant = new ChatMessage("assistant", t.AssistantText),
            ReasoningText = t.ReasoningText,
            ToolTraceText = t.ToolTraceText,
            ToolCallLines = t.ToolCallLines,
            TimelineText = t.TimelineText,
            ToolStepCount = t.ToolStepCount,
            ToolTotalMs = t.ToolTotalMs
        };

        /// <summary>界面上删除会话：先确认再执行。Worker 会话与 [Loop] 会话都走这里。</summary>
        private void OnDeleteRequested(IReadOnlyList<Session> sessions)
        {
            if (sessions == null || sessions.Count == 0) return;
            if (MessageBox.Show(this,
                    $"删除 {sessions.Count} 个会话？对话记录会一并删除，不能恢复。",
                    "删除会话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            int loops = 0;
            foreach (var row in sessions.Where(IsLoopRow).ToList())
            {
                DeleteLoopSession(row.Workspace, row.Id[LoopRowPrefix.Length..]);
                loops++;
            }
            DeleteSessions(sessions.Where(s => !IsLoopRow(s)).Select(s => s.Id));
            if (loops > 0) RefreshOverview();
        }

        private const string LoopRowPrefix = "loop:";

        private static bool IsLoopRow(Session session)
            => session.Id.StartsWith(LoopRowPrefix, StringComparison.Ordinal);

        /// <summary>删掉一个 Loop 会话：先关标签（关标签会落盘），再删对话文件与索引。</summary>
        private void DeleteLoopSession(string workspace, string sessionId)
        {
            if (_loops.TryGetValue(LoopKey(workspace, sessionId), out var entry)
                && _tabs.Pages.Contains(entry.Page))
                CloseTab(entry.Page);
            ChatStore.Delete(workspace, sessionId);
            LoopIndex.Remove(workspace, sessionId);
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

        /// <summary>移除项目：连同该项目下的会话、Loop 会话与对话记录一起删掉（否则重启时会被带回来）。</summary>
        public int RemoveProject(string workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace)) return 0;

            foreach (var loop in LoopIndex.List().Where(l => Paths.Same(l.Workspace, workspace)).ToList())
                DeleteLoopSession(workspace, loop.SessionId);

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
                _ = ForgetOnWorkerAsync(session.Id);
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
                    case "about": ShowAbout(); break;
                    case "exit": Close(); break;
                }
            }
            finally
            {
                _menuBusy = false;
            }
        }

        private void OnSettings(object sender, EventArgs e) => OpenSettings();

        /// <summary>Puppet 用：与「关于」弹窗同一份版本数据（壳 / Worker / Loop）。</summary>
        public string Versions => string.Join(" | ", VersionLines());

        /// <summary>Puppet 用：主菜单的条目文本（确认「关于」在菜单里）。</summary>
        public string MenuTexts
        {
            get
            {
                var names = new List<string>();
                foreach (var item in _btnMenu.Items)
                    if (item is AntdUI.SelectItem select) names.Add(select.Text ?? "");
                return string.Join(",", names);
            }
        }

        private void ShowAbout()
            => MessageBox.Show(this, string.Join(Environment.NewLine, VersionLines()),
                Localization.T("MenuAbout"), MessageBoxButtons.OK, MessageBoxIcon.Information);

        /// <summary>各部件版本：壳取自身 exe，A/I 取运行中的 runtime/&lt;role&gt;.json，没跑就退回 exe。</summary>
        private string[] VersionLines() =>
        [
            "壳：" + OrUnknown(ExeVersion(Application.ExecutablePath)),
            "Worker：" + OrUnknown(RoleVersion(global::OpenLoop.Service.Bus.BusRoles.A)),
            "Loop：" + OrUnknown(RoleVersion(global::OpenLoop.Service.Bus.BusRoles.I))
        ];

        private static string RoleVersion(string role)
        {
            var runtime = global::OpenLoop.Service.Runtime.RuntimeInfo.TryLoad(
                global::OpenLoop.Service.Runtime.AppPaths.RuntimeFile(role));
            if (!string.IsNullOrWhiteSpace(runtime?.Version)) return runtime.Version;

            var store = new global::OpenLoop.Service.Shell.VersionStore();
            string exe = store.Exe(role);
            if (string.IsNullOrWhiteSpace(exe))
                exe = System.IO.Path.Combine(AppContext.BaseDirectory, "worker",
                    global::OpenLoop.Service.Shell.VersionStore.ExeName(role));
            return ExeVersion(exe);
        }

        private static string ExeVersion(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return "";
                string raw = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
                return global::OpenLoop.Service.Source.SourceSync.ParseVersion(raw);
            }
            catch
            {
                return "";
            }
        }

        private static string OrUnknown(string version)
            => string.IsNullOrWhiteSpace(version) ? "未知" : version;

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

        /// <summary>开发模式下默认工作区取源码仓根（从程序位置向上找 .git/HEAD 或 OpenLoop.App.csproj）；
        /// 找不到就是部署模式，退回当前目录。</summary>
        private static string TryGetDevRepoWorkspace()
        {
            try
            {
                var dir = new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory;
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, ".git", "HEAD")) ||
                        System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "OpenLoop.App", "OpenLoop.App.csproj")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return null;
            }
            catch { return null; }
        }
    }
}
