using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.1: Saison-Deko in der Kopfleiste – automatisch nach Datum (an/aus in den Einstellungen):
    /// Halloween (24.10.–1.11.): leuchtender Kuerbis und ein Spinnennetz in der Ecke.
    /// Weihnachten (1.–26.12.): Lichterkette an der Unterkante, die Lichter blinken langsam.
    /// Silvester (31.12.–1.1.): kleine Feuerwerke in der freien Mitte.
    /// </summary>
    internal static partial class SeasonDeco
    {
        public const int None = 0, Halloween = 1, Christmas = 2, NewYear = 3;
        /// <summary>Einstellung: 0 automatisch, 1 aus, 2 immer Halloween, 3 immer Weihnachten, 4 immer Silvester.</summary>
        public static readonly string[] ModeNames = { "Automatisch (nach Datum)", "Aus", "Immer Halloween", "Immer Weihnachten", "Immer Silvester" };

        /// <summary>Welche Deko gerade gilt.</summary>
        public static int For(int mode, DateTime d)
        {
            switch (mode)
            {
                case 1: return None;
                case 2: return Halloween;
                case 3: return Christmas;
                case 4: return NewYear;
                case 5: if (MoreOn) return Easter; break;     // 4.0: mehr Deko
                case 6: if (MoreOn) return Spring; break;
                case 7: if (MoreOn) return Summer; break;
                case 8: if (MoreOn) return Autumn; break;
                case 9: if (MoreOn) return Birthday; break;
            }
            if (MoreOn && IsBirthday(d)) return Birthday;   // 4.0: Geburtstag geht vor
            if ((d.Month == 10 && d.Day >= 24) || (d.Month == 11 && d.Day == 1)) return Halloween;
            if (d.Month == 12 && d.Day >= 1 && d.Day <= 26) return Christmas;
            if ((d.Month == 12 && d.Day == 31) || (d.Month == 1 && d.Day == 1)) return NewYear;
            return MoreOn ? MoreFor(d) : None;
        }

        public static string Name(int deco) => NameMore(deco) ?? (deco == Halloween ? "Halloween" : deco == Christmas ? "Weihnachten" : deco == NewYear ? "Silvester" : "keine");

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public static double Now => Clock.Elapsed.TotalSeconds;

        // ---------------------------------------------------------------- Weihnachten: Lichterkette

        private static readonly Color[] BulbColors =
        {
            Color.FromArgb(0xff, 0x3b, 0x30), Color.FromArgb(0x3b, 0xdb, 0x6f), Color.FromArgb(0xff, 0xc2, 0x33), Color.FromArgb(0x4f, 0x9d, 0xff),
        };

        /// <summary>Lichterkette entlang der Unterkante (y = Hoehe der Leiste). Jedes Licht blinkt in seinem eigenen Takt.</summary>
        public static void DrawLights(Graphics g, float width, float height)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float span = Theme.F(46), sag = Theme.F(4), top = height - Theme.F(9);
            int n = Math.Max(2, (int)(width / span) + 1);
            using (var wire = new Pen(Color.FromArgb(0x22, 0x3a, 0x22), Math.Max(1f, Theme.F(1.2f))))
            {
                var pts = new List<PointF>();
                for (int i = 0; i <= n * 8; i++)
                {
                    float x = i * span / 8f;
                    float u = (i % 8) / 8f;
                    pts.Add(new PointF(x, top + (float)Math.Sin(u * Math.PI) * sag));
                }
                g.DrawLines(wire, pts.ToArray());
            }
            double t = Now;
            for (int i = 0; i < n; i++)
            {
                float x = i * span + span / 2f, y = top + sag;
                var c = BulbColors[i % BulbColors.Length];
                bool on = ((int)(t / 0.7 + i * 0.37) % 3) != 0;   // gegeneinander versetzt: nie alle gleichzeitig aus
                float bw = Theme.F(4), bh = Theme.F(6);
                var r = new RectangleF(x - bw / 2f, y, bw, bh);
                if (on)
                    using (var b = new SolidBrush(Color.FromArgb(60, c))) g.FillEllipse(b, RectangleF.Inflate(r, Theme.F(3), Theme.F(3)));
                using (var b = new SolidBrush(on ? c : Theme.Darken(c, 0.6f))) g.FillEllipse(b, r);
                using (var b = new SolidBrush(Color.FromArgb(0x30, 0x30, 0x30))) g.FillRectangle(b, x - Theme.F(1.5f), y - Theme.F(1.5f), Theme.F(3), Theme.F(2));
                if (on) using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillEllipse(b, r.X + bw * 0.25f, r.Y + bh * 0.2f, bw * 0.3f, bh * 0.3f);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Takt der Lichter (aendert sich, wenn neu gezeichnet werden muss).</summary>
        public static int LightsStep => (int)(Now / 0.7);

        // ---------------------------------------------------------------- Halloween: Kuerbis und Spinnennetz

        /// <summary>Kuerbis mit flackernder Kerze (r = Platz dafuer).</summary>
        public static void DrawPumpkin(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double t = Now;
            float flick = 0.75f + 0.25f * (float)(Math.Sin(t * 13.0) * 0.5 + Math.Sin(t * 7.3 + 1.0) * 0.5);
            float w = r.Width, h = r.Height * 0.82f, y = r.Bottom - h;
            // Schein
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(RectangleF.Inflate(new RectangleF(r.X, y, w, h), Theme.F(5), Theme.F(4)));
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(70 * flick), 255, 150, 30), SurroundColors = new[] { Color.FromArgb(0, 255, 150, 30) } })
                    g.FillPath(pb, path);
            }
            // Koerper: drei Rippen
            var orange = Color.FromArgb(0xf2, 0x7a, 0x12);
            for (int i = 0; i < 3; i++)
            {
                float rw = w * (i == 1 ? 0.56f : 0.5f), rx = r.X + (i == 0 ? 0 : i == 1 ? w * 0.22f : w * 0.5f);
                var er = new RectangleF(rx, y, rw, h);
                using (var b = new LinearGradientBrush(er, Theme.Lighten(orange, 0.15f), Theme.Darken(orange, 0.35f), 90f)) g.FillEllipse(b, er);
                using (var p = new Pen(Theme.Darken(orange, 0.45f), Math.Max(0.8f, Theme.F(0.7f)))) g.DrawEllipse(p, er);
            }
            // Stiel
            using (var b = new SolidBrush(Color.FromArgb(0x4a, 0x6b, 0x22)))
                g.FillRectangle(b, r.X + w * 0.46f, y - Theme.F(3), w * 0.1f, Theme.F(4));
            // Gesicht leuchtet
            var glow = Color.FromArgb((int)(255 * flick), 255, (int)(200 * flick + 40), 60);
            using (var b = new SolidBrush(glow))
            {
                float ex = w * 0.13f, ey = h * 0.17f, cy = y + h * 0.36f;
                g.FillPolygon(b, new[] { new PointF(r.X + w * 0.27f, cy + ey), new PointF(r.X + w * 0.27f + ex, cy + ey), new PointF(r.X + w * 0.27f + ex / 2f, cy) });
                g.FillPolygon(b, new[] { new PointF(r.X + w * 0.6f, cy + ey), new PointF(r.X + w * 0.6f + ex, cy + ey), new PointF(r.X + w * 0.6f + ex / 2f, cy) });
                float my = y + h * 0.66f;
                g.FillPolygon(b, new[]
                {
                    new PointF(r.X + w * 0.24f, my), new PointF(r.X + w * 0.36f, my + h * 0.07f), new PointF(r.X + w * 0.44f, my), new PointF(r.X + w * 0.52f, my + h * 0.07f),
                    new PointF(r.X + w * 0.6f, my), new PointF(r.X + w * 0.68f, my + h * 0.07f), new PointF(r.X + w * 0.76f, my),
                    new PointF(r.X + w * 0.64f, my + h * 0.17f), new PointF(r.X + w * 0.36f, my + h * 0.17f),
                });
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Feines Spinnennetz in der Ecke oben links (Radius rad).</summary>
        public static void DrawWeb(Graphics g, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(70, 230, 230, 230), Math.Max(0.6f, Theme.F(0.6f))))
            {
                var c = new PointF(0, 0);
                int spokes = 5;
                for (int i = 0; i <= spokes; i++)
                {
                    double a = i * (Math.PI / 2) / spokes;
                    g.DrawLine(p, c, new PointF((float)Math.Cos(a) * rad, (float)Math.Sin(a) * rad));
                }
                for (int ring = 1; ring <= 4; ring++)
                {
                    float rr = rad * ring / 4.4f;
                    var pts = new PointF[spokes + 1];
                    for (int i = 0; i <= spokes; i++)
                    {
                        double a = i * (Math.PI / 2) / spokes;
                        float sagF = 0.88f;   // Faeden haengen leicht durch
                        float r2 = i % 2 == 0 ? rr : rr * sagF;
                        pts[i] = new PointF((float)Math.Cos(a) * r2, (float)Math.Sin(a) * r2);
                    }
                    g.DrawCurve(p, pts, 0.4f);
                }
            }
            g.SmoothingMode = sm;
        }

        public static int PumpkinStep => (int)(Now * 12);

        // ---------------------------------------------------------------- Silvester: Feuerwerk

        private sealed class Burst { public float X, Y; public double Start; public Color Color; public int Seed; }
        private static readonly List<Burst> _bursts = new List<Burst>();
        private static double _nextBurst;
        private static readonly Random _rnd = new Random(2026);
        private static readonly Color[] FireColors =
        {
            Color.FromArgb(0xff, 0xd2, 0x4a), Color.FromArgb(0xff, 0x5a, 0x5a), Color.FromArgb(0x6a, 0xe0, 0xff), Color.FromArgb(0xb0, 0x7a, 0xff), Color.FromArgb(0x7a, 0xff, 0x9a),
        };
        private const double BurstLen = 1.4;

        /// <summary>Neue Feuerwerke anstossen (aus dem Takt); area = freie Flaeche. true = es gibt gerade etwas zu zeichnen.</summary>
        public static bool TickFireworks(RectangleF area)
        {
            double t = Now;
            _bursts.RemoveAll(b => t - b.Start > BurstLen);
            if (area.Width > Theme.F(40) && t >= _nextBurst)
            {
                _nextBurst = t + 0.6 + _rnd.NextDouble() * 1.4;
                _bursts.Add(new Burst
                {
                    X = area.X + (float)_rnd.NextDouble() * area.Width, Y = area.Y + (float)(0.3 + _rnd.NextDouble() * 0.4) * area.Height,
                    Start = t, Color = FireColors[_rnd.Next(FireColors.Length)], Seed = _rnd.Next(),
                });
            }
            return _bursts.Count > 0;
        }

        public static void DrawFireworks(Graphics g, RectangleF clip)
        {
            double t = Now;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            g.SetClip(clip, CombineMode.Intersect);
            foreach (var b in _bursts)
            {
                double u = (t - b.Start) / BurstLen;
                if (u < 0 || u > 1) continue;
                float ease = (float)(1 - Math.Pow(1 - u, 3));
                float R = Theme.F(16) * ease;
                int alpha = (int)(255 * (1 - u));
                var rnd = new Random(b.Seed);
                using (var br = new SolidBrush(Color.FromArgb(alpha, b.Color)))
                using (var tail = new Pen(Color.FromArgb(alpha / 3, b.Color), Math.Max(0.8f, Theme.F(0.8f))))
                    for (int i = 0; i < 14; i++)
                    {
                        double a = i * Math.PI * 2 / 14 + rnd.NextDouble() * 0.2;
                        float rr = R * (0.8f + (float)rnd.NextDouble() * 0.3f);
                        float x = b.X + (float)Math.Cos(a) * rr, y = b.Y + (float)Math.Sin(a) * rr + (float)(u * u) * Theme.F(6);   // sinkt etwas
                        g.DrawLine(tail, b.X + (float)Math.Cos(a) * rr * 0.6f, b.Y + (float)Math.Sin(a) * rr * 0.6f, x, y);
                        float d = Math.Max(1.2f, Theme.F(1.6f));
                        g.FillEllipse(br, x - d / 2f, y - d / 2f, d, d);
                    }
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
