using System.Windows;
using System.Windows.Media;

namespace PhasmaStrap.UI
{
    public static class ThemeTokens
    {
        public static readonly Color DefaultAccent = Color.FromRgb(0xE0, 0x20, 0x2F);

        public static readonly string[] AccentPresets =
        {
            "#E0202F", "#FF8A3D", "#F5B841", "#3DDC97", "#38BDF8", "#7C8CFF", "#C26BFF", "#F06FB0"
        };

        public static readonly string[] PanelPresets =
        {
            "#0D0D12", "#0B0B0F", "#1A1F2E", "#221A2B", "#16241F", "#2A1A1A", "#F4F4F8"
        };

        public const double MinWindowTint = 0.4;

        public const double MinPanelOpacity = 0.3;

        public static Color? UserAccent => Opaque(App.Settings.Prop.ThemeAccent);

        public static Color? UserPanelColor => Opaque(App.Settings.Prop.ThemePanelColor);

        public static Color? SectionAccent { get; set; }

        public static Color? CustomAccent { get; set; }

        public static Color ResolveAccent() => CustomAccent ?? (App.Settings.Prop.ThemeSectionColors ? SectionAccent : null) ?? UserAccent ?? DefaultAccent;

        public static Color EffectiveAccent => LookupColor("SystemAccentColor", ResolveAccent());

        public static Color EffectivePanelColor => UserPanelColor ?? ThemeSurface(IsDark);

        private static bool IsDark => App.Settings.Prop.Theme.GetFinal() == PhasmaStrap.Enums.Theme.Dark;

        public static Color WindowTintColor
        {
            get
            {
                Color background = LookupColor("ApplicationBackgroundColor", IsDark ? Color.FromRgb(0x06, 0x06, 0x08) : Color.FromRgb(0xF6, 0xF6, 0xF9));
                double opacity = Math.Clamp(App.Settings.Prop.ThemeWindowTintOpacity, MinWindowTint, 1.0);
                return Color.FromArgb((byte)Math.Round(opacity * 255), background.R, background.G, background.B);
            }
        }

        public static void Apply()
        {
            Application? app = Application.Current;

            if (app is null)
                return;

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(Apply);
                return;
            }

            try
            {
                ApplyCore(app.Resources);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("ThemeTokens", $"Could not apply the theme tokens: {ex.Message}");
            }
        }

