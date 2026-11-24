using OpenLoop.Core.Store;
using OpenLoop.Service.Web;

namespace OpenLoop.App
{
    /// <summary>
    /// 工作区 Harness 配置窗体：管本工作区激活集里的技能 / 规则 / 限制 / 护栏四类对象。
    /// 与「设置」页同一个套路（表格列、按类一页、底部按钮），但只管一个工作区；
    /// 所有读写都走 I 的 /api/loop/objects——账本只由 I 写，界面不碰文件。
    /// </summary>
    internal sealed partial class WorkspaceHarnessForm : Form
    {
        /// <summary>表格一行（Table 按属性名取值；On 由 Render 画成徽标）。</summary>
        internal sealed record ObjectRow(HarnessItem Item)
        {
            public bool On => Item.Active;
            public string Name => Item.Name;
            public string Params => Item.ParamsText;
            public string Note => string.IsNullOrWhiteSpace(Item.Text) ? Item.Problem : Item.Text;
        }

        private readonly string _workspace;
        private readonly Dictionary<string, List<ObjectRow>> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AntdUI.Table> _tables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AntdUI.Button> _resetButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AntdUI.Button> _toggleButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AntdUI.Button> _paramButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AntdUI.Button> _defaultButtons = new(StringComparer.Ordinal);
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _filling;
        private LoopApiClient _client;

        public WorkspaceHarnessForm(string workspace)
        {
            _workspace = workspace ?? "";
            InitializeComponent();
            Icon = UiIcons.App;
            Disposed += (_, _) => _client?.Dispose();
            BuildPages();
            _tabs.SelectedIndexChanged += OnTabChanged;
            SelectKind(HarnessObjects.Skill);
            _ = ReloadAllAsync();
        }

        /// <summary>当前停在哪个页（技能 / 规则 / 限制 / 护栏），「让Agent辅助我配置」按它给上下文。</summary>
        public string CurrentPage
            => string.Equals(PageKind(_tabs.SelectedIndex), HarnessObjects.Skill, StringComparison.Ordinal)
                ? Localization.T("TabSkills")
                : string.Equals(PageKind(_tabs.SelectedIndex), HarnessObjects.Rule, StringComparison.Ordinal)
                    ? Localization.T("HarnessTabRules")
                    : string.Equals(PageKind(_tabs.SelectedIndex), HarnessObjects.Limit, StringComparison.Ordinal)
                        ? Localization.T("HarnessTabLimits")
                        : Localization.T("HarnessTabGuards");

        /// <summary>用户点了「让Agent辅助我配置」：关窗体，把当前页交给 Loop 会话。</summary>
        public string AiConfigPage { get; private set; }

        /// <summary>有一个对象被改过：外层据此刷新账本摘要。</summary>
        public bool Changed { get; private set; }

        /// <summary>宿主入口：打开配置窗体并停在指定类别（别人的调用点用 kind 常量）。</summary>
        internal static void Open(Form owner, string workspace, string kind, Action<string> aiConfig)
        {
            if (!Directory.Exists(workspace))
            {
                AntdUI.Notification.error(owner, Localization.T("PickWorkspace"),
                    Localization.T("WorkspaceMissing") + workspace, AntdUI.TAlignFrom.TR, null, 4);
                return;
            }

            using var form = new WorkspaceHarnessForm(workspace);
            form.SelectKind(kind);
            form.ShowDialog(owner);
            if (!string.IsNullOrWhiteSpace(form.AiConfigPage)) aiConfig?.Invoke(form.AiConfigPage);
        }

        private LoopApiClient Loop => _client ??= new LoopApiClient();

        private static string KindOfPage(int index) => index switch
        {
            1 => HarnessObjects.Rule,
            2 => HarnessObjects.Limit,
            3 => HarnessObjects.Guard,
            _ => HarnessObjects.Skill
        };

        private static string PageKind(int index) => KindOfPage(index);

        private void SelectKind(string kind)
        {
            int index = Array.IndexOf(HarnessObjects.Kinds, kind ?? "");
            _filling = true;
            try { _tabs.SelectedIndex = index < 0 ? 0 : index; }
            finally { _filling = false; }
            ApplyPage(PageKind(_tabs.SelectedIndex));
        }

        private void OnTabChanged(object sender, AntdUI.IntEventArgs e)
            => ApplyPage(PageKind(e?.Value ?? 0));

        private void ApplyPage(string kind)
        {
            if (_filling) return;
            _lblPath.Text = HarnessObjects.KindPath(_workspace, kind);
            _lblStatus.Text = string.Format(Localization.T("HarnessKindCount"),
                _rows.TryGetValue(kind, out var rows) ? rows.Count(row => row.On) : 0,
                _rows.TryGetValue(kind, out var all) ? all.Count : 0);
        }

