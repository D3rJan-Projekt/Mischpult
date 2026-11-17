using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.4.6 – Optik: Deko auf dem Pult, Kabel, Regen und Schnee, Roehrenbild, Kappe mit Griffmulde.</summary>
    internal static partial class Theme
    {
        // ================================================================ Deko

        public static readonly string[] DekoNames = { "Kaffeetasse", "Pflanze", "Kopfhörer", "Gummiente" };
        /// <summary>Groesse eines Deko-Stuecks (logische Pixel).</summary>
        public const float DekoSize = 58f;

        /// <summary>Deko-Stueck mit Mitte c; t = Zeit in s (Dampf, Blaetter).</summary>
        public static void DrawDeko(Graphics g, int kind, PointF c, double t)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = F(DekoSize);
            // Schatten auf dem Pult
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(sh, c.X - s * 0.36f, c.Y + s * 0.30f, s * 0.72f, s * 0.16f);
            switch (kind)
            {
                case 0: DrawCup(g, c, s, t); break;
                case 1: DrawPlant(g, c, s, t); break;
                case 2: DrawHeadphones(g, c, s); break;
                case 3: DrawDuck(g, c, s, t); break;
                default: DrawDeko400(g, kind, c, s, t); break;   // 4.0 finale: Diskokugel, Lava-Lampe, Katze
            }
            g.SmoothingMode = sm;
        }

        private static void DrawCup(Graphics g, PointF c, float s, double t)
        {
            var body = new RectangleF(c.X - s * 0.26f, c.Y - s * 0.12f, s * 0.46f, s * 0.48f);
            // Henkel
            using (var p = new Pen(Color.FromArgb(0xe8, 0xe4, 0xdc), s * 0.07f))
                g.DrawArc(p, body.Right - s * 0.10f, body.Y + s * 0.08f, s * 0.22f, s * 0.24f, -80, 160);
            using (var b = new LinearGradientBrush(body, Color.FromArgb(0xf4, 0xf1, 0xea), Color.FromArgb(0xb9, 0xb4, 0xaa), LinearGradientMode.Horizontal))
                FillRound(g, b, body, s * 0.06f);
            // Kaffee
            var top = new RectangleF(body.X + s * 0.02f, body.Y - s * 0.05f, body.Width - s * 0.04f, s * 0.11f);
            using (var b = new SolidBrush(Color.FromArgb(0xe9, 0xe5, 0xdd))) g.FillEllipse(b, RectangleF.Inflate(top, s * 0.01f, s * 0.008f));
            using (var b = new SolidBrush(Color.FromArgb(0x4a, 0x2c, 0x1a))) g.FillEllipse(b, RectangleF.Inflate(top, -s * 0.02f, -s * 0.015f));
            // Aufdruck in der Akzentfarbe
            using (var b = new SolidBrush(Alpha(Accent, 200))) g.FillEllipse(b, body.X + body.Width / 2 - s * 0.06f, body.Y + body.Height * 0.45f, s * 0.12f, s * 0.12f);
            // Dampf: drei Schwaden steigen und vergehen
            for (int i = 0; i < 3; i++)
            {
                double ph = (t * 0.45 + i / 3.0) % 1.0;
                float x = body.X + body.Width * (0.3f + 0.2f * i);
                float y0 = top.Y - (float)ph * s * 0.55f;
                int a = (int)(90 * Math.Sin(ph * Math.PI));
                if (a <= 2) continue;
                using (var path = new GraphicsPath())
                {
                    var pts = new PointF[5];
                    for (int k = 0; k < 5; k++)
                    {
                        float yy = y0 - k * s * 0.07f;
                        pts[k] = new PointF(x + (float)Math.Sin(t * 2.2 + i + k * 0.9) * s * 0.035f, yy);
                    }
                    path.AddCurve(pts);
                    using (var p = new Pen(Color.FromArgb(a, 0xf0, 0xf0, 0xf0), s * 0.035f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawPath(p, path);
                }
            }
        }

        private static void DrawPlant(Graphics g, PointF c, float s, double t)
        {
            // Blaetter (wiegen sich leicht)
            float sway = (float)Math.Sin(t * 0.8) * 4f;
            var leaf = Color.FromArgb(0x3f, 0x9a, 0x4f);
            var leafDark = Color.FromArgb(0x2b, 0x6e, 0x38);
            for (int i = 0; i < 7; i++)
            {
                float ang = -150 + i * 20 + sway * (i % 2 == 0 ? 1 : -1);
                var st = g.Save();
                g.TranslateTransform(c.X, c.Y);
                g.RotateTransform(ang + 90);
                var r = new RectangleF(-s * 0.055f, -s * 0.42f - (i % 3) * s * 0.03f, s * 0.11f, s * 0.36f);
                using (var b = new LinearGradientBrush(r, leaf, leafDark, LinearGradientMode.Horizontal)) g.FillEllipse(b, r);
                using (var p = new Pen(Color.FromArgb(90, 0x1c, 0x4a, 0x26), 1f)) g.DrawLine(p, 0, r.Y + s * 0.03f, 0, r.Bottom - s * 0.02f);
                g.Restore(st);
            }
            // Topf
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new PointF(c.X - s * 0.22f, c.Y + s * 0.02f), new PointF(c.X + s * 0.22f, c.Y + s * 0.02f), new PointF(c.X + s * 0.16f, c.Y + s * 0.36f), new PointF(c.X - s * 0.16f, c.Y + s * 0.36f) });
                using (var b = new LinearGradientBrush(new RectangleF(c.X - s * 0.22f, c.Y, s * 0.44f, s * 0.36f), Color.FromArgb(0xd0, 0x6e, 0x45), Color.FromArgb(0x8e, 0x40, 0x24), LinearGradientMode.Horizontal))
                    g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(0xb0, 0x58, 0x36))) FillRound(g, b, new RectangleF(c.X - s * 0.25f, c.Y - s * 0.02f, s * 0.5f, s * 0.08f), s * 0.02f);
        }

        private static void DrawHeadphones(Graphics g, PointF c, float s)
        {
            using (var p = new Pen(Color.FromArgb(0x2a, 0x2d, 0x33), s * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p, c.X - s * 0.28f, c.Y - s * 0.30f, s * 0.56f, s * 0.56f, 190, 160);
            using (var p = new Pen(Color.FromArgb(0x55, 0x5a, 0x63), s * 0.02f))
                g.DrawArc(p, c.X - s * 0.26f, c.Y - s * 0.28f, s * 0.52f, s * 0.52f, 200, 140);
            foreach (int side in new[] { -1, 1 })
            {
                var cup = new RectangleF(c.X + side * s * 0.25f - s * 0.10f, c.Y - s * 0.04f, s * 0.2f, s * 0.3f);
                using (var b = new LinearGradientBrush(cup, Color.FromArgb(0x3a, 0x3e, 0x46), Color.FromArgb(0x16, 0x18, 0x1c), LinearGradientMode.Vertical))
                    FillRound(g, b, cup, s * 0.08f);
                using (var b = new SolidBrush(Alpha(Accent, 220))) g.FillEllipse(b, cup.X + cup.Width / 2 - s * 0.03f, cup.Y + cup.Height / 2 - s * 0.03f, s * 0.06f, s * 0.06f);
            }
        }

        private static void DrawDuck(Graphics g, PointF c, float s, double t)
        {
            float bob = (float)Math.Sin(t * 1.3) * s * 0.01f;
            var body = new RectangleF(c.X - s * 0.28f, c.Y + bob, s * 0.5f, s * 0.32f);
            var yellow1 = Color.FromArgb(0xff, 0xd8, 0x3a); var yellow2 = Color.FromArgb(0xe6, 0xa8, 0x12);
            using (var b = new LinearGradientBrush(body, yellow1, yellow2, LinearGradientMode.Vertical)) g.FillEllipse(b, body);
            var head = new RectangleF(c.X - s * 0.02f, c.Y - s * 0.24f + bob, s * 0.26f, s * 0.26f);
            using (var b = new LinearGradientBrush(head, yellow1, yellow2, LinearGradientMode.Vertical)) g.FillEllipse(b, head);
            using (var b = new SolidBrush(Color.FromArgb(0xff, 0x7a, 0x1a)))
                g.FillPolygon(b, new[] { new PointF(head.Right - s * 0.03f, head.Y + s * 0.11f), new PointF(head.Right + s * 0.09f, head.Y + s * 0.14f), new PointF(head.Right - s * 0.02f, head.Y + s * 0.18f) });
            using (var b = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20))) g.FillEllipse(b, head.X + s * 0.15f, head.Y + s * 0.07f, s * 0.045f, s * 0.045f);
            using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(b, body.X + s * 0.08f, body.Y + s * 0.05f, s * 0.14f, s * 0.07f);
        }

        // ================================================================ Kabel

        /// <summary>Ein Kabel haengt unten aus einem Zug: Stecker, Bogen zur Seite; swing = Ausschlag in px (Bass).</summary>
        public static void DrawCable(Graphics g, float x, float top, float bottom, Color col, int dir, float swing)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = F(4.2f);
            var plug = new RectangleF(x - F(4), top, F(8), F(9));
            using (var b = new LinearGradientBrush(plug, Color.FromArgb(0xd0, 0xd4, 0xda), Color.FromArgb(0x70, 0x76, 0x80), LinearGradientMode.Horizontal))
                g.FillRectangle(b, plug);
            using (var path = new GraphicsPath())
            {
                float y0 = plug.Bottom - F(1);
                path.AddBezier(x, y0, x, y0 + (bottom - y0) * 0.5f, x + dir * F(16) + swing, bottom - F(4), x + dir * F(34) + swing, bottom + F(14));
                using (var p = new Pen(Color.FromArgb(150, 0, 0, 0), w + F(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, path);
                using (var p = new Pen(Darken(col, 0.15f), w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, path);
                using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), F(1.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var st = g.Save();
                    g.TranslateTransform(-F(1), 0);
                    g.DrawPath(p, path);
                    g.Restore(st);
                }
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Regen und Schnee (bewegter Hintergrund 4 und 5)

        private static void DrawRainSnow(Graphics g, Rectangle area, Point origin, double t, bool snow)
        {
            int cell = Math.Max(14, Px(snow ? 26 : 18));
            int x0 = (origin.X + area.X) / cell - 1, x1 = (origin.X + area.Right) / cell + 1;
            float H = Math.Max(F(900), origin.Y + area.Bottom + F(60));
            for (int cx = x0; cx <= x1; cx++)
                for (int layer = 0; layer < (snow ? 3 : 2); layer++)
                {
                    uint hsh = (uint)(cx * 73856093 ^ layer * 19349663 ^ 0x2f6b);
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    float speed = snow ? F(18 + hsh % 22) : F(380 + hsh % 260);
                    float off = hsh % 1000 / 1000f * H;
                    float fy = (float)((t * speed + off) % H) - origin.Y;
                    float fx = cx * cell + (hsh >> 8) % (uint)cell - origin.X;
                    if (snow)
                    {
                        fx += (float)Math.Sin(t * (0.5 + (hsh % 5) * 0.1) + (hsh % 50)) * F(10);
                        float d = F(1.6f + (hsh >> 4) % 3 * 0.8f);
                        int a = 120 + (int)(hsh % 100);
                        using (var b = new SolidBrush(Color.FromArgb(a, 0xf2, 0xf6, 0xff))) g.FillEllipse(b, fx - d / 2, fy - d / 2, d, d);
                    }
                    else
                    {
                        float len = F(10 + hsh % 10);
                        using (var p = new Pen(Color.FromArgb(55 + (int)(hsh % 40), 0xb8, 0xcc, 0xe8), F(1.1f)))
                            g.DrawLine(p, fx, fy, fx - len * 0.12f, fy + len);
                    }
                }
            if (!snow)
            {
                // ein paar Tropfen rinnen langsam an der Scheibe hinunter
                int cellD = Math.Max(60, Px(110));
                int dx0 = (origin.X + area.X) / cellD - 1, dx1 = (origin.X + area.Right) / cellD + 1;
                for (int cx = dx0; cx <= dx1; cx++)
                {
                    uint hsh = (uint)(cx * 2654435761u ^ 0x51ed);
                    hsh ^= hsh >> 15;
                    float speed = F(6 + hsh % 14);
                    float fy = (float)((t * speed + (hsh % 997)) % H) - origin.Y;
                    float fx = cx * cellD + (hsh >> 6) % (uint)cellD - origin.X;
                    float d = F(3 + hsh % 3);
                    using (var b = new SolidBrush(Color.FromArgb(70, 0xd8, 0xe6, 0xff))) g.FillEllipse(b, fx - d / 2, fy - d / 2, d, d * 1.25f);
                    using (var p = new Pen(Color.FromArgb(28, 0xd8, 0xe6, 0xff), F(1.2f))) g.DrawLine(p, fx, fy - F(26), fx, fy - d / 2);
                }
            }
        }

        // ================================================================ Roehrenbild

        public static bool CrtOn;
        private static TextureBrush _crtLines;
        private static int _crtPx;

        /// <summary>Zeilen und leichtes Flimmern ueber einer Anzeige (wie ein alter Roehren-Monitor).</summary>
        public static void CrtOverlay(Graphics g, RectangleF r)
        {
            if (!CrtOn || r.Width < 4 || r.Height < 4) return;
            int px = Math.Max(1, Px(1));
            if (_crtLines == null || _crtPx != px)
            {
                _crtLines?.Dispose();
                var bmp = new Bitmap(1, px * 3);
                for (int y = 0; y < px * 3; y++) bmp.SetPixel(0, y, y < px * 2 ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(70, 0, 0, 0));
                _crtLines = new TextureBrush(bmp, WrapMode.Tile);
                _crtPx = px;
            }
            var st = g.Save();
            g.SetClip(r, CombineMode.Intersect);
            g.FillRectangle(_crtLines, r);
            // langsam wandernder heller Streifen und leichtes Flimmern
            double t = LiveClock.Elapsed.TotalSeconds;
            float band = r.Height * 0.18f;
            float by = r.Y + (float)((t * 0.25) % 1.0) * (r.Height + band) - band;
            using (var b = new LinearGradientBrush(new RectangleF(r.X, by - 1, r.Width, band + 2), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(18, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillRectangle(b, r.X, by, r.Width, band);
            int fl = (int)(6 + 6 * Math.Sin(t * 37) * Math.Sin(t * 11));
            if (fl > 0) using (var b = new SolidBrush(Color.FromArgb(fl, 0, 0, 0))) g.FillRectangle(b, r);
            // Ecken dunkler (gewoelbtes Glas)
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r.X - r.Width * 0.15f, r.Y - r.Height * 0.25f, r.Width * 1.3f, r.Height * 1.5f);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(90, 0, 0, 0) } })
                    g.FillRectangle(pb, r);
            }
            g.Restore(st);
        }

        // ================================================================ Kappe mit Griffmulde (Kappen-Form 5)

        public static void DrawDishCap(Graphics g, RectangleF r, bool master, bool hover, Color accent)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = F(3f);
            FillRound(g, Color.FromArgb(120, 0, 0, 0), new RectangleF(r.X + F(1), r.Y + F(3), r.Width, r.Height), rad + F(1));
            Color b1 = hover ? Color.FromArgb(0x4a, 0x4e, 0x56) : Color.FromArgb(0x3c, 0x40, 0x47), b2 = Color.FromArgb(0x16, 0x18, 0x1b);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), b1, b2, LinearGradientMode.Vertical))
                FillRound(g, b, r, rad);
            // Mulde: dunkle Rinne quer, unten heller Rand (Licht von oben)
            float my = r.Y + r.Height * 0.5f, mh = Math.Max(F(4), r.Height * 0.42f);
            var dish = new RectangleF(r.X + F(2), my - mh / 2, r.Width - F(4), mh);
            using (var b = new LinearGradientBrush(new RectangleF(dish.X, dish.Y - 1, dish.Width, dish.Height + 2), Color.FromArgb(0x0c, 0x0d, 0x0f), Color.FromArgb(0x33, 0x37, 0x3d), LinearGradientMode.Vertical))
                g.FillEllipse(b, dish);
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), F(0.8f))) g.DrawArc(p, dish, 20, 140);
            // Markierung in der Kanalfarbe (MASTER rot)
            Color m = master ? Color.FromArgb(0xe8, 0x4a, 0x3c) : accent;
            using (var p = new Pen(m, Math.Max(1.5f, F(1.6f)))) g.DrawLine(p, r.X + F(4), my, r.Right - F(4), my);
            StrokeRound(g, Color.FromArgb(0x08, 0x09, 0x0a), F(1), r, rad);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), F(0.8f))) g.DrawLine(p, r.X + rad, r.Y + F(1), r.Right - rad, r.Y + F(1));
            g.SmoothingMode = sm;
        }
    }
}
