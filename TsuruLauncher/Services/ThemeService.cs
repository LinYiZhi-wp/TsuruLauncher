using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TsuruLauncher.Services
{
    /// <summary>Base appearance of the launcher surfaces.</summary>
    public enum ThemeMode
    {
        Light,
        Dark,
        Oled
    }

    /// <summary>An accent swatch offered in the settings page.</summary>
    public class AccentSwatch
    {
        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Hex { get; init; } = "#4ADE80";
        public Brush Preview => ThemeService.PreviewBrush(Hex);
    }

    /// <summary>
    /// Runtime theming built around one idea: the user picks a *mode* and an
    /// *accent hue*; every other colour (surfaces, borders, text, accent
    /// variants) is derived algorithmically. That keeps any combination —
    /// including a custom hex or the Windows accent colour — looking coherent,
    /// and avoids shipping hand-tuned palettes.
    /// </summary>
    public static class ThemeService
    {
        private static ResourceDictionary? _override;

        /// <summary>Full surface + text ramp for one appearance mode.</summary>
        private sealed record ModePalette(
            string S1, string S2, string S3, string S4, string S5,
            string Bg, string Card, string Popover,
            string T1, string T2, string T3, string T4,
            byte BorderAlpha, byte BorderHoverAlpha);

        // ---- own ramps: cool neutral greys, light mode included ----
        private static readonly ModePalette LightMode = new(
            S1: "#F4F6F9", S2: "#FFFFFF", S3: "#FFFFFF", S4: "#EAEEF3", S5: "#DFE5EC",
            Bg: "#F4F6F9", Card: "#FFFFFF", Popover: "#FFFFFF",
            T1: "#141922", T2: "#3C4653", T3: "#586373", T4: "#6E7885",
            BorderAlpha: 0x3A, BorderHoverAlpha: 0x66);

        private static readonly ModePalette DarkMode = new(
            S1: "#0F1216", S2: "#161A20", S3: "#1B2028", S4: "#232A33", S5: "#2C343E",
            Bg: "#0F1216", Card: "#1B2028", Popover: "#242B34",
            T1: "#E8EDF2", T2: "#C3CCD6", T3: "#94A0AC", T4: "#6B7681",
            BorderAlpha: 0x26, BorderHoverAlpha: 0x48);

        private static readonly ModePalette OledMode = new(
            S1: "#000000", S2: "#0A0C0F", S3: "#101317", S4: "#171B20", S5: "#1F242A",
            Bg: "#000000", Card: "#101317", Popover: "#191E24",
            T1: "#EDF2F6", T2: "#C6CFD8", T3: "#8E9AA6", T4: "#66707A",
            BorderAlpha: 0x2C, BorderHoverAlpha: 0x52);

        private static ModePalette PaletteFor(ThemeMode mode) => mode switch
        {
            ThemeMode.Light => LightMode,
            ThemeMode.Oled => OledMode,
            _ => DarkMode
        };

        /// <summary>Accent presets — plain, widely used hues.</summary>
        public static IReadOnlyList<AccentSwatch> AccentPresets { get; } = new List<AccentSwatch>
        {
            new AccentSwatch { Key = "green",  DisplayName = "翠绿", Hex = "#4ADE80" },
            new AccentSwatch { Key = "blue",   DisplayName = "天蓝", Hex = "#5B9BF8" },
            new AccentSwatch { Key = "violet", DisplayName = "紫罗兰", Hex = "#A78BFA" },
            new AccentSwatch { Key = "cyan",   DisplayName = "青碧", Hex = "#2DD4BF" },
            new AccentSwatch { Key = "orange", DisplayName = "落日橙", Hex = "#FB923C" },
            new AccentSwatch { Key = "rose",   DisplayName = "胭脂", Hex = "#F472B6" },
            new AccentSwatch { Key = "amber",  DisplayName = "琥珀", Hex = "#FACC15" }
        };

        public const string SystemAccentKey = "system";

        public static event Action? ThemeChanged;

        #region Public API

        /// <summary>Applies mode + accent. <paramref name="accent"/> is a preset key, "#rrggbb" or "system".</summary>
        public static void Apply(string? mode, string? accent)
        {
            var app = Application.Current;
            if (app == null) return;

            try
            {
                var themeMode = ParseMode(mode);
                var accentHex = ResolveAccent(accent);

                var dict = new ResourceDictionary();
                Paint(dict, themeMode, accentHex);

                var dicts = app.Resources.MergedDictionaries;
                if (_override != null) { try { dicts.Remove(_override); } catch { } }
                dicts.Add(dict);
                _override = dict;

                var check = (app.TryFindResource("AccentBrush") as SolidColorBrush)?.Color.ToString();
                Utilities.Logger.LogInfo($"[Theme] mode={themeMode} accent={accentHex} -> {check}");

                ThemeChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ThemeService.Apply");
            }
        }

        public static ThemeMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "oled" => ThemeMode.Oled,
            _ => ThemeMode.Dark
        };

        /// <summary>Resolves an accent setting to a concrete colour.</summary>
        public static string ResolveAccent(string? accent)
        {
            if (string.IsNullOrWhiteSpace(accent)) return AccentPresets[0].Hex;

            if (string.Equals(accent, SystemAccentKey, StringComparison.OrdinalIgnoreCase))
                return SystemAccentHex() ?? AccentPresets[1].Hex;

            if (accent.StartsWith("#") && accent.Length >= 7)
                return NormalizeHex(accent);

            var preset = AccentPresets.FirstOrDefault(a => string.Equals(a.Key, accent, StringComparison.OrdinalIgnoreCase));
            return preset?.Hex ?? AccentPresets[0].Hex;
        }

        public static Brush PreviewBrush(string hex)
        {
            var b = new SolidColorBrush(ToColor(hex));
            b.Freeze();
            return b;
        }

        /// <summary>Reads the Windows accent colour (registry) and adapts it to our palette.</summary>
        public static string? SystemAccentHex()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (key?.GetValue("AccentColor") is int raw)
                {
                    // stored as ABGR
                    int r = raw & 0xFF, g = (raw >> 8) & 0xFF, b = (raw >> 16) & 0xFF;
                    return AdaptHue(Color.FromRgb((byte)r, (byte)g, (byte)b));
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Keeps only the hue of an arbitrary colour and re-applies our own
        /// saturation/lightness, so a muddy system colour still produces a
        /// usable accent.
        /// </summary>
        public static string AdaptHue(Color source)
        {
            var (h, s, _) = ToHsl(source);
            double sat = Math.Clamp(Math.Max(s, 52), 45, 92);
            return ToHex(FromHsl(h, sat, 62));
        }

        #endregion

        #region Painting

        private static void Paint(ResourceDictionary d, ThemeMode mode, string accentHex)
        {
            var b = PaletteFor(mode);

            var bg = ToColor(b.Bg);
            var card = ToColor(b.Card);
            var popover = ToColor(b.Popover);
            var textPrimary = ToColor(b.T1);

            // accent ramp derived from the single base colour
            var accent = ToColor(accentHex);
            var (ah, asat, al) = ToHsl(accent);

            // 浅色模式下把强调色压深：亮色强调色当文字/图标用会看不清（对比度 < 2）
            double lightnessCeiling = mode == ThemeMode.Light ? 36 : 70;
            double lightnessFloor = mode == ThemeMode.Light ? 28 : 48;
            double mainL = Math.Clamp(al, lightnessFloor, lightnessCeiling);
            if (mode == ThemeMode.Light && asat < 45) asat = Math.Min(asat + 12, 100);
            var accentMain = FromHsl(ah, asat, mainL);
            var accentHover = FromHsl(ah, Math.Min(asat * 1.06, 100), Math.Min(mainL + 8, 82));
            var accentDim = FromHsl(ah, asat, Math.Max(mainL - 16, 26));
            var accentLight = FromHsl(ah, Math.Max(asat - 12, 30), Math.Min(mainL + 20, 86));
            // 浅色模式的强调色被压深了，前景改用白色才够清楚；深色模式强调色偏亮，用深色文字
            var accentFg = mode == ThemeMode.Light ? Colors.White : FromHsl(ah, Math.Min(asat, 60), 9);

            // WPF-UI 自己的控件（ui:TextBox 占位符、ui:Button 等）需要显式切主题，
            // 否则浅色模式下它们的文字/占位符仍是深色主题的白色
            try
            {
                bool isLight = mode == ThemeMode.Light;
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    isLight ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark,
                    Wpf.Ui.Controls.WindowBackdropType.None,
                    false);

                // 让 WPF-UI 自带控件的强调色跟随我们的主题强调色（否则默认还是系统的蓝色）
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                    accentMain,
                    isLight ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark,
                    false);
            }
            catch { }

            Brush(d, "AccentBrush", accentMain);
            Brush(d, "AccentHoverBrush", accentHover);
            Brush(d, "AccentDimBrush", accentDim);
            Brush(d, "AccentLightBrush", accentLight);
            Brush(d, "AccentForegroundBrush", accentFg);
            Brush(d, "SystemAccentColorBrush", accentMain);
            Brush(d, "SystemAccentColorPrimaryBrush", accentMain);
            Brush(d, "SystemAccentColorSecondaryBrush", accentDim);
            Brush(d, "SystemAccentColorTertiaryBrush", accentLight);
            Brush(d, "AccentGlassLightBrush", accentMain, 0x16);
            Brush(d, "AccentGlassMediumBrush", accentMain, 0x22);
            Brush(d, "AccentGlassStrongBrush", accentMain, 0x33);
            Brush(d, "AccentBorderBrush", accentMain, 0x50);

            d["AccentColor"] = accentMain;
            d["AccentHoverColor"] = accentHover;
            d["AccentDimColor"] = accentDim;
            d["AccentLightColor"] = accentLight;

            // surfaces
            Brush(d, "BackgroundBrush", bg);
            Brush(d, "CardBrush", card);
            Brush(d, "PopoverBrush", popover);
            Brush(d, "TopBarBrush", bg, 0xCC);
            Brush(d, "TileBrush", card, 0x99);

            // 5-step surface ramp
            Brush(d, "Surface1Brush", b.S1);
            Brush(d, "Surface2Brush", b.S2);
            Brush(d, "Surface3Brush", b.S3);
            Brush(d, "Surface4Brush", b.S4);
            Brush(d, "Surface5Brush", b.S5);

            // text
            Brush(d, "TextPrimaryBrush", textPrimary);
            Brush(d, "TextSecondaryBrush", ToColor(b.T2));
            Brush(d, "TextTertiaryBrush", ToColor(b.T3));
            Brush(d, "TextDimBrush", ToColor(b.T4));

            // glass hierarchy — light mode uses solid surfaces, dark keeps a
            // little translucency so the user background image shows through
            if (mode == ThemeMode.Light)
            {
                Brush(d, "GlassLightBrush", b.S2);
                Brush(d, "GlassMediumBrush", b.S3);
                Brush(d, "GlassStrongBrush", b.S4);
                Brush(d, "GlassHeavyBrush", b.S5);
            }
            else
            {
                Brush(d, "GlassLightBrush", card, 0xD0);
                Brush(d, "GlassMediumBrush", card, 0xE6);
                Brush(d, "GlassStrongBrush", popover, 0xF0);
                Brush(d, "GlassHeavyBrush", popover, 0xF5);
            }
            Brush(d, "GlassBorderBrush", accentMain, b.BorderAlpha);
            Brush(d, "GlassBorderHoverBrush", accentMain, b.BorderHoverAlpha);
            Brush(d, "SurfaceContentBrush", bg, 0xF0);
            Brush(d, "SurfaceFloatingBrush", card, 0xF0);
            Brush(d, "SurfaceInputBrush", textPrimary, 0x14);
            Brush(d, "SurfaceDropdownBrush", popover, 0xFA);

            // semantic（浅色模式下加深，否则亮色语义色当文字用对比度不足）
            bool lightMode = mode == ThemeMode.Light;
            Brush(d, "WarningBrush", lightMode ? "#B26A00" : "#FACC15");
            Brush(d, "DangerBrush", lightMode ? "#D64545" : "#F87171");
            Brush(d, "SuccessBrush", lightMode ? "#17874B" : "#4ADE80");
            Brush(d, "InfoBrush", lightMode ? "#1F6FB2" : "#38BDF8");
            // 半透明底色：徽标 / 标签 / 状态胶囊，深浅色下都保持可读
            Brush(d, "WarningGlassBrush", lightMode ? "#B26A00" : "#FACC15", 0x28);
            Brush(d, "DangerGlassBrush", lightMode ? "#D64545" : "#F87171", 0x28);
            Brush(d, "SuccessGlassBrush", lightMode ? "#17874B" : "#4ADE80", 0x28);
            Brush(d, "InfoGlassBrush", lightMode ? "#1F6FB2" : "#38BDF8", 0x28);
            Brush(d, "ScrimBrush", "#000000", mode == ThemeMode.Light ? (byte)0x38 : (byte)0x80);
            Brush(d, "ScrollBarBrush", textPrimary, 0x45);
            Brush(d, "ScrollBarHoverBrush", textPrimary, 0x70);

            // legacy aliases still referenced by older styles
            Brush(d, "iOS26.Accent", accentMain);
            Brush(d, "iOS26.AccentHover", accentHover);
            Brush(d, "iOS26.AccentDim", accentDim);
            Brush(d, "iOS26.SidebarLayer", bg, 0x4D);
            Brush(d, "iOS26.ContentLayer", card, 0xF0);
            Brush(d, "iOS26.FloatingLayer", popover, 0xF0);
            Brush(d, "iOS26.CardLayer", textPrimary, 0x30);
            // scrim colour follows the mode (dark veil vs light veil)
            if (mode == ThemeMode.Light)
                Brush(d, "iOS26.Background", "#FFFFFF", 0x55);
            else
                Brush(d, "iOS26.Background", "#000000", 0x40);
        }

        #endregion

        #region Colour helpers

        private static void Brush(ResourceDictionary d, string key, string hex, byte? alpha = null)
            => d[key] = new SolidColorBrush(ApplyAlpha(ToColor(hex), alpha));

        private static void Brush(ResourceDictionary d, string key, Color color, byte? alpha = null)
            => d[key] = new SolidColorBrush(ApplyAlpha(color, alpha));

        private static Color ApplyAlpha(Color c, byte? alpha)
            => alpha.HasValue ? Color.FromArgb(alpha.Value, c.R, c.G, c.B) : c;

        private static Color Mix(Color a, Color b, double t)
        {
            byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromArgb(255, L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }

        private static Color ToColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(NormalizeHex(hex)); }
            catch { return Colors.Gray; }
        }

        private static string NormalizeHex(string hex)
        {
            hex = hex.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return hex.Length >= 7 ? hex.Substring(0, 7) : hex;
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>RGB → HSL (h in 0..360, s/l in 0..100).</summary>
        private static (double h, double s, double l) ToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double d = max - min;
            double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
            double h = 0;
            if (d != 0)
            {
                if (max == r) h = ((g - b) / d) % 6;
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60;
                if (h < 0) h += 360;
            }
            return (h, s * 100, l * 100);
        }

        /// <summary>HSL → RGB.</summary>
        private static Color FromHsl(double h, double s, double l)
        {
            s = Math.Clamp(s, 0, 100) / 100.0;
            l = Math.Clamp(l, 0, 100) / 100.0;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hp = (((h % 360) + 360) % 360) / 60.0;
            double x = c * (1 - Math.Abs((hp % 2) - 1));
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; }
            else if (hp < 2) { r = x; g = c; }
            else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; }
            else if (hp < 5) { r = x; b = c; }
            else { r = c; b = x; }
            double m = l - c / 2;
            byte B(double v) => (byte)Math.Round(Math.Clamp(v + m, 0, 1) * 255);
            return Color.FromRgb(B(r), B(g), B(b));
        }

        #endregion
    }
}
