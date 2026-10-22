using System.Drawing;

namespace OpenLoop.App
{
    /// <summary>
    /// 内联 SVG 图标（AntdUI 的 IconSvg 需要 SVG 字符串，不依赖外部文件）。
    /// 模板里的 {0} 是填充色占位符，便于同一图标在不同背景上取色。
    /// </summary>
    internal static class UiIcons
    {
        public static Icon App { get; } =
            new Icon(typeof(UiIcons).Assembly.GetManifestResourceStream("OpenLoop.App.app.ico"));

        public const string Menu =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M3 5h18v2H3zM3 11h18v2H3zM3 17h18v2H3z'/></svg>";

        public const string Plus =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M11 4h2v7h7v2h-7v7h-2v-7H4v-2h7z'/></svg>";

        public const string Close =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M6.4 5L12 10.6 17.6 5 19 6.4 13.4 12 19 17.6 17.6 19 12 13.4 6.4 19 5 17.6 10.6 12 5 6.4z'/></svg>";

        public const string Send =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M3 20.5l18-8.5L3 3.5v6.2L15 12 3 14.3z'/></svg>";

        public const string Stop =
            "<svg viewBox='0 0 24 24'><rect fill='{0}' x='6' y='6' width='12' height='12' rx='2'/></svg>";

        public const string Settings =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M3 5h18v2H3zM3 17h18v2H3z'/><circle fill='{0}' cx='8' cy='6' r='3'/><circle fill='{0}' cx='16' cy='18' r='3'/></svg>";

        public const string List =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M4 5h3v3H4zM9 5h11v3H9zM4 10.5h3v3H4zM9 10.5h11v3H9zM4 16h3v3H4zM9 16h11v3H9z'/></svg>";

        public const string Folder =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M3 6a2 2 0 012-2h5l2 2h7a2 2 0 012 2v10a2 2 0 01-2 2H5a2 2 0 01-2-2z'/></svg>";

        public const string Model =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M8 3h2v3h4V3h2v3h3v3h3v2h-3v4h3v2h-3v3h-3v3h-2v-3h-4v3H8v-3H5v-3H2v-2h3v-4H2V9h3V6h3zm0 6v8h8V9z'/></svg>";

        public const string Effort =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M13 2L4 14h6l-1 8 9-12h-6z'/></svg>";

        public const string PanelOpen =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M14.4 5L16 6.6 10.6 12 16 17.4 14.4 19 7.4 12z'/></svg>";

        public const string PanelClose =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M9.6 5L8 6.6 13.4 12 8 17.4 9.6 19 16.6 12z'/></svg>";

        public const string Refresh =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 5a7 7 0 016.7 5H21l-3.5 4L14 10h2.4A5 5 0 007 12H5a7 7 0 017-7zm-6.7 9H3l3.5-4L10 14H7.6A5 5 0 0017 12h2a7 7 0 01-13.7 2z'/></svg>";

        public const string Undo =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 5V2L7 6.5 12 11V8a4.5 4.5 0 11-4.5 4.5H5A7 7 0 1012 5z'/></svg>";

        public const string Search =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M10 3a7 7 0 015.6 11.2l5.6 5.6-1.4 1.4-5.6-5.6A7 7 0 1110 3zm0 2a5 5 0 100 10 5 5 0 000-10z'/></svg>";

        public const string Edit =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M4 20h4l10.5-10.5-4-4L4 16v4zm12.7-12.7l1.6-1.6a1 1 0 000-1.4l-2.6-2.6a1 1 0 00-1.4 0l-1.6 1.6z'/></svg>";

        public static string Dot(Color color)
            => $"<svg viewBox='0 0 24 24'><circle cx='12' cy='12' r='7' fill='{Hex(color)}'/></svg>";

        public static Image Avatar(string text, Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            int r = Math.Max(4, size / 4);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(size - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(size - r * 2, size - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, size - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            string letter = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[..1].ToUpperInvariant();
            using var font = new Font("Segoe UI", size * 0.45F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(letter, font, Brushes.White, new RectangleF(0, 0, size, size), sf);
            return bmp;
        }

        public static string Of(string template, Color color) => string.Format(template, Hex(color));

        private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
