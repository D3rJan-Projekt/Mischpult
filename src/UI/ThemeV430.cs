using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.3 Optik: drei Designs (Steampunk, Arktis, Japan) mit eigenem Hintergrund. Bewegte Hintergruende, Waende und
    /// der Plattenspieler stehen in ThemeV430b.cs.
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleSteam = 18, StyleArctic = 19, StyleJapan = 20;
        public static bool SteamDesign => Design == StyleSteam;
        public static bool ArcticDesign => Design == StyleArctic;
        public static bool JapanDesign => Design == StyleJapan;
        /// <summary>Die 4.3-Designs bringen alle einen eigenen Hintergrund mit.</summary>
        public static bool DesignBg430 => SteamDesign || ArcticDesign || JapanDesign;

        public static readonly Color SteamCopper = Color.FromArgb(0xc8, 0x76, 0x3a), SteamBrass = Color.FromArgb(0xe0, 0xb2, 0x5a);
        public static readonly Color ArcticIce = Color.FromArgb(0x9c, 0xe4, 0xff), ArcticDeep = Color.FromArgb(0x1c, 0x5c, 0x8c);
        public static readonly Color JapanRed = Color.FromArgb(0xd2, 0x3c, 0x28), JapanGold = Color.FromArgb(0xd8, 0xb0, 0x5c);

        private static void Designs430(int scheme)
        {
            if (SteamDesign) SteamColors(scheme);
            else if (ArcticDesign) ArcticColors(scheme);
            else if (JapanDesign) JapanColors(scheme);
        }

        /// <summary>Steampunk: Kupfer und Messing auf dunkelbraunem Grund, Nixie-Anzeigen, Nieten.</summary>
        private static void SteamColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x120c08); top = C(0x4a3220); bottom = C(0x342216); break;
                case 2: body = C(0x22160e); top = C(0x664530); bottom = C(0x4a3122); break;
                default: body = C(0x181009); top = C(0x5a3c26); bottom = C(0x3e2918); break;
            }
            Body = body; WindowTop = Lighten(body, 0.06f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Mix(top, SteamBrass, 0.45f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xfa, 0xec, 0xd2); TextDim = C(0xe0, 0xc8, 0xa0); TextFaint = C(0xb0, 0x96, 0x70);
            MenuBg = Darken(bottom, 0.25f); MenuHover = Mix(MenuBg, SteamCopper, 0.25f); MenuBorder = Mix(MenuBg, SteamBrass, 0.35f); MenuSep = Lighten(MenuBg, 0.12f); MenuSepLight = MenuBg;
            KeyTop = C(0x4e, 0x36, 0x24); KeyBottom = C(0x30, 0x20, 0x14); KeyHoverTop = C(0x62, 0x44, 0x2e); KeyHoverBottom = C(0x3c, 0x28, 0x1a); KeyEdge = C(0x10, 0x09, 0x05);
            Well = Darken(bottom, 0.5f); WellHover = Darken(bottom, 0.35f); WellEdge = Darken(body, 0.4f);
            BadgeBg = Darken(bottom, 0.3f); BadgeEdge = SteamBrass; LogoCap = SteamBrass;
            ScaleTick = C(0xd8, 0xb8, 0x88); ScaleLabel = C(0xf4, 0xe0, 0xbc); EngraveText = C(0xd0, 0xb0, 0x80);
            DispBg = Darken(bottom, 0.55f); DispHover = Darken(bottom, 0.4f); DispEdgeHover = SteamBrass; ScrollThumbHover = SteamCopper;
            Primary = C(0x8a, 0x52, 0x24); PrimaryHover = C(0xa0, 0x62, 0x2c);
            LcdBg = C(0x0c, 0x05, 0x02); LcdText = C(0xff, 0x8c, 0x2c); ValueText = SteamBrass;
        }

        /// <summary>Arktis: eisblau und hell, dunkelblaue Schrift, Schnee auf den Zuegen, Glas-Kappen wie Eis.</summary>
        private static void ArcticColors(int scheme)
        {
            Color body, top, bottom, edge;
            switch (scheme)
            {
                case 1: body = C(0x94b4ce); top = C(0xd6eaf8); bottom = C(0xb4d0e6); edge = C(0x6888a6); break;
                case 2: body = C(0xa6bccc); top = C(0xe0eef6); bottom = C(0xc4d8e6); edge = C(0x7894aa); break;
                default: body = C(0x9fbcd4); top = C(0xdcedf9); bottom = C(0xbcd6ea); edge = C(0x6f90ac); break;
            }
            LightFrom(body, top, bottom, edge);
            Text = C(0x0e, 0x22, 0x38); TextDim = C(0x2e, 0x4a, 0x66); TextFaint = C(0x5a, 0x76, 0x92); ButtonText = Text;
            ScaleLabel = C(0x3a, 0x56, 0x72); ScaleTick = C(0x6a, 0x88, 0xa6); EngraveText = Darken(bottom, 0.3f);
            LogoCap = ArcticDeep; BadgeEdge = Mix(BadgeEdge, ArcticDeep, 0.3f);
            Primary = C(0x2a, 0x78, 0xb0); PrimaryHover = C(0x34, 0x8c, 0xc8);
            LcdBg = C(0x06, 0x14, 0x24); LcdText = ArcticIce; ValueText = ArcticDeep;
            DispEdgeHover = ArcticDeep; ScrollThumbHover = ArcticDeep;
        }

        /// <summary>Japan: schwarz-roter Lack, Gold-Beschlaege an den Ecken, Wellenmuster und Kirschblueten im Hintergrund.</summary>
        private static void JapanColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x080505); top = C(0x241210); bottom = C(0x140909); break;
                case 2: body = C(0x140c0a); top = C(0x3a1c18); bottom = C(0x241210); break;
                default: body = C(0x0c0707); top = C(0x2e1613); bottom = C(0x1a0c0a); break;
            }
            Body = body; WindowTop = Lighten(body, 0.05f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.2f); PanelEdge = C(0x02, 0x01, 0x01);
            Text = C(0xfa, 0xf2, 0xe6); TextDim = C(0xe0, 0xd0, 0xbc); TextFaint = C(0xa8, 0x94, 0x80);
            MenuBg = Darken(bottom, 0.2f); MenuHover = Mix(MenuBg, JapanRed, 0.25f); MenuBorder = Mix(MenuBg, JapanGold, 0.35f); MenuSep = Lighten(MenuBg, 0.12f); MenuSepLight = MenuBg;
            KeyTop = C(0x2a, 0x14, 0x12); KeyBottom = C(0x16, 0x0a, 0x09); KeyHoverTop = C(0x3a, 0x1c, 0x18); KeyHoverBottom = C(0x20, 0x0e, 0x0c); KeyEdge = C(0x04, 0x02, 0x02);
            Well = Darken(bottom, 0.55f); WellHover = Darken(bottom, 0.4f); WellEdge = Darken(body, 0.4f);
            BadgeBg = Darken(bottom, 0.3f); BadgeEdge = JapanGold; LogoCap = JapanRed;
            ScaleTick = C(0xc8, 0xa8, 0x70); ScaleLabel = C(0xf0, 0xe0, 0xc4); EngraveText = C(0xc0, 0xa0, 0x70);
            DispBg = Darken(bottom, 0.55f); DispHover = Darken(bottom, 0.4f); DispEdgeHover = JapanGold; ScrollThumbHover = JapanRed;
            Primary = C(0xa8, 0x30, 0x22); PrimaryHover = C(0xc0, 0x3a, 0x28);
            LcdBg = C(0x0a, 0x03, 0x02); LcdText = C(0xff, 0x6a, 0x48); ValueText = JapanGold;
        }

        // ================================================================ Zuege: Deko je Design

        private static void PanelDeco430(Graphics g, RectangleF r, float rad, Color? tint)
        {
            if (r.Width < F(14) || r.Height < F(14)) return;
            if (SteamDesign) SteamDeco(g, r, rad);
            else if (ArcticDesign) ArcticDeco(g, r, rad);
            else if (JapanDesign) JapanDeco(g, r, rad);
        }

        /// <summary>Steampunk: Kupfer-Linie innen und Messing-Nieten an den Seiten.</summary>
        private static void SteamDeco(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inset = RectangleF.Inflate(r, -F(2.2f), -F(2.2f));
            StrokeRound(g, Color.FromArgb(130, SteamCopper), Math.Max(1f, F(1f)), inset, Math.Max(1f, rad - F(2)));
            float m = F(3.2f), rr = Math.Max(1.3f, F(2.0f));
            if (r.Height >= F(60))
            {
                float y0 = r.Y + F(14), y1 = r.Bottom - F(14);
                int n = Math.Max(1, (int)((y1 - y0) / F(40)));
                for (int i = 0; i <= n; i++)
                {
                    float y = y0 + (y1 - y0) * i / n;
                    Rivet(g, r.X + m, y, rr);
                    Rivet(g, r.Right - m, y, rr);
                }
            }
            else
            {
                Rivet(g, r.X + m, r.Y + m, rr); Rivet(g, r.Right - m, r.Y + m, rr);
                Rivet(g, r.X + m, r.Bottom - m, rr); Rivet(g, r.Right - m, r.Bottom - m, rr);
            }
            g.SmoothingMode = sm;
        }

        private static void Rivet(Graphics g, float x, float y, float rr)
        {
            FillEllipse(g, Color.FromArgb(120, 0, 0, 0), x + F(0.4f), y + F(0.6f), rr + F(0.4f));
            var rc = new RectangleF(x - rr, y - rr, rr * 2, rr * 2);
            using (var b = new LinearGradientBrush(RectangleF.Inflate(rc, 0.5f, 0.5f), C(0xf4, 0xd0, 0x88), C(0x6a, 0x40, 0x18), 45f)) g.FillEllipse(b, rc);
            FillEllipse(g, Color.FromArgb(160, 255, 244, 214), x - rr * 0.35f, y - rr * 0.35f, rr * 0.33f);
        }

        /// <summary>Arktis: frostiger Rand und Eiskristalle an den Seiten (der Schnee liegt auf der farbigen Kante, ArcticSnow).</summary>
        private static void ArcticDeco(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            StrokeRound(g, Color.FromArgb(150, 255, 255, 255), Math.Max(1f, F(1.1f)), RectangleF.Inflate(r, -F(1.6f), -F(1.6f)), Math.Max(1f, rad - F(1.6f)));
            // Eiskristalle an den Seiten
            if (r.Height >= F(120))
                foreach (float fy in new[] { 0.42f, 0.71f })
                {
                    Flake(g, new PointF(r.X + F(3.2f), r.Y + r.Height * fy), F(2.4f), Color.FromArgb(170, 0x5c, 0x9c, 0xcc));
                    Flake(g, new PointF(r.Right - F(3.2f), r.Y + r.Height * (fy + 0.06f)), F(2.4f), Color.FromArgb(170, 0x5c, 0x9c, 0xcc));
                }
            g.SmoothingMode = sm;
        }

        /// <summary>Arktis: Schnee liegt in zwei Haufen oben auf der farbigen Kante, darunter haengen kleine Eiszapfen.</summary>
        public static void ArcticSnow(Graphics g, RectangleF r, float rad)
        {
            if (!ArcticDesign || r.Width < F(30)) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float inner = r.Width - rad * 1.2f;
            var lumps = r.Width < F(70) ? new[] { (0.12f, 0.62f) } : new[] { (0.04f, 0.42f), (0.56f, 0.86f) };
            int seed = (int)(r.Width * 7 + r.Height);
            foreach (var (fa, fb) in lumps)
            {
                float x0 = r.X + rad * 0.6f + inner * fa, x1 = r.X + rad * 0.6f + inner * fb;
                float top = r.Y + F(0.2f), thick = F(6.5f);
                var upper = new List<PointF>();
                int n = Math.Max(3, (int)((x1 - x0) / Math.Max(2f, F(4))));
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n;
                    float hump = (float)Math.Sin(Math.PI * u);
                    hump = (float)Math.Pow(hump, 0.45);
                    uint h = (uint)((i + seed) * 2654435761u); h ^= h >> 13;
                    upper.Add(new PointF(x0 + (x1 - x0) * u, top + thick * (1 - hump) - (h % 3) * F(0.2f) * hump));
                }
                float baseY = top + thick;
                using (var path = new GraphicsPath())
                {
                    path.AddCurve(upper.ToArray(), 0.5f);
                    path.AddLine(x1, baseY, x0, baseY);
                    path.CloseFigure();
                    var st = g.Save();
                    g.TranslateTransform(0, F(0.8f));
                    using (var b = new SolidBrush(Color.FromArgb(70, ArcticDeep))) g.FillPath(b, path);
                    g.Restore(st);
                    using (var b = new LinearGradientBrush(new RectangleF(x0, top - 1, x1 - x0, thick + 2), Color.White, C(0xd8, 0xec, 0xfa), LinearGradientMode.Vertical)) g.FillPath(b, path);
                }
                // Eiszapfen unter dem Haufen
                for (int k = 0; k < 2; k++)
                {
                    float fx = x0 + (x1 - x0) * (0.3f + 0.38f * k + ((seed + k * 7) % 5) * 0.02f);
                    float len = F(4.5f + (seed + k * 3) % 4), wdt = F(1.7f), ty = baseY - F(0.6f);
                    var pts = new[] { new PointF(fx - wdt, ty), new PointF(fx + wdt, ty), new PointF(fx + F(0.2f), ty + len) };
                    using (var b = new LinearGradientBrush(new RectangleF(fx - wdt, ty - 1, wdt * 2, len + 2), Color.FromArgb(250, 255, 255, 255), Color.FromArgb(230, 0x6c, 0xb8, 0xe8), LinearGradientMode.Vertical)) g.FillPolygon(b, pts);
                    using (var p = new Pen(Color.FromArgb(110, ArcticDeep), Math.Max(0.8f, F(0.6f)))) g.DrawLines(p, new[] { pts[0], pts[2], pts[1] });
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Kleiner sechsarmiger Eiskristall.</summary>
        private static void Flake(Graphics g, PointF c, float r, Color col)
        {
            using (var p = new Pen(col, Math.Max(1f, F(0.7f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3 + Math.PI / 6;
                    float ex = c.X + (float)Math.Cos(a) * r, ey = c.Y + (float)Math.Sin(a) * r;
                    g.DrawLine(p, c.X, c.Y, ex, ey);
                    float mx = c.X + (float)Math.Cos(a) * r * 0.55f, my = c.Y + (float)Math.Sin(a) * r * 0.55f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        double b = a + s * 0.7;
                        g.DrawLine(p, mx, my, mx + (float)Math.Cos(b) * r * 0.35f, my + (float)Math.Sin(b) * r * 0.35f);
                    }
                }
        }

        /// <summary>Japan: Lack-Glanz, feine Gold-Linie und Gold-Beschlaege an allen vier Ecken (wie an einer Tansu-Kommode).</summary>
        private static void JapanDeco(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, rad))
            using (var b = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Color.Black, Color.Black, 58f))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(24, 255, 236, 220), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                    Positions = new[] { 0f, 0.14f, 0.3f, 1f },
                };
                g.FillPath(b, path);
            }
            StrokeRound(g, Color.FromArgb(120, JapanGold), Math.Max(1f, F(0.8f)), RectangleF.Inflate(r, -F(4.2f), -F(4.2f)), Math.Max(1f, rad - F(4)));
            for (int k = 0; k < 4; k++) JapanCorner(g, r, rad, k);
            g.SmoothingMode = sm;
        }

        private static void JapanCorner(Graphics g, RectangleF r, float rad, int corner)
        {
            var st = g.Save();
            g.TranslateTransform(corner % 2 == 0 ? r.X : r.Right, corner < 2 ? r.Y : r.Bottom);
            g.ScaleTransform(corner % 2 == 0 ? 1 : -1, corner < 2 ? 1 : -1);
            float m = F(2.4f), L = Math.Min(F(13), Math.Min(r.Width, r.Height) * 0.22f), cr = Math.Max(F(1.5f), rad - m);
            using (var path = new GraphicsPath())
            {
                path.AddLine(m + L, m, m + cr, m);
                path.AddArc(m, m, cr * 2, cr * 2, 270, -90);
                path.AddLine(m, m + cr, m, m + L);
                using (var p = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(2f, F(2.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var st2 = g.Save();
                    g.TranslateTransform(F(0.4f), F(0.6f));
                    g.DrawPath(p, path);
                    g.Restore(st2);
                }
                using (var p = new Pen(JapanGold, Math.Max(1.5f, F(2.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, path);
                using (var p = new Pen(Color.FromArgb(160, 255, 244, 210), Math.Max(1f, F(0.6f)))) g.DrawPath(p, path);
            }
            FillEllipse(g, Darken(JapanGold, 0.45f), m + L, m, F(1.1f));
            FillEllipse(g, Darken(JapanGold, 0.45f), m, m + L, F(1.1f));
            g.Restore(st);
        }

        // ================================================================ Hintergrund der Designs

        private static Bitmap MakeDesignBg430(int w, int h) => SteamDesign ? MakeSteamBg(w, h) : ArcticDesign ? MakeArcticBg(w, h) : MakeJapanBg(w, h);

        /// <summary>Steampunk: dunkles Braun, grosse Messing-Zahnraeder und Kupferrohre, stark abgedunkelt.</summary>
        public static Bitmap MakeSteamBg(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-w * 0.3f, -h * 0.3f, w * 1.6f, h * 1.6f);
                    using (var pb = new PathGradientBrush(path) { CenterColor = C(0x2c, 0x1c, 0x10), SurroundColors = new[] { C(0x0a, 0x06, 0x03) } })
                        g.FillRectangle(pb, 0, 0, w, h);
                }
                float u = Math.Min(w, h);
                // Zahnraeder (Lage relativ zum Fenster)
                var gears = new[]
                {
                    new { X = 0.08f, Y = 0.18f, R = 0.26f, T = 22 }, new { X = 0.30f, Y = 0.05f, R = 0.14f, T = 14 },
                    new { X = 0.92f, Y = 0.82f, R = 0.30f, T = 26 }, new { X = 0.70f, Y = 0.97f, R = 0.15f, T = 14 },
                    new { X = 0.62f, Y = 0.12f, R = 0.10f, T = 12 }, new { X = 0.20f, Y = 0.86f, R = 0.12f, T = 12 },
                };
                for (int i = 0; i < gears.Length; i++)
                {
                    var gr = gears[i];
                    Gear(g, new PointF(w * gr.X, h * gr.Y), u * gr.R, gr.T, i * 0.21f, i % 2 == 0 ? SteamBrass : SteamCopper);
                }
                // Kupferrohre mit Flanschen
                float pw = Math.Max(6f, F(11));
                Pipe(g, new RectangleF(0, h * 0.62f, w, pw), true);
                Pipe(g, new RectangleF(w * 0.46f, 0, pw, h * 0.62f + pw), false);
                Pipe(g, new RectangleF(0, h * 0.94f, w * 0.55f, pw * 0.8f), true);
                // Druckanzeige am Rohr
                var gc = new PointF(w * 0.46f + pw / 2f, h * 0.3f);
                float gr2 = Math.Max(14f, F(24));
                using (var b = new LinearGradientBrush(new RectangleF(gc.X - gr2, gc.Y - gr2, gr2 * 2, gr2 * 2), SteamBrass, Darken(SteamCopper, 0.4f), 45f)) g.FillEllipse(b, gc.X - gr2, gc.Y - gr2, gr2 * 2, gr2 * 2);
                using (var b = new SolidBrush(C(0xe8, 0xdc, 0xc0))) g.FillEllipse(b, gc.X - gr2 * 0.8f, gc.Y - gr2 * 0.8f, gr2 * 1.6f, gr2 * 1.6f);
                using (var p = new Pen(C(0x3a, 0x2a, 0x1a), Math.Max(1f, F(1))))
                    for (int k = 0; k <= 8; k++)
                    {
                        double a = Math.PI * (0.75 + k * 1.5 / 8);
                        g.DrawLine(p, gc.X + (float)Math.Cos(a) * gr2 * 0.62f, gc.Y + (float)Math.Sin(a) * gr2 * 0.62f, gc.X + (float)Math.Cos(a) * gr2 * 0.76f, gc.Y + (float)Math.Sin(a) * gr2 * 0.76f);
                    }
                using (var p = new Pen(C(0xa0, 0x20, 0x10), Math.Max(1.2f, F(1.4f)))) g.DrawLine(p, gc.X, gc.Y, gc.X + gr2 * 0.55f, gc.Y - gr2 * 0.3f);
                using (var b = new SolidBrush(Color.FromArgb(130, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);
            }
            return bmp;
        }

        private static void Gear(Graphics g, PointF c, float r, int teeth, float rot, Color col)
        {
            var pts = new List<PointF>();
            float ri = r * 0.86f;
            for (int i = 0; i < teeth; i++)
            {
                double a0 = (i / (double)teeth) * Math.PI * 2 + rot, da = Math.PI * 2 / teeth;
                foreach (var (f, rr) in new[] { (0.0, ri), (0.12, r), (0.45, r), (0.57, ri) })
                {
                    double a = a0 + da * f;
                    pts.Add(new PointF(c.X + (float)Math.Cos(a) * rr, c.Y + (float)Math.Sin(a) * rr));
                }
            }
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts.ToArray());
                using (var b = new SolidBrush(Color.FromArgb(70, col))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(150, Lighten(col, 0.2f)), Math.Max(1f, F(1.4f)))) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(90, 0x0a, 0x06, 0x03))) g.FillEllipse(b, c.X - r * 0.62f, c.Y - r * 0.62f, r * 1.24f, r * 1.24f);
            using (var p = new Pen(Color.FromArgb(150, col), Math.Max(1.5f, r * 0.08f)))
            {
                g.DrawEllipse(p, c.X - r * 0.62f, c.Y - r * 0.62f, r * 1.24f, r * 1.24f);
                for (int k = 0; k < 5; k++)
                {
                    double a = rot + k * Math.PI * 2 / 5;
                    g.DrawLine(p, c.X + (float)Math.Cos(a) * r * 0.16f, c.Y + (float)Math.Sin(a) * r * 0.16f, c.X + (float)Math.Cos(a) * r * 0.6f, c.Y + (float)Math.Sin(a) * r * 0.6f);
                }
            }
            using (var b = new SolidBrush(Color.FromArgb(170, col))) g.FillEllipse(b, c.X - r * 0.17f, c.Y - r * 0.17f, r * 0.34f, r * 0.34f);
        }

        private static void Pipe(Graphics g, RectangleF r, bool horizontal)
        {
            using (var b = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Lighten(SteamCopper, 0.25f), Darken(SteamCopper, 0.55f), horizontal ? 90f : 0f))
            {
                b.SetBlendTriangularShape(0.35f);
                g.FillRectangle(b, r);
            }
            float len = horizontal ? r.Width : r.Height, th = horizontal ? r.Height : r.Width;
            for (float d = th * 6; d < len - th; d += th * 14)
            {
                var fr = horizontal ? new RectangleF(r.X + d, r.Y - th * 0.25f, th * 0.8f, th * 1.5f) : new RectangleF(r.X - th * 0.25f, r.Y + d, th * 1.5f, th * 0.8f);
                using (var b = new LinearGradientBrush(RectangleF.Inflate(fr, 1, 1), Lighten(SteamBrass, 0.2f), Darken(SteamBrass, 0.5f), horizontal ? 90f : 0f)) g.FillRectangle(b, fr);
            }
        }

        /// <summary>Arktis: heller Himmel, ferne Eisberge und Schollen, ein paar glitzernde Punkte.</summary>
        public static Bitmap MakeArcticBg(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var top = Darken(Body, 0.12f);
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, w, h + 2), top, Lighten(Body, 0.35f), LinearGradientMode.Vertical)) g.FillRectangle(b, 0, 0, w, h);
                var rnd = new Random(19);
                // ferne Berge
                for (int layer = 0; layer < 2; layer++)
                {
                    float baseY = h * (0.58f + layer * 0.12f);
                    var pts = new List<PointF> { new PointF(0, h) };
                    float x = 0;
                    while (x < w + F(60))
                    {
                        pts.Add(new PointF(x, baseY - (float)(rnd.NextDouble() * h * (layer == 0 ? 0.22 : 0.12))));
                        x += F(60) + (float)rnd.NextDouble() * F(110);
                    }
                    pts.Add(new PointF(w, h));
                    Color c = layer == 0 ? Mix(Body, Color.White, 0.25f) : Mix(Body, Color.White, 0.55f);
                    using (var b = new SolidBrush(Color.FromArgb(layer == 0 ? 170 : 200, c))) g.FillPolygon(b, pts.ToArray());
                    using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, F(1.2f)))) g.DrawLines(p, pts.GetRange(1, pts.Count - 2).ToArray());
                }
                // Eisschollen vorne
                for (int i = 0; i < 14; i++)
                {
                    float fw = F(80) + (float)rnd.NextDouble() * F(160), fh = fw * 0.12f;
                    float fx = (float)rnd.NextDouble() * w - fw / 2, fy = h * 0.86f + (float)rnd.NextDouble() * h * 0.12f;
                    using (var b = new SolidBrush(Color.FromArgb(90, ArcticDeep))) g.FillEllipse(b, fx, fy + fh * 0.4f, fw, fh);
                    using (var b = new SolidBrush(Color.FromArgb(220, 0xf4, 0xfa, 0xff))) g.FillEllipse(b, fx, fy, fw, fh);
                }
                // Glitzern
                for (int i = 0; i < Math.Max(30, w * h / 14000); i++)
                {
                    float sx = (float)(rnd.NextDouble() * w), sy = (float)(rnd.NextDouble() * h);
                    float s = F(1f) + (float)rnd.NextDouble() * F(1.5f);
                    using (var b = new SolidBrush(Color.FromArgb(rnd.Next(90, 220), 255, 255, 255))) g.FillEllipse(b, sx - s / 2, sy - s / 2, s, s);
                }
            }
            return bmp;
        }

        /// <summary>Japan: schwarz-roter Lack, goldenes Wellenmuster (Seigaiha) unten, ein Kirschzweig oben rechts.</summary>
        public static Bitmap MakeJapanBg(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color c0 = C(0x1c, 0x0a, 0x08), c1 = C(0x08, 0x03, 0x03);
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, w, h + 2), c0, c1, LinearGradientMode.Vertical)) g.FillRectangle(b, 0, 0, w, h);
                // Seigaiha: Reihen aus Halbkreisen, nach oben auslaufend
                float R = Math.Max(14f, F(26));
                float yStart = h * 0.42f;
                int row = 0;
                for (float y = yStart; y < h + R; y += R * 0.5f, row++)
                {
                    float fade = Math.Min(1f, (y - yStart) / (h * 0.4f));
                    Color bg = Mix(c0, c1, Math.Max(0f, Math.Min(1f, y / h)));
                    for (float x = row % 2 == 0 ? 0 : R; x < w + R; x += R * 2)
                    {
                        using (var b = new SolidBrush(bg)) g.FillEllipse(b, x - R, y - R, R * 2, R * 2);
                        for (int k = 0; k < 4; k++)
                        {
                            float rr = R * (1f - k * 0.22f);
                            using (var p = new Pen(Color.FromArgb((int)(22 + 58 * fade * (k == 0 ? 1f : 0.7f)), JapanGold), Math.Max(1f, F(1.1f)))) g.DrawEllipse(p, x - rr, y - rr, rr * 2, rr * 2);
                        }
                    }
                }
                // Kirschzweig oben rechts
                var rnd = new Random(20);
                float by = h * 0.24f;   // unter der Meterbridge, damit er zwischen den Zuegen zu sehen ist
                var branch = new[] { new PointF(w + F(10), by), new PointF(w - F(120), by + h * 0.10f), new PointF(w - F(230), by + h * 0.08f), new PointF(w - F(330), by + h * 0.17f) };
                using (var p = new Pen(C(0x2a, 0x14, 0x10), Math.Max(3f, F(6))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, branch, 0.5f);
                using (var p = new Pen(C(0x2a, 0x14, 0x10), Math.Max(2f, F(3))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawCurve(p, new[] { new PointF(w - F(120), by + h * 0.10f), new PointF(w - F(160), by + h * 0.17f), new PointF(w - F(190), by + h * 0.2f) }, 0.5f);
                    g.DrawCurve(p, new[] { new PointF(w - F(230), by + h * 0.08f), new PointF(w - F(260), by + h * 0.02f), new PointF(w - F(300), by) }, 0.5f);
                }
                var spots = new[] { (0.0f, 0.0f), (-120f, 0.10f), (-160f, 0.17f), (-190f, 0.2f), (-230f, 0.08f), (-260f, 0.02f), (-300f, 0.0f), (-330f, 0.17f), (-60f, 0.06f), (-280f, 0.12f) };
                foreach (var (dx, fy) in spots)
                    for (int k = 0; k < 2; k++)
                        Blossom(g, new PointF(w + F(dx) + (float)(rnd.NextDouble() - 0.5) * F(26), by + h * fy + (float)(rnd.NextDouble() - 0.5) * F(22)), F(6 + rnd.Next(4)), (float)rnd.NextDouble() * 72f, 200);
                // fallende Blaetter
                for (int i = 0; i < 16; i++)
                {
                    float px = w - (float)rnd.NextDouble() * w * 0.55f, py = by + h * 0.1f + (float)rnd.NextDouble() * h * 0.4f;
                    var st = g.Save();
                    g.TranslateTransform(px, py);
                    g.RotateTransform((float)rnd.NextDouble() * 360f);
                    using (var b = new SolidBrush(Color.FromArgb(rnd.Next(90, 170), 0xf4, 0xb6, 0xc8))) g.FillEllipse(b, -F(3), -F(1.6f), F(6), F(3.2f));
                    g.Restore(st);
                }
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);
            }
            return bmp;
        }

        private static void Blossom(Graphics g, PointF c, float r, float rot, int alpha)
        {
            for (int i = 0; i < 5; i++)
            {
                var st = g.Save();
                g.TranslateTransform(c.X, c.Y);
                g.RotateTransform(rot + i * 72f);
                using (var b = new SolidBrush(Color.FromArgb(alpha, 0xf8, 0xc4, 0xd2))) g.FillEllipse(b, -r * 0.32f, -r, r * 0.64f, r * 0.9f);
                g.Restore(st);
            }
            FillEllipse(g, Color.FromArgb(alpha, 0xd8, 0x5a, 0x7a), c.X, c.Y, r * 0.22f);
        }
    }
}
