using OpenLoop.Core.Messages;
using OpenLoop.Core.Reasoning;

namespace OpenLoop.App
{
    internal sealed partial class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private readonly List<ProviderConfig> _providers = [];
        private ProviderConfig _loaded;
        private bool _filling;

        public SettingsForm(AppSettings source)
        {
            _source = source ?? new AppSettings();
            InitializeComponent();
            SelectTips.Bind(_tips, _cboLanguage, _cboTheme, _cboProvider);
            Icon = UiIcons.App;

            foreach (var provider in _source.Providers ?? [])
                _providers.Add(Clone(provider));

            _filling = true;
            _cboLanguage.SelectedValue = _source.Language ?? "";
            _cboTheme.SelectedValue = string.IsNullOrWhiteSpace(_source.Theme) ? "light" : _source.Theme;
            _switchFullAccess.Checked = _source.FullAccess;
            _switchAgents.Checked = _source.UseAgentsMd;
            _switchThinking.Checked = _source.ThinkingExpandedByDefault;
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
                UseAgentsMd = _switchAgents.Checked,
                ThinkingExpandedByDefault = _switchThinking.Checked,
                ReasoningEffort = _source.ReasoningEffort,
                LoopCommand = _source.LoopCommand,
                Providers = _providers
            };
            DialogResult = DialogResult.OK;
        }

        private void OnCancel(object sender, EventArgs e) => DialogResult = DialogResult.Cancel;
    }
}
