using OpenLoop.Service.Pending;

namespace OpenLoop.App
{
    /// <summary>
    /// 输入框上方的待发送队列：每行「模式 + 文本 + 编辑/删除/立即」。
    /// 只管显示与交互，什么时候发送由宿主决定（Worker 会话等轮次，Loop 会话等 step）。
    /// </summary>
    public partial class PendingQueueView : UserControl
    {
        private PendingQueue _queue;

        public PendingQueueView()
        {
            InitializeComponent();
            _list.SizeChanged += (_, _) => FitRows();
        }

        /// <summary>点了「编辑」：宿主把文本放回输入框，并把发送按钮改成「保存」。</summary>
        public event Action<PendingItem> EditRequested;

        /// <summary>点了「立即」：模式已经切换；变成「立即」时宿主去打断当前轮。</summary>
        public event Action<PendingItem> ImmediateRequested;

        /// <summary>点了「删除」：宿主如果正在编辑这一条，要退出编辑态。</summary>
        public event Action<PendingItem> Removed;

        public int Count => _queue?.Count ?? 0;

        public void Bind(PendingQueue queue)
        {
            _queue = queue;
            Sync();
        }

        /// <summary>队列内容变了就重建行；可见性与高度跟着条数走。</summary>
        public void Sync()
        {
            if (IsDisposed) return;

            while (_list.Controls.Count > 0)
            {
                var old = _list.Controls[0];
                _list.Controls.RemoveAt(0);
                // 行按钮的 Click 正在执行时销毁会崩，延后一拍
                if (IsHandleCreated) BeginInvoke(new Action(old.Dispose));
                else old.Dispose();
            }

            int count = _queue?.Count ?? 0;
            _lblCount.Text = $"待发送 {count}";
            if (_queue != null)
                foreach (var item in _queue.Items)
                    _list.Controls.Add(CreateRow(item));

            // 最多露出 4 行，其余滚动
            Height = count == 0 ? 0 : Dip(30) + Math.Min(count, 4) * Dip(28) + Dip(6);
            Visible = count > 0;
            FitRows();
        }

        private Control CreateRow(PendingItem item)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Height = Dip(24),
                Margin = new Padding(0, 0, 0, Dip(4)),
                BackColor = UiStyle.Surface,
                Padding = new Padding(Dip(8), 0, Dip(2), 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 立即的条目用主题强调色，排队的用暗色 —— 三种配色下都靠 UiStyle 走
            Color tone = item.Immediate ? UiStyle.Accent : UiStyle.Muted;

            // 两个字换成图标：⚡ 插到下一步、🕐 等这一轮返回
            var mode = new AntdUI.Button
            {
                IconSvg = UiIcons.Of(item.Immediate ? UiIcons.Bolt : UiIcons.Clock, tone),
                Ghost = true,
                Type = AntdUI.TTypeMini.Default,
                TabStop = false,
                Radius = 6,
                Width = Dip(26),
                Height = Dip(22),
                Margin = new Padding(0, 0, Dip(6), 0)
            };
            _tips.SetToolTip(mode, item.Immediate
                ? "立即：插到下一步（Enter 发送）"
                : "等本轮：等这一轮返回（Ctrl+Enter 发送）");

            var text = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = item.Text.Replace("\r", " ").Replace("\n", " "),
                ForeColor = tone,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            _tips.SetToolTip(text, item.Text);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0)
            };

            var now = RowButton("立即", primary: item.Immediate);
            var remove = RowButton("删除");
            var edit = RowButton("编辑");

            now.Click += (_, _) =>
            {
                if (_queue?.Toggle(item) == null) return;
                Sync();
                ImmediateRequested?.Invoke(item);
            };
            remove.Click += (_, _) =>
            {
                if (_queue?.Remove(item) != true) return;
                Sync();
                Removed?.Invoke(item);
            };
            edit.Click += (_, _) => EditRequested?.Invoke(item);

            actions.Controls.Add(now);
            actions.Controls.Add(remove);
            actions.Controls.Add(edit);

            row.Controls.Add(mode, 0, 0);
            row.Controls.Add(text, 1, 0);
            row.Controls.Add(actions, 2, 0);
            return row;
        }

        private AntdUI.Button RowButton(string text, bool primary = false)
            => new()
            {
                Text = text,
                Ghost = !primary,
                Type = primary ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default,
                Radius = 6,
                Width = Dip(52),
                Height = Dip(24),
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(Dip(6), 0, 0, 0)
            };

        private void FitRows()
        {
            int width = Math.Max(Dip(200),
                _list.ClientSize.Width - Dip(4) - SystemInformation.VerticalScrollBarWidth);
            foreach (Control row in _list.Controls)
                row.Width = width;
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
    }
}
