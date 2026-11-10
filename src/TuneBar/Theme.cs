using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TuneBar
{
    /// <summary>Turns the user's color settings into the brushes the widget uses.</summary>
    internal static class Theme
    {
        public static readonly Color DefaultAccent = Color.FromRgb(0x1D, 0xB9, 0x54);
        private static readonly Color DarkBg = Color.FromRgb(0x20, 0x20, 0x20);
        private static readonly Color LightBg = Color.FromRgb(0xF3, 0xF3, 0xF3);

        /// <summary>Main color of the current album art (used when "match album art" is on).</summary>
        public static Color? ArtColor { get; set; }

        /// <summary>Set to true by the widget while it floats above the taskbar.</summary>
        public static bool Floating { get; set; }

        public static bool WindowsUsesLightTaskbar()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
            }
            catch
            {
                return false;
            }
        }

        public static void Apply()
        {
            var s = Settings.Current;
            var r = Application.Current.Resources;

            Color bg = s.BackgroundMode switch
            {
                "Dark" => DarkBg,
                "Light" => LightBg,
                "Custom" => ParseOr(s.BackgroundHex, DarkBg),
                _ => WindowsUsesLightTaskbar() ? LightBg : DarkBg,
            };

            double opacity = Math.Clamp(s.BackgroundOpacity, 0, 1);
            if (Floating) opacity = Math.Max(opacity, 0.85); // a see-through floating box looks odd
            // Alpha 1 instead of 0 so the widget still catches clicks when "see-through".
            byte alpha = (byte)Math.Max(1, Math.Round(opacity * 255));

            bool lightBg = Luma(bg) > 0.55;
            Color accent = s.AccentFromArt && ArtColor.HasValue ? ArtColor.Value : ParseOr(s.AccentHex, DefaultAccent);

            r["BgBrush"] = Brush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
            r["EdgeBrush"] = Brush(opacity < 0.05 ? Colors.Transparent
                : lightBg ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x1F, 255, 255, 255));
            r["FgBrush"] = Brush(lightBg ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
            r["SubFgBrush"] = Brush(lightBg ? Color.FromArgb(0x99, 0, 0, 0) : Color.FromArgb(0xA6, 255, 255, 255));
            r["HoverBrush"] = Brush(lightBg ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1A, 255, 255, 255));
            r["PressBrush"] = Brush(lightBg ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x30, 255, 255, 255));
            r["TrackBrush"] = Brush(lightBg ? Color.FromArgb(0x33, 0, 0, 0) : Color.FromArgb(0x40, 255, 255, 255));
            r["ArtPlaceholderBrush"] = Brush(lightBg ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1A, 255, 255, 255));
            r["AccentBrush"] = Brush(accent);
            r["PlayGlyphBrush"] = Brush(Luma(accent) > 0.6 ? Colors.Black : Colors.White);
        }

        public static bool TryParse(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex.Trim();
            if (!h.StartsWith("#")) h = "#" + h;
            if (h.Length != 7 && h.Length != 9) return false;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(h);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Color ParseOr(string? hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Finds a nice, colorful "main" color in an album cover.</summary>
        public static Color? DominantColor(BitmapSource? source)
        {
            if (source == null || source.PixelWidth == 0 || source.PixelHeight == 0) return null;
            try
            {
                var small = new TransformedBitmap(source,
                    new ScaleTransform(24.0 / source.PixelWidth, 24.0 / source.PixelHeight));
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);

                double r = 0, g = 0, b = 0, total = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    int pb = px[i], pg = px[i + 1], pr = px[i + 2];
                    int max = Math.Max(pr, Math.Max(pg, pb));
                    int min = Math.Min(pr, Math.Min(pg, pb));
                    double sat = max == 0 ? 0 : (max - min) / (double)max;
                    double light = max / 255.0;
                    double weight = sat * sat * light + 0.01;
                    if (light < 0.15) weight *= 0.05; // ignore near-black
                    r += pr * weight;
                    g += pg * weight;
                    b += pb * weight;
                    total += weight;
                }
                if (total <= 0) return null;
                r /= total;
                g /= total;
                b /= total;

                // Brighten so it shows up nicely on a dark taskbar.
                double top = Math.Max(r, Math.Max(g, b));
                if (top < 1) return DefaultAccent;
                double boost = 230 / top;
                return Color.FromRgb(
                    (byte)Math.Min(255, r * boost),
                    (byte)Math.Min(255, g * boost),
                    (byte)Math.Min(255, b * boost));
            }
            catch
            {
                return null;
            }
        }
    }
}
