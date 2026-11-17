using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>4.0 (finale): Spektrum "Skyline" – Stadt bei Nacht: Hochhaeuser wachsen mit dem Ton, Fenster leuchten, Antennenlichter blinken.</summary>
    internal sealed partial class SpectrumView
    {
        public const int SkylineShape = 7;
        private static readonly System.Diagnostics.Stopwatch SkyClock = System.Diagnostics.Stopwatch.StartNew();

        private int SkyCount => Math.Max(6, Math.Min(28, (int)(_area.Width / Theme.F(16))));

        private float SkyLevel(int b, int n)
        {
            int a = b * _bars / n, e = Math.Max(a + 1, (b + 1) * _bars / n);
            float m = 0f;
            for (int i = a; i < e && i < _lvl.Length; i++) m = Math.Max(m, _lvl[i]);
            return m;
        }

        /// <summary>Gleiche "Zufalls"-Werte fuer dasselbe Haus (Breite, Fensterraster, Antenne).</summary>
        private static float Hash(int i, int k) { unchecked { uint x = (uint)(i * 73856093) ^ (uint)(k * 19349663) ^ 0x5bd1e995u; x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15; return (x & 0xffff) / 65535f; } }

        private void StaticSkyline(Graphics g, bool dimmed)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new RectangleF(_area.X, _area.Y - 1, _area.Width, _area.Height + 2), Color.FromArgb(0x06, 0x0a, 0x1c), Color.FromArgb(0x1a, 0x1c, 0x3a), LinearGradientMode.Vertical))
                g.FillRectangle(b, _area);
            // Sterne und Mond
            for (int i = 0; i < Math.Max(8, (int)(_area.Width / Theme.F(9))); i++)
            {
                float x = _area.X + Hash(i, 1) * _area.Width, y = _area.Y + Hash(i, 2) * _area.Height * 0.55f;
                using (var b = new SolidBrush(Color.FromArgb(dimmed ? 50 : 60 + (int)(120 * Hash(i, 3)), 0xf0, 0xf2, 0xff))) g.FillEllipse(b, x, y, Theme.F(1.1f), Theme.F(1.1f));
            }
            float mr = Math.Min(Theme.F(7), _area.Height * 0.12f);
            Theme.DrawMoon(g, new PointF(_area.Right - mr * 2.2f, _area.Y + mr * 1.8f), mr, Theme.MoonPhase(DateTime.UtcNow));
            // ferne Stadt (Silhouette, unbeleuchtet)
            using (var b = new SolidBrush(Color.FromArgb(0x10, 0x13, 0x24)))
            {
                int n = SkyCount * 2;
                float w = _area.Width / n;
                for (int i = 0; i < n; i++)
                {
                    float h = _area.Height * (0.08f + 0.16f * Hash(i, 9));
                    g.FillRectangle(b, _area.X + i * w, _area.Bottom - h, w + 0.5f, h);
                }
            }
            g.SmoothingMode = sm;
        }

        private void DrawSkyline(Graphics g)
        {
            int n = SkyCount;
            float slot = _area.Width / n, t = (float)SkyClock.Elapsed.TotalSeconds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            var st = g.Save();
            g.SetClip(_area, CombineMode.Intersect);
            for (int i = 0; i < n; i++)
            {
                float lv = Math.Min(1f, SkyLevel(i, n));
                float w = slot * (0.62f + 0.3f * Hash(i, 4));
                float x = _area.X + i * slot + (slot - w) / 2f;
                float h = _area.Height * (0.1f + 0.8f * lv);
                float top = _area.Bottom - h;
                Color body = Color.FromArgb(0x1c + (int)(8 * Hash(i, 5)), 0x20 + (int)(8 * Hash(i, 5)), 0x36 + (int)(10 * Hash(i, 6)));
                using (var b = new SolidBrush(body)) g.FillRectangle(b, x, top, w, h);
                using (var b = new SolidBrush(Color.FromArgb(40, 0x8a, 0x9c, 0xd8))) g.FillRectangle(b, x, top, Math.Max(1f, Theme.F(1)), h);   // Mondlicht an der Kante
                // Fenster: Raster; je lauter, desto mehr leuchten
                float fw = Math.Max(1.5f, Theme.F(2.2f)), fh = Math.Max(1.5f, Theme.F(2.6f)), gx = fw * 1.9f, gy = fh * 1.9f;
                int cols = Math.Max(1, (int)((w - fw) / gx)), rows = Math.Max(0, (int)((h - fh * 2) / gy));
                float ox = x + (w - (cols - 1) * gx - fw) / 2f;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float hv = Hash(i * 97 + r, c);
                        bool on = hv < 0.25f + 0.65f * lv;
                        if (!on) continue;
                        bool warm = Hash(i, r * 7 + c) > 0.3f;
                        int a = 150 + (int)(100 * Hash(r, i + c));
                        using (var b = new SolidBrush(Color.FromArgb(a, warm ? Color.FromArgb(0xff, 0xd6, 0x7a) : Color.FromArgb(0xa8, 0xd8, 0xff))))
                            g.FillRectangle(b, ox + c * gx, _area.Bottom - h + fh + r * gy, fw, fh);
                    }
                // Antenne mit Blinklicht auf hohen Haeusern
                if (Hash(i, 8) > 0.45f && h > _area.Height * 0.3f)
                {
                    float ax = x + w / 2f, ah = Math.Min(Theme.F(10), _area.Height * 0.12f);
                    using (var p = new Pen(Color.FromArgb(0x50, 0x58, 0x70), 1f)) g.DrawLine(p, ax, top, ax, top - ah);
                    bool blink = (t * 1.1f + Hash(i, 11)) % 1f < 0.45f;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    if (blink)
                    {
                        Theme.FillEllipse(g, Color.FromArgb(70, 0xff, 0x30, 0x30), ax, top - ah, Theme.F(3.5f));
                        Theme.FillEllipse(g, Color.FromArgb(0xff, 0x50, 0x48), ax, top - ah, Theme.F(1.4f));
                    }
                    g.SmoothingMode = SmoothingMode.None;
                }
                // Spitze: kleines Dachlicht
                float pk = i < n ? Math.Min(1f, SkyLevel(i, n)) : 0f;
                if (pk > 0.05f)
                    using (var b = new SolidBrush(Color.FromArgb(120, Theme.Accent))) g.FillRectangle(b, x, top - Math.Max(1f, Theme.F(1.2f)), w, Math.Max(1f, Theme.F(1.2f)));
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