        private static void ApplyCore(ResourceDictionary res)
        {
            var settings = App.Settings.Prop;
            bool dark = IsDark;

            double panelOpacity = Math.Clamp(settings.ThemePanelOpacity, MinPanelOpacity, 1.0);
            int radius = Math.Clamp(settings.ThemeCornerRadius, 0, 24);

            Color ink = dark ? Colors.White : Colors.Black;
            Color? picked = UserPanelColor;
            Color surface = picked ?? ThemeSurface(dark);
            Color raised = picked is Color own
                ? Mix(own, Luminance(own) > 0.5 ? Colors.Black : Colors.White, 0.06)
                : LookupColor("SolidBackgroundFillColorTertiary", dark ? Color.FromRgb(0x14, 0x14, 0x1B) : Color.FromRgb(0xF3, 0xF3, 0xF6));

            Paint(res, "PhasmaPanelBrush", WithAlpha(surface, panelOpacity));
            Paint(res, "PhasmaPanelRaisedBrush", WithAlpha(raised, Math.Clamp(panelOpacity + 0.2, 0.35, 1.0)));
            Paint(res, "PhasmaPopupBrush", WithAlpha(raised, 1.0));
            Paint(res, "PhasmaToggleOffBrush", dark ? Color.FromRgb(0x1E, 0x1E, 0x26) : WithAlpha(ink, 0.22));
            Paint(res, "PhasmaToggleOffHoverBrush", dark ? Color.FromRgb(0x26, 0x26, 0x30) : WithAlpha(ink, 0.30));
            Paint(res, "PhasmaStrokeBrush", WithAlpha(ink, dark ? 0.06 : 0.10));
            Paint(res, "PhasmaStrokeStrongBrush", WithAlpha(ink, dark ? 0.11 : 0.16));
            Paint(res, "PhasmaHoverBrush", WithAlpha(ink, dark ? 0.06 : 0.05));
            Paint(res, "PhasmaSelectedBrush", WithAlpha(ink, dark ? 0.09 : 0.08));
            Paint(res, "PhasmaScrimBrush", dark ? Color.FromArgb(0xB8, 0x03, 0x03, 0x05) : Color.FromArgb(0xF0, 0xF6, 0xF6, 0xF9));

            double large = Math.Max(0, radius - 2);
            Put(res, "PhasmaCornerRadius", new CornerRadius(Math.Max(0, radius - 2)));
            Put(res, "PhasmaCornerRadiusSmall", new CornerRadius(Math.Max(0, radius - 6)));
            Put(res, "PhasmaCornerRadiusLarge", new CornerRadius(large));
            Put(res, "PhasmaCornerRadiusTopLarge", new CornerRadius(large, large, 0, 0));

            double control = Math.Round(radius * 0.5);
            Put(res, "ControlCornerRadius", new CornerRadius(control));
            Put(res, "OverlayCornerRadius", new CornerRadius(control));
            Put(res, "PopupCornerRadius", new CornerRadius(Math.Round(radius * 0.75)));
            Put(res, "PhasmaCutSize", CutSize(radius));
            Put(res, "PhasmaCutSizeSmall", Math.Round(CutSize(radius) * 2 / 3));

            bool compact = settings.ThemeCompact;
            Put(res, "PhasmaRowHeight", compact ? 44.0 : 60.0);
            Put(res, "PhasmaRowPadding", compact ? new Thickness(10, 4, 10, 4) : new Thickness(10, 8, 10, 8));
            Put(res, "PhasmaOptionMargin", compact ? new Thickness(0, 4, 0, 0) : new Thickness(0, 8, 0, 0));
            Put(res, "PhasmaOptionPadding", compact ? new Thickness(16, 8, 16, 8) : new Thickness(20, 14, 20, 14));
            Put(res, "PhasmaPagePadding", compact ? new Thickness(20, 16, 12, 0) : new Thickness(36, 28, 20, 0));
            Put(res, "PhasmaGap", compact ? 10.0 : 16.0);

            Put(res, "PhasmaMistOpacity", settings.ThemeMistEnabled ? 1.0 : 0.0);

            Color accent = EffectiveAccent;
            Put(res, "PhasmaAccentColor", accent);
            Paint(res, "PhasmaAccentBrush", accent);
            Paint(res, "PhasmaAccentSoftBrush", WithAlpha(accent, dark ? 0.12 : 0.14));
            Paint(res, "PhasmaAccentStrokeBrush", WithAlpha(accent, 0.6));
            Color brand = CustomAccent ?? UserAccent ?? DefaultAccent;
            Paint(res, "PhasmaBrandBrush", brand);
            Put(res, "PhasmaBrandTextColor", Luminance(brand) > 0.42 ? Color.FromRgb(0x16, 0x07, 0x06) : Colors.White);

            Paint(res, "PhasmaAccentTextBrush", Luminance(accent) > 0.42 ? Color.FromRgb(0x16, 0x07, 0x06) : Colors.White);
        }

        private static void Paint(ResourceDictionary res, string key, Color color)
        {
            if (res.Contains(key) && res[key] is SolidColorBrush current && current.Color == color)
                return;

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush;
        }

        private static void Put(ResourceDictionary res, string key, object value)
        {
            if (res.Contains(key) && Equals(res[key], value))
                return;

            res[key] = value;
        }

        private static Color ThemeSurface(bool dark) =>
            LookupColor("SolidBackgroundFillColorBase", dark ? Color.FromRgb(0x0D, 0x0D, 0x12) : Color.FromRgb(0xFF, 0xFF, 0xFF));

        private static Color? Opaque(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !PhasmaStrap.Utility.AppColorTheme.TryParseColor(text, out Color color))
                return null;

            return Color.FromRgb(color.R, color.G, color.B);
        }

        public static double CutSize(int radius) => Math.Round(Math.Clamp(radius, 0, 24) * 0.45);

        public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static Color LookupColor(string key, Color fallback)
        {
            try
            {
                object? value = Application.Current?.TryFindResource(key);
                if (value is Color color)
                    return color;
                if (value is SolidColorBrush brush)
                    return brush.Color;
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255), color.R, color.G, color.B);

        private static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
    }
}
