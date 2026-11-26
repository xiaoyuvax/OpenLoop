using System.ComponentModel;
using System.Drawing;

namespace OpenLoop.App
{
    internal sealed record NewSessionRequest(string Workspace, string Text, string Model, string Effort);

    /// <summary>
    /// “新建会话”标签页：LOGO + 初始对话框（模型 / 强度 / 项目路径）。
    /// 项目既可从现有工作区里选，也可新建（选一个新路径）。
    /// </summary>
    public partial class NewSessionView : UserControl
    {
        private const string NewProjectTag = "\u0000new";

        private readonly List<string> _projects = new();
        private AppSettings _settings;
        private string _project = "";
        private string _model = "";
        private string _effort = "";
        private bool _onlyFree = true;
        private bool _filling;
        private bool _fillingProviders;

        public NewSessionView()
        {
            InitializeComponent();
        }

        internal event Action<NewSessionRequest> Submit;

        internal void Initialize(AppSettings settings, IEnumerable<string> projects, string defaultWorkspace)
        {
            _settings = settings;
            _projects.Clear();
            _projects.AddRange(Paths.Dedup(projects).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(defaultWorkspace) &&
                !_projects.Any(p => Paths.Same(p, defaultWorkspace)))
                _projects.Insert(0, defaultWorkspace);

            // 新会话的默认模型：最近用过的第一条，其次设置里上次选的
            _model = ModelCatalog.DefaultModel(settings);
            _effort = settings?.ReasoningEffort ?? "";
            _onlyFree = settings?.OnlyFreeModels ?? true;
            // 没有指定工作区（侧栏 + 号）时默认落在「新建项目」上
            _project = string.IsNullOrWhiteSpace(defaultWorkspace)
                ? ""
                : _projects.FirstOrDefault(p => Paths.Same(p, defaultWorkspace)) ?? defaultWorkspace;

            _filling = true;
            try { _freeSwitch.Checked = _onlyFree; }
            finally { _filling = false; }

            FillProviders();
            FillModels();
            SelectEffort(_effort);
            FillProjects();
            SelectTips.Bind(_tips, _selProvider, _selModel, _selEffort, _selProject);
        }

