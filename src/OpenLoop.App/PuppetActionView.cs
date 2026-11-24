using System.ComponentModel;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧栏「Puppet」页：壳自己的 B 面 Action 清单（<c>/appagent/manifest</c>），
    /// 由 /puppet 命令或「Puppet」页签打开。双击一行把 <c>/puppet &lt;action&gt; </c> 填进输入框。
    /// </summary>
    public partial class PuppetActionView : UserControl
    {
        private readonly List<PuppetActionInfo> _rows = [];
        private List<PuppetActionInfo> _view = [];
        private string _status = "";
        private bool _loading;

        public PuppetActionView()
        {
            InitializeComponent();
        }

        /// <summary>双击某行：宿主把「/puppet 名称 」填进本会话输入框。</summary>
        internal event Action<string> CommandRequested;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ActionCount => _rows.Count;

        internal void Initialize()
        {
            _btnRefresh.Click += (_, _) => _ = ReloadAsync(force: true);
            _txtFilter.TextChanged += (_, _) => ApplyFilter();
            _table.CellDoubleClick += OnCellDoubleClick;
            _ = ReloadAsync(force: false);
        }

        /// <summary>页签被选中时再拉一次（30 秒内复用缓存，不打断用户视线）。</summary>
        internal void Reload() => _ = ReloadAsync(force: false);

        private async Task ReloadAsync(bool force)
        {
            if (_loading || IsDisposed) return;
            _loading = true;
            _lblHint.Text = Localization.T("PuppetListLoading");
            try
            {
                var sheet = await PuppetActions.LoadAsync(force);
                if (IsDisposed) return;
                Apply(sheet);
            }
            finally
            {
                _loading = false;
            }
        }

        private void Apply(PuppetActionSheet sheet)
        {
            _rows.Clear();
            _rows.AddRange(sheet?.Actions ?? []);

            if (!string.IsNullOrEmpty(sheet?.Error))
            {
                _view = [];
                _table.DataSource = null;
                _status = "";
                _lblHint.Text = Localization.T("PuppetUnreadable") + sheet.Error;
                _lblHint.ForeColor = UiStyle.Error;
                _lblUsage.Text = Localization.T("PuppetShellDisabled");
                return;
            }

            _lblHint.ForeColor = UiStyle.Muted;
            _status = string.Format(Localization.T("PuppetStatus"), sheet?.Instance, _rows.Count)
                + (sheet == null ? "" : " · " + sheet.At.ToLocalTime().ToString("HH:mm:ss"));
            _lblUsage.Text = Localization.T("PuppetUsage");
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = _txtFilter.Text?.Trim() ?? "";
            _view = q.Length == 0
                ? [.. _rows]
                : _rows.Where(r =>
                    r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.ParamText.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Desc.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            _table.DataSource = null;
            _table.DataSource = _view;

            if (string.IsNullOrEmpty(_status)) return;
            _lblHint.Text = _view.Count == _rows.Count ? _status : _status + string.Format(Localization.T("PuppetFiltered"), _view.Count);
        }

        private void OnCellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            int index = (e?.RowIndex ?? 0) - 1;
            if (index < 0 || index >= _view.Count) return;
            CommandRequested?.Invoke("/puppet " + _view[index].Name + " ");
        }
    }
}
