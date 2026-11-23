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

        public const string Minimize =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M4 11h16v2H4z'/></svg>";

        public const string Maximize =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M5 5h14v14H5zm2 2v10h10V7z'/></svg>";

        public const string Restore =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M8 3h11v11h-2V5H8zM5 8h11v11H5zm2 2v7h7v-7z'/></svg>";

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

        public const string Trash =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M9 3h6l1 2h4v2H4V5h4zM6 8h12l-1 12a1 1 0 01-1 1H8a1 1 0 01-1-1zm3 2v9h2v-9zm4 0v9h2v-9z'/></svg>";

        /// <summary>立即插入下一步。</summary>
        public const string Bolt =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M13 2L4 14h6l-1 8 9-12h-6z'/></svg>";

        /// <summary>地址栏「前往」。</summary>
        public const string Go =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M13 4.6L20.4 12 13 19.4l-1.4-1.4 5-5H3.6v-2h13l-5-5z'/></svg>";

        /// <summary>用系统默认浏览器打开当前页。</summary>
        public const string External =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M14 3h7v7h-2V6.4l-8.3 8.3-1.4-1.4L17.6 5H14zM4 4h6v2H6v12h12v-4h2v6H4z'/></svg>";

        /// <summary>锁定网页交互。</summary>
        public const string Lock =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 2a5 5 0 015 5v2h1a2 2 0 012 2v9a2 2 0 01-2 2H6a2 2 0 01-2-2v-9a2 2 0 012-2h1V7a5 5 0 015-5zm0 2a3 3 0 00-3 3v2h6V7a3 3 0 00-3-3zm0 8a2 2 0 00-1 3.7V19h2v-3.3A2 2 0 0012 12z'/></svg>";

        /// <summary>解锁网页交互。</summary>
        public const string Unlock =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 2a5 5 0 015 5h-2a3 3 0 00-6 0v2h8a2 2 0 012 2v9a2 2 0 01-2 2H6a2 2 0 01-2-2v-9a2 2 0 012-2h1V7a5 5 0 015-5zm0 10a2 2 0 00-1 3.7V19h2v-3.3A2 2 0 0012 12z'/></svg>";

        /// <summary>搬进独立窗口。</summary>
        public const string Undock =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M3 5h13v3h-2V7H5v10h3v2H3zM10 9h11v11H10zm2 2v7h7v-7z'/></svg>";

        /// <summary>Loop 标识：同心圆（OpenLoop 自己的 Worker），与 Worker 的纯文字标题区分。</summary>
        public const string Loop =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 2a10 10 0 110 20 10 10 0 010-20zm0 3a7 7 0 100 14 7 7 0 000-14zm0 4a3 3 0 110 6 3 3 0 010-6z'/></svg>";

        /// <summary>agent 自动插入的消息（协作汇报）。</summary>
        public const string Auto =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M11 2h2v4h3a3 3 0 013 3v8a3 3 0 01-3 3H8a3 3 0 01-3-3V9a3 3 0 013-3h3zM9 10v2h2v-2zm4 0v2h2v-2zM9 14.5V16h6v-1.5z'/></svg>";

        /// <summary>等这一轮返回。</summary>
        public const string Clock =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M12 3a9 9 0 110 18 9 9 0 010-18zm0 2a7 7 0 100 14 7 7 0 000-14zm-1 2h2v5.6l4 2.4-1 1.7-5-3z'/></svg>";

        /// <summary>生图模型（画框 + 山 + 太阳）。</summary>
        public const string Image =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M5 4h14a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V6a2 2 0 012-2zm0 2v9.6l4.2-4.2 3.3 3.3 3.1-4.1L19 14V6z'/><circle fill='{0}' cx='15.5' cy='9' r='1.5'/></svg>";

        /// <summary>上传附件（回形针）。</summary>
        public const string Attach =
            "<svg viewBox='0 0 24 24'><path fill='{0}' d='M16.5 6.5l-7.8 7.8a2.5 2.5 0 003.5 3.5l8-8a4.5 4.5 0 00-6.4-6.4l-8.3 8.3a6.5 6.5 0 009.2 9.2l6.6-6.6-1.4-1.4-6.6 6.6a4.5 4.5 0 01-6.4-6.4l8.3-8.3a2.5 2.5 0 013.5 3.5l-8 8a.5.5 0 01-.7-.7l7.8-7.8-1.3-1.3z'/></svg>";

        public const string DotShape =
            "<svg viewBox='0 0 24 24'><circle cx='12' cy='12' r='7' fill='{0}'/></svg>";

        public static string Dot(Color color) => Of(DotShape, color);

        /// <summary>呼吸动画的帧数：运行中的标签页图标在底色与本色之间来回渐变。</summary>
        public const int BreathFrames = 8;

        public static string Breath(string template, Color color, int phase)
        {
            double wave = (Math.Sin(2 * Math.PI * (phase % BreathFrames) / BreathFrames) + 1) / 2;
            return Of(template, Mix(UiStyle.Surface, color, 0.3 + 0.7 * wave));
        }

        private static Color Mix(Color back, Color color, double ratio) => Color.FromArgb(
            (int)Math.Round(back.R + (color.R - back.R) * ratio),
            (int)Math.Round(back.G + (color.G - back.G) * ratio),
            (int)Math.Round(back.B + (color.B - back.B) * ratio));

        // 项目列表每次刷新都会重画全部头像：同（字母, 颜色, 尺寸）只画一次，位图全程复用。
        private static readonly Dictionary<(string, Color, int), Image> Avatars = new();

        public static Image Avatar(string text, Color color, int size)
        {
            string letter = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[..1].ToUpperInvariant();
            if (Avatars.TryGetValue((letter, color, size), out var cached)) return cached;

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
            using var font = new Font("Segoe UI", size * 0.45F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(letter, font, Brushes.White, new RectangleF(0, 0, size, size), sf);
            Avatars[(letter, color, size)] = bmp;
            return bmp;
        }

        public static string Of(string template, Color color) => string.Format(template, Hex(color));

        private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
