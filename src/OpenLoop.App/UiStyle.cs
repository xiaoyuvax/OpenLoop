using System.Drawing;

namespace OpenLoop.App
{
    internal enum AppTheme
    {
        Light,
        Dark,
        GrayBlue
    }

    /// <summary>三套配色：浅色（Ant Design 默认）、深色、灰蓝。启动时按设置应用。</summary>
    internal static class UiStyle
    {
        public static Color Accent { get; private set; } = Color.FromArgb(22, 119, 255);
        public static Color AccentHover { get; private set; } = Color.FromArgb(64, 150, 255);
        public static Color Surface { get; private set; } = Color.White;
        public static Color Page { get; private set; } = Color.FromArgb(245, 245, 245);
        public static Color Muted { get; private set; } = Color.FromArgb(140, 140, 140);
        public static Color Border { get; private set; } = Color.FromArgb(232, 232, 232);
        public static Color Text { get; private set; } = Color.FromArgb(31, 31, 31);
        public static Color SecondaryBack { get; private set; } = Color.FromArgb(250, 250, 250);
        public static Color Success { get; private set; } = Color.FromArgb(82, 196, 26);
        public static Color Error { get; private set; } = Color.FromArgb(255, 77, 79);
        public static Color Loop { get; private set; } = Color.FromArgb(114, 46, 209);

        public static AppTheme Theme { get; private set; } = AppTheme.Light;

        public static AppTheme Parse(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "dark" => AppTheme.Dark,
            "grayblue" => AppTheme.GrayBlue,
            _ => AppTheme.Light
        };

        public static void Apply(AppTheme theme)
        {
            Theme = theme;
            switch (theme)
            {
                case AppTheme.Dark:
                    Accent = Color.FromArgb(64, 150, 255);
                    AccentHover = Color.FromArgb(96, 170, 255);
                    Surface = Color.FromArgb(31, 31, 31);
                    Page = Color.FromArgb(20, 20, 20);
                    Muted = Color.FromArgb(166, 166, 166);
                    Border = Color.FromArgb(48, 48, 48);
                    Text = Color.FromArgb(232, 232, 232);
                    SecondaryBack = Color.FromArgb(38, 38, 38);
                    Success = Color.FromArgb(82, 196, 26);
                    Error = Color.FromArgb(255, 120, 117);
                    Loop = Color.FromArgb(179, 127, 235);
                    break;
                case AppTheme.GrayBlue:
                    Accent = Color.FromArgb(59, 110, 165);
                    AccentHover = Color.FromArgb(84, 136, 194);
                    Surface = Color.White;
                    Page = Color.FromArgb(238, 242, 246);
                    Muted = Color.FromArgb(107, 114, 128);
                    Border = Color.FromArgb(220, 227, 234);
                    Text = Color.FromArgb(31, 41, 55);
                    SecondaryBack = Color.FromArgb(245, 248, 250);
                    Success = Color.FromArgb(82, 196, 26);
                    Error = Color.FromArgb(220, 60, 60);
                    Loop = Color.FromArgb(124, 107, 176);
                    break;
                default:
                    Accent = Color.FromArgb(22, 119, 255);
                    AccentHover = Color.FromArgb(64, 150, 255);
                    Surface = Color.White;
                    Page = Color.FromArgb(245, 245, 245);
                    Muted = Color.FromArgb(140, 140, 140);
                    Border = Color.FromArgb(232, 232, 232);
                    Text = Color.FromArgb(31, 31, 31);
                    SecondaryBack = Color.FromArgb(250, 250, 250);
                    Success = Color.FromArgb(82, 196, 26);
                    Error = Color.FromArgb(255, 77, 79);
                    Loop = Color.FromArgb(114, 46, 209);
                    break;
            }

            AntdUI.Config.IsDark = theme == AppTheme.Dark;
            ApplyChatTheme(theme);
        }

        private static void ApplyChatTheme(AppTheme theme)
        {
            if (theme == AppTheme.Light) return;

            bool dark = theme == AppTheme.Dark;
            ChatFlow.ChatTheme.Set(new ChatFlow.ChatPalette
            {
                Back = dark ? Color.FromArgb(31, 31, 31) : Page,
                Fore = Text,
                ForeDim = Muted,
                Accent = Accent,
                Border = Border,
                Control = dark ? Color.FromArgb(43, 43, 43) : Surface,
                MenuHover = dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(235, 240, 246),
                InputBack = Surface,
                InputFore = Text,
                ChatFore = Text,
                ChatThinkingFore = Muted
            });
        }
    }
}
