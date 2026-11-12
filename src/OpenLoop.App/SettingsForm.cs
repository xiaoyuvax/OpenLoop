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
        private readonly List<SkillRow> _skillRows = [];
        private int _skillLoad;
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

            foreach (var provider in _source.Providers ?? [])
                _providers.Add(Clone(provider));
            foreach (var pair in _source.ModelProps ?? [])
                _modelProps[pair.Key] = new ModelProps { Name = pair.Value?.Name ?? "", ContextLimit = pair.Value?.ContextLimit ?? 0 };

            _filling = true;
            _cboLanguage.SelectedValue = _source.Language ?? "";
            _cboTheme.SelectedValue = string.IsNullOrWhiteSpace(_source.Theme) ? "light" : _source.Theme;
            _switchFullAccess.Checked = _source.FullAccess;
            _switchAutoApprove.Checked = _source.AutoApprove;
            _switchOpenCode.Checked = _source.OpenCodeAskPermissions;
            _switchAgents.Checked = _source.UseAgentsMd;
            _switchThinking.Checked = _source.ThinkingExpandedByDefault;
            _switchAutoEvolve.Checked = _source.AutoEvolve;
            _filling = false;

            RefreshProviderCombo(_providers.FirstOrDefault());
            _loaded = _providers.FirstOrDefault();
            LoadProvider(_loaded);
        }

        public AppSettings Result { get; private set; }

        private ProviderConfig Current => _cboProvider.SelectedValue as ProviderConfig;

        private static ProviderConfig Clone(ProviderConfig p) => new()
        {
            Name = p.Name,
            Kind = p.Kind,
            BaseUrl = p.BaseUrl,
            KeyProtected = p.KeyProtected,
            Models = [.. (p.Models ?? [])]
        };

        private void RefreshProviderCombo(ProviderConfig select)
        {
            _filling = true;
            try
            {
                _cboProvider.Items.Clear();
                foreach (var provider in _providers)
                    _cboProvider.Items.Add(new AntdUI.SelectItem(DisplayName(provider)) { Tag = provider });
                if (select != null) _cboProvider.SelectedValue = select;
            }
            finally
            {
                _filling = false;
            }
        }

        private static string DisplayName(ProviderConfig p)
            => string.IsNullOrWhiteSpace(p.Name) ? "未命名" : p.Name.Trim();

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
                _txtName.Text = has ? provider.Name : "";
                _txtUrl.Text = has ? provider.BaseUrl : "";
                _txtKey.Text = has ? AppSettings.Unprotect(provider.KeyProtected) : "";
                _txtModelId.Text = "";
                ReloadModelList(provider);

                _btnFetch.Visible = has && provider.AutoModels;
                _lblHint.Text = !has
                    ? "先添加一个提供商。"
                    : provider.AutoModels
                        ? "内置提供商：点「拉取模型」同步该提供商的模型列表。"
                        : "自定义提供商：逐条添加模型 id（例如 gpt-4o-mini）。";
                _lblStatus.Text = "";
                _btnRemoveProvider.Enabled = has;
                _btnAddModel.Enabled = has;
                _btnRemoveModel.Enabled = has;
                _btnModelProps.Enabled = has;
                _btnTest.Enabled = has;
            }
            finally
            {
                _filling = false;
            }
        }

        private void CommitProvider(ProviderConfig provider)
        {
            if (provider == null || _filling) return;
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
            items.Add(new AntdUI.ContextMenuStripItem("自定义") { ID = "custom" });

            AntdUI.ContextMenuStrip.open(_btnAddProvider, selected =>
            {
                CommitProvider(_loaded);
                var preset = ProviderPresets.All.FirstOrDefault(p => p.Kind == selected?.ID);
                var provider = new ProviderConfig
                {
                    Kind = preset?.Kind ?? "custom",
                    Name = UniqueName(preset?.Name ?? "自定义"),
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
            if (MessageBox.Show(this, $"删除提供商「{DisplayName(provider)}」？", "设置",
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
            _lblStatus.Text = "正在拉取模型…";
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
                _lblStatus.Text = $"拉取到 {ids.Count} 个模型";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "拉取失败";
                AppLog.Error("拉取模型失败", ex);
                AntdUI.Notification.error(this, "拉取失败", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
            }
            finally
            {
                _btnFetch.Enabled = true;
            }
        }

        private void ReloadModelList(ProviderConfig provider)
        {
            string filter = _txtModelId.Text.Trim();
            _lstModels.BeginUpdate();
            try
            {
                _lstModels.Items.Clear();
                if (provider == null) return;
                foreach (string id in provider.Models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                    if (filter.Length == 0 || id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        _lstModels.Items.Add(id);
            }
            finally
            {
                _lstModels.EndUpdate();
            }
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

            if (MessageBox.Show(this, $"把模型「{id}」加入「{DisplayName(provider)}」？", "添加模型",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            provider.Models.Add(id);
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
            provider.Models.RemoveAll(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
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
                    AntdUI.Notification.warn(this, "先选模型", "在下面的列表里选中一个模型再改属性。",
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
                    AntdUI.Notification.warn(this, "模型已存在", $"列表里已有 {newId}，换个 id。",
                        AntdUI.TAlignFrom.TR, null, 4);
                    return;
                }
                int at = provider.Models.FindIndex(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) provider.Models[at] = newId;
                _modelProps.Remove(id);
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
            provider.Models.Clear();
            _filling = true;
            try { _txtModelId.Text = ""; }
            finally { _filling = false; }
            ReloadModelList(provider);
            _lblStatus.Text = count > 0 ? $"已清除 {count} 个模型" : "模型列表已为空";
        }

        private async void OnTestProvider(object sender, EventArgs e)
        {
            var provider = Current;
            if (provider == null) return;
            CommitProvider(provider);

            string model = _lstModels.SelectedItem as string ?? provider.Models.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            {
                AntdUI.Notification.warn(this, "缺少配置", "接口地址和至少一个模型都要有。",
                    AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            _btnTest.Enabled = false;
            _lblStatus.Text = "正在测试连接…";
            try
            {
                var reasoner = new OpenAiCompatibleReasoner(
                    provider.BaseUrl.Trim(), model, AppSettings.ResolveKey(provider));
                var reply = await reasoner.CompleteAsync([new LlmMessage("user", "只回复两个字母：ok")]);
                _lblStatus.Text = "连接正常";
                AntdUI.Notification.success(this, "连接正常",
                    $"{model} → {reply.Content?.Trim()}", AntdUI.TAlignFrom.TR, null, 4);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "连接失败";
                AppLog.Error("测试连接失败", ex);
                AntdUI.Notification.error(this, "连接失败", ex.Message, AntdUI.TAlignFrom.TR, null, 6);
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
                _lblSkillsPath.Text = "先在项目里打开一个会话，或切到「全局」管理全局技能";
                _tblSkills.EmptyText = "没有可管理的技能";
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
            _tblSkills.EmptyText = global ? "全局还没有技能" : "这个工作区还没有技能";
            foreach (var skill in catalog.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                _skillRows.Add(new SkillRow(skill.Name, skill.Description,
                    enabled.Contains(skill.Name), skill.Global ? "全局" : "项目", skill));
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
            _btnSkillToggle.Text = rows.Count == 0 ? "启用/停用" : rows.Any(row => row.On) ? "停用" : "启用";
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
                AntdUI.Notification.error(this, "技能", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }
            await LoadSkillsAsync();
        }

        private void OnSkillInstall(object sender, EventArgs e) => _ = InstallSkillAsync();

        private async Task InstallSkillAsync()
        {
            if (!GlobalSkillScope && string.IsNullOrWhiteSpace(_workspace)) return;
            string root = SkillRoot;
            using var dialog = new FolderBrowserDialog { Description = "选择技能目录（里面要有 SKILL.md）" };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

            try
            {
                await Task.Run(() => SkillStore.Install(dialog.SelectedPath, root));
            }
            catch (Exception ex)
            {
                AntdUI.Notification.error(this, "安装技能", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
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
                AntdUI.Notification.warn(this, "卸载技能", "一次只卸载一个技能", AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            var row = rows[0];
            if (row.Skill == null) return;
            //项目页同名的全局技能只是被项目那份盖住，卸载得回全局页做
            if (!GlobalSkillScope && row.Skill.Global)
            {
                AntdUI.Notification.warn(this, "卸载技能", "全局技能请切到「全局」里卸载", AntdUI.TAlignFrom.TR, null, 4);
                return;
            }
            if (MessageBox.Show(this,
                    $"确定卸载技能「{row.Name}」？\n\n会删除整个目录，不可恢复：\n{row.Skill.Directory}",
                    "卸载技能", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string root = SkillRoot;
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
                AntdUI.Notification.error(this, "卸载技能", ex.Message, AntdUI.TAlignFrom.TR, null, 5);
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
                ? new AntdUI.CellBadge(AntdUI.TState.Success, "启用")
                : new AntdUI.CellBadge(AntdUI.TState.Default, "停用");

        private void OnSave(object sender, EventArgs e)
        {
            if (_switchFullAccess.Checked && !_source.FullAccess
                && MessageBox.Show(this,
                    "完全许可会关闭路径边界、命令审批与环境变量隔离，子进程可能拿到密钥，也可以直接访问网络与任意路径。确定启用？",
                    "完全许可", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            CommitProvider(_loaded);
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
                //记住的「后续都许可」由审批弹框写入，设置页不重置
                AllowOutsideWorkspace = _source.AllowOutsideWorkspace,
                AllowSensitiveActions = _source.AllowSensitiveActions,
                UseAgentsMd = _switchAgents.Checked,
                ThinkingExpandedByDefault = _switchThinking.Checked,
                AutoEvolve = _switchAutoEvolve.Checked,
                AutoEvolveRuns = _source.AutoEvolveRuns,
                AutoEvolveCooldownMinutes = _source.AutoEvolveCooldownMinutes,
                AutoEvolvePerDay = _source.AutoEvolvePerDay,
                ReasoningEffort = _source.ReasoningEffort,
                LoopCommand = _source.LoopCommand,
                ModelProps = new Dictionary<string, ModelProps>(_modelProps, StringComparer.OrdinalIgnoreCase),
                Providers = _providers
            };
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
