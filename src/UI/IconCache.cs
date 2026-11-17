using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>App-Symbole in drei Varianten: normal, gedimmt (inaktiv), blass (nicht gestartet).</summary>
    internal sealed class IconCache
    {
        public sealed class Entry
        {
            public Bitmap Normal, Dim, Off;
        }

        private readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly int _size;

        public IconCache(int size) { _size = size; }

        /// <summary>Nur Vorschau: auch Buchstaben-Symbole einfaerben (dort gibt es keine echten Programme).</summary>
        public static bool AllowLetters;
        private readonly Dictionary<string, Color?> _dominant = new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 3.1: kraeftigste Farbe des App-Symbols (z. B. Spotify gruen, Discord lila) – null, wenn das Symbol grau/schwarz/weiss ist
        /// oder es nur ein Buchstaben-Symbol gibt (kein Pfad).
        /// </summary>
        public Color? Dominant(string key, string name, string path)
        {
            if (string.IsNullOrEmpty(path) || key == AppKeys.System) return null;
            try { if (!AllowLetters && !File.Exists(path)) return null; } catch { return null; }   // kein echtes Symbol (nur Buchstabe): eigene Farbe lassen
            string ck = key + "|" + path;
            if (_dominant.TryGetValue(ck, out var c)) return c;
            c = DominantOf(Get(key, name, path).Normal);
            _dominant[ck] = c;
            return c;
        }

        internal static Color? DominantOf(Bitmap bmp)
        {
            if (bmp == null) return null;
            var w = new double[12]; var r = new double[12]; var gr = new double[12]; var b = new double[12];
            try
            {
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var p = bmp.GetPixel(x, y);
                        if (p.A < 128) continue;
                        float h = p.GetHue(), s = p.GetSaturation(), l = p.GetBrightness();
                        if (s < 0.3f || l < 0.18f || l > 0.9f) continue;   // grau, fast schwarz oder fast weiss
                        int k = (int)(h / 30f) % 12;
                        double wt = s * (1.0 - Math.Abs(l - 0.5) * 1.2) * (p.A / 255.0);
                        w[k] += wt; r[k] += p.R * wt; gr[k] += p.G * wt; b[k] += p.B * wt;
                    }
            }
            catch { return null; }
            int best = 0;
            double total = 0;
            for (int k = 0; k < 12; k++) { total += w[k]; if (w[k] > w[best]) best = k; }
            if (total <= 0 || w[best] < 1.5) return null;   // zu wenig Farbe
            var col = Color.FromArgb((int)(r[best] / w[best]), (int)(gr[best] / w[best]), (int)(b[best] / w[best]));
            // wie eine Kanalfarbe: kraeftig und hell genug
            float lum = (0.2126f * col.R + 0.7152f * col.G + 0.0722f * col.B) / 255f;
            if (lum < 0.45f) col = Theme.Lighten(col, Math.Min(0.5f, (0.45f - lum) * 1.3f));
            return col;
        }

        public Entry Get(string key, string name, string path)
        {
            string cacheKey = key + "|" + (path ?? "");
            if (_cache.TryGetValue(cacheKey, out var e)) return e;

            Bitmap bmp = null;
            if (key == AppKeys.System) bmp = SpeakerIcon();
            else if (!string.IsNullOrEmpty(path)) bmp = LoadIcon(path);
            if (bmp == null) bmp = LetterIcon(name ?? key);

            e = new Entry { Normal = bmp, Dim = Fade(bmp, 0.55f, 0f), Off = Fade(bmp, 0.3f, 1f) };
            _cache[cacheKey] = e;
            return e;
        }

        private Bitmap LoadIcon(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var ico = Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    using (var src = ico.ToBitmap())
                    {
                        var bmp = new Bitmap(_size, _size, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawImage(src, new Rectangle(0, 0, _size, _size));
                        }
                        return bmp;
                    }
                }
            }
            catch { return null; }
        }

        private Bitmap LetterIcon(string name)
        {
            var bmp = new Bitmap(_size, _size, PixelFormat.Format32bppPArgb);
            int h = 0;
            foreach (char c in name ?? "?") h = h * 31 + c;
            var pal = MixerSettings.Palette;
            Color col = Color.FromArgb(pal[Math.Abs(h) % (pal.Length - 1)]);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Setup(g);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;   // Bild mit durchsichtigen Ecken: kein ClearType
                var r = new RectangleF(0.5f, 0.5f, _size - 1, _size - 1);
                Theme.FillVertical(g, r, Theme.Lighten(col, 0.1f), Theme.Darken(col, 0.25f), _size * 0.28f);
                string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                using (var f = new Font(Theme.UiSemi(10).FontFamily, _size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Theme.TextOn(col)))
                    g.DrawString(letter, f, b, new RectangleF(0, 0.5f, _size, _size), Theme.Center);
            }
            return bmp;
        }

        private Bitmap SpeakerIcon()
        {
            var bmp = new Bitmap(_size, _size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Theme.Setup(g);
                float s = _size;
                var r = new RectangleF(0.5f, 0.5f, s - 1, s - 1);
                Theme.FillVertical(g, r, Color.FromArgb(0x5b, 0x63, 0x6e), Color.FromArgb(0x33, 0x38, 0x40), s * 0.28f);
                using (var b = new SolidBrush(Color.FromArgb(0xee, 0xf0, 0xf3)))
                {
                    g.FillPolygon(b, new[]
                    {
                        new PointF(s * 0.18f, s * 0.40f), new PointF(s * 0.34f, s * 0.40f), new PointF(s * 0.52f, s * 0.24f),
                        new PointF(s * 0.52f, s * 0.76f), new PointF(s * 0.34f, s * 0.60f), new PointF(s * 0.18f, s * 0.60f)
                    });
                }
                using (var pen = new Pen(Color.FromArgb(0xee, 0xf0, 0xf3), Math.Max(1f, s * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, s * 0.46f, s * 0.36f, s * 0.20f, s * 0.28f, -60, 120);
                    g.DrawArc(pen, s * 0.46f, s * 0.26f, s * 0.34f, s * 0.48f, -60, 120);
                }
            }
            return bmp;
        }

        private static Bitmap Fade(Bitmap src, float alpha, float desaturate)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            float lr = 0.3f * desaturate, lg = 0.59f * desaturate, lb = 0.11f * desaturate, k = 1f - desaturate;
            var cm = new ColorMatrix(new[]
            {
                new[] { lr + k, lr, lr, 0, 0 },
                new[] { lg, lg + k, lg, 0, 0 },
                new[] { lb, lb, lb + k, 0, 0 },
                new[] { 0f, 0, 0, alpha, 0 },
                new[] { 0f, 0, 0, 0, 1 },
            });
            using (var ia = new ImageAttributes())
            using (var g = Graphics.FromImage(bmp))
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return bmp;
        }
    }
}
