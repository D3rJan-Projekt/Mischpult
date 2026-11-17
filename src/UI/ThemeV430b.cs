using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.3 Optik: bewegte Hintergruende (Gewitter mit Blitzen zum Bass, Sternenwirbel, Gluehwuermchen), Waende
    /// (Graffiti, Buecherregal, Serverraum) und der Plattenspieler als Deko (mit dem Album-Cover als Etikett).
    /// </summary>
    internal static partial class Theme
    {
        // ================================================================ 7 bewegte Hintergruende

        /// <summary>Gewitter: Helligkeit des Blitzes 0..1 (vom Takt gesetzt, klingt ab) und Form des Blitzes.</summary>
        public static float StormFlash;
        public static int StormSeed = 1;
        /// <summary>Groesse des Fensters (Sternenwirbel: die Mitte liegt in der Fenstermitte).</summary>
        public static Size LiveForm = new Size(1400, 860);

        private static void DrawLiveBg430(Graphics g, Rectangle area, Point origin, double t)
        {
            switch (LiveBg)
            {
                case 10: DrawStorm(g, area, origin, t); break;
                case 11: DrawSwirl(g, area, origin, t); break;
                default: DrawFireflies(g, area, origin, t); break;
            }
        }

        /// <summary>Gewitter: dunkle Wolken ziehen oben vorbei, Regen faellt schraeg, bei Bass-Schlaegen blitzt es.</summary>
        private static void DrawStorm(Graphics g, Rectangle area, Point origin, double t)
        {
            using (var b = new LinearGradientBrush(new Rectangle(area.X, area.Y - 1, area.Width, area.Height + 2), Color.FromArgb(46, 0x10, 0x18, 0x30), Color.FromArgb(12, 0x04, 0x08, 0x10), LinearGradientMode.Vertical))
                g.FillRectangle(b, area);
            float fl = StormFlash;
            float k = fl > 0.6f ? fl : fl * (0.55f + 0.45f * (float)Math.Abs(Math.Sin(t * 41)));   // flackert beim Ausklingen
            // Wolken (Lage im Fenster, damit beide Bereiche zusammenpassen)
            float span = Math.Max(F(1800), origin.X + area.Right + F(400));
            Color cloud = Mix(C(0x1c, 0x20, 0x2c), C(0xa8, 0xb4, 0xd8), k * 0.6f);
            for (int i = 0; i < 9; i++)
            {
                float cw = F(320 + (i % 4) * 70), ch = F(110 + (i % 3) * 30);
                float fx = (float)((t * (5 + i % 3 * 2) + i * 263) % (span + cw)) - cw;
                var e = new RectangleF(fx - origin.X, area.Y - ch * 0.45f + F((i % 3) * 18), cw, ch);
                if (e.Right < area.X || e.X > area.Right || e.Bottom < area.Y) continue;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(e);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(160, cloud), SurroundColors = new[] { Color.FromArgb(0, cloud) } }) g.FillPath(pb, path);
                }
            }
            // Regen
            int cell = Math.Max(16, Px(26));
            int x0 = (origin.X + area.X) / cell - 2, x1 = (origin.X + area.Right) / cell + 4;
            float hh = Math.Max(area.Height, Px(500)) + F(60);
            using (var p = new Pen(Color.FromArgb(70, 0xa8, 0xb8, 0xd0), Math.Max(1f, F(1))))
                for (int cx = x0; cx <= x1; cx++)
                    for (int j = 0; j < 2; j++)
                    {
                        uint h = (uint)(cx * 2654435761u + (uint)j * 40503u); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                        float speed = F(520 + h % 260);
                        float y = area.Y + (float)((t * speed + h % 1000) % hh) - F(30);
                        float x = cx * cell + (h >> 8) % (uint)cell - origin.X - (y - area.Y) * 0.18f;
                        float len = F(12 + h % 10);
                        g.DrawLine(p, x, y, x - len * 0.18f, y + len);
                    }
            if (k <= 0.02f) return;
            // Himmel leuchtet auf, dazu der Blitz (Fensterkoordinaten)
            using (var b = new SolidBrush(Color.FromArgb((int)(64 * k), 0xd8, 0xe4, 0xff))) g.FillRectangle(b, area);
            var rnd = new Random(StormSeed);
            float bx = (float)(F(80) + rnd.NextDouble() * Math.Max(F(200), LiveForm.Width - F(160))) - origin.X;
            float depth = F(220) + (float)rnd.NextDouble() * F(260);
            var main = Bolt(rnd, bx, area.Y - F(10), depth, F(16), 0);
            DrawBolt(g, main, k, Math.Max(1.2f, F(1.8f)));
            for (int i = 0; i < 2 && main.Count > 5; i++)
            {
                var from = main[2 + rnd.Next(main.Count - 4)];
                DrawBolt(g, Bolt(rnd, from.X, from.Y, depth * 0.35f, F(12), rnd.NextDouble() < 0.5 ? -1 : 1), k * 0.7f, Math.Max(1f, F(1.1f)));
            }
        }

        private static List<PointF> Bolt(Random rnd, float x, float y, float depth, float jitter, int drift)
        {
            var pts = new List<PointF> { new PointF(x, y) };
            float y1 = y + depth;
            while (y < y1)
            {
                y += F(14) + (float)rnd.NextDouble() * F(18);
                x += (float)(rnd.NextDouble() - 0.5) * jitter * 2 + drift * F(7);
                pts.Add(new PointF(x, y));
            }
            return pts;
        }

        private static void DrawBolt(Graphics g, List<PointF> pts, float k, float width)
        {
            if (pts.Count < 2) return;
            var arr = pts.ToArray();
            using (var p = new Pen(Color.FromArgb((int)(45 * k), 0xa8, 0xc0, 0xff), width * 6) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(p, arr);
            using (var p = new Pen(Color.FromArgb((int)(110 * k), 0xc8, 0xd8, 0xff), width * 2.4f) { LineJoin = LineJoin.Round }) g.DrawLines(p, arr);
            using (var p = new Pen(Color.FromArgb((int)(250 * k), 255, 255, 255), width) { LineJoin = LineJoin.Round }) g.DrawLines(p, arr);
        }

        private static float[] _swirlR, _swirlA, _swirlS;

        /// <summary>Sternenwirbel: eine Galaxie mit zwei Spiralarmen dreht sich langsam um die Fenstermitte (innen schneller).</summary>
        private static void DrawSwirl(Graphics g, Rectangle area, Point origin, double t)
        {
            if (_swirlR == null)
            {
                var rnd = new Random(11);
                int n = 560;
                _swirlR = new float[n]; _swirlA = new float[n]; _swirlS = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float r = (float)Math.Pow(rnd.NextDouble(), 0.8) * 0.97f + 0.03f;
                    int arm = i % 2;
                    _swirlR[i] = r;
                    _swirlA[i] = (float)(arm * Math.PI + r * 4.6 + (rnd.NextDouble() - 0.5) * (0.35 + r * 0.5));
                    _swirlS[i] = (float)rnd.NextDouble();
                }
            }
            float cx = LiveForm.Width * 0.5f - origin.X, cy = LiveForm.Height * 0.5f - origin.Y;
            float maxR = Math.Max(LiveForm.Width, LiveForm.Height) * 0.62f;
            // Kern
            var core = new RectangleF(cx - maxR * 0.25f, cy - maxR * 0.16f, maxR * 0.5f, maxR * 0.32f);
            if (core.IntersectsWith(area))
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(core);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(60, 0xe8, 0xc8, 0xff), SurroundColors = new[] { Color.FromArgb(0, 0x8a, 0x6c, 0xff) } }) g.FillPath(pb, path);
                }
            for (int i = 0; i < _swirlR.Length; i++)
            {
                float r = _swirlR[i];
                double a = _swirlA[i] + t * 0.6 * (0.15 / (r + 0.15));
                float x = cx + (float)Math.Cos(a) * r * maxR, y = cy + (float)Math.Sin(a) * r * maxR * 0.62f;
                if (x < area.X - 4 || x > area.Right + 4 || y < area.Y - 4 || y > area.Bottom + 4) continue;
                Color col = r < 0.25f ? Mix(C(0xff, 0xec, 0xc8), C(0xa8, 0xc0, 0xff), r / 0.25f) : Mix(C(0xa8, 0xc0, 0xff), C(0xd0, 0x8c, 0xff), Math.Min(1f, (r - 0.25f) / 0.6f));
                double tw = 0.6 + 0.4 * Math.Sin(t * (1.1 + _swirlS[i]) + i);
                int alpha = (int)((90 + 150 * (1 - r * 0.6f)) * tw);
                float d = F(1.1f + _swirlS[i] * 1.8f);
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(10, Math.Min(255, alpha)), col))) g.FillEllipse(b, x - d / 2, y - d / 2, d, d);
            }
        }

        /// <summary>Gluehwuermchen: kleine Lichter schweben langsam umher und leuchten nacheinander auf.</summary>
        private static void DrawFireflies(Graphics g, Rectangle area, Point origin, double t)
        {
            using (var b = new LinearGradientBrush(new Rectangle(area.X, area.Y - 1, area.Width, area.Height + 2), Color.FromArgb(0, 0x10, 0x30, 0x10), Color.FromArgb(40, 0x18, 0x40, 0x14), LinearGradientMode.Vertical))
                g.FillRectangle(b, area);
            int cell = Math.Max(60, Px(100));
            int x0 = (origin.X + area.X) / cell - 1, x1 = (origin.X + area.Right) / cell + 1;
            int y0 = (origin.Y + area.Y) / cell - 1, y1 = (origin.Y + area.Bottom) / cell + 1;
            for (int cyy = y0; cyy <= y1; cyy++)
                for (int cxx = x0; cxx <= x1; cxx++)
                {
                    uint h = (uint)(cxx * 73856093 ^ cyy * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    if (h % 3 == 0) continue;
                    float x = cxx * cell + (h >> 4) % (uint)cell + (float)Math.Sin(t * (0.21 + (h % 7) * 0.03) + h % 13) * F(26) - origin.X;
                    float y = cyy * cell + (h >> 12) % (uint)cell + (float)Math.Cos(t * (0.17 + (h % 5) * 0.03) + h % 11) * F(18) - origin.Y;
                    if (x < area.X - F(14) || x > area.Right + F(14) || y < area.Y - F(14) || y > area.Bottom + F(14)) continue;
                    double ph = (t * (0.3 + (h % 9) * 0.04) + (h % 100) * 0.13) % 1.0;
                    float glow = ph < 0.4 ? (float)Math.Sin(ph / 0.4 * Math.PI) : 0f;
                    if (glow > 0.05f)
                    {
                        float gr = F(11) * (0.6f + glow * 0.6f);
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(x - gr, y - gr, gr * 2, gr * 2);
                            using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(150 * glow), 0xd8, 0xff, 0x6a), SurroundColors = new[] { Color.FromArgb(0, 0xa8, 0xff, 0x40) } }) g.FillPath(pb, path);
                        }
                    }
                    FillEllipse(g, Color.FromArgb((int)(50 + 205 * glow), 0xf0, 0xff, 0x9a), x, y, Math.Max(1f, F(1.4f)));
                }
        }

        // ================================================================ 8 Waende

        private static List<KeyValuePair<PointF, Color>> _serverLeds;

        private static void MakeWall430(Graphics g, int w, int h, int kind, Random rnd)
        {
            if (kind == 7) WallGraffiti(g, w, h, rnd);
            else if (kind == 8) WallBooks(g, w, h, rnd);
            else WallServer(g, w, h, rnd);
        }

        /// <summary>Nach dem Abdunkeln: Lichter, die hell bleiben sollen (LEDs im Serverraum).</summary>
        private static void WallLights430(Graphics g, int kind)
        {
            if (kind != 9 || _serverLeds == null) return;
            foreach (var kv in _serverLeds)
            {
                FillEllipse(g, Color.FromArgb(60, kv.Value), kv.Key.X, kv.Key.Y, Math.Max(2f, F(3.2f)));
                FillEllipse(g, kv.Value, kv.Key.X, kv.Key.Y, Math.Max(1f, F(1.1f)));
            }
            _serverLeds = null;
        }

        private static readonly Color[] SprayColors = { C(0xff, 0x3c, 0x9c), C(0x2c, 0xe0, 0xff), C(0xff, 0xd8, 0x2c), C(0x6c, 0xff, 0x5a), C(0xa8, 0x5c, 0xff), C(0xff, 0x7a, 0x2c) };

        /// <summary>Graffiti: grauer Putz, gespruehte Farbwolken, Schriftzuege in Blasen-Schrift mit Laufnasen, Sterne.</summary>
        private static void WallGraffiti(Graphics g, int w, int h, Random rnd)
        {
            g.Clear(C(0x4a, 0x4a, 0x4e));
            for (int i = 0; i < 220; i++)
            {
                float cx = (float)(rnd.NextDouble() * w), cy = (float)(rnd.NextDouble() * h), rr = F(10) + (float)rnd.NextDouble() * F(60);
                int v = rnd.Next(-20, 20);
                using (var b = new SolidBrush(Color.FromArgb(22, Clamp255(0x4a + v), Clamp255(0x4a + v), Clamp255(0x4e + v)))) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 1.5f);
            }
            for (int i = 0; i < Math.Max(8, w * h / 60000); i++)
            {
                float cx = (float)(rnd.NextDouble() * w), cy = (float)(rnd.NextDouble() * h), rr = F(60) + (float)rnd.NextDouble() * F(100);
                var c = SprayColors[rnd.Next(SprayColors.Length)];
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - rr, cy - rr, rr * 2, rr * 2);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(120, c), SurroundColors = new[] { Color.FromArgb(0, c) } }) g.FillPath(pb, path);
                }
            }
            FontFamily fam;
            bool own = true;
            try { fam = new FontFamily("Impact"); } catch { fam = FontFamily.GenericSansSerif; own = false; }
            try
            {
                string[] words = { "MISCHPULT", "MIX", "LOUD", "BASS", "SOLO", "FADE" };
                int wi = 0;
                float rowH = Math.Max(120f, F(190));
                for (float y = F(30); y < h; y += rowH)
                    for (float x = -F(40) + (float)rnd.NextDouble() * F(60); x < w; x += F(380) + (float)rnd.NextDouble() * F(120))
                    {
                        string word = words[wi++ % words.Length];
                        float em = F(word.Length > 5 ? 64 : 84) + (float)rnd.NextDouble() * F(20);
                        Color c1 = SprayColors[rnd.Next(SprayColors.Length)], c2 = SprayColors[rnd.Next(SprayColors.Length)];
                        GraffitiWord(g, fam, word, em, new PointF(x, y + (float)rnd.NextDouble() * F(40)), (float)(rnd.NextDouble() * 14 - 9), c1, c2, rnd);
                    }
                // kleines Kuerzel
                using (var path = new GraphicsPath())
                {
                    path.AddString("D3r_Jan", FontFamily.GenericSansSerif, (int)(FontStyle.Bold | FontStyle.Italic), F(26), new PointF(w * 0.62f, h * 0.48f), StringFormat.GenericTypographic);
                    using (var p = new Pen(Color.FromArgb(220, 0x10, 0x10, 0x12), F(4)) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                    using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillPath(b, path);
                }
            }
            finally { if (own) fam.Dispose(); }
            // Sterne
            for (int i = 0; i < Math.Max(6, w * h / 90000); i++)
            {
                var c = SprayColors[rnd.Next(SprayColors.Length)];
                float cx = (float)(rnd.NextDouble() * w), cy = (float)(rnd.NextDouble() * h), rr = F(10) + (float)rnd.NextDouble() * F(14);
                var pts = new PointF[10];
                for (int k = 0; k < 10; k++)
                {
                    double a = -Math.PI / 2 + k * Math.PI / 5;
                    float rad = k % 2 == 0 ? rr : rr * 0.45f;
                    pts[k] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
                }
                using (var p = new Pen(Color.FromArgb(220, c), Math.Max(2f, F(2.6f))) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, pts);
            }
        }

        private static void GraffitiWord(Graphics g, FontFamily fam, string word, float em, PointF at, float angle, Color c1, Color c2, Random rnd)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(word, fam, (int)FontStyle.Bold, em, at, StringFormat.GenericTypographic);
                var bounds = path.GetBounds();
                if (bounds.Width < 2 || bounds.Height < 2) return;
                using (var m = new Matrix())
                {
                    m.RotateAt(angle, new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
                    path.Transform(m);
                }
                bounds = path.GetBounds();
                // Laufnasen unter den Buchstaben
                for (int i = 0; i < 4 + rnd.Next(4); i++)
                {
                    float dx = bounds.X + (float)rnd.NextDouble() * bounds.Width, dy = bounds.Bottom - bounds.Height * (0.15f + (float)rnd.NextDouble() * 0.2f);
                    float len = F(10) + (float)rnd.NextDouble() * F(34);
                    using (var p = new Pen(Color.FromArgb(220, c2), Math.Max(2f, F(2.6f))) { EndCap = LineCap.Round }) g.DrawLine(p, dx, dy, dx, dy + len);
                    FillEllipse(g, Color.FromArgb(230, c2), dx, dy + len, Math.Max(1.5f, F(2.2f)));
                }
                using (var p = new Pen(Color.FromArgb(240, 0x0c, 0x0c, 0x10), Math.Max(4f, em * 0.12f)) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                using (var b = new LinearGradientBrush(RectangleF.Inflate(bounds, 1, 1), c1, c2, LinearGradientMode.Vertical)) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, em * 0.025f)) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
            }
        }

        private static readonly Color[] SpineColors = { C(0x7a, 0x1c, 0x1c), C(0x1c, 0x3c, 0x6a), C(0x2a, 0x5a, 0x2e), C(0x6a, 0x4a, 0x1c), C(0xd8, 0xc8, 0xa0), C(0x3a, 0x2a, 0x4a), C(0x8a, 0x5a, 0x2a), C(0x1a, 0x1a, 0x1a), C(0xa8, 0x7a, 0x2c), C(0x2c, 0x5c, 0x5c) };

        /// <summary>Buecherregal: Holzbretter voller Buecher, ein paar liegen gestapelt oder lehnen schraeg.</summary>
        private static void WallBooks(Graphics g, int w, int h, Random rnd)
        {
            g.Clear(C(0x24, 0x16, 0x0c));
            float shelfH = Math.Max(70f, F(118)), board = Math.Max(6f, F(10));
            for (float y = -F(30); y < h; y += shelfH)
            {
                float floor = y + shelfH - board;
                float x = F(4) + (float)rnd.NextDouble() * F(20);
                while (x < w)
                {
                    double roll = rnd.NextDouble();
                    if (roll < 0.04) { x += F(20) + (float)rnd.NextDouble() * F(40); continue; }   // Luecke
                    if (roll < 0.09)
                    {
                        // liegender Stapel
                        int n = 2 + rnd.Next(3);
                        float sw = F(56) + (float)rnd.NextDouble() * F(30), fy = floor;
                        for (int k = 0; k < n; k++)
                        {
                            float bh = F(9) + (float)rnd.NextDouble() * F(5), bw = sw - (float)rnd.NextDouble() * F(12);
                            var col = SpineColors[rnd.Next(SpineColors.Length)];
                            var br = new RectangleF(x + (float)rnd.NextDouble() * F(6), fy - bh, bw, bh);
                            using (var b = new LinearGradientBrush(RectangleF.Inflate(br, 0, 1), Lighten(col, 0.15f), Darken(col, 0.35f), LinearGradientMode.Vertical)) g.FillRectangle(b, br);
                            fy -= bh;
                        }
                        x += sw + F(8);
                        continue;
                    }
                    float bwid = F(9) + (float)rnd.NextDouble() * F(14), bhh = (shelfH - board - F(8)) * (0.62f + (float)rnd.NextDouble() * 0.36f);
                    var c = SpineColors[rnd.Next(SpineColors.Length)];
                    int v = rnd.Next(-14, 15);
                    c = Color.FromArgb(Clamp255(c.R + v), Clamp255(c.G + v), Clamp255(c.B + v));
                    bool lean = roll > 0.97;
                    var st = g.Save();
                    if (lean)
                    {
                        g.TranslateTransform(x + bwid, floor);
                        g.RotateTransform(-14f);
                        g.TranslateTransform(-bwid, -bhh);
                    }
                    else g.TranslateTransform(x, floor - bhh);
                    var spine = new RectangleF(0, 0, bwid, bhh);
                    using (var b = new LinearGradientBrush(RectangleF.Inflate(spine, 1, 0), Lighten(c, 0.2f), Darken(c, 0.4f), LinearGradientMode.Horizontal))
                    {
                        b.SetBlendTriangularShape(0.35f);
                        g.FillRectangle(b, spine);
                    }
                    var band = Color.FromArgb(150, 0xe8, 0xc8, 0x78);
                    using (var p = new Pen(band, Math.Max(1f, F(1))))
                    {
                        g.DrawLine(p, F(1.5f), bhh * 0.1f, bwid - F(1.5f), bhh * 0.1f);
                        g.DrawLine(p, F(1.5f), bhh * 0.88f, bwid - F(1.5f), bhh * 0.88f);
                    }
                    if (rnd.NextDouble() < 0.35)
                        using (var b = new SolidBrush(Color.FromArgb(170, 0xf0, 0xe4, 0xc8))) g.FillRectangle(b, bwid * 0.2f, bhh * 0.32f, bwid * 0.6f, bhh * 0.16f);
                    g.Restore(st);
                    x += lean ? bwid + bhh * 0.25f : bwid + F(0.8f);
                }
                // Brett mit Schatten
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 0, floor + board, w, F(6));
                using (var b = new LinearGradientBrush(new RectangleF(0, floor - 1, w, board + 2), C(0x9a, 0x68, 0x3c), C(0x4a, 0x2e, 0x18), LinearGradientMode.Vertical)) g.FillRectangle(b, 0, floor, w, board);
            }
        }

        /// <summary>Serverraum: Schraenke mit Einschueben, Lueftungsgittern und Laufwerken; die LEDs leuchten ueber der Abdunklung.</summary>
        private static void WallServer(Graphics g, int w, int h, Random rnd)
        {
            g.Clear(C(0x0a, 0x0c, 0x10));
            var leds = new List<KeyValuePair<PointF, Color>>();
            float rackW = Math.Max(140f, F(200)), gap = F(14), unit = Math.Max(9f, F(15));
            var ledCols = new[] { C(0x3c, 0xff, 0x6a), C(0x3c, 0xff, 0x6a), C(0x3c, 0xff, 0x6a), C(0x4a, 0xa8, 0xff), C(0xff, 0xb0, 0x3a) };
            for (float x = gap / 2; x < w; x += rackW + gap)
            {
                var rr = new RectangleF(x, -F(4), rackW, h + F(8));
                using (var b = new LinearGradientBrush(RectangleF.Inflate(rr, 1, 0), C(0x2a, 0x2e, 0x34), C(0x16, 0x18, 0x1c), LinearGradientMode.Horizontal)) g.FillRectangle(b, rr);
                float rail = F(10);
                using (var b = new SolidBrush(C(0x10, 0x12, 0x15)))
                    for (float hy = F(4); hy < h; hy += F(7))
                    {
                        g.FillRectangle(b, x + rail * 0.35f, hy, F(2.5f), F(2.5f));
                        g.FillRectangle(b, x + rackW - rail * 0.65f, hy, F(2.5f), F(2.5f));
                    }
                float ix0 = x + rail + F(2), iw = rackW - rail * 2 - F(4);
                float iy = F(6);
                int[] sizes = { 1, 1, 1, 2, 2, 3, 4 };
                while (iy < h)
                {
                    int u = sizes[rnd.Next(sizes.Length)];
                    float uh = unit * u;
                    var ur = new RectangleF(ix0, iy, iw, uh - F(1.5f));
                    if (rnd.NextDouble() < 0.08)
                    {
                        using (var b = new SolidBrush(C(0x1a, 0x1c, 0x20))) g.FillRectangle(b, ur);   // Blindblech
                        iy += uh;
                        continue;
                    }
                    using (var b = new LinearGradientBrush(RectangleF.Inflate(ur, 0, 1), C(0x38, 0x3c, 0x44), C(0x1e, 0x21, 0x26), LinearGradientMode.Vertical)) g.FillRectangle(b, ur);
                    using (var p = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) g.DrawLine(p, ur.X, ur.Y, ur.Right, ur.Y);
                    // Lueftungsgitter rechts
                    using (var b = new SolidBrush(C(0x0e, 0x10, 0x12)))
                        for (float gy = ur.Y + F(3); gy < ur.Bottom - F(2); gy += F(4))
                            for (float gx = ur.X + iw * 0.55f; gx < ur.Right - F(4); gx += F(4))
                                g.FillRectangle(b, gx, gy, F(2), F(2));
                    // Laufwerke
                    if (u >= 2)
                        for (int d = 0; d < 4; d++)
                        {
                            var dr = new RectangleF(ur.X + F(20) + d * iw * 0.09f, ur.Y + F(3), iw * 0.075f, uh - F(7));
                            using (var b = new SolidBrush(C(0x14, 0x16, 0x1a))) g.FillRectangle(b, dr);
                            leds.Add(new KeyValuePair<PointF, Color>(new PointF(dr.X + dr.Width / 2, dr.Bottom - F(2.5f)), ledCols[rnd.Next(3)]));
                        }
                    int nl = 1 + rnd.Next(3);
                    for (int k = 0; k < nl; k++)
                        leds.Add(new KeyValuePair<PointF, Color>(new PointF(ur.X + F(6) + k * F(5), ur.Y + Math.Min(uh, unit) * 0.5f), rnd.NextDouble() < 0.04 ? C(0xff, 0x3a, 0x3a) : ledCols[rnd.Next(ledCols.Length)]));
                    iy += uh;
                }
            }
            _serverLeds = leds;
        }

        // ================================================================ 9 Plattenspieler (Deko)

        /// <summary>Fuer den Plattenspieler: Album-Cover (null = rotes Etikett), Wiedergabe, Drehwinkel, Tonarm 0 (Ruhe) .. 1 (auf der Platte).</summary>
        public static Bitmap DekoCover;
        public static bool DekoPlaying;
        public static double DekoSpin;
        public static float DekoArm;

        private static void DrawTurntable(Graphics g, PointF c, float s)
        {
            var pl = new RectangleF(c.X - s * 0.45f, c.Y - s * 0.36f, s * 0.9f, s * 0.64f);
            float cr = s * 0.05f;
            FillRound(g, C(0x24, 0x14, 0x0a), new RectangleF(pl.X, pl.Y + s * 0.02f, pl.Width, pl.Height + s * 0.05f), cr);   // Kante vorne
            using (var b = new LinearGradientBrush(RectangleF.Inflate(pl, 1, 1), C(0x8a, 0x5a, 0x30), C(0x52, 0x32, 0x18), 60f)) FillRound(g, b, pl, cr);
            using (var p = new Pen(Color.FromArgb(26, 0, 0, 0), 1f))
                for (int k = 0; k < 5; k++) g.DrawLine(p, pl.X + s * 0.03f, pl.Y + pl.Height * (0.14f + 0.18f * k), pl.Right - s * 0.03f, pl.Y + pl.Height * (0.11f + 0.18f * k));
            StrokeRound(g, Color.FromArgb(60, 255, 230, 200), 1f, RectangleF.Inflate(pl, -0.5f, -0.5f), cr);
            // Teller und Platte
            var pc = new PointF(pl.X + pl.Width * 0.40f, pl.Y + pl.Height * 0.5f);
            float pr = pl.Height * 0.44f;
            FillEllipse(g, Color.FromArgb(90, 0, 0, 0), pc.X + s * 0.01f, pc.Y + s * 0.015f, pr + s * 0.02f);
            FillEllipse(g, C(0xa4, 0xa8, 0xae), pc.X, pc.Y, pr + s * 0.018f);
            FillEllipse(g, C(0x12, 0x12, 0x14), pc.X, pc.Y, pr);
            using (var p = new Pen(Color.FromArgb(36, 255, 255, 255), 1f))
                for (float rr = pr * 0.48f; rr < pr * 0.96f; rr += Math.Max(1.5f, pr * 0.08f)) g.DrawEllipse(p, pc.X - rr, pc.Y - rr, rr * 2, rr * 2);
            using (var path = new GraphicsPath())
            {
                path.AddPie(pc.X - pr, pc.Y - pr, pr * 2, pr * 2, 200, 32);
                path.AddPie(pc.X - pr, pc.Y - pr, pr * 2, pr * 2, 20, 32);
                using (var b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(b, path);   // Lichtreflex dreht nicht mit
            }
            // Etikett: das Album-Cover, dreht sich mit
            float lr = pr * 0.42f, ang = (float)(DekoSpin % 360.0);
            var cover = DekoCover;
            bool drawn = false;
            if (cover != null && cover.Width > 1 && cover.Height > 1)
                try
                {
                    using (var tb = new TextureBrush(cover, WrapMode.Clamp))
                    using (var m = new Matrix())
                    {
                        m.Translate(pc.X, pc.Y);
                        m.Rotate(ang);
                        m.Scale(lr * 2 / cover.Width, lr * 2 / cover.Height);
                        m.Translate(-cover.Width / 2f, -cover.Height / 2f);
                        tb.Transform = m;
                        g.FillEllipse(tb, pc.X - lr, pc.Y - lr, lr * 2, lr * 2);
                        drawn = true;
                    }
                }
                catch { drawn = false; }
            if (!drawn)
            {
                FillEllipse(g, C(0xc8, 0x3a, 0x2a), pc.X, pc.Y, lr);
                double a = ang * Math.PI / 180;
                using (var p = new Pen(Color.FromArgb(210, 255, 236, 214), Math.Max(1f, F(1))))
                    g.DrawLine(p, pc.X + (float)Math.Cos(a) * lr * 0.35f, pc.Y + (float)Math.Sin(a) * lr * 0.35f, pc.X + (float)Math.Cos(a) * lr * 0.85f, pc.Y + (float)Math.Sin(a) * lr * 0.85f);
            }
            FillEllipse(g, C(0xd0, 0xd4, 0xd8), pc.X, pc.Y, Math.Max(1f, s * 0.018f));
            // Tonarm: in Ruhe nach unten, beim Abspielen schwenkt er auf die Platte
            var piv = new PointF(pl.Right - pl.Width * 0.13f, pl.Y + pl.Height * 0.2f);
            var target = new PointF(pc.X + pr * 0.55f, pc.Y + pr * 0.45f);
            double restA = Math.PI * 0.5, playA = Math.Atan2(target.Y - piv.Y, target.X - piv.X);
            double arm = restA + (playA - restA) * Math.Max(0f, Math.Min(1f, DekoArm));
            float len = (float)Math.Sqrt((target.X - piv.X) * (target.X - piv.X) + (target.Y - piv.Y) * (target.Y - piv.Y));
            var end = new PointF(piv.X + (float)Math.Cos(arm) * len, piv.Y + (float)Math.Sin(arm) * len);
            var cw = new PointF(piv.X - (float)Math.Cos(arm) * s * 0.08f, piv.Y - (float)Math.Sin(arm) * s * 0.08f);
            using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(1.4f, s * 0.03f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, cw.X + s * 0.012f, cw.Y + s * 0.02f, end.X + s * 0.012f, end.Y + s * 0.02f);
            FillEllipse(g, C(0x2c, 0x2c, 0x30), piv.X, piv.Y, s * 0.065f);
            using (var p = new Pen(C(0xcc, 0xd0, 0xd6), Math.Max(1.2f, s * 0.026f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, cw, end);
            FillEllipse(g, C(0x56, 0x58, 0x5e), cw.X, cw.Y, s * 0.04f);
            FillEllipse(g, C(0xb8, 0xbc, 0xc2), piv.X, piv.Y, s * 0.032f);
            var st = g.Save();
            g.TranslateTransform(end.X, end.Y);
            g.RotateTransform((float)(arm * 180 / Math.PI) + 25f);
            using (var b = new SolidBrush(C(0x26, 0x26, 0x2a))) g.FillRectangle(b, -s * 0.025f, -s * 0.03f, s * 0.075f, s * 0.06f);
            g.Restore(st);
            // Start-Taste und Lampe
            FillEllipse(g, C(0x1c, 0x1c, 0x1e), pl.X + pl.Width * 0.1f, pl.Bottom - pl.Height * 0.12f, s * 0.035f);
            FillEllipse(g, DekoPlaying ? C(0x6c, 0xff, 0x7a) : C(0xff, 0x4a, 0x3a), pl.Right - pl.Width * 0.08f, pl.Bottom - pl.Height * 0.1f, Math.Max(1f, s * 0.018f));
        }
    }
}
