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
        private string _project = "";
        private string _model = "";
        private string _effort = "";
        private bool _filling;

        public NewSessionView()
        {
            InitializeComponent();
        }

        internal event Action<NewSessionRequest> Submit;

        internal void Initialize(AppSettings settings, IEnumerable<string> projects, string defaultWorkspace)
        {
            _projects.Clear();
            _projects.AddRange((projects ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(defaultWorkspace) &&
                !_projects.Contains(defaultWorkspace, StringComparer.OrdinalIgnoreCase))
                _projects.Insert(0, defaultWorkspace);

            _model = settings?.Model ?? "";
            _effort = settings?.ReasoningEffort ?? "";
            _project = _projects.FirstOrDefault(p =>
                string.Equals(p, defaultWorkspace, StringComparison.OrdinalIgnoreCase)) ??
                _projects.FirstOrDefault() ?? "";

            FillModels();
            SelectEffort(_effort);
            FillProjects();
        }

        private void FillModels()
        {
            _filling = true;
            try
            {
                _selModel.Items.Clear();
                var models = ModelCatalog.All.Where(m => m.IsFree).ToList();
                if (models.Count == 0) models = ModelCatalog.All.ToList();

                foreach (var model in models)
                    _selModel.Items.Add(new AntdUI.SelectItem(model.Name)
                    {
                        Tag = model.Id,
                        IconSvg = UiIcons.Dot(UiStyle.Success),
                        SubText = "免费"
                    });

                if (models.All(m => m.Id != _model))
                {
                    var current = ModelCatalog.All.FirstOrDefault(m => m.Id == _model);
                    if (!string.IsNullOrWhiteSpace(_model))
                        _selModel.Items.Add(new AntdUI.SelectItem(current?.Name ?? _model) { Tag = _model });
                    else if (_selModel.Items.Count > 0)
                        _model = (_selModel.Items[0] as AntdUI.SelectItem)?.Tag as string ?? "";
                }

                _selModel.SelectedValue = _model;
            }
            finally
            {
                _filling = false;
            }
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

                _selProject.SelectedValue = _projects.Contains(_project, StringComparer.OrdinalIgnoreCase)
                    ? _project
                    : null;
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
            if (!_projects.Contains(chosen, StringComparer.OrdinalIgnoreCase))
                _projects.Add(chosen);
            _project = chosen;
            FillProjects();
            _lblPath.Text = _project;
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
