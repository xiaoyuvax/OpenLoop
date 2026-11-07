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

        public IReadOnlyList<PendingItem> Items => _items;

        public int Count => _items.Count;

        /// <summary>被「停止」暂停时不再自动续发，队列保留。</summary>
        public bool Paused { get; set; }

        public bool AnyImmediate => _items.Exists(i => i.Immediate);

        public PendingItem Add(string text, string tag = "task", PendingMode mode = PendingMode.AfterTurn)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var item = new PendingItem { Text = text.Trim(), Tag = tag ?? "task", Mode = mode };
            _items.Add(item);
            return item;
        }

        public bool Remove(PendingItem item) => item != null && _items.Remove(item);

        /// <summary>在「等本轮」与「立即」之间切换；返回切换后的条目（不在队列里则返回 null）。</summary>
        public PendingItem Toggle(PendingItem item)
        {
            if (item == null || !_items.Contains(item)) return null;
            item.Mode = item.Immediate ? PendingMode.AfterTurn : PendingMode.NextStep;
            return item;
        }

        /// <summary>取下一个要发的：优先「立即」，其次按入队顺序；暂停时不取。</summary>
        public PendingItem TakeNext()
        {
            if (Paused || _items.Count == 0) return null;
            var next = _items.FirstOrDefault(i => i.Immediate) ?? _items[0];
            _items.Remove(next);
            return next;
        }

        public void Clear() => _items.Clear();
    }
}
