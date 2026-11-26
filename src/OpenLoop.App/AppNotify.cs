using AntdUI;

namespace OpenLoop.App
{
    /// <summary>弹通知并同时记进 <see cref="NotificationLog"/>：闪过去之后还能从「通知记录」回看。</summary>
    internal static class AppNotify
    {
        public static void Show(Form owner, string title, string text, bool error,
            Func<bool> open = null, TType type = TType.Info)
            => Toast(owner, title, text, error, type, TAlignFrom.TR, null, error ? 12 : 10, open);

        public static void success(Form owner, string title, string text, TAlignFrom align, Font font, int? time)
            => Toast(owner, title, text, false, TType.Success, align, font, time, null);

        public static void info(Form owner, string title, string text, TAlignFrom align, Font font, int? time)
            => Toast(owner, title, text, false, TType.Info, align, font, time, null);

        public static void warn(Form owner, string title, string text, TAlignFrom align, Font font, int? time)
            => Toast(owner, title, text, true, TType.Warn, align, font, time, null);

        public static void error(Form owner, string title, string text, TAlignFrom align, Font font, int? time)
            => Toast(owner, title, text, true, TType.Error, align, font, time, null);

        private static void Toast(Form owner, string title, string text, bool error, TType type,
            TAlignFrom align, Font font, int? time, Func<bool> open)
        {
            NotificationLog.Add(title, text, error);
            if (owner == null || owner.IsDisposed) return;

            var config = new Notification.Config(owner, title, text, type, align, font, time ?? 6);
            var links = new List<Notification.IConfigControl>();
            if (open != null)
                links.Add(new Notification.ConfigLink(Localization.T("打开会话"), () =>
                {
                    open();
                    return false;
                }));
            links.Add(new Notification.ConfigLink(Localization.T("查看记录"), () =>
            {
                NotificationsForm.ShowLog(owner);
                return false;
            }));
            if (links.Count > 0) config.Links = links.ToArray();
            Notification.open(config);
        }
    }
}
