using System.Text;

namespace OpenLoop.App
{
    /// <summary>通知记录：把闪过的通知按时间列出来，可选中复制；开着时新通知实时追加。</summary>
    internal sealed partial class NotificationsForm : Form
    {
        private static NotificationsForm _open;

        public static void ShowLog(IWin32Window owner)
        {
            if (_open != null && !_open.IsDisposed)
            {
                _open.Activate();
                return;
            }
            var form = new NotificationsForm();
            _open = form;
            form.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_open, form)) _open = null;
            };
            if (owner is Form parent && !parent.IsDisposed) form.Show(parent);
            else form.Show();
        }

        public NotificationsForm()
        {
            InitializeComponent();
            Icon = UiIcons.App;
            NotificationLog.Changed += RefreshLog;
            RefreshLog();
        }

        private void RefreshLog()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(RefreshLog)); }
                catch (ObjectDisposedException) { }
                return;
            }

            bool atEnd = _txtLog.TextLength == 0 || _txtLog.SelectionStart >= _txtLog.TextLength - 1;
            var entries = NotificationLog.Snapshot();
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.Append(entry.At.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ")
                    .Append(Localization.T(entry.Error ? "NotificationLevelError" : "NotificationLevelInfo"))
                    .Append("  ").AppendLine(entry.Title);
                sb.AppendLine(entry.Text);
                sb.AppendLine();
            }
            _txtLog.Text = entries.Length == 0 ? Localization.T("NotificationEmpty") : sb.ToString().TrimEnd();
            if (atEnd)
            {
                _txtLog.SelectionStart = _txtLog.TextLength;
                _txtLog.SelectionLength = 0;
                _txtLog.ScrollToCaret();
            }
        }

        private void OnClear(object sender, EventArgs e) => NotificationLog.Clear();

        private void OnClose(object sender, EventArgs e) => Close();
    }
}
