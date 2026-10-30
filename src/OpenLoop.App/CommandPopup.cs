using System.Drawing;

namespace OpenLoop.App
{
    /// <summary>
    /// 输入框上方的命令联想列表。自绘（不依赖原生控件外观），跟随主题；
    /// 上下键移动、回车或点击确认，Esc 关闭。位置由宿主 <see cref="SessionView"/> 摆。
    /// 命令数量超过 <see cref="MaxVisibleRows"/> 时只显示这么多行，靠上下键/滚轮滚动。
    /// </summary>
    internal sealed class CommandPopup : Control
    {
        private readonly List<(string Command, string Description)> items = [];
        private int selected;
        private int scrollOffset;

        /// <summary>最多同时显示多少行；超出的靠滚动查看。</summary>
        private const int MaxVisibleRows = 6;

        internal event Action<string> Accepted;

        internal CommandPopup()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
            Visible = false;
            BackColor = UiStyle.Surface;
            Cursor = Cursors.Hand;
        }

        internal bool IsOpen => Visible;

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        internal int RowHeight => Dip(30);

        /// <summary>最多显示的行数（命令更多时滚动）。</summary>
        private int VisibleRows => Math.Max(1, Math.Min(items.Count, MaxVisibleRows));

        /// <summary>列表所需高度；宿主用返回值设置所在行高。</summary>
        internal int PreferredHeight => VisibleRows * RowHeight + Dip(8);

        internal void Open(IReadOnlyList<(string Command, string Description)> matches)
        {
            items.Clear();
            items.AddRange(matches);
            if (items.Count == 0)
            {
                Close();
                return;
            }

            selected = 0;
            scrollOffset = 0;
            Visible = true;
            Invalidate();
        }

        internal void Close()
        {
            if (Visible) Visible = false;
            items.Clear();
        }

        /// <summary>上下移动选中项；到头就停住，不绕回（配合滚动更直观）。</summary>
        internal void MoveSelection(int delta)
        {
            if (items.Count == 0) return;
            int next = Math.Clamp(selected + delta, 0, items.Count - 1);
            if (next == selected) return;
            selected = next;
            EnsureVisible();
            Invalidate();
        }

        /// <summary>滚轮：整行滚动，不改选中项。</summary>
        internal void ScrollBy(int rows)
        {
            int maxOffset = Math.Max(0, items.Count - VisibleRows);
            int next = Math.Clamp(scrollOffset + rows, 0, maxOffset);
            if (next == scrollOffset) return;
            scrollOffset = next;
            Invalidate();
        }

        private void EnsureVisible()
        {
            if (selected < scrollOffset) scrollOffset = selected;
            else if (selected >= scrollOffset + VisibleRows) scrollOffset = selected - VisibleRows + 1;
            scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(0, items.Count - VisibleRows));
        }

        internal void Accept()
        {
            if (selected < 0 || selected >= items.Count) return;
            string command = items[selected].Command;
            Close();
            Accepted?.Invoke(command);
        }

        private int IndexAt(int y)
        {
            if (items.Count == 0) return -1;
            int index = scrollOffset + (y - Dip(4)) / RowHeight;
            return index < 0 || index >= items.Count ? -1 : index;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = IndexAt(e.Y);
            if (index < 0) return;
            selected = index;
            Accept();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Y);
            if (index < 0 || index == selected) return;
            selected = index;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta > 0 ? -3 : 3);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UiStyle.Surface);
            using (var border = new Pen(UiStyle.Border))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            int padX = Dip(12);
            int gap = Dip(14);
            int rows = VisibleRows;
            int top0 = Dip(4);
            for (int i = 0; i < rows; i++)
            {
                int index = scrollOffset + i;
                if (index >= items.Count) break;
                int top = top0 + i * RowHeight;
                if (index == selected)
                {
                    using var brush = new SolidBrush(Color.FromArgb(28, UiStyle.Accent));
                    g.FillRectangle(brush, new Rectangle(1, top, Width - 2, RowHeight));
                }

                var (command, description) = items[index];
                Size commandSize = TextRenderer.MeasureText(g, command, Font, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, command, Font,
                    new Rectangle(padX, top, commandSize.Width + 4, RowHeight), UiStyle.Accent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                int descLeft = padX + commandSize.Width + gap;
                TextRenderer.DrawText(g, description, Font,
                    new Rectangle(descLeft, top, Math.Max(0, Width - descLeft - padX), RowHeight), UiStyle.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
            }

            // 命令多于可见行时，右侧画一条细滚动指示条（只读指示，滚动靠上下键/滚轮）
            if (items.Count > rows)
            {
                int track = Height - top0 * 2;
                int thumb = Math.Max(Dip(24), track * rows / items.Count);
                int maxOffset = items.Count - rows;
                int thumbTop = top0 + (maxOffset == 0 ? 0 : (track - thumb) * scrollOffset / maxOffset);
                using var thumbBrush = new SolidBrush(Color.FromArgb(70, UiStyle.Accent));
                g.FillRectangle(thumbBrush, new Rectangle(Width - Dip(5), thumbTop, Dip(3), thumb));
            }
        }
    }
}
