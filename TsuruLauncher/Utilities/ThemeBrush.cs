using System.Windows;
using System.Windows.Media;

namespace TsuruLauncher.Utilities
{
    /// <summary>
    /// 代码后置里取主题画刷的统一入口（浅色 / 深色 / OLED 都能正确取色）。
    /// </summary>
    public static class ThemeBrush
    {
        public static Brush Get(string key, Brush? fallback = null)
            => Application.Current == null
                ? (fallback ?? Brushes.Transparent)
                : (Application.Current.TryFindResource(key) as Brush) ?? (fallback ?? Brushes.Transparent);

        private static SolidColorBrush Solid(string key, Color fallback)
            => Get(key) as SolidColorBrush ?? new SolidColorBrush(fallback);

        public static SolidColorBrush TextPrimary => Solid("TextPrimaryBrush", Colors.Black);
        public static SolidColorBrush TextSecondary => Solid("TextSecondaryBrush", Colors.DimGray);
        public static SolidColorBrush TextTertiary => Solid("TextTertiaryBrush", Colors.Gray);
        public static SolidColorBrush Surface2 => Solid("Surface2Brush", Colors.WhiteSmoke);
        public static SolidColorBrush Surface3 => Solid("Surface3Brush", Colors.White);
        public static SolidColorBrush Surface4 => Solid("Surface4Brush", Colors.Gainsboro);
        public static SolidColorBrush Accent => Solid("AccentBrush", Colors.SeaGreen);
        public static SolidColorBrush AccentForeground => Solid("AccentForegroundBrush", Colors.White);
        public static SolidColorBrush Danger => Solid("DangerBrush", Colors.IndianRed);
        public static SolidColorBrush Warning => Solid("WarningBrush", Colors.Goldenrod);
        public static SolidColorBrush Success => Solid("SuccessBrush", Colors.SeaGreen);
        public static SolidColorBrush Info => Solid("InfoBrush", Colors.SkyBlue);
        public static SolidColorBrush Border => Solid("GlassBorderBrush", Colors.LightGray);

        /// <summary>带透明度的强调色（用于 chip / 徽标底色）。</summary>
        public static SolidColorBrush AccentAlpha(byte alpha)
        {
            var solid = Solid("AccentBrush", Colors.SeaGreen);
            return new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
        }
    }
}
