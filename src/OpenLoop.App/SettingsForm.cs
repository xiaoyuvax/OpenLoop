using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;
using OpenLoop.Core.Skills;
using OpenLoop.Core.Store;

namespace OpenLoop.App
{
    /// <summary>设置页里技能列表的一行（Table 按属性名取值，On 由 Render 画成徽标）。</summary>
    internal sealed record SkillRow(string Name, string Description, bool On, string From, SkillInfo Skill = null);

    internal sealed partial class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private readonly List<ProviderConfig> _providers = [];
        private readonly List<ProviderConfig> _pluginProviders = [];
        private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SkillRow> _skillRows = [];
        private int _skillLoad;
        private int _providerLoad;
        private bool _listing;
        private readonly Dictionary<string, ModelProps> _modelProps = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _workspace;
        private ProviderConfig _loaded;
        private bool _filling;

        /// <summary>账本归 I：技能列表与启用/停用都走它的 API。</summary>
        private OpenLoop.Service.Web.LoopApiClient _loopClient;
        private OpenLoop.Service.Web.LoopApiClient Loop => _loopClient ??= new OpenLoop.Service.Web.LoopApiClient();

        public SettingsForm(AppSettings source, string workspace = "")
        {
            _source = source ?? new AppSettings();
            _workspace = workspace ?? "";
            InitializeComponent();
            Disposed += (_, _) => _loopClient?.Dispose();
            SelectTips.Bind(_tips, _cboLanguage, _cboTheme, _cboProvider);
            Icon = UiIcons.App;
            _colSkillOn.Render = (value, row, index) => SkillBadge(row as SkillRow);
            _tblSkills.DataSource = _skillRows;
            //没有工作区时先让「全局」可用，否则这个页什么都管不了
            _filling = true;
            try { _selSkillScope.SelectedValue = string.IsNullOrWhiteSpace(_workspace) ? SkillScopeGlobal : SkillScopeProject; }
            finally { _filling = false; }
            _ = LoadSkillsAsync();
            _ = LoadPluginsAsync();

            foreach (var provider in _source.Providers ?? [])
                _providers.Add(Clone(provider));
            foreach (string id in _source.DisabledModels ?? [])
                if (!string.IsNullOrWhiteSpace(id)) _disabled.Add(id.Trim());
            foreach (var pair in _source.ModelProps ?? [])
                _modelProps[pair.Key] = new ModelProps { Name = pair.Value?.Name ?? "", ContextLimit = pair.Value?.ContextLimit ?? 0 };

            _filling = true;
            _cboLanguage.SelectedValue = _source.Language ?? "";
            _cboTheme.SelectedValue = string.IsNullOrWhiteSpace(_source.Theme) ? "light" : _source.Theme;
            _switchFullAccess.Checked = _source.FullAccess;
            _switchAutoApprove.Checked = _source.AutoApprove;
            _switchOpenCode.Checked = _source.OpenCodeAskPermissions;
            _cboWebFetch.SelectedValue = string.IsNullOrWhiteSpace(_source.WebFetchMode) ? "allow" : _source.WebFetchMode;
            _switchAgents.Checked = _source.UseAgentsMd;
            _switchThinking.Checked = _source.ThinkingExpandedByDefault;
            _switchAutoEvolve.Checked = _source.AutoEvolve;
            _switchAutoIterate.Checked = _source.AutoIterate;
            _switchEnableCollab.Checked = _source.EnableCollab;
            _cboCollabPoll.SelectedValue = PollChoice(_source.CollabPollSeconds);
            _switchCollabReuse.Checked = _source.CollabReuseContext;
            _filling = false;

            RefreshProviderCombo(_providers.FirstOrDefault());
            _loaded = _providers.FirstOrDefault();
            LoadProvider(_loaded);
            _ = LoadPluginProvidersAsync();
        }

        public AppSettings Result { get; private set; }

        private ProviderConfig Current => _cboProvider.SelectedValue as ProviderConfig;

