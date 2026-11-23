using OpenLoop.Service.Pending;
using System.Text;

namespace OpenLoop.App
{
    /// <summary>
    /// 输入框上方的待发送队列：每行「模式 + 文本 + 编辑/删除/立即」。
    /// 只管显示与交互，什么时候发送由宿主决定（Worker 会话等轮次，Loop 会话等 step）。
    /// </summary>
    public partial class PendingQueueView : UserControl
    {
        private PendingQueue _queue;
        private IReadOnlyList<string> _agentNotes = [];

        /// <summary>上次建行时的内容指纹：宿主高频 Sync 而内容没变就跳过重建。</summary>
        private string _syncSignature;

        public PendingQueueView()
        {
            InitializeComponent();
            // 队列行按内容整体重建：双缓冲，避免重建过程中闪白
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
            _list.SizeChanged += (_, _) => FitRows();
        }

        /// <summary>
        /// agent 自动插入的消息（协作监测汇报）：只在本机队列栏里显示，不归本机队列管，
        /// 所以没有编辑/删除/立即按钮，也不参与发送。
        /// </summary>
        public void SetAgentNotes(IReadOnlyList<string> notes)
        {
            _agentNotes = notes ?? [];
            Sync();
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
            _syncSignature = null;   // 换了队列：下一次 Sync 必须重建
            Sync();
        }

        /// <summary>队列内容变了就重建行；可见性与高度跟着条数走。</summary>
        public void Sync()
        {
            if (IsDisposed) return;

            // 宿主每 250ms 把 A 的快照并进来一次：内容没变就别重建行，否则队列栏一直闪
            string signature = Signature();
            if (signature == _syncSignature) return;
            _syncSignature = signature;

            SuspendLayout();
            _list.SuspendLayout();
            try
            {
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
                // agent 自动插入的排在前面：用户一眼看出哪些不是自己敲的
                foreach (string note in _agentNotes)
                    _list.Controls.Add(CreateNoteRow(note));
                if (_queue != null)
                    foreach (var item in _queue.Items)
                        _list.Controls.Add(CreateRow(item));

                // 最多露出 4 行，其余滚动
                int rows = count + _agentNotes.Count;
                Height = rows == 0 ? 0 : Dip(30) + Math.Min(rows, 4) * Dip(28) + Dip(6);
                Visible = rows > 0;
                FitRows();
            }
            finally
            {
                _list.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        /// <summary>队列栏要显示的内容指纹：条数、自动汇报文本、每条的模式/来源/附件/正文。</summary>
        private string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(_queue?.Count ?? 0).Append('|').Append(_agentNotes.Count).Append('|');
            foreach (string note in _agentNotes)
                sb.Append(note).Append('\u001F');
            if (_queue != null)
                foreach (var item in _queue.Items)
                    sb.Append(item.Auto ? 'a' : 'u')
                        .Append(item.Immediate ? 'i' : 'w')
                        .Append(item.Attachments.Count).Append(':')
                        .Append(item.Text).Append('\u001F');
            return sb.ToString();
        }

        /// <summary>agent 自动插入的一行：特殊底色 + 自动图标，没有操作按钮。</summary>
        private Control CreateNoteRow(string text)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Height = Dip(24),
                Margin = new Padding(0, 0, 0, Dip(4)),
                BackColor = UiStyle.AutoInsertBack,
                Padding = new Padding(Dip(8), 0, Dip(8), 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var tag = AutoIcon();

            string line = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            var label = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextMultiLine = false,
                ShowTooltip = false,
                Text = line,
                ForeColor = UiStyle.Text,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            _tips.SetToolTip(label, line);

            row.Controls.Add(tag, 0, 0);
            row.Controls.Add(label, 1, 0);
            return row;
        }

        private Control CreateRow(PendingItem item)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Height = Dip(24),
                Margin = new Padding(0, 0, 0, Dip(4)),
                // agent 自动插入的（协作监测汇报）用特殊底色标出，用户敲的仍是普通底色
                BackColor = item.Auto ? UiStyle.AutoInsertBack : UiStyle.Surface,
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
                ? "立即：插到下一步（当前轮在下一个 step 前收尾）"
                : "等本轮：等这一轮返回（Ctrl+Enter 发送）");

            // 自动插入的条目没有「立即/等本轮」的选择：位置上放自动图标，说明这条不是用户敲的
            Control lead = item.Auto ? AutoIcon() : mode;

            string line = item.Text.Length > 0 ? item.Text : "📎 附件";
            var text = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextMultiLine = false,
                ShowTooltip = false,
                Text = line.Replace("\r", " ").Replace("\n", " "),
                ForeColor = tone,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            _tips.SetToolTip(text, line);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0)
            };

            var remove = RowButton("删除");
            var edit = RowButton("编辑");

            // 自动插入的已经是「立即」，不给切换按钮（与 Web 一致）
            if (!item.Auto)
            {
                var now = RowButton("立即", primary: item.Immediate);
                now.Click += (_, _) =>
                {
                    if (_queue?.Toggle(item) == null) return;
                    Sync();
                    ImmediateRequested?.Invoke(item);
                };
                actions.Controls.Add(now);
            }
            remove.Click += (_, _) =>
            {
                if (_queue?.Remove(item) != true) return;
                Sync();
                Removed?.Invoke(item);
            };
            edit.Click += (_, _) => EditRequested?.Invoke(item);

            actions.Controls.Add(remove);
            actions.Controls.Add(edit);

            row.Controls.Add(lead, 0, 0);
            row.Controls.Add(text, 1, 0);
            row.Controls.Add(actions, 2, 0);
            return row;
        }

        /// <summary>自动插入消息的图标：与「立即/等本轮」的图标同一尺寸，左侧对齐。</summary>
        private AntdUI.Button AutoIcon()
        {
            var icon = new AntdUI.Button
            {
                IconSvg = UiIcons.Of(UiIcons.Auto, UiStyle.Accent),
                Ghost = true,
                Type = AntdUI.TTypeMini.Default,
                TabStop = false,
                Radius = 6,
                Width = Dip(26),
                Height = Dip(22),
                Margin = new Padding(0, 0, Dip(6), 0)
            };
            _tips.SetToolTip(icon, "自动：agent 插入（立即发送）");
            return icon;
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