        // ---- 页面搭建：四个页一个模子（path + 表格 + 一排按钮），省得写四份 Designer ----

        private void BuildPages()
        {
            foreach (var pair in new (AntdUI.TabPage Page, string Kind)[]
                     {
                         (_pageSkills, HarnessObjects.Skill),
                         (_pageRules, HarnessObjects.Rule),
                         (_pageLimits, HarnessObjects.Limit),
                         (_pageGuards, HarnessObjects.Guard)
                     })
            {
                BuildPage(pair.Page, pair.Kind);
            }
        }

        private void BuildPage(AntdUI.TabPage page, string kind)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 10, 0, 0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(44)));

            var table = new AntdUI.Table
            {
                Dock = DockStyle.Fill,
                Bordered = false,
                VisibleHeader = true,
                RowHeight = Dip(40),
                MultipleRows = true,
                EmptyText = Localization.T("HarnessObjectsEmpty"),
                Margin = new Padding(0, 0, 0, 4)
            };
            table.Columns.Add(new AntdUI.Column("On", Localization.T("Status")) { Width = "80" });
            table.Columns.Add(new AntdUI.Column("Name", Localization.T("HarnessColName")) { Width = "240" });
            table.Columns.Add(new AntdUI.Column("Params", Localization.T("HarnessColParams")) { Width = "220" });
            table.Columns.Add(new AntdUI.Column("Note", Localization.T("ColDescription")) { Width = "280", Ellipsis = true });
            table.Columns[0].Render = (_, data, _) => Badge(data as ObjectRow);
            table.DataSource = _rows[kind] = [];
            table.SelectIndexChanged += (_, _) => UpdateButtons(kind);
            _tables[kind] = table;

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var toggle = MakeButton(Localization.T("SkillToggle"), UiIcons.Edit, 112);
            toggle.Click += (_, _) => _ = ToggleAsync(kind);
            var parameters = MakeButton(Localization.T("HarnessEditParams"), UiIcons.Settings, 96);
            parameters.Click += (_, _) => _ = EditParamsAsync(kind);
            var defaults = MakeButton(Localization.T("HarnessResetDefault"), UiIcons.Undo, 112);
            defaults.Click += (_, _) => _ = ResetOneAsync(kind);
            var reset = MakeButton(Localization.T("HarnessResetKind"), UiIcons.Refresh, 128);
            reset.Click += (_, _) => _ = ResetKindAsync(kind);

            actions.Controls.Add(toggle);
            actions.Controls.Add(parameters);
            actions.Controls.Add(defaults);
            actions.Controls.Add(reset);

            _toggleButtons[kind] = toggle;
            _paramButtons[kind] = parameters;
            _defaultButtons[kind] = defaults;
            _resetButtons[kind] = reset;

            layout.Controls.Add(table, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            page.Controls.Add(layout);
        }

        /// <summary>96-DPI 设计值 → 本屏物理像素（运行期 new 的控件不参与设计器自动缩放）。</summary>
        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private AntdUI.Button MakeButton(string text, string icon, int width)
        {
            // AntdUI.Button 放进 FlowLayoutPanel 也要显式给尺寸，否则会算成 0；尺寸必须是物理像素，
            // 否则 250% 下按钮只有一半大、字号却按设计器缩放过，文字就被裁掉、行高也压住下一行
            var button = new AntdUI.Button
            {
                Text = text,
                Radius = Dip(8),
                Size = new Size(Dip(width), Dip(30)),
                IconSvg = UiIcons.Of(icon, UiStyle.Text),
                Margin = new Padding(0, Dip(4), Dip(8), Dip(4))
            };
            return button;
        }

        private static AntdUI.CellBadge Badge(ObjectRow row)
            => row?.On == true
                ? new AntdUI.CellBadge(AntdUI.TState.Success, Localization.T("Enabled"))
                : new AntdUI.CellBadge(AntdUI.TState.Default, Localization.T("Disabled"));

        // ---- 数据 ----

        private async Task ReloadAllAsync()
        {
            foreach (string kind in HarnessObjects.Kinds) await ReloadKindAsync(kind);
            ApplyPage(PageKind(_tabs.SelectedIndex));
        }

        private async Task ReloadKindAsync(string kind)
        {
            try
            {
                var items = await Loop.ListObjectsAsync(_workspace, kind);
                if (IsDisposed) return;
                var rows = _rows[kind];
                rows.Clear();
                foreach (var item in items) rows.Add(new ObjectRow(item));
                Rebind(kind);
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                _lblStatus.Text = Localization.T("HarnessReadFailed") + ex.Message;
                AppLog.Warn($"读 Harness 对象失败（{kind}）：" + ex.Message);
            }
        }

        private void Rebind(string kind)
        {
            var table = _tables[kind];
            table.DataSource = null;
            table.DataSource = _rows[kind];
            UpdateButtons(kind);
        }

        private List<ObjectRow> Selected(string kind)
            => (_tables[kind].SelectedIndexs ?? [])
                .Where(index => index >= 1 && index <= _rows[kind].Count)
                .Select(index => _rows[kind][index - 1])
                .Distinct()
                .ToList();

        private void UpdateButtons(string kind)
        {
            var rows = Selected(kind);
            bool any = rows.Count > 0;
            _toggleButtons[kind].Text = !any ? Localization.T("SkillToggle")
                : rows.Any(row => row.On) ? Localization.T("SkillDisable")
                : Localization.T("SkillEnable");
            _paramButtons[kind].Enabled = any && !_busy;
            _defaultButtons[kind].Enabled = any && !_busy;
            _toggleButtons[kind].Enabled = any && !_busy;
            _resetButtons[kind].Enabled = !_busy && kind != HarnessObjects.Skill;
            _tables[kind].Enabled = true;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (string kind in HarnessObjects.Kinds) UpdateButtons(kind);
            _btnAiConfig.Enabled = !busy;
        }

        // ---- 操作 ----

        /// <summary>一个按钮管两边：选中的里只要有一个启用就全停用，否则全启用（与设置页的技能页同一语义）。</summary>
        private async Task ToggleAsync(string kind)
        {
            var rows = Selected(kind);
            if (rows.Count == 0 || _busy) return;
            bool enable = !rows.Any(row => row.On);
            await ApplyAsync(kind, rows, enable ? "enable" : "disable", null);
        }

        private async Task EditParamsAsync(string kind)
        {
            var rows = Selected(kind);
            if (rows.Count == 0 || _busy) return;
            if (rows.Count > 1)
            {
                _lblStatus.Text = Localization.T("HarnessPickOne");
                return;
            }

            var row = rows[0];
            if (!row.On)
            {
                _lblStatus.Text = Localization.T("HarnessEnableFirst");
                return;
            }
            if (row.Item.Values.Count == 0)
            {
                _lblStatus.Text = Localization.T("HarnessNoParams");
                return;
            }

            var input = new AntdUI.Input
            {
                Text = row.Item.ParamsText,
                Width = 360,
                Height = 34,
                Radius = 8,
                BorderWidth = 1,
                PlaceholderText = Localization.T("HarnessParamsPlaceholder")
            };
            if (AntdUI.Modal.open(this, $"{Localization.T("HarnessEditParams")}：{row.Name}", input) != DialogResult.OK)
                return;

            await ApplyAsync(kind, [row], "params", input.Text?.Trim() ?? "");
        }

        private async Task ResetOneAsync(string kind)
        {
            var rows = Selected(kind);
            if (rows.Count == 0 || _busy) return;
            await ApplyAsync(kind, rows, "default", null);
        }

        private async Task ResetKindAsync(string kind)
        {
            if (_busy || kind == HarnessObjects.Skill) return;
            if (MessageBox.Show(this,
                    string.Format(Localization.T("HarnessResetKindAsk"), CurrentPage),
                    Localization.T("HarnessResetKind"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            try
            {
                int version = await Loop.SetObjectAsync(_workspace, kind, "", "reset", null, cts.Token);
                await ReloadKindAsync(kind);
                if (IsDisposed) return;
                _lblStatus.Text = string.Format(Localization.T("HarnessApplied"), version);
                Changed = true;
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _lblStatus.Text = Localization.T("HarnessApplyFailed") + ex.Message;
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    ApplyPage(kind);
                }
            }
        }

        private async Task ApplyAsync(string kind, IReadOnlyList<ObjectRow> rows, string action, string parameters)
        {
            if (_busy || rows.Count == 0) return;
            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            int version = 0;
            string error = null;
            try
            {
                foreach (var row in rows)
                    version = await Loop.SetObjectAsync(_workspace, kind, row.Item.Id, action, parameters, cts.Token);
                await ReloadKindAsync(kind);
                Changed = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_cts, cts)) _cts = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    _lblStatus.Text = error != null
                        ? Localization.T("HarnessApplyFailed") + error
                        : string.Format(Localization.T("HarnessApplied"), version);
                    ApplyPage(kind);
                }
            }
        }

        private void OnAiConfig(object sender, EventArgs e)
        {
            // 关窗体把上下文交出去：先记下当前页，外层关完窗体再起 Loop 会话
            AiConfigPage = CurrentPage;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void OnClose(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
