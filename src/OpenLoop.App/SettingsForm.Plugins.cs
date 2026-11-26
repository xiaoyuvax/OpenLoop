using System.Diagnostics;
using OpenLoop.Service.Plugins;

namespace OpenLoop.App
{
    /// <summary>
    /// 设置页的「插件」页：读写两处 plugin.json（&lt;工作区&gt;/.openloop/plugins 与 %APPDATA%\OpenLoop\plugins），
    /// 不托管插件进程。清单的增删改查都在 <see cref="PluginStore"/>，Web 设置页用的也是那一份。
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private List<PluginStore.Row> _pluginRows = [];
        private int _pluginLoad;
        private TableLayoutPanel _plugins;
        private AntdUI.Label _lblPluginsPath;
        private AntdUI.Table _tblPlugins;
        private AntdUI.Button _btnPluginRefresh;
        private AntdUI.Button _btnPluginAdd;
        private AntdUI.Button _btnPluginEdit;
        private AntdUI.Button _btnPluginOpen;
        private AntdUI.Button _btnPluginRemove;
        private AntdUI.Label _lblPluginsHint;

        /// <summary>这一页整体在代码里搭，Designer 里只多挂一个空 TabPage，省得动那 800 行。</summary>
        private void InitPluginsPage(AntdUI.TabPage page)
        {
            _plugins = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8, 12, 8, 8)
            };
            _plugins.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _plugins.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _plugins.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _plugins.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _plugins.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));

            _lblPluginsPath = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                ForeColor = UiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0),
                Text = PluginStore.GlobalRoot
            };

            _btnPluginRefresh = new AntdUI.Button
            {
                Text = Localization.T("Refresh"),
                Radius = 8,
                Size = new Size(88, 30),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            _btnPluginRefresh.Click += OnPluginRefresh;

            _tblPlugins = new AntdUI.Table
            {
                Dock = DockStyle.Fill,
                Bordered = false,
                VisibleHeader = true,
                RowHeight = 40,
                MultipleRows = false,
                EmptyText = Localization.T("NoPlugins"),
                Margin = new Padding(0, 4, 0, 4)
            };
            _tblPlugins.SelectIndexChanged += OnPluginSelectionChanged;
            _tblPlugins.Columns.Add(new AntdUI.Column("Name", Localization.T("Name")) { Width = "180" });
            _tblPlugins.Columns.Add(new AntdUI.Column("Id", "id") { Width = "110" });
            _tblPlugins.Columns.Add(new AntdUI.Column("Version", Localization.T("ColVersion")) { Width = "70" });
            _tblPlugins.Columns.Add(new AntdUI.Column("Kind", Localization.T("ColContribution")) { Width = "170" });
            _tblPlugins.Columns.Add(new AntdUI.Column("From", Localization.T("ColFrom")) { Width = "80" });

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _btnPluginAdd = new AntdUI.Button
            {
                Text = Localization.T("Add"),
                Radius = 8,
                Size = new Size(88, 30),
                Margin = new Padding(0, 2, 8, 0)
            };
            _btnPluginAdd.Click += OnPluginAdd;

            _btnPluginEdit = new AntdUI.Button
            {
                Text = Localization.T("Edit"),
                Type = AntdUI.TTypeMini.Primary,
                Radius = 8,
                Size = new Size(88, 30),
                Margin = new Padding(0, 2, 8, 0),
                Enabled = false
            };
            _btnPluginEdit.Click += OnPluginEdit;

            _btnPluginOpen = new AntdUI.Button
            {
                Text = Localization.T("OpenPluginFolder"),
                Radius = 8,
                Size = new Size(96, 30),
                Margin = new Padding(0, 2, 8, 0),
                Enabled = false
            };
            _btnPluginOpen.Click += OnPluginOpen;

            _btnPluginRemove = new AntdUI.Button
            {
                Text = Localization.T("Uninstall"),
                Radius = 8,
                Size = new Size(88, 30),
                Margin = new Padding(0, 2, 8, 0),
                Enabled = false
            };
            _btnPluginRemove.Click += OnPluginRemove;

            _lblPluginsHint = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = UiStyle.Muted,
                AutoEllipsis = true,
                Margin = new Padding(0, 4, 0, 0),
                Text = Localization.T("PluginsNote")
            };

            buttons.Controls.Add(_btnPluginAdd);
            buttons.Controls.Add(_btnPluginEdit);
            buttons.Controls.Add(_btnPluginOpen);
            buttons.Controls.Add(_btnPluginRemove);

            header.Controls.Add(_lblPluginsPath, 0, 0);
            header.Controls.Add(_btnPluginRefresh, 1, 0);
            _plugins.Controls.Add(header, 0, 0);
            _plugins.Controls.Add(_tblPlugins, 0, 1);
            _plugins.Controls.Add(buttons, 0, 2);
            _plugins.Controls.Add(_lblPluginsHint, 0, 3);
            page.Controls.Add(_plugins);
        }

        private void OnPluginRefresh(object sender, EventArgs e) => _ = LoadPluginsAsync();

        private void OnPluginSelectionChanged(object sender, EventArgs e) => UpdatePluginButtons();

        /// <summary>选中的那行（Table 的 SelectedIndexs 从 1 起）。</summary>
        private PluginStore.Row CurrentPlugin
        {
            get
            {
                int index = (_tblPlugins.SelectedIndexs ?? []).FirstOrDefault();
                return index >= 1 && index <= _pluginRows.Count ? _pluginRows[index - 1] : null;
            }
        }

        private void UpdatePluginButtons()
        {
            var row = CurrentPlugin;
            _btnPluginOpen.Enabled = row != null;
            _btnPluginEdit.Enabled = row is { Providers: > 0 };
            _btnPluginRemove.Enabled = row != null;
        }

        /// <summary>文件扫描不在 UI 线程上等；连点只认最后一次。</summary>
        private async Task LoadPluginsAsync()
        {
            int load = ++_pluginLoad;
            _pluginRows = [];
            RebindPlugins();

            _lblPluginsPath.Text = string.IsNullOrWhiteSpace(_workspace)
                ? PluginStore.GlobalRoot + Localization.T("OnlyGlobalNoWorkspace")
                : PluginStore.RootFor(_workspace) + "　·　" + PluginStore.GlobalRoot;

            List<PluginStore.Row> rows;
            try
            {
                rows = await Task.Run(() => PluginStore.List(_workspace));
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取插件失败：" + ex.Message);
                return;
            }

            if (load != _pluginLoad) return;
            _pluginRows = rows;
            RebindPlugins();
            UpdatePluginButtons();
        }

        private void RebindPlugins()
        {
            _tblPlugins.DataSource = null;
            _tblPlugins.DataSource = _pluginRows;
        }

        /// <summary>新插件写进当前工作区；没开工作区就写全局。</summary>
        private void OnPluginAdd(object sender, EventArgs e)
        {
            try
            {
                using var dialog = PluginEditForm.Create(_workspace);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            catch (Exception ex)
            {
                AppNotify.error(this, Localization.T("AddFailed"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }

            _ = LoadPluginsAsync();
            RefreshPluginPull();
        }

        private void OnPluginEdit(object sender, EventArgs e)
        {
            var row = CurrentPlugin;
            if (row == null) return;
            if (row.Providers == 0)
            {
                AppNotify.warn(this, Localization.T("NotEditable"),
                    Localization.T("NotEditableBody"), AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            try
            {
                using var dialog = PluginEditForm.Edit(_workspace, row.Directory);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            catch (Exception ex)
            {
                AppNotify.error(this, Localization.T("CannotOpen"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }

            _ = LoadPluginsAsync();
            RefreshPluginPull();
        }

        private void OnPluginOpen(object sender, EventArgs e)
        {
            string dir = CurrentPlugin?.Directory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开插件目录失败：" + ex.Message);
            }
        }

        private void OnPluginRemove(object sender, EventArgs e)
        {
            var row = CurrentPlugin;
            if (row == null) return;

            string where = string.Equals(row.From, Localization.T("ScopeGlobal"), StringComparison.Ordinal)
                ? Localization.T("ScopeGlobal")
                : Localization.T("CurrentWorkspace");
            if (AntdUI.Modal.open(new AntdUI.Modal.Config(this,
                    Localization.T("UninstallPlugin"),
                    string.Format(Localization.T("UninstallPluginBody"), where, row.Id), AntdUI.TType.Warn))
                != DialogResult.OK)
                return;

            try
            {
                PluginStore.Remove(_workspace, row.Directory);
            }
            catch (Exception ex)
            {
                AppLog.Warn("卸载插件失败：" + ex.Message);
                AppNotify.error(this, Localization.T("UninstallFailed"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                return;
            }

            _ = LoadPluginsAsync();
            RefreshPluginPull();
        }

        /// <summary>清单变了，会话/模型下拉得重填；服务型插件还可能要起进程，交给 PluginRefill 补拍。</summary>
        private void RefreshPluginPull() => PluginRefill.WhenReady(this, () => { });

        /// <summary>
        /// 「模型」页把插件的 provider 贡献也列为一行（名字后面标「（插件）」）：
        /// 模型列表与停用状态在这里统一管理，改造写回 plugin.json / settings.json 各自的文件。
        /// </summary>
        private async Task LoadPluginProvidersAsync()
        {
            int load = ++_providerLoad;
            List<PluginCatalog.PluginProviderEntry> entries;
            try
            {
                entries = await Task.Run(() =>
                {
                    using var catalog = PluginCatalog.Load(_workspace);
                    return catalog.ProviderEntries().ToList();
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取插件提供商失败：" + ex.Message);
                return;
            }
            if (load != _providerLoad) return;

            var select = _cboProvider.SelectedValue as ProviderConfig;
            _pluginProviders.Clear();
            foreach (var entry in entries)
                _pluginProviders.Add(new ProviderConfig
                {
                    Name = entry.Name,
                    Kind = "custom",
                    BaseUrl = entry.ResolvedBaseUrl ?? "",
                    Models = [.. entry.Models],
                    PluginDirectory = entry.Directory,
                    PluginProviderIndex = entry.Index,
                    PluginId = entry.PluginId,
                    PluginName = entry.PluginName,
                    PluginRawBaseUrl = entry.BaseUrl,
                    PluginRawModelsUrl = entry.ModelsUrl,
                    PluginRawModelPrefix = entry.ModelPrefix,
                    PluginDeclaredModels = [.. entry.Declared],
                    PluginKnownModels = [.. entry.Models],
                    PluginReady = entry.Ready,
                    RuntimeKey = entry.Key
                });

            var next = select?.IsPlugin == true
                ? _pluginProviders.FirstOrDefault(p => p.PluginDirectory == select.PluginDirectory
                    && p.PluginProviderIndex == select.PluginProviderIndex)
                : select;
            next ??= _pluginProviders.FirstOrDefault() ?? _providers.FirstOrDefault();
            RefreshProviderCombo(next);
            _loaded = Current;
            LoadProvider(_loaded);
        }

        /// <summary>把模型页里对插件 provider 的增删写回它自己的 plugin.json（发现的模型只增不删）。</summary>
        private void SavePluginProviders()
        {
            foreach (var provider in _pluginProviders)
            {
                var known = new HashSet<string>(provider.PluginKnownModels ?? [], StringComparer.OrdinalIgnoreCase);
                var declaredAtLoad = new HashSet<string>(provider.PluginDeclaredModels ?? [], StringComparer.OrdinalIgnoreCase);
                var declared = provider.Models
                    .Where(id => !known.Contains(id) || declaredAtLoad.Contains(id))
                    .ToList();
                // 没动模型列表就不碰清单，免得把用户手写的 plugin.json 重排一遍
                if (declared.Count == declaredAtLoad.Count && declared.All(declaredAtLoad.Contains)) continue;
                try
                {
                    PluginStore.Save(_workspace, new PluginStore.Item
                    {
                        Directory = provider.PluginDirectory,
                        Id = provider.PluginId,
                        ProviderIndex = provider.PluginProviderIndex,
                        Name = provider.Name,
                        BaseUrl = provider.PluginRawBaseUrl,
                        ModelsUrl = provider.PluginRawModelsUrl,
                        ModelPrefix = provider.PluginRawModelPrefix,
                        ModelList = declared
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"写回插件清单失败：{provider.PluginDirectory} {ex.Message}");
                    AppNotify.error(this, Localization.T("PluginManifest"), ex.Message, AntdUI.TAlignFrom.TR, null, 5);
                }
            }
        }
    }
}