        private void FillProviders()
        {
            string selected = (_selProvider.SelectedValue as ProviderConfig)?.Name;
            _fillingProviders = true;
            try
            {
                _selProvider.Items.Clear();
                _selProvider.Items.Add(new AntdUI.SelectItem(Localization.T("AllProviders")) { Tag = null });
                foreach (var (name, tag) in ProviderList.All(_settings))
                    _selProvider.Items.Add(new AntdUI.SelectItem(name) { Tag = tag });

                int index = 0;
                for (int i = 1; i < _selProvider.Items.Count; i++)
                {
                    if (_selProvider.Items[i] is AntdUI.SelectItem item
                        && item.Tag is ProviderConfig provider
                        && string.Equals(provider.Name, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                _selProvider.SelectedIndex = index;
            }
            finally
            {
                _fillingProviders = false;
            }

            PluginRefill.WhenReady(this, FillProviders);
        }

        private void OnProviderChanged(object sender, AntdUI.ObjectNEventArgs e)
        {
            if (_filling || _fillingProviders) return;
            FillModels();
        }

        private void FillModels()
        {
            _filling = true;
            try
            {
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var models = ModelChoices.FilterByProvider(
                    ModelChoices.Build(_settings, onlyFree: _onlyFree), provider);

                ModelChoices.Fill(_selModel, models, _settings);

                if (provider == null && models.All(m => m.Id != _model))
                {
                    var current = ModelCatalog.All.FirstOrDefault(m => m.Id == _model);
                    if (!string.IsNullOrWhiteSpace(_model))
                        _selModel.Items.Add(new AntdUI.SelectItem(current?.Name ?? _model) { Tag = _model });
                }

                if (provider != null && models.All(m => m.Id != _model))
                    _model = models.FirstOrDefault()?.Id ?? "";
                else if (string.IsNullOrWhiteSpace(_model) && models.Count > 0)
                    _model = models[0].Id;

                _selModel.SelectedValue = _model;
            }
            finally
            {
                _filling = false;
            }
        }

        private void OnFreeToggle(object sender, AntdUI.BoolEventArgs e)
        {
            if (_settings == null || _filling) return;
            _onlyFree = e.Value;
            try
            {
                _settings.OnlyFreeModels = _onlyFree;
                _settings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Warn("保存设置失败：" + ex.Message);
            }
            FillModels();
        }

        internal void SetProjects(IEnumerable<string> projects)
        {
            _projects.Clear();
            _projects.AddRange(Paths.Dedup(projects).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(_project) && !_projects.Any(p => Paths.Same(p, _project)))
                _project = _projects.FirstOrDefault() ?? "";
            FillProjects();
        }

        /// <summary>切到指定工作区；空字符串 = 落在「新建项目」上（侧栏 + 号）。</summary>
        internal void SelectProject(string workspace)
        {
            if (!string.IsNullOrWhiteSpace(workspace) && !_projects.Any(p => Paths.Same(p, workspace)))
                _projects.Add(workspace);
            _project = string.IsNullOrWhiteSpace(workspace)
                ? ""
                : _projects.FirstOrDefault(p => Paths.Same(p, workspace)) ?? "";
            FillProjects();
        }

        private void FillProjects()
        {
            _filling = true;
            try
            {
                _selProject.Items.Clear();
                _selProject.Items.Add(new AntdUI.SelectItem(Localization.T("NewProject")) { Tag = NewProjectTag });
                foreach (var path in _projects)
                {
                    string name = ProjectName(path);
                    _selProject.Items.Add(new AntdUI.SelectItem(name)
                    {
                        Tag = path
                    });
                }

                _selProject.SelectedValue =
                    _projects.FirstOrDefault(p => Paths.Same(p, _project)) ?? NewProjectTag;
                _lblPath.Text = _project;
            }
            finally
            {
                _filling = false;
            }
        }

        private void SelectEffort(string effort)
        {
            _effort = effort ?? "";
            _filling = true;
            try { _selEffort.SelectedValue = _effort; }
            finally { _filling = false; }
        }

        private void OnProjectChanged(object sender, EventArgs e)
        {
            if (_filling) return;

            string value = _selProject.SelectedValue as string ?? "";
            if (!string.Equals(value, NewProjectTag, StringComparison.Ordinal))
            {
                _project = value;
                _lblPath.Text = _project;
                return;
            }

            using var dialog = new FolderBrowserDialog
            {
                Description = Localization.T("选择作为新项目的工作区文件夹"),
                SelectedPath = Directory.Exists(_project) ? _project : ""
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                FillProjects();
                return;
            }

            string chosen = dialog.SelectedPath;
            //同一目录的不同写法算同一个项目：直接落到已有项目上，不新增重复条目
            string existing = _projects.FirstOrDefault(p => Paths.Same(p, chosen));
            if (existing != null)
            {
                _project = existing;
            }
            else
            {
                _projects.Add(chosen);
                _project = chosen;
            }
            FillProjects();
            _lblPath.Text = _project;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool OnlyFreeModels
        {
            get => _onlyFree;
            set
            {
                _onlyFree = value;
                _freeSwitch.Checked = value;
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int ModelOptionCount => _selModel.Items.Count;

        internal bool SelectProvider(string name)
        {
            for (int i = 0; i < _selProvider.Items.Count; i++)
            {
                if (_selProvider.Items[i] is AntdUI.SelectItem item
                    && item.Tag is ProviderConfig provider
                    && string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    _selProvider.SelectedIndex = i;
                    return true;
                }
            }
            return false;
        }

        internal string Draft
        {
            get => _txtInput.Text;
            set => _txtInput.Text = value;
        }

        internal void SubmitDraft() => OnSend(null, EventArgs.Empty);

        private void OnSend(object sender, EventArgs e)
        {
            string text = _txtInput.Text?.Trim() ?? "";
            if (text.Length == 0)
            {
                Notify(Localization.T("请先描述任务"), AntdUI.TState.Warn);
                _txtInput.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(_project))
            {
                Notify(Localization.T("请选择项目或新建项目"), AntdUI.TState.Warn);
                return;
            }

            string model = _selModel.SelectedValue as string ?? _model;
            string effort = _selEffort.SelectedValue as string ?? "";
            if (_settings != null)
            {
                ModelChoices.RecordUse(_settings, model);
                try { _settings.Save(); }
                catch (Exception ex) { AppLog.Warn("保存设置失败：" + ex.Message); }
            }
            Submit?.Invoke(new NewSessionRequest(_project, text, model, effort));
        }

        /// <summary>与对话页一致：Enter 发送，Shift+Enter 换行，Ctrl+Enter 发送。</summary>
        private void OnComposerKeyboard(object sender, AntdUI.InputVerifyKeyboardEventArgs e)
        {
            if (e.KeyData == Keys.Up && ComposerKeys.MoveToFirstLineStart(_txtInput))
            {
                e.Result = false;
                return;
            }
            bool shift = e.KeyData == (Keys.Shift | Keys.Enter);
            bool ctrl = e.KeyData == (Keys.Control | Keys.Enter);
            if (!shift && e.KeyData != Keys.Enter && !ctrl) return;

            e.Result = false;
            if (shift) { ComposerKeys.InsertSoftNewline(_txtInput); return; }
            if (_txtInput.Text.Trim().Length == 0) return;
            OnSend(sender, EventArgs.Empty);
        }

        private void Notify(string text, AntdUI.TState state)
        {
            var form = FindForm();
            if (form == null) return;
            if (state == AntdUI.TState.Warn) AppNotify.warn(form, Localization.T("新建会话"), text, AntdUI.TAlignFrom.TR, Font, 3);
            else AppNotify.info(form, Localization.T("新建会话"), text, AntdUI.TAlignFrom.TR, Font, 3);
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
    }
}