        private static ProviderConfig Clone(ProviderConfig p) => new()
        {
            Name = p.Name,
            Kind = p.Kind,
            BaseUrl = p.BaseUrl,
            KeyProtected = p.KeyProtected,
            Models = [.. (p.Models ?? [])],
            PluginDirectory = p.PluginDirectory,
            PluginProviderIndex = p.PluginProviderIndex,
            PluginId = p.PluginId,
            PluginName = p.PluginName,
            PluginRawBaseUrl = p.PluginRawBaseUrl,
            PluginRawModelsUrl = p.PluginRawModelsUrl,
            PluginRawModelPrefix = p.PluginRawModelPrefix,
            PluginDeclaredModels = [.. (p.PluginDeclaredModels ?? [])],
            PluginKnownModels = [.. (p.PluginKnownModels ?? [])],
            PluginReady = p.PluginReady,
            RuntimeKey = p.RuntimeKey
        };

        /// <summary>插件提供商只读地址与密钥，模型改动写回插件清单；会话下拉里的插件贡献不在这里。</summary>
        private IEnumerable<ProviderConfig> AllProviders => _providers.Concat(_pluginProviders);

        private void RefreshProviderCombo(ProviderConfig select)
        {
            _filling = true;
            try
            {
                _cboProvider.Items.Clear();
                foreach (var provider in AllProviders)
                    _cboProvider.Items.Add(new AntdUI.SelectItem(DisplayName(provider)) { Tag = provider });
                if (select != null) _cboProvider.SelectedValue = select;
            }
            finally
            {
                _filling = false;
            }
        }

        private static string DisplayName(ProviderConfig p)
        {
            if (p == null) return Localization.T("Unnamed");
            if (!p.IsPlugin) return string.IsNullOrWhiteSpace(p.Name) ? Localization.T("Unnamed") : p.Name.Trim();
            string plugin = string.IsNullOrWhiteSpace(p.PluginName) ? p.PluginId : p.PluginName.Trim();
            string own = p.Name?.Trim() ?? "";
            string label = string.IsNullOrWhiteSpace(plugin) ? own : plugin;
            if (own.Length > 0 && plugin.Length > 0 && !string.Equals(own, plugin, StringComparison.OrdinalIgnoreCase))
                label += " · " + own;
            return (label.Length == 0 ? Localization.T("Unnamed") : label) + Localization.T("PluginSuffix");
        }

