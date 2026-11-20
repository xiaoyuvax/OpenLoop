namespace OpenLoop.Service.Pending
{
    /// <summary>排队文本的插入时机。</summary>
    public enum PendingMode
    {
        /// <summary>等当前这一轮跑完再插（默认）。</summary>
        AfterTurn,

        /// <summary>当前 step 一结束就插，打断这一轮。</summary>
        NextStep
    }

    public sealed class PendingItem
    {
        public string Text { get; set; } = "";

        /// <summary>和这条消息一起排队的附件路径；发送时才读文件。</summary>
        public List<string> Attachments { get; set; } = [];

        /// <summary>task / criterion / correction，沿用发送时的模式标签。</summary>
        public string Tag { get; set; } = "task";

        public PendingMode Mode { get; set; } = PendingMode.AfterTurn;

        public bool Immediate => Mode == PendingMode.NextStep;
    }

    /// <summary>
    /// 运行中提交的文本队列。只排队，不发送：发送时机由宿主决定
    /// （Worker 会话看轮次，Loop 会话看 step）。
    /// </summary>
    public sealed class PendingQueue
    {
        private readonly List<PendingItem> _items = [];

        /// <summary>正在输入框里编辑的条目：留在队列里，但不被自动取走。</summary>
        private readonly HashSet<PendingItem> _held = [];

        /// <summary>队列内容变了（增删、切模式、取走）：宿主用它落盘。</summary>
        public event Action Changed;

        public IReadOnlyList<PendingItem> Items => _items;

        public int Count => _items.Count;

        /// <summary>被「停止」暂停时不再自动续发，队列保留。</summary>
        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value) return;
                _paused = value;
                Changed?.Invoke();
            }
        }

        private bool _paused;

        /// <summary>
        /// 还有没有会被真正发出的「立即」项。正在编辑的那条被搁置、不会被取走，
        /// 算进来会让当前轮白打断一次却什么也不发。
        /// </summary>
        public bool AnyImmediate => _items.Exists(i => i.Immediate && !_held.Contains(i));

        public PendingItem Add(string text, string tag = "task", PendingMode mode = PendingMode.AfterTurn,
            IEnumerable<string> attachments = null)
        {
            var files = (attachments ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(text) && files.Count == 0) return null;
            var item = new PendingItem { Text = text?.Trim() ?? "", Tag = tag ?? "task", Mode = mode, Attachments = files };
            _items.Add(item);
            Changed?.Invoke();
            return item;
        }

        /// <summary>启动时把上次残留的条目放回来（不逐条触发 Changed）。</summary>
        public void Restore(IEnumerable<PendingItem> items, bool paused = false)
        {
            _items.Clear();
            _held.Clear();
            foreach (var item in items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item?.Text) && (item?.Attachments?.Count ?? 0) == 0) continue;
                _items.Add(item);
            }
            _paused = paused;
        }

        public bool Remove(PendingItem item)
        {
            if (item == null || !_items.Remove(item)) return false;
            _held.Remove(item);
            Changed?.Invoke();
            return true;
        }

        /// <summary>进入编辑态：这一条搁置，等 <see cref="Release"/> 之后才重新可发。</summary>
        public void Hold(PendingItem item)
        {
            if (item == null) return;
            _held.Add(item);
        }

        /// <summary>编辑结束（保存）：重新入列可发。</summary>
        public void Release(PendingItem item)
        {
            if (item == null || !_held.Remove(item)) return;
            Changed?.Invoke();
        }

        /// <summary>在「等本轮」与「立即」之间切换；返回切换后的条目（不在队列里则返回 null）。</summary>
        public PendingItem Toggle(PendingItem item)
        {
            if (item == null || !_items.Contains(item)) return null;
            item.Mode = item.Immediate ? PendingMode.AfterTurn : PendingMode.NextStep;
            Changed?.Invoke();
            return item;
        }

        /// <summary>取下一个要发的：优先「立即」，其次按入队顺序；暂停或正在编辑时不取。</summary>
        public PendingItem TakeNext()
        {
            if (Paused || _items.Count == 0) return null;
            var next = _items.FirstOrDefault(i => i.Immediate && !_held.Contains(i))
                       ?? _items.FirstOrDefault(i => !_held.Contains(i));
            if (next == null) return null;
            _items.Remove(next);
            _held.Remove(next);
            Changed?.Invoke();
            return next;
        }

        /// <summary>
        /// 按类批次取下一批要发的：优先「立即」，其次按入队顺序；
        /// 同一批里所有条目 Mode 相同（暂停或编辑中的条目跳过）。
        /// 宿主把整批合并成一条发送，避免逐条开轮。
        /// </summary>
        public IReadOnlyList<PendingItem> TakeBatch()
        {
            if (Paused || _items.Count == 0) return new List<PendingItem>();
            var first = _items.FirstOrDefault(i => i.Immediate && !_held.Contains(i))
                        ?? _items.FirstOrDefault(i => !_held.Contains(i));
            if (first == null) return new List<PendingItem>();
            var mode = first.Mode;
            var batch = _items.Where(i => i.Mode == mode && !_held.Contains(i)).ToList();
            foreach (var item in batch)
            {
                _items.Remove(item);
                _held.Remove(item);
            }
            Changed?.Invoke();
            return batch;
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            _held.Clear();
            Changed?.Invoke();
        }
    }
}