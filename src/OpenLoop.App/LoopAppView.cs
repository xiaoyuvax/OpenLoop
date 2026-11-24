using System.Text.Json;
using System.Text.Json.Nodes;
using OpenLoop.Service.OpenUI;

namespace OpenLoop.App
{
    /// <summary>
    /// 右侧栏「LoopApp」页：本工作区生成过的应用（名称 / 用途 / 路径）。
    /// 双击在「网页」页打开（应用的虚拟域只有壳里的 WebView 认）；右键可改名称与用途、删除。
    /// 落盘布局见 <see cref="LoopAppPaths"/>：目录名 = 名称，app.json 的 title = 用途。
    /// </summary>
    public partial class LoopAppView : UserControl
    {
        private readonly List<LoopAppRow> _rows = [];
        private List<LoopAppRow> _view = [];
        private Func<string> _workspace = () => "";
        private int _hoverRow = -1;

        public LoopAppView()
        {
            InitializeComponent();
        }

        /// <summary>双击一行：宿主在「网页」页打开这个应用（参数是目录名）。</summary>
        internal event Action<string> OpenRequested;

        internal void Initialize(Func<string> workspace)
        {
            _workspace = workspace ?? (() => "");
            _txtFilter.TextChanged += (_, _) => ApplyFilter();
            _btnRefresh.Click += (_, _) => Reload();
            _table.CellHover += (_, e) => _hoverRow = (e?.RowIndex ?? 0) - 1;
            _table.CellDoubleClick += OnCellDoubleClick;
            _table.MouseUp += OnTableMouseUp;
            Reload();
        }

        /// <summary>重读本工作区的应用（页签切过来时调；纯读，不建目录）。</summary>
        internal void Reload()
        {
            string workspace = _workspace();
            _rows.Clear();
            foreach (var (slug, title, dir) in LoopAppPaths.List(workspace))
                _rows.Add(new LoopAppRow { Name = slug, Usage = title, Path = dir });
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = _txtFilter.Text?.Trim() ?? "";
            _view = q.Length == 0
                ? [.. _rows]
                : _rows.Where(r =>
                    r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Usage.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            _table.DataSource = null;
            _table.DataSource = _view;

            string root = LoopAppPaths.Root(_workspace());
            _lblHint.Text = _rows.Count == 0
                ? string.Format(Localization.T("LoopAppEmptyHint"), root)
                : string.Format(Localization.T("LoopAppCount"), _view.Count, _rows.Count, root);
        }

        private LoopAppRow RowAt(int index)
            => index >= 0 && index < _view.Count ? _view[index] : null;

        private void OnCellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            var row = RowAt((e?.RowIndex ?? 0) - 1);
            if (row != null) OpenRequested?.Invoke(row.Name);
        }

        /// <summary>右键菜单：打开 / 编辑 / 删除。AntdUI 的 Table 没有右键事件，用悬浮行定位。</summary>
        private void OnTableMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            var row = RowAt(_hoverRow);
            if (row == null) return;

            var items = new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem(Localization.T("LoopAppOpen")) { ID = "open" },
                new AntdUI.ContextMenuStripItem(Localization.T("LoopAppEditMenu")) { ID = "edit" },
                new AntdUI.ContextMenuStripItemDivider(),
                new AntdUI.ContextMenuStripItem(Localization.T("LoopAppDeleteMenu")) { ID = "delete" }
            };
            AntdUI.ContextMenuStrip.open(_table, item => OnRowMenu(row, item?.ID), items);
        }

        private void OnRowMenu(LoopAppRow row, string id)
        {
            switch (id)
            {
                case "open": OpenRequested?.Invoke(row.Name); break;
                case "edit": Edit(row); break;
                case "delete": Delete(row); break;
            }
        }

        private void Edit(LoopAppRow row)
        {
            if (!Directory.Exists(row.Path))
            {
                _lblHint.Text = Localization.T("LoopAppGone") + row.Path;
                Reload();
                return;
            }

            var name = Field(row.Name);
            var usage = Field(row.Usage);
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 320
            };
            panel.Controls.Add(Caption(Localization.T("LoopAppNameLabel")));
            panel.Controls.Add(name);
            panel.Controls.Add(Caption(Localization.T("LoopAppUsageLabel")));
            panel.Controls.Add(usage);

            var owner = FindForm();
            if (owner == null) return;
            if (AntdUI.Modal.open(owner, Localization.T("LoopAppEditTitle"), (object)panel) != DialogResult.OK) return;

            string wanted = name.Text?.Trim() ?? "";
            if (wanted.Length == 0)
            {
                _lblHint.Text = Localization.T("LoopAppNameEmpty");
                return;
            }

            string dir = row.Path;
            string slug = LoopAppPaths.Slug(wanted);
            string target = Path.Combine(Path.GetDirectoryName(dir) ?? "", slug);
            if (!string.Equals(target, dir, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(target))
                {
                    _lblHint.Text = string.Format(Localization.T("LoopAppNameTaken"), slug);
                    return;
                }
                try
                {
                    Directory.Move(dir, target);
                    dir = target;
                }
                catch (Exception ex)
                {
                    _lblHint.Text = Localization.T("LoopAppRenameFailed") + ex.Message;
                    return;
                }
            }

            try
            {
                WriteMeta(dir, slug, usage.Text?.Trim() ?? "");
                Reload();
            }
            catch (Exception ex)
            {
                _lblHint.Text = Localization.T("LoopAppMetaFailed") + ex.Message;
            }
        }

        private void Delete(LoopAppRow row)
        {
            if (MessageBox.Show(this, string.Format(Localization.T("LoopAppDeleteAsk"), row.Usage, row.Path),
                    Localization.T("LoopAppDeleteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                if (Directory.Exists(row.Path)) Directory.Delete(row.Path, recursive: true);
                Reload();
            }
            catch (Exception ex)
            {
                _lblHint.Text = Localization.T("LoopAppDeleteFailed") + ex.Message;
                AppLog.Warn("删除 LoopApp 失败：" + ex.Message);
            }
        }

        /// <summary>app.json 只改 name/title，其余（window/size 等）原样留着。</summary>
        private static void WriteMeta(string dir, string slug, string usage)
        {
            string path = Path.Combine(dir, "app.json");
            JsonObject meta = null;
            if (File.Exists(path))
            {
                try { meta = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
                catch (JsonException) { meta = null; }
            }
            meta ??= new JsonObject();
            meta["name"] = slug;
            if (usage.Length == 0) meta.Remove("title");
            else meta["title"] = usage;
            File.WriteAllText(path, meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static AntdUI.Label Caption(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = UiStyle.Muted,
            Margin = new Padding(0, 0, 0, 6)
        };

        private static AntdUI.Input Field(string text) => new()
        {
            Text = text ?? "",
            Width = 300,
            Height = 34,
            Radius = 8,
            BorderWidth = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    /// <summary>表格行：AntdUI 按属性名绑列。</summary>
    internal sealed class LoopAppRow
    {
        public string Name { get; init; } = "";
        public string Usage { get; init; } = "";
        public string Path { get; init; } = "";
    }
}