        private void OnProviderChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_filling) return;
            CommitProvider(_loaded);
            _loaded = Current;
            LoadProvider(_loaded);
            RefreshProviderCombo(_loaded);
        }

        private void LoadProvider(ProviderConfig provider)
        {
            _filling = true;
            try
            {
                bool has = provider != null;
                bool plugin = has && provider.IsPlugin;
                _txtName.Text = has ? provider.Name : "";
                _txtUrl.Text = plugin ? provider.PluginRawBaseUrl ?? provider.BaseUrl : has ? provider.BaseUrl : "";
                _txtKey.Text = plugin || !has ? "" : AppSettings.Unprotect(provider.KeyProtected);
                _txtModelId.Text = "";
                ReloadModelList(provider);

                _txtName.Enabled = has && !plugin;
                _txtUrl.Enabled = has && !plugin;
                _txtKey.Enabled = has && !plugin;
                _btnFetch.Visible = has && !plugin && provider.AutoModels;
                _lblHint.Text = !has
                    ? Localization.T("ProviderNone")
                    : plugin
                        ? (provider.PluginReady
                            ? string.Format(Localization.T("ProviderPluginHint"), provider.PluginName)
                            : string.Format(Localization.T("ProviderPluginOfflineHint"), provider.PluginName))
                        : provider.AutoModels
                            ? Localization.T("ProviderBuiltInHint")
                            : Localization.T("ProviderCustomHint");
                _lblStatus.Text = "";
                _btnRemoveProvider.Enabled = has && !plugin;
                _btnAddModel.Enabled = has;
                _btnRemoveModel.Enabled = has;
                _btnModelProps.Enabled = has;
                _btnTest.Enabled = has && (!plugin || provider.PluginReady);
            }
            finally
            {
                _filling = false;
            }
        }

        private void CommitProvider(ProviderConfig provider)
        {
            if (provider == null || _filling || provider.IsPlugin) return;
            string name = _txtName.Text.Trim();
            if (name.Length > 0) provider.Name = name;
            provider.BaseUrl = _txtUrl.Text.Trim();
            provider.KeyProtected = AppSettings.Protect(_txtKey.Text.Trim());
        }

        private void OnAddProvider(object sender, EventArgs e)
        {
            var items = new List<AntdUI.IContextMenuStripItem>();
            foreach (var preset in ProviderPresets.All)
                items.Add(new AntdUI.ContextMenuStripItem(preset.Name) { ID = preset.Kind });
            items.Add(new AntdUI.ContextMenuStripItemDivider());
            items.Add(new AntdUI.ContextMenuStripItem(Localization.T("CustomPreset")) { ID = "custom" });

            AntdUI.ContextMenuStrip.open(_btnAddProvider, selected =>
            {
                CommitProvider(_loaded);
                var preset = ProviderPresets.All.FirstOrDefault(p => p.Kind == selected?.ID);
                var provider = new ProviderConfig
                {
                    Kind = preset?.Kind ?? "custom",
                    Name = UniqueName(preset?.Name ?? Localization.T("CustomPreset")),
                    BaseUrl = preset?.BaseUrl ?? ""
                };
                _providers.Add(provider);
                RefreshProviderCombo(provider);
                _loaded = provider;
                LoadProvider(provider);
                _txtName.Focus();
            }, items.ToArray());
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            int n = 2;
            while (_providers.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                candidate = $"{name} {n++}";
            return candidate;
        }

        private void OnRemoveProvider(object sender, EventArgs e)
        {
            var provider = Current;
            if (provider == null) return;
            if (provider.IsPlugin)
            {
                AppNotify.warn(this, Localization.T("PluginProviderTitle"),
                    Localization.T("PluginProviderBody"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            if (MessageBox.Show(this, string.Format(Localization.T("DeleteProvider"), DisplayName(provider)),
                    Localization.T("SettingsTitle"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _providers.Remove(provider);
            var next = _providers.FirstOrDefault();
            RefreshProviderCombo(next);
            _loaded = next;
            LoadProvider(next);
        }

        private async void OnFetchModels(object sender, EventArgs e)
        {
            var provider = Current;
            if (provider == null) return;
            CommitProvider(provider);

            _btnFetch.Enabled = false;
            _lblStatus.Text = Localization.T("FetchingModels");
            try
            {
                var ids = await ProviderCatalog.FetchModelsAsync(provider);
                foreach (string id in ids)
                    if (!provider.Models.Contains(id, StringComparer.OrdinalIgnoreCase))
                        provider.Models.Add(id);

                if (string.Equals(provider.Kind, "openrouter", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var models = await OpenRouterCatalog.FetchModelsAsync(AppSettings.ResolveKey(provider));
                        if (models.Count > 0) ModelCatalog.Set(models);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("刷新免费模型目录失败：" + ex.Message);
                    }
                }

                _filling = true;
                try { _txtModelId.Text = ""; }
                finally { _filling = false; }
                ReloadModelList(provider);
                _lblStatus.Text = string.Format(Localization.T("FetchedModels"), ids.Count);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = Localization.T("FetchFailed");
                AppLog.Error("拉取模型失败", ex);
                AppNotify.error(this, Localization.T("FetchFailed"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
            }
            finally
            {
                _btnFetch.Enabled = true;
            }
        }

        private void ReloadModelList(ProviderConfig provider)
        {
            string filter = _txtModelId.Text.Trim();
            _listing = true;
            _lstModels.BeginUpdate();
            try
            {
                _lstModels.Items.Clear();
                if (provider == null) return;
                foreach (string id in provider.Models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                    if (filter.Length == 0 || id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        _lstModels.Items.Add(id, !_disabled.Contains(id));
            }
            finally
            {
                _lstModels.EndUpdate();
                _listing = false;
            }
        }

        /// <summary>勾选=进模型下拉；取消勾选只停用，不删配置（插件模型也一样）。</summary>
        private void OnModelChecked(object sender, ItemCheckEventArgs e)
        {
            if (_listing) return;
            string id = e.Index >= 0 && e.Index < _lstModels.Items.Count
                ? _lstModels.Items[e.Index] as string
                : null;
            if (string.IsNullOrWhiteSpace(id)) return;
            if (e.NewValue == CheckState.Checked) _disabled.Remove(id);
            else _disabled.Add(id);
        }

        private void OnModelSearchChanged(object sender, EventArgs e)
        {
            if (_filling) return;
            ReloadModelList(Current);
        }

        private void OnModelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddModel();
        }

        private void OnAddModel(object sender, EventArgs e) => AddModel();

        private void AddModel()
        {
            var provider = Current;
            if (provider == null) return;
            CommitProvider(provider);

            string id = _txtModelId.Text.Trim();
            if (id.Length == 0) return;

            if (provider.Models.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                _lstModels.SelectedItem = provider.Models.FirstOrDefault(m =>
                    string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
                return;
            }

            if (MessageBox.Show(this, string.Format(Localization.T("AddModelBody"), id, DisplayName(provider)),
                    Localization.T("AddModel"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            provider.Models.Add(id);
            _disabled.Remove(id);
            _filling = true;
            try { _txtModelId.Text = ""; }
            finally { _filling = false; }
            ReloadModelList(provider);
            _lstModels.SelectedItem = id;
            _txtModelId.Focus();
        }

        private void OnRemoveModel(object sender, EventArgs e)
        {
            var provider = Current;
            string id = _lstModels.SelectedItem as string;
            if (provider == null || string.IsNullOrWhiteSpace(id)) return;
            // 插件清单里没声明的（modelsUrl 发现来的）删不掉，停用即可
            if (provider.IsPlugin && !(provider.PluginDeclaredModels ?? [])
                    .Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                _disabled.Add(id);
                ReloadModelList(provider);
                return;
            }
            provider.Models.RemoveAll(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
            _disabled.Remove(id);
            ReloadModelList(provider);
        }

        /// <summary>编辑选中模型的 id / 显示名称 / 上下文上限；取消则原样保留。</summary>
        private void OnModelProps(object sender, EventArgs e)
        {
            var provider = Current;
            string id = _lstModels.SelectedItem as string;
            if (provider == null || string.IsNullOrWhiteSpace(id))
            {
                if (ReferenceEquals(sender, _btnModelProps))
                    AppNotify.warn(this, Localization.T("PickModelFirst"), Localization.T("PickModelFirstBody"),
                        AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            _modelProps.TryGetValue(id, out var current);
            using var form = new ModelPropsForm();
            // 自动值按「不带用户覆盖」算，否则打开属性页会把自己的值当成基准。
            form.Initialize(id, current?.Name ?? "", current?.ContextLimit ?? 0, ModelCatalog.ContextLimit(id));
            if (form.ShowDialog(this) != DialogResult.OK) return;

            string newId = form.ModelId;
            if (!string.Equals(newId, id, StringComparison.OrdinalIgnoreCase))
            {
                if (provider.Models.Contains(newId, StringComparer.OrdinalIgnoreCase))
                {
                    AppNotify.warn(this, Localization.T("ModelExists"), string.Format(Localization.T("ModelExistsBody"), newId),
                        AntdUI.TAlignFrom.TR, null, 4);
                    return;
                }
                int at = provider.Models.FindIndex(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) provider.Models[at] = newId;
                _modelProps.Remove(id);
                if (_disabled.Remove(id)) _disabled.Add(newId);
            }

            var props = new ModelProps { Name = form.DisplayName, ContextLimit = form.ContextLimit };
            if (props.Name.Length == 0 && props.ContextLimit <= 0) _modelProps.Remove(newId);
            else _modelProps[newId] = props;

            ReloadModelList(provider);
            _lstModels.SelectedItem = newId;
        }

        private void OnClearModels(object sender, EventArgs e)
        {
            var provider = Current;
            if (provider == null) return;
            CommitProvider(provider);
            int count = provider.Models.Count;
            foreach (string id in provider.Models) _disabled.Remove(id);
            provider.Models.Clear();
            _filling = true;
            try { _txtModelId.Text = ""; }
            finally { _filling = false; }
            ReloadModelList(provider);
            _lblStatus.Text = count > 0
                ? string.Format(Localization.T("ClearedModels"), count)
                : Localization.T("ModelListEmpty");
        }

        private async void OnTestProvider(object sender, EventArgs e)
        {
            var provider = Current;
            if (provider == null) return;
            CommitProvider(provider);

            string model = _lstModels.SelectedItem as string ?? provider.Models.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            {
                AppNotify.warn(this, Localization.T("MissingConfig"), Localization.T("MissingConfigBody"),
                    AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            _btnTest.Enabled = false;
            _lblStatus.Text = Localization.T("TestingConnection");
            try
            {
                var reasoner = new OpenAiCompatibleReasoner(
                    provider.BaseUrl.Trim(), model, AppSettings.ResolveKey(provider));
                var reply = await reasoner.CompleteAsync([new LlmMessage("user", "只回复两个字母：ok")]);
                _lblStatus.Text = Localization.T("ConnectionOk");
                AppNotify.success(this, Localization.T("ConnectionOk"),
                    $"{model} → {reply.Content?.Trim()}", AntdUI.TAlignFrom.TR, null, 4);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = Localization.T("ConnectionFailed");
                AppLog.Error("测试连接失败", ex);
                AppNotify.error(this, Localization.T("ConnectionFailed"), ex.Message, AntdUI.TAlignFrom.TR, null, 6);
            }
            finally
            {
                _btnTest.Enabled = true;
            }
        }

        private const string SkillScopeGlobal = "global";
        private const string SkillScopeProject = "project";

        private bool GlobalSkillScope
            => string.Equals(_selSkillScope.SelectedValue as string, SkillScopeGlobal, StringComparison.Ordinal);

        private string SkillRoot => GlobalSkillScope
            ? AppSettings.GlobalSkillsPath
            : Path.Combine(_workspace, ".openloop", "skills");

        /// <summary>项目技能库有两个：.openloop/skills 与 OpenCode 的 .opencode/skills（只读兼容）。</summary>
        private IEnumerable<string> ProjectSkillRoots()
        {
            if (GlobalSkillScope || string.IsNullOrWhiteSpace(_workspace)) return [];
            return [
                Path.Combine(_workspace, ".openloop", "skills"),
                Path.Combine(_workspace, ".opencode", "skills")
            ];
        }

        /// <summary>
        /// 技能列表：文件扫描和 I 的激活集都不该在 UI 线程上等——同步等待 HTTP 会把消息循环堵死
        /// （`GetResult()` 等续体、续体又排在同一个循环里，就是死锁）。
        /// 全局页不用工作区，开关读 skills.json；项目页读工作区账本，同名技能以项目那份为准。
        /// </summary>
        private async Task LoadSkillsAsync()
        {
            int load = ++_skillLoad;
            _skillRows.Clear();
            bool global = GlobalSkillScope;

            if (!global && string.IsNullOrWhiteSpace(_workspace))
            {
                _lblSkillsPath.Text = Localization.T("SkillsNoWorkspace");
                _tblSkills.EmptyText = Localization.T("SkillsNoneManageable");
                RebindSkills();
                return;
            }

            _lblSkillsPath.Text = SkillRoot;
            IReadOnlyList<SkillInfo> catalog = [];
            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                catalog = await Task.Run(() => global
                    ? SkillCatalog.Load(null, AppSettings.GlobalSkillsPath)
                    : SkillCatalog.Load(_workspace, AppSettings.GlobalSkillsPath));

                if (global)
                {
                    var disabled = await Task.Run(() => SkillStore.LoadDisabled(AppSettings.GlobalSkillsStatePath));
                    foreach (var skill in catalog)
                        if (!disabled.Contains(skill.Name)) enabled.Add(skill.Name);
                }
                else
                {
                    //账本归 I：这里只读它的激活集（I 会顺带启用首次出现的技能）
                    var payload = await Loop.GetActivationAsync(_workspace);
                    foreach (string name in payload?.Skills ?? []) enabled.Add(name);
                    if (payload is { Problems.Count: > 0 })
                        AppLog.Warn("技能不可用：" + string.Join("；", payload.Problems));
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取技能失败：" + ex.Message);
            }

            if (load != _skillLoad) return;   //切页/连点时只认最后一次
            _tblSkills.EmptyText = global ? Localization.T("SkillsEmptyGlobal") : Localization.T("SkillsEmptyProject");
            foreach (var skill in catalog.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                _skillRows.Add(new SkillRow(skill.Name, skill.Description,
                    enabled.Contains(skill.Name),
                    skill.Global ? Localization.T("ScopeGlobal")
                        : (skill.Source ?? "").StartsWith(".opencode/", StringComparison.Ordinal) ? Localization.T("ProjectOpencode")
                        : Localization.T("ScopeProject"),
                    skill));
            }
            RebindSkills();
        }

        private void RebindSkills()
        {
            _tblSkills.DataSource = null;
            _tblSkills.DataSource = _skillRows;
            UpdateSkillToggle();
        }

        private void OnSkillSelectionChanged(object sender, EventArgs e) => UpdateSkillToggle();

        /// <summary>一个按钮管两边：选中里只要有一个已启用就停用，否则启用；没选中时只是提示文案。</summary>
        private void UpdateSkillToggle()
        {
            var rows = SelectedSkills();
            _btnSkillToggle.Text = rows.Count == 0
                ? Localization.T("SkillToggle")
                : rows.Any(row => row.On) ? Localization.T("SkillDisable") : Localization.T("SkillEnable");
        }

        private void OnSkillScopeChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_filling) return;
            _ = LoadSkillsAsync();
        }

        private void OnSkillToggle(object sender, EventArgs e) => _ = ToggleSelectedSkillsAsync();

        private async Task ToggleSelectedSkillsAsync()
        {
            var rows = SelectedSkills();
            if (rows.Count == 0) return;
            bool enabled = !rows.Any(row => row.On);
            try
            {
                if (GlobalSkillScope)
                    await Task.Run(() => SkillStore.SetEnabled(
                        AppSettings.GlobalSkillsStatePath, rows.Select(row => row.Name), enabled));
                else
                    foreach (var row in rows) await Loop.SetSkillAsync(_workspace, row.Name, enabled);
            }
            catch (Exception ex)
            {
                AppLog.Error("切换技能失败", ex);
                AppNotify.error(this, Localization.T("TabSkills"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }
            await LoadSkillsAsync();
        }

        private void OnSkillInstall(object sender, EventArgs e) => _ = InstallSkillAsync();

        private async Task InstallSkillAsync()
        {
            if (!GlobalSkillScope && string.IsNullOrWhiteSpace(_workspace)) return;
            string root = SkillRoot;
            using var dialog = new FolderBrowserDialog { Description = Localization.T("PickSkillFolder") };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

            try
            {
                string[] others = ProjectSkillRoots().Where(r => !Paths.Same(r, root)).ToArray();
                await Task.Run(() => SkillStore.Install(dialog.SelectedPath, root, others));
            }
            catch (Exception ex)
            {
                AppNotify.error(this, Localization.T("InstallSkill"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }
            await LoadSkillsAsync();
        }

        private void OnSkillUninstall(object sender, EventArgs e) => _ = UninstallSkillAsync();

        private async Task UninstallSkillAsync()
        {
            var rows = SelectedSkills();
            if (rows.Count == 0) return;
            if (rows.Count > 1)
            {
                AppNotify.warn(this, Localization.T("UninstallSkill"), Localization.T("UninstallOneOnly"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            var row = rows[0];
            if (row.Skill == null) return;
            //项目页同名的全局技能只是被项目那份盖住，卸载得回全局页做
            if (!GlobalSkillScope && row.Skill.Global)
            {
                AppNotify.warn(this, Localization.T("UninstallSkill"), Localization.T("UninstallGlobalFirst"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            //.opencode 下的是 opencode agent 装的：不归 OpenLoop 管，也不在技能库根目录下
            if (!GlobalSkillScope && (row.Skill.Source ?? "").StartsWith(".opencode/", StringComparison.Ordinal))
            {
                AppNotify.warn(this, Localization.T("UninstallSkill"),
                    Localization.T("UninstallOpencodeOwned"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            if (MessageBox.Show(this,
                    string.Format(Localization.T("UninstallSkillConfirm"), row.Name, row.Skill.Directory),
                    Localization.T("UninstallSkill"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            // 技能可能来自 .openloop 或 .opencode 两个项目技能库：用技能自己的目录做根，
            // Uninstall 仍只允许删「库里那一层目录」，不会连库一起删
            string root = GlobalSkillScope ? SkillRoot : Path.GetDirectoryName(row.Skill.Directory);
            bool global = GlobalSkillScope;
            try
            {
                await Task.Run(() => SkillStore.Uninstall(row.Skill, root));
                //全局那份卸载后把开关条目清掉，重装同名技能时默认启用
                if (global) await Task.Run(() => SkillStore.SetEnabled(
                    AppSettings.GlobalSkillsStatePath, row.Name, true));
            }
            catch (Exception ex)
            {
                AppNotify.error(this, Localization.T("UninstallSkill"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }
            await LoadSkillsAsync();
        }

        /// <summary>选中的行（Table 的 SelectedIndexs 是 1 起）；Ctrl 逐点、Shift 连选由表格自己处理。</summary>
        private List<SkillRow> SelectedSkills()
            => (_tblSkills.SelectedIndexs ?? [])
                .Where(index => index >= 1 && index <= _skillRows.Count)
                .Select(index => _skillRows[index - 1])
                .Distinct()
                .ToList();

        private static AntdUI.CellBadge SkillBadge(SkillRow row)
            => row?.On == true
                ? new AntdUI.CellBadge(AntdUI.TState.Success, Localization.T("Enabled"))
                : new AntdUI.CellBadge(AntdUI.TState.Default, Localization.T("Disabled"));

        private void OnSave(object sender, EventArgs e)
        {
            if (_switchFullAccess.Checked && !_source.FullAccess
                && MessageBox.Show(this,
                    Localization.T("FullAccessConfirm"),
                    Localization.T("FullAccess"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            CommitProvider(_loaded);
            SavePluginProviders();
            var first = _providers.FirstOrDefault(p => p.Models.Count > 0) ?? _providers.FirstOrDefault();

            Result = new AppSettings
            {
                BaseUrl = first?.BaseUrl ?? _source.BaseUrl,
                Model = first?.Models.FirstOrDefault() ?? _source.Model,
                KeyProtected = first?.KeyProtected ?? "",
                Workspace = _source.Workspace,
                Projects = _source.Projects,
                Theme = _cboTheme.SelectedValue as string ?? _source.Theme,
                Language = _cboLanguage.SelectedValue as string ?? _source.Language,
                OnlyFreeModels = _source.OnlyFreeModels,
                FullAccess = _switchFullAccess.Checked,
                AutoApprove = _switchAutoApprove.Checked,
                OpenCodeAskPermissions = _switchOpenCode.Checked,
                WebFetchMode = _cboWebFetch.SelectedValue as string ?? _source.WebFetchMode,
                //记住的「后续都许可」由审批弹框写入，设置页不重置
                AllowOutsideWorkspace = _source.AllowOutsideWorkspace,
                AllowSensitiveActions = _source.AllowSensitiveActions,
                UseAgentsMd = _switchAgents.Checked,
                ThinkingExpandedByDefault = _switchThinking.Checked,
                MarkdownMaxChars = _source.MarkdownMaxChars,
                AutoEvolve = _switchAutoEvolve.Checked,
                AutoIterate = _switchAutoIterate.Checked,
                EnableCollab = _switchEnableCollab.Checked,
                CollabPollSeconds = _cboCollabPoll.SelectedValue is int poll ? poll : _source.CollabPollSeconds,
                CollabReuseContext = _switchCollabReuse.Checked,
                AutoEvolveRuns = _source.AutoEvolveRuns,
                AutoEvolveCooldownMinutes = _source.AutoEvolveCooldownMinutes,
                AutoEvolvePerDay = _source.AutoEvolvePerDay,
                ReasoningEffort = _source.ReasoningEffort,
                LoopCommand = _source.LoopCommand,
                ModelProps = new Dictionary<string, ModelProps>(_modelProps, StringComparer.OrdinalIgnoreCase),
                DisabledModels = [.. _disabled],
                Providers = _providers
            };
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;

        /// <summary>设置里的秒数落到下拉档位：没有正好相等的档就取最近的上一档。</summary>
        private static int PollChoice(int seconds) => seconds switch
        {
            <= 10 => 10,
            <= 30 => 30,
            <= 60 => 60,
            <= 120 => 120,
            _ => 300
        };

        /// <summary>点了「让 AI 帮我配置」时记下当前页；非空表示关掉设置页后由 MainForm 起一个 Loop 会话。</summary>
        public string AiConfigPage { get; private set; }

        private void OnAiConfig(object sender, EventArgs e)
        {
            // 对话框是模态的，必须先关掉用户才能去 Loop 页答话；未保存的改动按「取消」处理
            AiConfigPage = (_tabs.SelectedTab as AntdUI.TabPage)?.Text ?? "";
            DialogResult = DialogResult.Cancel;
        }
    }
}
