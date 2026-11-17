using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0: mehr Saison-Deko (Schalter "Mehr Saison-Deko"): Ostern (bunte Eier), Fruehling (Blumen an der Unterkante),
    /// Sommer (Sonne), Herbst (fallende Blaetter) und Geburtstag (Torte mit Kerze und Konfetti).
    /// </summary>
    internal static partial class SeasonDeco
    {
        public const int Easter = 4, Spring = 5, Summer = 6, Autumn = 7, Birthday = 8;
        /// <summary>Schalter "Mehr Saison-Deko".</summary>
        public static bool MoreOn;
        public static int BirthDay, BirthMonth;
        /// <summary>Weniger Bewegung: Deko steht still.</summary>
        public static bool Calm;

        public static readonly string[] MoreModeNames = { "Immer Ostern", "Immer Frühling", "Immer Sommer", "Immer Herbst", "Immer Geburtstag" };
        /// <summary>Auswahl in den Einstellungen (mit "mehr Deko" auch die neuen).</summary>
        public static string[] ModeNamesNow
        {
            get
            {
                if (!MoreOn) return ModeNames;
                var l = new List<string>(ModeNames);
                l.AddRange(MoreModeNames);
                return l.ToArray();
            }
        }
        public const int AllModes = 10;

        /// <summary>Ostersonntag (Gauss / anonymer Gregorianischer Algorithmus).</summary>
        public static DateTime EasterSunday(int year)
        {
            int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
            return new DateTime(year, month, day);
        }

        /// <summary>Neue Deko nach Datum (nach Halloween/Weihnachten/Silvester; Geburtstag geht vor).</summary>
        private static int MoreFor(DateTime d)
        {
            var e = EasterSunday(d.Year);
            if (d.Date >= e.AddDays(-3) && d.Date <= e.AddDays(1)) return Easter;   // Gruendonnerstag bis Ostermontag
            int md = d.Month * 100 + d.Day;
            if (md >= 320 && md <= 531) return Spring;
            if (md >= 601 && md <= 831) return Summer;
            if (md >= 915 && md <= 1120) return Autumn;
            return None;
        }

        public static bool IsBirthday(DateTime d) => BirthMonth > 0 && d.Month == BirthMonth && d.Day == BirthDay;

        public static string NameMore(int deco) =>
            deco == Easter ? "Ostern" : deco == Spring ? "Frühling" : deco == Summer ? "Sommer" : deco == Autumn ? "Herbst" : deco == Birthday ? "Geburtstag" : null;

        // ---------------------------------------------------------------- neben dem "made by"-Schild

        /// <summary>Kleines Bild rechts neben dem Schild (Eier, Sonne, Torte). Liefert den neuen rechten Rand.</summary>
        public static float DrawNearBadge(Graphics g, int deco, float right, float height, out RectangleF rect)
        {
            rect = RectangleF.Empty;
            float s = Theme.F(22);
            switch (deco)
            {
                case Easter:
                    rect = new RectangleF((float)Math.Round(right + Theme.F(10)), (float)Math.Round((height - s) / 2f), Theme.F(36), s);
                    DrawEggs(g, rect);
                    return rect.Right;
                case Summer:
                    rect = new RectangleF((float)Math.Round(right + Theme.F(10)), (float)Math.Round((height - s) / 2f), s, s);
                    DrawSun(g, rect);
                    return rect.Right;
                case Birthday:
                    rect = new RectangleF((float)Math.Round(right + Theme.F(10)), (float)Math.Round((height - s) / 2f), s, s);
                    DrawCake(g, rect);
                    return rect.Right;
            }
            return right;
        }

        /// <summary>Was ueber die ganze Leiste gemalt wird (Blumen, Blaetter, Konfetti, Gras).</summary>
        public static void DrawOver(Graphics g, int deco, float width, float height, RectangleF free)
        {
            switch (deco)
            {
                case Easter: DrawGrass(g, width, height); break;
                case Spring: DrawFlowers(g, width, height); break;
                case Autumn: DrawParticles(g, free, false); break;
                case Birthday: DrawParticles(g, free, true); break;
            }
        }

        /// <summary>Aus dem Takt: Schritt der Bewegung (aendert er sich, neu zeichnen) – -1 = steht still.</summary>
        public static int StepOf(int deco)
        {
            if (Calm) return -1;
            double t = Now;
            switch (deco)
            {
                case Spring: return (int)(t * 8);
                case Summer: return (int)(t * 6);
                case Birthday: return (int)(t * 24);
                case Autumn: return (int)(t * 24);
            }
            return -1;
        }

        // ---------------------------------------------------------------- Ostern

        private static readonly Color[] EggColors = { Color.FromArgb(0xff, 0x9a, 0xb8), Color.FromArgb(0x8a, 0xd8, 0xff), Color.FromArgb(0xff, 0xd8, 0x6a), Color.FromArgb(0x9a, 0xe8, 0x8a) };

        private static void DrawEggs(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float ew = r.Height * 0.62f, eh = r.Height * 0.86f;
            for (int i = 0; i < 3; i++)
            {
                var er = new RectangleF(r.X + i * ew * 0.85f, r.Bottom - eh - (i == 1 ? Theme.F(1.5f) : 0f), ew, eh);
                var c = EggColors[i];
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(er);
                    using (var b = new PathGradientBrush(path) { CenterPoint = new PointF(er.X + er.Width * 0.38f, er.Y + er.Height * 0.35f), CenterColor = Theme.Lighten(c, 0.45f), SurroundColors = new[] { c } })
                        g.FillPath(b, path);
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    var stripe = EggColors[(i + 1) % EggColors.Length];
                    using (var pen = new Pen(Theme.Darken(stripe, 0.1f), Math.Max(1.2f, Theme.F(1.6f))))
                    {
                        float y = er.Y + er.Height * 0.48f, step = er.Width / 5f;
                        var pts = new PointF[7];
                        for (int k = 0; k < 7; k++) pts[k] = new PointF(er.X - step + k * step, y + (k % 2 == 0 ? -Theme.F(1.6f) : Theme.F(1.6f)));
                        g.DrawLines(pen, pts);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                        for (int k = 0; k < 3; k++) g.FillEllipse(b, er.X + er.Width * (0.25f + k * 0.25f) - Theme.F(1), er.Y + er.Height * 0.72f, Theme.F(2), Theme.F(2));
                    g.Restore(st);
                }
                using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), Math.Max(1f, Theme.F(0.8f)))) g.DrawEllipse(pen, er);
            }
            g.SmoothingMode = sm;
        }

        private static void DrawGrass(Graphics g, float width, float height)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rnd = new Random(5);
            using (var p1 = new Pen(Color.FromArgb(150, 0x5a, 0xb8, 0x4a), Math.Max(1f, Theme.F(1.2f))))
            using (var p2 = new Pen(Color.FromArgb(120, 0x3e, 0x96, 0x38), Math.Max(1f, Theme.F(1f))))
                for (float x = Theme.F(4); x < width; x += Theme.F(9) + (float)rnd.NextDouble() * Theme.F(14))
                {
                    float h = Theme.F(4) + (float)rnd.NextDouble() * Theme.F(5);
                    g.DrawBezier(rnd.Next(2) == 0 ? p1 : p2, x, height, x, height - h * 0.5f, x + Theme.F(1.5f), height - h * 0.8f, x + Theme.F(2.5f), height - h);
                }
            g.SmoothingMode = sm;
        }

        // ---------------------------------------------------------------- Fruehling

        private static readonly Color[] PetalColors = { Color.FromArgb(0xff, 0xb3, 0xd1), Color.FromArgb(0xc9, 0xa8, 0xff), Color.FromArgb(0xff, 0xff, 0xff), Color.FromArgb(0xff, 0xd0, 0x7a) };

        private static void DrawFlowers(Graphics g, float width, float height)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double t = Calm ? 0 : Now;
            float span = Theme.F(64);
            int i = 0;
            for (float x = Theme.F(30); x < width - Theme.F(10); x += span, i++)
            {
                float sway = (float)Math.Sin(t * 1.6 + i * 1.3) * Theme.F(1.6f);
                float stemTop = height - Theme.F(9);
                using (var pen = new Pen(Color.FromArgb(170, 0x5a, 0xa8, 0x4a), Math.Max(1f, Theme.F(1.1f))))
                    g.DrawLine(pen, x, height, x + sway, stemTop);
                float cx = x + sway, cy = stemTop - Theme.F(1);
                var col = PetalColors[i % PetalColors.Length];
                float pr = Theme.F(2.1f);
                using (var b = new SolidBrush(Color.FromArgb(220, col)))
                    for (int k = 0; k < 5; k++)
                    {
                        double a = k * Math.PI * 2 / 5 + t * 0.2;
                        g.FillEllipse(b, cx + (float)Math.Cos(a) * pr * 1.1f - pr, cy + (float)Math.Sin(a) * pr * 1.1f - pr, pr * 2, pr * 2);
                    }
                using (var b = new SolidBrush(Color.FromArgb(0xff, 0xd2, 0x3a))) g.FillEllipse(b, cx - pr * 0.7f, cy - pr * 0.7f, pr * 1.4f, pr * 1.4f);
            }
            g.SmoothingMode = sm;
        }

        // ---------------------------------------------------------------- Sommer

        private static void DrawSun(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, core = r.Width * 0.27f;
            double rot = Calm ? 0 : Now * 0.35;
            using (var glow = new SolidBrush(Color.FromArgb(40, 0xff, 0xc8, 0x3a))) g.FillEllipse(glow, cx - core * 1.9f, cy - core * 1.9f, core * 3.8f, core * 3.8f);
            using (var pen = new Pen(Color.FromArgb(0xff, 0xc4, 0x2e), Math.Max(1.2f, Theme.F(1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int k = 0; k < 10; k++)
                {
                    double a = rot + k * Math.PI / 5;
                    float r1 = core * 1.35f, r2 = core * (k % 2 == 0 ? 1.85f : 1.6f);
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
                }
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - core, cy - core, core * 2, core * 2);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0xff, 0xf2, 0x9a), SurroundColors = new[] { Color.FromArgb(0xff, 0xb8, 0x24) } })
                    g.FillPath(b, path);
            }
            g.SmoothingMode = sm;
        }

        public static Rectangle SunBounds(RectangleF r) => Rectangle.Inflate(Rectangle.Ceiling(r), Theme.Px(6), Theme.Px(6));

        // ---------------------------------------------------------------- Geburtstag

        private static void DrawCake(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(r.X + r.Width * 0.08f, r.Y + r.Height * 0.5f, r.Width * 0.84f, r.Height * 0.46f);
            Theme.FillRound(g, Color.FromArgb(0xf3, 0xd9, 0xb1), body, Theme.F(2));
            using (var b = new SolidBrush(Color.FromArgb(0xff, 0x8a, 0xb8)))
            {
                g.FillRectangle(b, body.X, body.Y, body.Width, body.Height * 0.3f);
                for (int k = 0; k < 4; k++) g.FillEllipse(b, body.X + k * body.Width / 4f, body.Y + body.Height * 0.15f, body.Width / 4f, body.Height * 0.32f);
            }
            using (var b = new SolidBrush(Color.FromArgb(0xa8, 0x5a, 0x34))) g.FillRectangle(b, body.X, body.Bottom - body.Height * 0.22f, body.Width, body.Height * 0.12f);
            // Kerze
            var candle = new RectangleF(r.X + r.Width / 2f - Theme.F(1.4f), r.Y + r.Height * 0.22f, Theme.F(2.8f), r.Height * 0.3f);
            using (var b = new SolidBrush(Color.FromArgb(0x7a, 0xc8, 0xff))) g.FillRectangle(b, candle);
            double t = Calm ? 0 : Now;
            float fl = 1f + 0.18f * (float)Math.Sin(t * 13) + 0.1f * (float)Math.Sin(t * 29);
            float fw = Theme.F(3.2f), fh = Theme.F(5.5f) * fl;
            var flame = new RectangleF(candle.X + candle.Width / 2f - fw / 2f, candle.Y - fh - Theme.F(0.5f), fw, fh);
            using (var b = new SolidBrush(Color.FromArgb(70, 0xff, 0xc8, 0x40))) g.FillEllipse(b, RectangleF.Inflate(flame, Theme.F(2.5f), Theme.F(2)));
            using (var b = new SolidBrush(Color.FromArgb(0xff, 0xb0, 0x30))) g.FillEllipse(b, flame);
            using (var b = new SolidBrush(Color.FromArgb(0xff, 0xf4, 0xb0))) g.FillEllipse(b, flame.X + fw * 0.25f, flame.Y + fh * 0.35f, fw * 0.5f, fh * 0.55f);
            g.SmoothingMode = sm;
        }

        public static Rectangle CakeBounds(RectangleF r) => Rectangle.Inflate(Rectangle.Ceiling(r), Theme.Px(4), Theme.Px(4));

        // ---------------------------------------------------------------- Herbstblaetter und Konfetti

        private static readonly Color[] LeafColors = { Color.FromArgb(0xe0, 0x6a, 0x1e), Color.FromArgb(0xc8, 0x3a, 0x22), Color.FromArgb(0xd8, 0xa0, 0x2a), Color.FromArgb(0x9a, 0x5a, 0x2a) };
        private static readonly Color[] ConfettiColors = { Color.FromArgb(0xff, 0x5a, 0x7a), Color.FromArgb(0x5a, 0xc8, 0xff), Color.FromArgb(0xff, 0xd2, 0x3a), Color.FromArgb(0x7a, 0xe0, 0x6a), Color.FromArgb(0xc0, 0x8a, 0xff) };

        /// <summary>Fallende Teilchen (Blaetter bzw. Konfetti): Position nur aus der Zeit berechnet, kein Zustand.</summary>
        private static void DrawParticles(Graphics g, RectangleF area, bool confetti)
        {
            if (area.Width < Theme.F(30)) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            double t = Calm ? 3.3 : Now;
            int n = confetti ? Math.Max(10, (int)(area.Width / Theme.F(22))) : Math.Max(5, (int)(area.Width / Theme.F(55)));
            for (int i = 0; i < n; i++)
            {
                var rnd = new Random(1000 + i * 7919);
                double speed = confetti ? 0.18 + rnd.NextDouble() * 0.2 : 0.08 + rnd.NextDouble() * 0.07;   // Hoehen je Sekunde
                double ph = rnd.NextDouble();
                double u = (t * speed + ph) % 1.0;
                float x0 = area.X + (float)rnd.NextDouble() * area.Width;
                float x = x0 + (float)Math.Sin(t * (confetti ? 2.2 : 0.9) + i) * Theme.F(confetti ? 5 : 10);
                float y = area.Y - Theme.F(6) + (float)u * (area.Height + Theme.F(12));
                double rot = t * (confetti ? 4 : 1.4) + i;
                var state = g.Save();
                g.TranslateTransform(x, y);
                g.RotateTransform((float)(rot * 180 / Math.PI));
                if (confetti)
                {
                    var c = ConfettiColors[i % ConfettiColors.Length];
                    float w = Theme.F(3.2f), h = Theme.F(1.6f) * (0.4f + 0.6f * (float)Math.Abs(Math.Cos(rot * 1.7)));
                    using (var b = new SolidBrush(c)) g.FillRectangle(b, -w / 2f, -h / 2f, w, h);
                }
                else
                {
                    var c = LeafColors[i % LeafColors.Length];
                    float L = Theme.F(5.5f);
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(-L, 0, -L * 0.3f, -L * 0.55f, L * 0.4f, -L * 0.5f, L, 0);
                        path.AddBezier(L, 0, L * 0.4f, L * 0.5f, -L * 0.3f, L * 0.55f, -L, 0);
                        using (var b = new SolidBrush(Color.FromArgb(220, c))) g.FillPath(b, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(150, Theme.Darken(c, 0.35f)), Math.Max(0.8f, Theme.F(0.7f)))) g.DrawLine(pen, -L, 0, L * 0.9f, 0);
                }
                g.Restore(state);
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
