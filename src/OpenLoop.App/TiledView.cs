using System.Drawing;
using ChatFlow;
using OpenLoop.Service.Pending;

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
    /// 把所有打开的会话铺成 n 行 × m 列的方块：标题栏与标签页同格式（图标 + 标题 + 关闭）。
    /// 正文用 ChatFlow 动态渲染最新一轮（与普通会话窗口同一套体验：markdown、思考区、流式）；
    /// 底部一行输入框，回车发送，进入队列时在输入框上方弹出队列栏。
    /// 拖标题可换位置；拖到标签头由宿主接管（脱离平铺视图）。
    /// </summary>
    internal sealed partial class TiledView : UserControl
    {
        private readonly List<TiledItem> _items = new();
        private readonly List<TileBox> _tiles = new();
        /// <summary>轮询刷新标题/图标：正文的动态刷新走 Book.TurnChanged，不靠轮询。</summary>
        private readonly System.Windows.Forms.Timer _tick = new() { Interval = 400 };
        private TiledItem _dragItem;
        private Point _dragFrom;
        private bool _moved;

        /// <summary>方块被拖到视图外：server 判断是否落在标签头区，是则脱离。</summary>
        internal event Action<TiledItem, Point> DetachRequested;

        /// <summary>宿主注入：按需建会话视图（平铺视图打开时页面被收走，队列管线可能还没建）。</summary>
        internal Func<Session, SessionView> HostEnsureView { get; set; }

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

        /// <summary>拖入一个普通标签页：追加成方块（同一会话已在平铺里就不重复收）。</summary>
        internal void Add(object owner, AntdUI.TabPage page)
        {
            if (owner == null || page == null) return;
            if (_items.Any(i => ReferenceEquals(i.Owner, owner))) return;
            var item = new TiledItem
            {
                Owner = owner,
                Page = page,
                Book = owner switch
                {
                    Session session => session.Book,
                    LoopView loop => loop.Book,
                    _ => null
                },
                Title = () => page.Text,
                Icon = () => page.IconSvg
            };
            _items.Add(item);
            var tile = CreateTile(item);
            _tiles.Add(tile);
            _host.Controls.Add(tile);
            LayoutTiles();
            RefreshContent();
            if (!_tick.Enabled) _tick.Start();
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
                UnbindTile(tile);
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

        /// <summary>方块销毁前退订真实数据源上的事件：会话还活着，不能留下悬空订阅。</summary>
        private static void UnbindTile(TileBox tile)
        {
            if (tile.BookHandler != null) tile.Item.Book.TurnChanged -= tile.BookHandler;
            if (tile.Queue != null && tile.QueueChangedHandler != null) tile.Queue.Changed -= tile.QueueChangedHandler;
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
            // 与标签页同格式：图标 + 标题
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

            // 正文：ChatFlow 绑定镜像簿（只含最新一轮），渲染体验与普通会话窗口一致
            SyncMirror(tile, force: false);
            var chat = new ChatFlowControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = UiStyle.Surface
            };
            chat.ChatFont = new Font("Microsoft YaHei UI", 9F);
            chat.FlashInterval = SessionView.FlashIntervalMs(WorkspaceOf(item));
            chat.Bind(tile.Mirror);

            // 队列栏（进入队列时弹出的「上拉框」）+ 单行输入框，都贴底
            var popup = new PendingQueueView { Dock = DockStyle.Bottom };
            var input = new AntdUI.Input
            {
                Dock = DockStyle.Bottom,
                Height = Dip(34),
                PlaceholderText = Localization.T("TileInputHint"),
                Radius = 10,
                BorderWidth = 1,
                BackColor = UiStyle.Surface,
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(0)
            };

            // Dock 后加的先贴边：bar(Top) → input(Bottom) → popup(Bottom，输入框上方) → chat 填余下空间
            bar.Controls.Add(title);
            bar.Controls.Add(close);
            tile.Controls.Add(chat);
            tile.Controls.Add(popup);
            tile.Controls.Add(input);
            tile.Controls.Add(bar);
            tile.Title = title;
            tile.Input = input;
            tile.Chat = chat;

            BindQueue(tile, popup);
            WireQueueActions(tile, popup);

            close.Click += (_, _) => BeginInvoke(new Action(() => Remove(item)));
            // 边框用画的不占布局：header/正文/输入框都是 Dock，塞不进额外的线
            tile.Paint += (_, e) =>
            {
                using var pen = new Pen(UiStyle.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
            };

            // AntdUI 的 Enter 在 WM_KEYDOWN 里先被消化成换行（早于 KeyDown）：只能在这里拦。
            // 单行输入框：Enter（运行中=插队）与 Ctrl+Enter（等本轮）都发送，Shift+Enter 让它换行去。
            input.VerifyKeyboard += (_, e) =>
            {
                if (e.KeyData != Keys.Enter && e.KeyData != (Keys.Control | Keys.Enter)) return;
                e.Result = false;
                SendFromTile(tile);
            };

            // 真实簿变化（新轮次、流式输出）→ 立刻镜像进方块
            EventHandler onBookChanged = (_, _) => SyncMirror(tile, force: true);
            item.Book.TurnChanged += onBookChanged;
            tile.BookHandler = onBookChanged;

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

        /// <summary>方块输入框回车：复用普通会话视图的发送管线（空闲直接开跑，运行中进队列）。</summary>
        private void SendFromTile(TileBox tile)
        {
            string text = tile.Input.Text.Trim();
            if (text.Length == 0) return;
            switch (tile.Item.Owner)
            {
                case Session session:
                    // 队列与发送管线都在 SessionView 上：平铺视图打开时页面被收走，可能还没建视图
                    if (session.View == null && HostEnsureView != null) HostEnsureView(session);
                    if (session.View == null) return;
                    session.View.ComposerText = text;
                    session.View.SendMode("task", immediate: true);
                    break;
                case LoopView loop:
                    loop.Send(text, immediate: true);
                    break;
            }
            tile.Input.Clear();
        }

        /// <summary>把会话/Loop 的排队队列绑进方块：有排队项时队列栏自动出现在输入框上方。</summary>
        private void BindQueue(TileBox tile, PendingQueueView popup)
        {
            PendingQueue queue = tile.Item.Owner switch
            {
                Session session => session.View?.Queue,
                LoopView loop => loop.Queue,
                _ => null
            };
            if (queue == null) return;
            tile.Queue = queue;
            tile.Popup = popup;
            popup.Bind(queue);
            Action changed = () =>
            {
                if (popup.IsDisposed || !popup.IsHandleCreated) return;
                if (popup.InvokeRequired) popup.BeginInvoke(new Action(popup.Sync));
                else popup.Sync();
            };
            queue.Changed += changed;
            tile.QueueChangedHandler = changed;
        }

        /// <summary>队列栏的动作转发回会话视图：编辑/立即/删除走同一套打断与输入框逻辑。</summary>
        private static void WireQueueActions(TileBox tile, PendingQueueView popup)
        {
            popup.EditRequested += item => RunOwner(tile, v => v.TileQueueEdit(item), l => l.TileQueueEdit(item));
            popup.ImmediateRequested += item => RunOwner(tile, v => v.TileQueueImmediate(item), l => l.TileQueueImmediate(item));
            popup.Removed += item => RunOwner(tile, v => v.TileQueueRemoved(item), l => l.TileQueueRemoved(item));
        }

        private static void RunOwner(TileBox tile, Action<SessionView> onSession, Action<LoopView> onLoop)
        {
            switch (tile.Item.Owner)
            {
                case Session session when session.View != null: onSession(session.View); break;
                case LoopView loop: onLoop(loop); break;
            }
        }

        private static string WorkspaceOf(TiledItem item) => item.Owner switch
        {
            Session session => session.Workspace,
            LoopView loop => loop.Workspace,
            _ => ""
        };

        /// <summary>
        /// 把真实簿的最新一轮镜像进方块：轮次对象与真实簿共享，字段原地变化（流式）时
        /// 靠 Book.TurnChanged 事件让 ChatFlow 重读；换轮次则整簿重挂。
        /// </summary>
        private void SyncMirror(TileBox tile, bool force)
        {
            if (tile.IsDisposed) return;
            if (tile.InvokeRequired)
            {
                try { tile.BeginInvoke(new Action(() => SyncMirror(tile, force))); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            var book = tile.Item.Book;
            var turn = book?.Turns.LastOrDefault(t => t.User != null) ?? book?.Turns.LastOrDefault();
            if (ReferenceEquals(turn, tile.Mirrored))
            {
                if (force) tile.Mirror.RaiseChanged();   // 同一轮的字段在原地变：让 ChatFlow 重读同一对象
                return;
            }
            tile.Mirrored = turn;
            tile.Mirror.Clear();
            if (turn != null) tile.Mirror.AddTurn(turn);
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

        /// <summary>标题/图标兜底；队列还没绑上（视图是后建的）就补绑一次。</summary>
        private void RefreshContent()
        {
            foreach (var tile in _tiles)
            {
                var item = tile.Item;
                string title = item.Title();
                if (tile.Title.Text != title) tile.Title.Text = title;
                string icon = item.Icon();
                if (tile.Title.PrefixSvg != icon) tile.Title.PrefixSvg = icon;
                if (tile.Queue == null) BindQueue(tile, tile.Popup);
            }
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private sealed class TileBox : Panel
        {
            public TiledItem Item { get; init; }
            public AntdUI.Label Title { get; set; }
            public AntdUI.Input Input { get; set; }
            public ChatFlowControl Chat { get; set; }
            /// <summary>只装最新一轮的镜像簿：ChatFlow 的数据源，轮次对象与真实簿共享。</summary>
            public ChatTurnBook Mirror { get; } = new();
            /// <summary>上一次镜像进来的轮次：用于判断是原地变化还是换轮。</summary>
            public ChatTurn Mirrored { get; set; }
            /// <summary>本会话的排队队列（由真实视图注入）；null = 还没建视图或取不到。</summary>
            public PendingQueue Queue { get; set; }
            public PendingQueueView Popup { get; set; }
            public Action QueueChangedHandler { get; set; }
            public EventHandler BookHandler { get; set; }
        }
    }
}
