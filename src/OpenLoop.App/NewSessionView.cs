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

        internal void SetProjects(IEnumerable<string> projects)
        {
            _projects.Clear();
            _projects.AddRange(Paths.Dedup(projects).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            if (!_projects.Any(p => Paths.Same(p, _project)))
                _project = _projects.FirstOrDefault() ?? "";
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

        /// <summary>
        /// 和会话页的输入框一致：Enter 立即发送，Ctrl+Enter 也发送（新建时还没有本轮可排队），
        /// Shift+Enter 软换行。Enter 在 AntdUI 的 WM_KEYDOWN 里先变成换行，只能在 VerifyKeyboard 里拦。
        /// </summary>
        private void OnComposerKeyboard(object sender, AntdUI.InputVerifyKeyboardEventArgs e)
        {
            bool shift = e.KeyData == (Keys.Shift | Keys.Enter);
            bool ctrl = e.KeyData == (Keys.Control | Keys.Enter);
            if (!shift && e.KeyData != Keys.Enter && !ctrl) return;

            e.Result = false;
            if (shift) { InsertSoftNewline(); return; }
            if (_txtInput.Text.Trim().Length == 0) return;
            OnSend(sender, EventArgs.Empty);
        }

        private void InsertSoftNewline()
        {
            if (_txtInput == null || !_txtInput.IsHandleCreated) return;
            var selStart = _txtInput.SelectionStart;
            var selLen = _txtInput.SelectionLength;
            var text = _txtInput.Text;
            _txtInput.Text = text.Substring(0, selStart) + Environment.NewLine + text.Substring(selStart + selLen);
            _txtInput.SelectionStart = selStart + Environment.NewLine.Length;
            _txtInput.SelectionLength = 0;
            _txtInput.Focus();
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
