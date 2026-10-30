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

            _model = settings?.Model ?? "";
            _effort = settings?.ReasoningEffort ?? "";
            _onlyFree = settings?.OnlyFreeModels ?? true;
            _project = _projects.FirstOrDefault(p => Paths.Same(p, defaultWorkspace)) ??
                _projects.FirstOrDefault() ?? "";

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
            _fillingProviders = true;
            try
            {
                _selProvider.Items.Clear();
                _selProvider.Items.Add(new AntdUI.SelectItem(Localization.T("AllProviders")) { Tag = null });
                foreach (var provider in _settings?.Providers ?? [])
                {
                    _selProvider.Items.Add(new AntdUI.SelectItem(provider.Name)
                    {
                        Tag = provider,
                        SubText = provider.Kind
                    });
                }
                _selProvider.SelectedIndex = 0;
            }
            finally
            {
                _fillingProviders = false;
            }
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
                _selModel.Items.Clear();
                var provider = _selProvider.SelectedValue as ProviderConfig;
                var models = ModelChoices.FilterByProvider(
                    ModelChoices.Build(_settings, onlyFree: _onlyFree), provider);

                foreach (var model in models)
                    _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                    {
                        Tag = model.Id,
                        IconSvg = model.IsFree ? UiIcons.Dot(UiStyle.Success) : null,
                        SubText = model.IsFree ? "免费" : null
                    });

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

        private void FillProjects()
        {
            _filling = true;
            try
            {
                _selProject.Items.Clear();
                foreach (var path in _projects)
                {
                    string name = ProjectName(path);
                    _selProject.Items.Add(new AntdUI.SelectItem(name)
                    {
                        Tag = path,
                        SubText = path
                    });
                }
                _selProject.Items.Add(new AntdUI.SelectItem(Localization.T("NewProject")) { Tag = NewProjectTag });

                _selProject.SelectedValue = _projects.FirstOrDefault(p => Paths.Same(p, _project));
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
                Description = "选择作为新项目的工作区文件夹",
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
                Notify("请先描述任务", AntdUI.TState.Warn);
                _txtInput.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(_project))
            {
                Notify("请选择项目或新建项目", AntdUI.TState.Warn);
                return;
            }

            string model = _selModel.SelectedValue as string ?? _model;
            string effort = _selEffort.SelectedValue as string ?? "";
            Submit?.Invoke(new NewSessionRequest(_project, text, model, effort));
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                OnSend(sender, EventArgs.Empty);
            }
        }

        private void Notify(string text, AntdUI.TState state)
        {
            var form = FindForm();
            if (form == null) return;
            if (state == AntdUI.TState.Warn) AntdUI.Notification.warn(form, "新建会话", text, AntdUI.TAlignFrom.TR, Font, 3);
            else AntdUI.Notification.info(form, "新建会话", text, AntdUI.TAlignFrom.TR, Font, 3);
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
