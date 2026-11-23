using System.Drawing;
using ChatFlow;
using FastColoredTextBoxNS;

namespace OpenLoop.App
{
    /// <summary>平铺视图里的一个会话：Owner 是 Session 或 LoopView，标题/图标取它的标签页。</summary>
    internal sealed class TiledItem
    {
        public object Owner { get; init; }
        public AntdUI.TabPage Page { get; init; }
        public ChatTurnBook Book { get; init; }
        public Func<string> Title { get; init; }
        public Func<string> Icon { get; init; }
    }

    /// <summary>
    /// 把所有打开的会话铺成 n 行 × m 列的方块：标题栏与标签页同格式（图标 + 标题 + 关闭），
    /// 下面是显示最新一轮的只读 FCTB。拖标题可换位置；拖到标签头由宿主接管（脱离平铺视图）。
    /// </summary>
    internal sealed partial class TiledView : UserControl
    {
        private readonly List<TiledItem> _items = new();
        private readonly List<TileBox> _tiles = new();
        /// <summary>轮询刷新标题/图标/最新一轮：绑事件还得逐本书退订，直接看数据更省心。</summary>
        private readonly System.Windows.Forms.Timer _tick = new() { Interval = 400 };
        private TiledItem _dragItem;
        private Point _dragFrom;
        private bool _moved;

        /// <summary>方块被拖到视图外：server 判断是否落在标签头区，是则脱离。</summary>
        internal event Action<TiledItem, Point> DetachRequested;

        /// <summary>最后一个方块也没了：宿主关掉平铺视图页。</summary>
        internal event Action Emptied;

        public TiledView()
        {
            InitializeComponent();
            _tick.Tick += (_, _) => RefreshContent();
            _host.Resize += (_, _) => LayoutTiles();
        }

        internal IReadOnlyList<TiledItem> Items => _items;

        internal void SetItems(IEnumerable<TiledItem> items)
        {
            _items.Clear();
            _items.AddRange(items);
            Rebuild();
            if (_items.Count > 0 && !_tick.Enabled) _tick.Start();
        }

