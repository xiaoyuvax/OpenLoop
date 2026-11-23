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
            _table.CellDoubleClick += OnCellDoubleClick;
            _ = ReloadAsync(force: false);
        }

        /// <summary>页签被选中时再拉一次（30 秒内复用缓存，不打断用户视线）。</summary>
        internal void Reload() => _ = ReloadAsync(force: false);

        private async Task ReloadAsync(bool force)
        {
            if (_loading || IsDisposed) return;
            _loading = true;
            _lblHint.Text = "Puppet Action 清单 · 读取中…";
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

            _table.DataSource = null;
            _table.DataSource = _rows;

            if (!string.IsNullOrEmpty(sheet?.Error))
            {
                _lblHint.Text = "读不到清单：" + sheet.Error;
                _lblHint.ForeColor = UiStyle.Error;
                _lblUsage.Text = "壳启用 Puppet（B 面）后才能列出 action；清单来自本进程的 /appagent/manifest。";
                return;
            }

            _lblHint.ForeColor = UiStyle.Muted;
            _lblHint.Text = $"Puppet Action 清单 · 实例 {sheet?.Instance} · {_rows.Count} 个 action"
                + (sheet == null ? "" : " · " + sheet.At.ToLocalTime().ToString("HH:mm:ss"));
            _lblUsage.Text = "双击一行把这条命令填进输入框：/puppet <action> <参数…>（参数用自然语言写，模型自己对应）";
        }

        private void OnCellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            int index = (e?.RowIndex ?? 0) - 1;
            if (index < 0 || index >= _rows.Count) return;
            CommandRequested?.Invoke("/puppet " + _rows[index].Name + " ");
        }
    }
}
