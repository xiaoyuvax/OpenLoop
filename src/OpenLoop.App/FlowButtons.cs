using System.Windows.Forms;

namespace OpenLoop.App
{
    /// <summary>
    /// 按钮组居中：纵向 FlowLayoutPanel 只按子控件 Margin 摆放，不替它们做水平对齐，
    /// 侧栏被拖宽后按钮会一直贴在左边。这里按外层客户区宽度给按钮算左右留白。
    /// </summary>
    internal static class FlowButtons
    {
        /// <summary>
        /// 把 flow 里的按钮当成一组水平居中：组宽取其中最宽的那个，组内左边缘对齐，
        /// 宽窄不一的按钮不会摆成锯齿。在 flow 的 Layout 事件里调用。
        /// </summary>
        internal static void Center(FlowLayoutPanel flow)
        {
            if (flow == null || flow.IsDisposed) return;
            if (flow.FlowDirection != FlowDirection.TopDown) return;

            // ClientSize 已扣掉滚动条，直接用它算可用宽度
            int inner = flow.ClientSize.Width - flow.Padding.Horizontal;
            if (inner <= 0) return;

            var group = new List<Control>();
            int width = 0;
            foreach (Control control in flow.Controls)
            {
                // 只收按钮类：同层还有左对齐的信息标签，它们不该跟着居中
                if (control is not AntdUI.Button && control is not FlowLayoutPanel) continue;
                if (!control.Visible) continue;
                group.Add(control);
                width = Math.Max(width, control.Width);
            }
            if (group.Count == 0) return;

            int left = Math.Max(0, (inner - width) / 2);
            foreach (Control control in group)
            {
                int right = Math.Max(0, inner - left - control.Width);
                // 值没变就不写：写 Margin 会再触发一次 Layout
                var wanted = new Padding(left, control.Margin.Top, right, control.Margin.Bottom);
                if (control.Margin != wanted) control.Margin = wanted;
            }
        }
    }
}
