using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.2: eigene Schrift, eigene Pegel-Farben mit Grenzen, Akzentfarbe von Windows oder vom Album-Cover.
    /// </summary>
    internal static partial class Theme
    {
        // ---------------------------------------------------------------- Schrift

        /// <summary>Eigene Schrift fuers ganze Pult (null = Segoe UI wie bisher). Fehlt sie auf dem PC, bleibt Segoe UI.</summary>
        public static string UiFamily;

        /// <summary>Vorschlaege (stehen in jedem Windows 10/11).</summary>
        public static readonly string[] FontChoices = { "Bahnschrift", "Arial", "Calibri", "Candara", "Consolas", "Corbel", "Franklin Gothic Medium", "Georgia", "Tahoma", "Trebuchet MS", "Verdana" };

        /// <summary>Gibt es die Schrift auf diesem PC?</summary>
        public static bool FontExists(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return false;
            try
            {
                using (var f = new Font(family, 10f, FontStyle.Regular, GraphicsUnit.Pixel))
                    return string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- Eigene Pegel-Farben (MeterStyle 4)

        public static Color CustomLow = Color.FromArgb(0x39, 0xd6, 0x6b), CustomMid = Color.FromArgb(0xf2, 0xd1, 0x3a), CustomTop = Color.FromArgb(0xff, 0x4a, 0x3a);
        /// <summary>Ab hier (Anteil der Pegelhoehe, IEC-Skala) Mitte bzw. oben.</summary>
        public static float CustomMidFrac = 0.55f, CustomTopFrac = 0.85f;

        /// <summary>Farben zum Auswaehlen.</summary>
        public static readonly Color[] MeterPalette =
        {
            Color.FromArgb(0x39, 0xd6, 0x6b), Color.FromArgb(0x2e, 0xd3, 0xb7), Color.FromArgb(0x31, 0xc6, 0xff), Color.FromArgb(0x3d, 0x7b, 0xff),
            Color.FromArgb(0xa3, 0x5c, 0xff), Color.FromArgb(0xff, 0x4f, 0xa3), Color.FromArgb(0xf2, 0xd1, 0x3a), Color.FromArgb(0xff, 0x9a, 0x1f),
            Color.FromArgb(0xff, 0x4a, 0x3a), Color.FromArgb(0xf2, 0xf2, 0xf2),
        };
        public static readonly string[] MeterPaletteNames = { "Grün", "Türkis", "Cyan", "Blau", "Lila", "Pink", "Gelb", "Orange", "Rot", "Weiß" };

        /// <summary>Grenzen in dB (wie auf der Skala) in Anteile der Pegelhoehe umrechnen.</summary>
        public static void SetCustomMeter(Color low, Color mid, Color top, float midDb, float topDb)
        {
            CustomLow = low; CustomMid = mid; CustomTop = top;
            CustomMidFrac = MeterBallistics.Iec(midDb) - 0.0001f;
            CustomTopFrac = Math.Max(CustomMidFrac + 0.01f, MeterBallistics.Iec(topDb) - 0.0001f);
        }

        // ---------------------------------------------------------------- Akzentfarbe von aussen

        /// <summary>Akzentfarbe von Windows oder vom Album-Cover (null = wie ausgewaehlt).</summary>
        public static Color? AccentOverride;

        private static void ApplyAccentOverride()
        {
            if (!AccentOverride.HasValue) return;
            var a = Readable(AccentOverride.Value);
            Accent = IsLight ? Darken(a, 0.18f) : a;
            LcdText = a;
            ValueText = IsLight ? Darken(a, 0.36f) : a;
        }

        /// <summary>Zu dunkle oder blasse Farben so aufhellen, dass sie als Leuchtfarbe taugen.</summary>
        public static Color Readable(Color c)
        {
            float l = Luma(c);
            if (l < 0.42f) c = Lighten(c, Math.Min(0.65f, (0.42f - l) * 1.6f));
            return Color.FromArgb(255, c.R, c.G, c.B);
        }
    }
}
