using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>4.0 (finale): animierte Deko (Diskokugel, Lava-Lampe, schlafende Katze) und Buehnenscheinwerfer der Lichtshow.</summary>
    internal static partial class Theme
    {
        /// <summary>Animierte Deko anbieten (Einstellungen → Aussehen).</summary>
        public static bool MoreDeko;
        public static readonly string[] DekoNamesAll = { "Kaffeetasse", "Pflanze", "Kopfhörer", "Gummiente", "Diskokugel", "Lava-Lampe", "Schlafende Katze", "Plattenspieler" };   // 4.3: Plattenspieler
        public static int DekoCount => MoreDeko ? DekoNamesAll.Length : DekoNames.Length;
        /// <summary>Musik fuer die Deko: Pegel 0..1, Bass 0..1, Takt-Blitz 0..1 (aus dem UI-Takt).</summary>
        public static float DekoLevel, DekoBass, DekoBeat;
        public static double DekoPhase;   // laeuft schneller, wenn es laut ist (Lava-Lampe)

        private static void DrawDeko400(Graphics g, int kind, PointF c, float s, double t)
        {
            switch (kind)
            {
                case 4: DrawDiscoBall(g, c, s, t); break;
                case 5: DrawLavaLamp(g, c, s); break;
                case 7: DrawTurntable(g, c, s); break;   // 4.3
                default: DrawCat(g, c, s, t); break;
            }
        }

        private static void DrawDiscoBall(Graphics g, PointF c, float s, double t)
        {
            float r = s * 0.26f;
            var ctr = new PointF(c.X, c.Y - s * 0.02f);
            // Faden nach oben
            using (var p = new Pen(Color.FromArgb(160, 0xb0, 0xb4, 0xba), Math.Max(1f, F(0.8f)))) g.DrawLine(p, ctr.X, ctr.Y - r, ctr.X, c.Y - s * 0.5f);
            // Lichtpunkte im Takt: kreisen langsam, blitzen auf dem Schlag
            float beat = Math.Max(DekoBeat, DekoBass * 0.8f);
            int spots = 14;
            for (int i = 0; i < spots; i++)
            {
                double a = t * 0.6 + i * 2 * Math.PI / spots;
                float d = s * (0.55f + 0.35f * (float)((i * 37 % 10) / 10.0));
                float x = ctr.X + (float)Math.Cos(a) * d * 1.3f, y = ctr.Y + (float)Math.Sin(a) * d * 0.55f;
                Color hc = i % 3 == 0 ? Accent : i % 3 == 1 ? Color.FromArgb(0x7c, 0xd8, 0xff) : Color.FromArgb(0xff, 0x8c, 0xd8);
                int al = (int)(40 + 170 * beat);
                FillEllipse(g, Color.FromArgb(al / 3, hc), x, y, F(4.5f));
                FillEllipse(g, Color.FromArgb(al, Lighten(hc, 0.4f)), x, y, F(1.6f));
            }
            // Kugel aus Spiegelplaettchen (drehen sich)
            var rect = new RectangleF(ctr.X - r, ctr.Y - r, r * 2, r * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x5a, 0x60, 0x68))) g.FillEllipse(b, rect);
            var st = g.Save();
            using (var clip = new GraphicsPath()) { clip.AddEllipse(rect); g.SetClip(clip, CombineMode.Intersect); }
            int rows = 7, cols = 10;
            for (int rr = 0; rr < rows; rr++)
            {
                float lat0 = (float)(-Math.PI / 2 + Math.PI * rr / rows), lat1 = (float)(-Math.PI / 2 + Math.PI * (rr + 1) / rows);
                float y0 = ctr.Y + (float)Math.Sin(lat0) * r, y1 = ctr.Y + (float)Math.Sin(lat1) * r;
                float rw = (float)Math.Cos((lat0 + lat1) / 2) * r;
                for (int cc = 0; cc < cols; cc++)
                {
                    double lon = (cc / (double)cols) * 2 * Math.PI + t * 0.8;
                    double lon1 = lon + 2 * Math.PI / cols;
                    if (Math.Cos((lon + lon1) / 2) < 0) continue;   // Rueckseite
                    float x0 = ctr.X + (float)Math.Sin(lon) * rw, x1 = ctr.X + (float)Math.Sin(lon1) * rw;
                    float bright = (float)(0.45 + 0.55 * Math.Abs(Math.Sin(lon * 3 + rr)));
                    if (((cc + rr * 3 + (int)(t * 4)) % 9) == 0) bright = 1f;
                    int v = (int)(90 + 160 * bright);
                    using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, v), Math.Min(255, v), Math.Min(255, v + 10))))
                        g.FillRectangle(b, Math.Min(x0, x1) + 0.5f, y0 + 0.5f, Math.Abs(x1 - x0) - 0.6f, y1 - y0 - 0.6f);
                }
            }
            g.Restore(st);
            using (var p = new Pen(Color.FromArgb(160, 0x20, 0x22, 0x26), Math.Max(1f, F(0.8f)))) g.DrawEllipse(p, rect);
            using (var b = new SolidBrush(Color.FromArgb(130, 255, 255, 255))) g.FillEllipse(b, ctr.X - r * 0.5f, ctr.Y - r * 0.6f, r * 0.45f, r * 0.3f);
        }

        private static void DrawLavaLamp(Graphics g, PointF c, float s)
        {
            float h = s * 0.92f, top = c.Y - h * 0.5f;
            float wTop = s * 0.12f, wBot = s * 0.26f;
            // Fuss und Kappe (Metall)
            var foot = new PointF[] { new PointF(c.X - wBot * 0.75f, c.Y + h * 0.36f), new PointF(c.X + wBot * 0.75f, c.Y + h * 0.36f), new PointF(c.X + wBot * 0.95f, c.Y + h * 0.5f), new PointF(c.X - wBot * 0.95f, c.Y + h * 0.5f) };
            using (var b = new LinearGradientBrush(new RectangleF(c.X - wBot, c.Y + h * 0.3f, wBot * 2, h * 0.25f), Color.FromArgb(0xd8, 0xdc, 0xe0), Color.FromArgb(0x5a, 0x5e, 0x64), LinearGradientMode.Horizontal)) g.FillPolygon(b, foot);
            var cap = new PointF[] { new PointF(c.X - wTop * 0.8f, top), new PointF(c.X + wTop * 0.8f, top), new PointF(c.X + wTop * 1.05f, top + h * 0.1f), new PointF(c.X - wTop * 1.05f, top + h * 0.1f) };
            using (var b = new LinearGradientBrush(new RectangleF(c.X - wTop, top, wTop * 2, h * 0.12f), Color.FromArgb(0xd8, 0xdc, 0xe0), Color.FromArgb(0x5a, 0x5e, 0x64), LinearGradientMode.Horizontal)) g.FillPolygon(b, cap);
            // Glas mit Fluessigkeit
            var glass = new PointF[] { new PointF(c.X - wTop * 1.05f, top + h * 0.1f), new PointF(c.X + wTop * 1.05f, top + h * 0.1f), new PointF(c.X + wBot * 0.75f, c.Y + h * 0.36f), new PointF(c.X - wBot * 0.75f, c.Y + h * 0.36f) };
            Color liq = Color.FromArgb(0x2a, 0x10, 0x48), wax = Color.FromArgb(0xff, 0x5a, 0x2c);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(glass);
                using (var b = new LinearGradientBrush(path.GetBounds(), Lighten(liq, 0.15f), liq, LinearGradientMode.Vertical)) g.FillPath(b, path);
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                float gy0 = top + h * 0.1f, gy1 = c.Y + h * 0.36f, gh = gy1 - gy0;
                // Wachs-Blasen steigen und sinken (schneller, wenn die Musik laut ist)
                for (int i = 0; i < 4; i++)
                {
                    double ph = (DekoPhase * (0.18 + 0.05 * i) + i * 0.27) % 1.0;
                    float u = (float)(0.5 - 0.5 * Math.Cos(ph * 2 * Math.PI));   // unten → oben → unten
                    float by = gy1 - gh * (0.1f + 0.8f * u), bw = s * (0.07f + 0.025f * (i % 2)), bh = bw * (1.2f + 0.4f * (float)Math.Sin(ph * 6.28 * 2));
                    float bx = c.X + (float)Math.Sin(ph * 6.28 + i) * s * 0.04f;
                    using (var b = new SolidBrush(Color.FromArgb(235, wax))) g.FillEllipse(b, bx - bw, by - bh, bw * 2, bh * 2);
                }
                using (var b = new SolidBrush(Color.FromArgb(235, wax))) g.FillEllipse(b, c.X - wBot * 0.75f, gy1 - s * 0.06f, wBot * 1.5f, s * 0.12f);
                g.Restore(st);
                using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(40, wax), SurroundColors = new[] { Color.FromArgb(0, wax) } }) g.FillPath(glow, path);
                using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, F(0.8f)))) g.DrawLine(p, c.X - wTop * 0.6f, top + h * 0.14f, c.X - wBot * 0.45f, c.Y + h * 0.32f);
            }
            // Schein auf dem Pult
            using (var path = new GraphicsPath())
            {
                var gr = new RectangleF(c.X - s * 0.55f, c.Y + h * 0.38f, s * 1.1f, s * 0.25f);
                path.AddEllipse(gr);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(40 + 50 * DekoLevel), wax), SurroundColors = new[] { Color.FromArgb(0, wax) } }) g.FillPath(b, path);
            }
        }

        private static void DrawCat(Graphics g, PointF c, float s, double t)
        {
            Color fur = Color.FromArgb(0x3a, 0x34, 0x30), furLight = Color.FromArgb(0x5a, 0x50, 0x48), pink = Color.FromArgb(0xd8, 0x8a, 0x90);
            float breathe = 1f + 0.035f * (float)Math.Sin(t * 1.6);
            // Schwanz um den Koerper
            using (var p = new Pen(fur, s * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p, c.X - s * 0.4f, c.Y - s * 0.02f, s * 0.75f, s * 0.38f, 10, 150);
            // Koerper (eingerollt)
            var body = new RectangleF(c.X - s * 0.36f, c.Y - s * 0.12f * breathe, s * 0.62f, s * 0.36f * breathe);
            using (var b = new LinearGradientBrush(body, furLight, fur, LinearGradientMode.Vertical)) g.FillEllipse(b, body);
            // Kopf links vorne
            var head = new RectangleF(c.X - s * 0.42f, c.Y - s * 0.02f, s * 0.26f, s * 0.22f);
            // Ohren zucken bei Bass
            float tw = Math.Min(1f, DekoBass * 1.6f) * (float)Math.Sin(t * 40) * 0.35f;
            for (int k = 0; k < 2; k++)
            {
                float ex = head.X + head.Width * (k == 0 ? 0.22f : 0.7f), ey = head.Y + head.Height * 0.15f;
                float rot = (k == 0 ? -0.35f : 0.25f) + (k == 0 ? tw : -tw);
                var tip = new PointF(ex + (float)Math.Sin(rot) * s * 0.09f, ey - (float)Math.Cos(rot) * s * 0.09f);
                var pts = new[] { new PointF(ex - s * 0.045f, ey + s * 0.02f), tip, new PointF(ex + s * 0.045f, ey + s * 0.02f) };
                using (var b = new SolidBrush(fur)) g.FillPolygon(b, pts);
                var ip = new[] { new PointF(ex - s * 0.022f, ey + s * 0.01f), new PointF(tip.X * 0.6f + ex * 0.4f, tip.Y * 0.6f + ey * 0.4f), new PointF(ex + s * 0.022f, ey + s * 0.01f) };
                using (var b = new SolidBrush(pink)) g.FillPolygon(b, ip);
            }
            using (var b = new LinearGradientBrush(head, furLight, fur, LinearGradientMode.Vertical)) g.FillEllipse(b, head);
            // geschlossene Augen
            using (var p = new Pen(Color.FromArgb(0x18, 0x14, 0x12), Math.Max(1f, F(0.9f))))
            {
                g.DrawArc(p, head.X + head.Width * 0.2f, head.Y + head.Height * 0.38f, head.Width * 0.2f, head.Height * 0.16f, 0, 180);
                g.DrawArc(p, head.X + head.Width * 0.58f, head.Y + head.Height * 0.38f, head.Width * 0.2f, head.Height * 0.16f, 0, 180);
            }
            Theme.FillEllipse(g, pink, head.X + head.Width * 0.5f, head.Y + head.Height * 0.66f, F(1.4f));
            // Zzz
            for (int i = 0; i < 3; i++)
            {
                double ph = (t * 0.35 + i / 3.0) % 1.0;
                int a = (int)(170 * Math.Sin(ph * Math.PI));
                if (a < 8) continue;
                float zx = head.Right + s * (0.02f + 0.12f * (float)ph), zy = head.Y - s * (0.05f + 0.35f * (float)ph);
                using (var b = new SolidBrush(Color.FromArgb(a, TextDim))) Str(g, "z", Ui(Math.Max(7f, 8f + 5f * (float)ph), FontStyle.Bold), b, zx, zy, StringFormat.GenericTypographic);
            }
        }

        // ================================================================ 222 Lichtshow

        /// <summary>Ein Scheinwerfer der Lichtshow (Host-Koordinaten).</summary>
        internal struct Spot { public PointF Src; public float Angle; public float Spread; public Color Color; }

        /// <summary>Lichtkegel (durchscheinend, nach unten auslaufend). offset = Lage der Zeichenflaeche im Host.</summary>
        public static void DrawCones(Graphics g, Spot[] spots, float length, float intensity, float flash, RectangleF area, PointF offset)
        {
            if (spots == null || (intensity <= 0.01f && flash <= 0.01f)) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (intensity > 0.01f)
                foreach (var sp in spots)
                {
                    float sx = sp.Src.X - offset.X, sy = sp.Src.Y - offset.Y;
                    double a = sp.Angle * Math.PI / 180.0, w = sp.Spread * Math.PI / 180.0;
                    var p1 = new PointF(sx + (float)Math.Sin(a - w) * length, sy + (float)Math.Cos(a - w) * length);
                    var p2 = new PointF(sx + (float)Math.Sin(a + w) * length, sy + (float)Math.Cos(a + w) * length);
                    var q1 = new PointF(sx + (float)Math.Sin(a - w * 0.35) * length * 0.8f, sy + (float)Math.Cos(a - w * 0.35) * length * 0.8f);
                    var q2 = new PointF(sx + (float)Math.Sin(a + w * 0.35) * length * 0.8f, sy + (float)Math.Cos(a + w * 0.35) * length * 0.8f);
                    // in Abschnitten nach unten auslaufend (ohne Verlaufspinsel – laeuft ueberall gleich)
                    ConeBands(g, sx, sy, p1, p2, sp.Color, 95 * intensity);
                    ConeBands(g, sx, sy, q1, q2, Lighten(sp.Color, 0.5f), 60 * intensity);
                }
            if (flash > 0.01f)
                using (var b = new SolidBrush(Color.FromArgb((int)(55 * flash), 255, 255, 255))) g.FillRectangle(b, area);
            g.SmoothingMode = sm;
        }

        private static void ConeBands(Graphics g, float sx, float sy, PointF p1, PointF p2, Color c, float alpha)
        {
            const int N = 11;
            for (int k = 0; k < N; k++)
            {
                float t0 = k / (float)N, t1 = (k + 1) / (float)N;
                int a = (int)(alpha * Math.Pow(1 - (k + 0.5f) / N, 1.4));
                if (a < 2) continue;
                var pts = new[]
                {
                    new PointF(sx + (p1.X - sx) * t0, sy + (p1.Y - sy) * t0), new PointF(sx + (p2.X - sx) * t0, sy + (p2.Y - sy) * t0),
                    new PointF(sx + (p2.X - sx) * t1, sy + (p2.Y - sy) * t1), new PointF(sx + (p1.X - sx) * t1, sy + (p1.Y - sy) * t1),
                };
                using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, a), c))) g.FillPolygon(b, pts);
            }
        }

        /// <summary>Scheinwerfer-Gehaeuse an der Wand (nur im Host).</summary>
        public static void DrawSpotHousing(Graphics g, Spot sp, float intensity)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = F(7);
            var st = g.Save();
            g.TranslateTransform(sp.Src.X, sp.Src.Y);
            g.RotateTransform(-sp.Angle);
            using (var p = new Pen(Color.FromArgb(0x30, 0x32, 0x36), F(2))) g.DrawLine(p, 0, -r * 1.6f, 0, -r * 0.6f);   // Buegel
            var can = new RectangleF(-r * 0.8f, -r * 0.9f, r * 1.6f, r * 1.4f);
            using (var b = new LinearGradientBrush(can, Color.FromArgb(0x48, 0x4b, 0x52), Color.FromArgb(0x12, 0x13, 0x16), LinearGradientMode.Horizontal)) FillRound(g, b, can, F(2));
            var lens = new RectangleF(-r * 0.7f, r * 0.35f, r * 1.4f, r * 0.5f);
            using (var b = new SolidBrush(Color.FromArgb(80 + (int)(175 * intensity), Lighten(sp.Color, 0.4f)))) g.FillEllipse(b, lens);
            g.Restore(st);
            g.SmoothingMode = sm;
        }

        /// <summary>Farben der Scheinwerfer (wechseln mit dem Takt).</summary>
        public static Color SpotColor(int i, int step)
        {
            Color[] pal = { Color.FromArgb(0xff, 0x3c, 0x9a), Color.FromArgb(0x3c, 0xb4, 0xff), Color.FromArgb(0xb4, 0x5c, 0xff), Color.FromArgb(0x3c, 0xff, 0xb0), Color.FromArgb(0xff, 0xc8, 0x3c), Color.FromArgb(0xff, 0x5a, 0x3c) };
            return pal[((i * 2 + step) % pal.Length + pal.Length) % pal.Length];
        }
    }
}
