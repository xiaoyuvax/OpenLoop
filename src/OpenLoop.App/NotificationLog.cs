namespace OpenLoop.App
{
    internal sealed record NotificationEntry(DateTime At, string Title, string Text, bool Error);

    /// <summary>壳里弹过的通知留一份（进程内），通知记录窗口按这份回看。</summary>
    internal static class NotificationLog
    {
        private const int Max = 300;
        private static readonly List<NotificationEntry> Items = [];
        private static readonly object Gate = new();

        public static event Action Changed;

        public static NotificationEntry[] Snapshot()
        {
            lock (Gate) return [.. Items];
        }

        public static void Add(string title, string text, bool error)
        {
            lock (Gate)
            {
                Items.Add(new NotificationEntry(DateTime.Now, title ?? "", text ?? "", error));
                if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max);
            }
            Changed?.Invoke();
        }

        public static void Clear()
        {
            lock (Gate) Items.Clear();
            Changed?.Invoke();
        }
    }
}