        /// <summary>去掉一个方块（拖出、关闭、删除会话）；没了就通知宿主收页。</summary>
        internal void Remove(TiledItem item)
        {
            if (!_items.Remove(item)) return;
            Rebuild();
            if (_items.Count == 0)
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => Emptied?.Invoke()));
                else Emptied?.Invoke();
            }
        }

        private void Rebuild()
        {
            foreach (var tile in _tiles)
            {
                _host.Controls.Remove(tile);
                tile.Dispose();
            }
            _tiles.Clear();
            foreach (var item in _items)
            {
                var tile = CreateTile(item);
                _tiles.Add(tile);
                _host.Controls.Add(tile);
            }
            LayoutTiles();
            RefreshContent();
            if (_items.Count == 0) _tick.Stop();
        }

        /// <summary>按可用区域的纵横比挑列数，方块尽量接近方形并铺满。</summary>
        private void LayoutTiles()
        {
            int count = _tiles.Count;
            if (count == 0) return;

            int pad = Dip(8), gap = Dip(8);
            int width = _host.ClientSize.Width - pad * 2;
            int height = _host.ClientSize.Height - pad * 2;
            if (width <= 0 || height <= 0) return;

            double aspect = (double)width / height;
            int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(count * aspect)));
            cols = Math.Min(cols, count);
            int rows = (count + cols - 1) / cols;
            int cellW = (width - gap * (cols - 1)) / cols;
            int cellH = (height - gap * (rows - 1)) / rows;
            if (cellW <= 0 || cellH <= 0) return;

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                _tiles[i].SetBounds(pad + col * (cellW + gap), pad + row * (cellH + gap), cellW, cellH);
            }
        }

        private TileBox CreateTile(TiledItem item)
        {
            int headerHeight = Dip(32);
            var tile = new TileBox
            {
                Item = item,
                BackColor = UiStyle.Surface,
                Margin = new Padding(0)
            };

            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = headerHeight,
                BackColor = UiStyle.SecondaryBack
            };
            var close = new AntdUI.Button
            {
                Dock = DockStyle.Right,
                Width = Dip(30),
                Height = headerHeight,
                Ghost = true,
                Radius = 0,
                BorderWidth = 0,
                Cursor = Cursors.Hand,
                IconSvg = UiIcons.Of(UiIcons.Close, UiStyle.Muted)
            };
            // 与标签页同格式：图标 + 标题；标题下方是只读 FCTB，字号比默认聊天正文小一号
            var title = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                Text = item.Title(),
                PrefixSvg = item.Icon(),
                IconGap = Dip(6),
                ForeColor = UiStyle.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Dip(8), 0, Dip(4), 0),
                Cursor = Cursors.SizeAll
            };
            var text = new FastColoredTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ShowLineNumbers = false,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiStyle.Surface,
                ForeColor = UiStyle.Text,
                Font = new Font("Microsoft YaHei UI", 8F),
                Margin = new Padding(0)
            };

            bar.Controls.Add(title);
            bar.Controls.Add(close);
            tile.Controls.Add(text);
            tile.Controls.Add(bar);
            tile.Title = title;
            tile.Body = text;

            close.Click += (_, _) => BeginInvoke(new Action(() => Remove(item)));
            // 边框用画的不占布局：header/FCTB 都是 Fill，塞不进额外的线
            tile.Paint += (_, e) =>
            {
                using var pen = new Pen(UiStyle.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
            };

            void Down(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                _dragItem = item;
                _dragFrom = Cursor.Position;
                _moved = false;
                ((Control)sender).Capture = true;
            }

            void Move(object sender, MouseEventArgs e)
            {
                if (_dragItem != item) return;
                if (!_moved)
                {
                    var now = Cursor.Position;
                    if (Math.Abs(now.X - _dragFrom.X) < 4 && Math.Abs(now.Y - _dragFrom.Y) < 4) return;
                    _moved = true;
                    Cursor = Cursors.SizeAll;
                }
            }

            void Up(object sender, MouseEventArgs e)
            {
                if (_dragItem != item) return;
                ((Control)sender).Capture = false;
                Cursor = Cursors.Default;
                bool moved = _moved;
                _dragItem = null;
                _moved = false;
                if (!moved) return;
                var screen = Cursor.Position;
                // 事件里别拆掉正在派发的方块：把重排/脱离留到消息循环下一轮
                BeginInvoke(new Action(() => Drop(item, screen)));
            }

            title.MouseDown += Down;
            title.MouseMove += Move;
            title.MouseUp += Up;
            bar.MouseDown += Down;
            bar.MouseMove += Move;
            bar.MouseUp += Up;
            return tile;
        }

        private void Drop(TiledItem item, Point screen)
        {
            if (!_items.Contains(item)) return;
            var client = _host.PointToClient(screen);
            if (!_host.ClientRectangle.Contains(client))
            {
                DetachRequested?.Invoke(item, screen);
                return;
            }

            int to = _tiles.FindIndex(t => t.Bounds.Contains(client));
            if (to < 0)
            {
                // 落在缝里：按中心最近的一格算
                to = _tiles
                    .Select((t, i) => (i, Distance(t.Bounds, client)))
                    .OrderBy(p => p.Item2)
                    .Select(p => p.i)
                    .DefaultIfEmpty(-1)
                    .First();
            }
            int from = _items.IndexOf(item);
            if (from < 0 || to < 0 || to == from) return;
            _items.RemoveAt(from);
            _items.Insert(to, item);
            Rebuild();
        }

        private static int Distance(Rectangle rect, Point point)
        {
            int dx = point.X - (rect.Left + rect.Width / 2);
            int dy = point.Y - (rect.Top + rect.Height / 2);
            return dx * dx + dy * dy;
        }

        private void RefreshContent()
        {
            foreach (var tile in _tiles)
            {
                var item = tile.Item;
                string title = item.Title();
                if (tile.Title.Text != title) tile.Title.Text = title;
                string icon = item.Icon();
                if (tile.Title.PrefixSvg != icon) tile.Title.PrefixSvg = icon;

                string text = Preview(item.Book);
                if (tile.Body.Text == text) continue;
                tile.Body.Text = text;
                tile.Body.GoEnd();
            }
        }

        /// <summary>方块里显示最新一轮：用户问题 + 助手回答；都没有就显示状态占位。</summary>
        private static string Preview(ChatTurnBook book)
        {
            var turn = book?.Turns.LastOrDefault();
            if (turn == null) return "暂无对话";
            string user = turn.UserText?.Trim() ?? "";
            string answer = turn.AssistantText?.Trim() ?? "";
            var parts = new List<string>(2);
            if (user.Length > 0) parts.Add("用户：\n" + user);
            if (answer.Length > 0) parts.Add("AI：\n" + answer);
            if (parts.Count == 0) return turn.Streaming ? "思考中…" : "暂无对话";
            return string.Join("\n\n", parts);
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private sealed class TileBox : Panel
        {
            public TiledItem Item { get; init; }
            public AntdUI.Label Title { get; set; }
            public FastColoredTextBox Body { get; set; }
        }
    }
}
