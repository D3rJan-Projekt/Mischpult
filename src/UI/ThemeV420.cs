using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.2 Optik: drei Designs (Synthwave, Retro-Game, Cockpit), vier bewegte Hintergruende (Nordlicht, Unterwasser,
    /// Kaminfeuer, Matrix-Regen) und drei Waende (Samtvorhang, Sichtbeton, Kacheln).
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleSynth = 15, StyleRetro = 16, StyleCockpit = 17;
        public static bool SynthDesign => Design == StyleSynth;
        public static bool RetroDesign => Design == StyleRetro;
        public static bool CockpitDesign => Design == StyleCockpit;

        public static readonly Color SynthPink = Color.FromArgb(0xff, 0x4f, 0xd8), SynthCyan = Color.FromArgb(0x3c, 0xe6, 0xff), SynthSun = Color.FromArgb(0xff, 0x9a, 0x3c);
        public static readonly Color RetroGreen = Color.FromArgb(0x5c, 0xff, 0x6a), RetroYellow = Color.FromArgb(0xff, 0xd8, 0x3a);
        public static readonly Color CockpitAmber = Color.FromArgb(0xff, 0xb8, 0x3a), CockpitGreen = Color.FromArgb(0x7c, 0xff, 0x9a);

        private static void Designs420(int scheme)
        {
            if (SynthDesign) SynthColors(scheme);
            else if (RetroDesign) RetroColors(scheme);
            else if (CockpitDesign) CockpitColors(scheme);
        }

        /// <summary>Synthwave: tiefes Violett, Neon-Pink und Cyan, Sonnenuntergang mit Gitter im Hintergrund.</summary>
        private static void SynthColors(int scheme)
        {
            switch (scheme)
            {
                case 1: ModernFrom(C(0x0a0614), C(0x120a22), C(0x1c1032)); break;
                case 2: ModernFrom(C(0x150d22), C(0x1d1230), C(0x2a1a44)); break;
                default: ModernFrom(C(0x0e0819), C(0x170d29), C(0x22133b)); break;
            }
            PanelTop = Lighten(PanelBottom, 0.06f);
            Text = C(0xff, 0xf0, 0xfb); TextDim = C(0xd8, 0xb8, 0xe8); TextFaint = C(0x9a, 0x7c, 0xb2);
            MenuHover = Mix(MenuBg, SynthPink, 0.22f); DispEdgeHover = SynthPink; ScrollThumbHover = SynthPink;
            BadgeEdge = Mix(BadgeEdge, SynthPink, 0.5f); LogoCap = SynthPink;
            ScaleTick = C(0x9a, 0x7c, 0xc8); ScaleLabel = C(0xe0, 0xc8, 0xf0); EngraveText = C(0xa8, 0x8c, 0xc8);
            LcdBg = C(0x0a, 0x02, 0x10); LcdText = SynthCyan; ValueText = C(0xff, 0x8c, 0xe8);
            Primary = C(0xb8, 0x2c, 0x9c); PrimaryHover = C(0xd0, 0x38, 0xb4);
        }

        /// <summary>Retro-Game: Nachtblau wie alte Spielkonsolen, kraeftiges Gruen und Gelb, alles pixelig.</summary>
        private static void RetroColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x07071a); top = C(0x22224a); bottom = C(0x17173a); break;
                case 2: body = C(0x14142c); top = C(0x34346a); bottom = C(0x262650); break;
                default: body = C(0x0c0c22); top = C(0x2a2a58); bottom = C(0x1c1c44); break;
            }
            Body = body; WindowTop = Lighten(body, 0.05f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.35f); PanelEdge = C(0x04, 0x04, 0x0c);
            Text = C(0xff, 0xff, 0xff); TextDim = C(0xc8, 0xc8, 0xf0); TextFaint = C(0x8c, 0x8c, 0xc0);
            MenuBg = Darken(bottom, 0.15f); MenuHover = Mix(MenuBg, RetroGreen, 0.22f); MenuBorder = Lighten(MenuBg, 0.25f); MenuSep = Lighten(MenuBg, 0.15f); MenuSepLight = MenuBg;
            KeyTop = KeyBottom = C(0x34, 0x34, 0x6e); KeyHoverTop = KeyHoverBottom = C(0x44, 0x44, 0x88); KeyEdge = C(0x08, 0x08, 0x14);
            Well = Darken(bottom, 0.5f); WellHover = Darken(bottom, 0.35f); WellEdge = C(0x08, 0x08, 0x14);
            BadgeBg = Darken(bottom, 0.25f); BadgeEdge = RetroYellow; LogoCap = RetroYellow;
            ScaleTick = C(0xa0, 0xa0, 0xd8); ScaleLabel = C(0xe8, 0xe8, 0xff); EngraveText = C(0xa8, 0xa8, 0xe0);
            DispBg = Darken(bottom, 0.45f); DispHover = Darken(bottom, 0.3f); DispEdgeHover = RetroGreen; ScrollThumbHover = RetroGreen;
            Primary = C(0x2c, 0x9c, 0x3c); PrimaryHover = C(0x38, 0xb8, 0x4a);
            LcdBg = C(0x02, 0x08, 0x02); LcdText = RetroGreen; ValueText = RetroYellow;
        }

        /// <summary>Cockpit: mattes Schwarzgrau wie im Flugzeug, weisse Beschriftung, gruene und bernsteinfarbene Anzeigen.</summary>
        private static void CockpitColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x0e1012); top = C(0x2a2e33); bottom = C(0x202327); break;
                case 2: body = C(0x1a1c1f); top = C(0x3a3e44); bottom = C(0x2e3237); break;
                default: body = C(0x131517); top = C(0x30343a); bottom = C(0x25282d); break;
            }
            Body = body; WindowTop = Lighten(body, 0.05f); WindowBottom = body; BarBg = Darken(body, 0.25f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.18f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xff, 0xff, 0xff); TextDim = C(0xd4, 0xd8, 0xdc); TextFaint = C(0x9c, 0xa2, 0xa8);
            MenuBg = Darken(bottom, 0.2f); MenuHover = Lighten(MenuBg, 0.1f); MenuBorder = Lighten(MenuBg, 0.2f); MenuSep = Lighten(MenuBg, 0.12f); MenuSepLight = MenuBg;
            KeyTop = C(0x2a, 0x2d, 0x31); KeyBottom = C(0x1a, 0x1c, 0x1f); KeyHoverTop = C(0x36, 0x3a, 0x3f); KeyHoverBottom = C(0x22, 0x25, 0x28); KeyEdge = C(0x08, 0x09, 0x0a);
            Well = Darken(bottom, 0.5f); WellHover = Darken(bottom, 0.35f); WellEdge = Darken(body, 0.4f);
            BadgeBg = Darken(bottom, 0.25f); BadgeEdge = Lighten(bottom, 0.3f); LogoCap = CockpitAmber;
            ScaleTick = C(0xe8, 0xea, 0xec); ScaleLabel = C(0xff, 0xff, 0xff); EngraveText = C(0xd0, 0xd4, 0xd8);
            DispBg = Darken(bottom, 0.55f); DispHover = Darken(bottom, 0.4f); DispEdgeHover = CockpitAmber; ScrollThumbHover = CockpitAmber;
            Primary = C(0x3a, 0x6a, 0x4a); PrimaryHover = C(0x46, 0x80, 0x5a);
            LcdBg = C(0x02, 0x06, 0x03); LcdText = CockpitGreen; ValueText = CockpitAmber;
        }

        // ================================================================ Zuege: Deko je Design

        private static void PanelDeco420(Graphics g, RectangleF r, float rad, Color? tint)
        {
            if (SynthDesign) SynthEdge(g, r, rad);
            else if (RetroDesign) RetroEdge(g, r);
            else if (CockpitDesign) CockpitDeco(g, r, rad);
            else PanelDeco430(g, r, rad, tint);   // 4.3
        }

        /// <summary>Neon-Kante: oben Pink, unten Cyan, dazu eine Chrom-Linie unter dem Kopf.</summary>
        private static void SynthEdge(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inner = RectangleF.Inflate(r, -F(0.8f), -F(0.8f));
            for (int i = 3; i >= 1; i--)
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(14 + 8 * (3 - i), SynthPink), Color.FromArgb(14 + 8 * (3 - i), SynthCyan), LinearGradientMode.Vertical))
                using (var p = new Pen(b, F(1.0f + i * 1.2f)))
                using (var path = Round(RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad)) g.DrawPath(p, path);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(230, Lighten(SynthPink, 0.25f)), Color.FromArgb(230, Lighten(SynthCyan, 0.2f)), LinearGradientMode.Vertical))
            using (var p = new Pen(b, Math.Max(1f, F(1.1f))))
            using (var path = Round(inner, Math.Max(0f, rad - F(0.8f)))) g.DrawPath(p, path);
            // Chrom-Linie
            float y = r.Y + F(3);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, y, r.Width, F(2)), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(120, 255, 255, 255), LinearGradientMode.Horizontal) { WrapMode = WrapMode.TileFlipX })
            {
                b.SetBlendTriangularShape(0.5f);
                g.FillRectangle(b, r.X + rad, y, r.Width - rad * 2, Math.Max(1f, F(1)));
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Pixel-Rahmen: abgestufte Ecken, heller Rand oben/links, dunkler unten/rechts wie alte Spiele-Fenster.</summary>
        private static void RetroEdge(Graphics g, RectangleF r)
        {
            float p = Math.Max(2f, (float)Math.Round(F(3)));
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var light = new SolidBrush(Color.FromArgb(150, 0xc8, 0xc8, 0xff)))
            using (var dark = new SolidBrush(Color.FromArgb(200, 0x04, 0x04, 0x10)))
            {
                // Rand in Pixel-Stufen (die Ecken bleiben frei – so wirken sie abgestuft)
                g.FillRectangle(light, r.X + p, r.Y, r.Width - p * 2, p);          // oben
                g.FillRectangle(light, r.X, r.Y + p, p, r.Height - p * 2);          // links
                g.FillRectangle(dark, r.X + p, r.Bottom - p, r.Width - p * 2, p);   // unten
                g.FillRectangle(dark, r.Right - p, r.Y + p, p, r.Height - p * 2);   // rechts
                g.FillRectangle(light, r.X + p, r.Y + p, p, p);
                g.FillRectangle(dark, r.Right - p * 2, r.Bottom - p * 2, p, p);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Cockpit: Schnellverschluesse in den Ecken und eine feine weisse Umrandung wie bei Flugzeug-Paneelen.</summary>
        private static void CockpitDeco(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inset = RectangleF.Inflate(r, -F(2.5f), -F(2.5f));
            StrokeRound(g, Color.FromArgb(105, 255, 255, 255), Math.Max(1f, F(0.9f)), inset, Math.Max(1f, rad - F(2)));
            float m = F(3.2f), sr = F(2.4f);
            var pts = new List<PointF>();
            foreach (float fy in new[] { 0.36f, 0.64f }) { pts.Add(new PointF(r.X + m, r.Y + r.Height * fy)); pts.Add(new PointF(r.Right - m, r.Y + r.Height * fy)); }
            pts.Add(new PointF(r.X + m + F(1), r.Bottom - m - F(1))); pts.Add(new PointF(r.Right - m - F(1), r.Bottom - m - F(1)));
            foreach (var p in pts)
            {
                FillEllipse(g, Color.FromArgb(110, 0, 0, 0), p.X + F(0.5f), p.Y + F(0.7f), sr + F(0.5f));
                var rc = new RectangleF(p.X - sr, p.Y - sr, sr * 2, sr * 2);
                using (var b = new LinearGradientBrush(rc, C(0x6a, 0x6f, 0x76), C(0x24, 0x27, 0x2b), 45f)) g.FillEllipse(b, rc);
                using (var pen = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(1f, F(0.9f))))
                {
                    // Schlitz quer (Viertel-Drehverschluss)
                    g.DrawLine(pen, p.X - sr * 0.7f, p.Y, p.X + sr * 0.7f, p.Y);
                    g.DrawEllipse(pen, rc);
                }
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Hintergrund des Designs

        /// <summary>Eigener Hintergrund des Designs (Weltraum oder Synthwave).</summary>
        public static Bitmap MakeDesignBg(int w, int h) => SynthDesign ? MakeSynthBg(w, h) : DesignBg430 ? MakeDesignBg430(w, h) : MakeSpaceBg(w, h);

        /// <summary>Synthwave: Sonnenuntergang mit gestreifter Sonne, darunter ein Neon-Gitter, das in die Ferne laeuft.</summary>
        public static Bitmap MakeSynthBg(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float horizon = h * 0.58f;
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, w, horizon + 2), C(0x0c, 0x04, 0x1c), C(0x5a, 0x10, 0x5c), LinearGradientMode.Vertical))
                {
                    var cb = new ColorBlend(3) { Colors = new[] { C(0x08, 0x03, 0x16), C(0x3a, 0x0c, 0x50), C(0x9c, 0x24, 0x6a) }, Positions = new[] { 0f, 0.6f, 1f } };
                    b.InterpolationColors = cb;
                    g.FillRectangle(b, 0, 0, w, horizon);
                }
                // Sterne oben
                var rnd = new Random(15);
                for (int i = 0; i < Math.Max(40, w * h / 9000); i++)
                {
                    float x = (float)(rnd.NextDouble() * w), y = (float)(rnd.NextDouble() * horizon * 0.6);
                    float s = rnd.NextDouble() > 0.9 ? 1.8f : 1f;
                    using (var sb = new SolidBrush(Color.FromArgb(rnd.Next(60, 200), 255, 230, 255))) g.FillEllipse(sb, x, y, s, s);
                }
                // Sonne mit Streifen
                float sr = Math.Min(w, h) * 0.2f, scx = w * 0.62f, scy = horizon - sr * 0.15f;
                var sunR = new RectangleF(scx - sr, scy - sr, sr * 2, sr * 2);
                using (var glow = new GraphicsPath())
                {
                    glow.AddEllipse(RectangleF.Inflate(sunR, sr * 0.5f, sr * 0.5f));
                    using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(90, SynthPink), SurroundColors = new[] { Color.FromArgb(0, SynthPink) } }) g.FillPath(pb, glow);
                }
                var st = g.Save();
                g.SetClip(new RectangleF(0, 0, w, horizon));
                using (var sun = new GraphicsPath())
                {
                    sun.AddEllipse(sunR);
                    var clip = new Region(sun);
                    float band = sr * 0.09f;
                    for (int i = 0; i < 6; i++)
                    {
                        float y = scy + sr * 0.05f + i * band * 1.9f;
                        clip.Exclude(new RectangleF(scx - sr, y, sr * 2, band * (0.5f + i * 0.18f)));
                    }
                    g.SetClip(clip, CombineMode.Intersect);
                    using (var b = new LinearGradientBrush(sunR, C(0xff, 0xe0, 0x5a), C(0xff, 0x3c, 0x9c), LinearGradientMode.Vertical)) g.FillEllipse(b, sunR);
                    clip.Dispose();
                }
                g.Restore(st);
                // Boden mit Gitter
                using (var b = new LinearGradientBrush(new RectangleF(0, horizon - 1, w, h - horizon + 2), C(0x14, 0x02, 0x22), C(0x04, 0x00, 0x0a), LinearGradientMode.Vertical)) g.FillRectangle(b, 0, horizon, w, h - horizon);
                using (var hz = new Pen(Color.FromArgb(220, SynthPink), Math.Max(1.5f, F(2)))) g.DrawLine(hz, 0, horizon, w, horizon);
                float vpX = w * 0.5f;
                for (int i = -24; i <= 24; i++)
                {
                    float xb = vpX + i * w / 10f;
                    using (var p = new Pen(Color.FromArgb(150, SynthPink), 1.2f)) g.DrawLine(p, vpX + i * w / 160f, horizon, xb, h);
                }
                for (int k = 1; k <= 14; k++)
                {
                    float f = k / 14f, y = horizon + (h - horizon) * f * f;
                    using (var p = new Pen(Color.FromArgb((int)(60 + 120 * f), SynthCyan), 1f + f)) g.DrawLine(p, 0, y, w, y);
                }
                // abdunkeln, damit das Pult vorne bleibt
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);
            }
            return bmp;
        }

        // ================================================================ 282 neue Waende

        private static void MakeWall420(Graphics g, int w, int h, int kind, Random rnd)
        {
            if (kind == 4)
            {
                // Samtvorhang: senkrechte Falten in Dunkelrot, oben eine Schabracke
                g.Clear(C(0x2a, 0x04, 0x08));
                float fold = Math.Max(26f, F(38));
                for (float x = -fold; x < w + fold; x += fold)
                {
                    var fr = new RectangleF(x, 0, fold, h);
                    using (var b = new LinearGradientBrush(new RectangleF(fr.X - 1, 0, fr.Width + 2, 10), C(0x3c, 0x06, 0x0e), C(0x9a, 0x16, 0x24), LinearGradientMode.Horizontal))
                    {
                        b.SetBlendTriangularShape(0.55f);
                        g.FillRectangle(b, fr);
                    }
                }
                float vh = Math.Max(20f, F(30));
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, w, vh + 2), C(0x8a, 0x12, 0x20), C(0x4a, 0x08, 0x10), LinearGradientMode.Vertical)) g.FillRectangle(b, 0, 0, w, vh);
                using (var p = new Pen(C(0xc8, 0x9a, 0x3a), Math.Max(1.5f, F(2)))) g.DrawLine(p, 0, vh, w, vh);
                using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) for (float x = 0; x < w; x += fold * 1.5f) g.FillEllipse(b, x, vh - F(4), fold * 1.5f, F(10));
            }
            else if (kind == 5)
            {
                // Sichtbeton: grau, wolkig, Schalungsfugen und Ankerloecher
                g.Clear(C(0x5a, 0x5c, 0x5e));
                for (int i = 0; i < 160; i++)
                {
                    float cx = (float)(rnd.NextDouble() * w), cy = (float)(rnd.NextDouble() * h), rr = (float)(F(20) + rnd.NextDouble() * F(80));
                    int v = rnd.Next(-18, 18);
                    using (var b = new SolidBrush(Color.FromArgb(18, Clamp255(0x5a + v * 2), Clamp255(0x5c + v * 2), Clamp255(0x5e + v * 2)))) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 1.4f);
                }
                float pw = Math.Max(120f, F(220)), ph = Math.Max(60f, F(110));
                using (var p = new Pen(Color.FromArgb(70, 0, 0, 0), 1f))
                {
                    for (float x = pw; x < w; x += pw) g.DrawLine(p, x, 0, x, h);
                    for (float y = ph; y < h; y += ph) g.DrawLine(p, 0, y, w, y);
                }
                for (float y = ph / 2f; y < h; y += ph)
                    for (float x = pw / 4f; x < w; x += pw / 2f)
                        using (var b = new SolidBrush(Color.FromArgb(120, 0x2a, 0x2b, 0x2c))) g.FillEllipse(b, x - F(3), y - F(3), F(6), F(6));
            }
            else
            {
                // Kacheln: weisse Fliesen mit grauen Fugen, leicht glaenzend
                g.Clear(C(0x7a, 0x7e, 0x82));
                float t = Math.Max(22f, F(34)), gap = Math.Max(1.5f, F(2));
                for (float y = 0; y < h; y += t)
                    for (float x = 0; x < w; x += t)
                    {
                        int v = rnd.Next(-6, 7);
                        var tr = new RectangleF(x + gap / 2f, y + gap / 2f, t - gap, t - gap);
                        using (var b = new LinearGradientBrush(new RectangleF(tr.X, tr.Y - 1, tr.Width, tr.Height + 2), C(Clamp255(0xe8 + v), Clamp255(0xec + v), Clamp255(0xee + v)), C(Clamp255(0xc8 + v), Clamp255(0xcc + v), Clamp255(0xd0 + v)), 60f))
                            g.FillRectangle(b, tr);
                        using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillRectangle(b, tr.X + F(2), tr.Y + F(2), tr.Width * 0.35f, F(1.5f));
                    }
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);   // Kacheln sind hell: etwas mehr abdunkeln
            }
        }

        // ================================================================ 281 neue bewegte Hintergruende

        private static void DrawLiveBg420(Graphics g, Rectangle area, Point origin, double t)
        {
            switch (LiveBg)
            {
                case 6: DrawAurora(g, area, origin, t); break;
                case 7: DrawUnderwater(g, area, origin, t); break;
                case 8: DrawFire(g, area, origin, t); break;
                case 9: DrawMatrix(g, area, origin, t); break;
            }
        }

        /// <summary>Nordlicht: wehende gruen-violette Schleier oben (Lage im Fenster, damit beide Bereiche zusammenpassen).</summary>
        private static void DrawAurora(Graphics g, Rectangle area, Point origin, double t)
        {
            float x0 = area.X - F(20), x1 = area.Right + F(20), step = Math.Max(6f, F(12));
            for (int k = 0; k < 3; k++)
            {
                float baseY = area.Y + F(30 + k * 40);
                var top = new List<PointF>();
                var bottom = new List<PointF>();
                float minY = float.MaxValue, maxY = float.MinValue;
                for (float x = x0; x <= x1 + step; x += step)
                {
                    double wx = (x + origin.X) / F(260);   // Fenster-Koordinaten
                    double ph = wx + t * (0.18 + k * 0.05) + k * 1.7;
                    float y = baseY + (float)(Math.Sin(ph) * F(26) + Math.Sin(ph * 2.3 + 1) * F(10));
                    float hgt = F(150) * (0.55f + 0.45f * (float)Math.Sin(wx * 4.4 + t * 0.8 + k));
                    top.Add(new PointF(x, y));
                    bottom.Add(new PointF(x, y + hgt));
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + hgt);
                }
                bottom.Reverse();
                Color c = k == 1 ? Color.FromArgb(0x8a, 0x5c, 0xff) : Color.FromArgb(0x3c, 0xff, 0x9c);
                using (var path = new GraphicsPath())
                {
                    path.AddLines(top.ToArray());
                    path.AddLines(bottom.ToArray());
                    path.CloseFigure();
                    using (var br = new LinearGradientBrush(new RectangleF(x0, minY - 1, x1 - x0, maxY - minY + 2), Color.FromArgb(58, c), Color.FromArgb(0, c), LinearGradientMode.Vertical))
                        g.FillPath(br, path);
                }
                using (var p = new Pen(Color.FromArgb(80, Math.Min(255, c.R + 60), Math.Min(255, c.G + 60), Math.Min(255, c.B + 60)), Math.Max(1f, F(1.5f)))) g.DrawLines(p, top.ToArray());
            }
        }

        /// <summary>Unterwasser: schraege Lichtstrahlen von oben und aufsteigende Blasen.</summary>
        private static void DrawUnderwater(Graphics g, Rectangle area, Point origin, double t)
        {
            float span = Math.Max(F(1600), origin.X + area.Right);
            using (var b = new LinearGradientBrush(new Rectangle(area.X, area.Y - 1, area.Width, area.Height + 2), Color.FromArgb(30, 0x20, 0x90, 0xc0), Color.FromArgb(10, 0x04, 0x20, 0x40), LinearGradientMode.Vertical))
                g.FillRectangle(b, area);
            for (int i = 0; i < 6; i++)
            {
                float x = (float)((i * 0.17 + 0.05) * span + Math.Sin(t * 0.3 + i) * F(30)) - origin.X;
                float wTop = F(30 + i % 3 * 14), wBot = wTop * 3.2f;
                var poly = new[] { new PointF(x, area.Y), new PointF(x + wTop, area.Y), new PointF(x + wTop + F(120) + wBot, area.Bottom), new PointF(x + F(120), area.Bottom) };
                int a = (int)(16 + 10 * Math.Sin(t * 0.7 + i * 1.3));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(4, a), 0xc8, 0xf0, 0xff))) g.FillPolygon(b, poly);
            }
            int cell = Math.Max(30, Px(46));
            int x0 = (origin.X + area.X) / cell - 1, x1 = (origin.X + area.Right) / cell + 1;
            float hh = Math.Max(area.Height, Px(400));
            for (int cx = x0; cx <= x1; cx++)
            {
                uint hsh = (uint)(cx * 2654435761u); hsh ^= hsh >> 13;
                if (hsh % 3 != 0) continue;
                float speed = F(24 + hsh % 30);
                float y = area.Bottom - (float)((t * speed + hsh % 997) % (hh + F(40)));
                float x = cx * cell + (hsh >> 8) % (uint)cell - origin.X + (float)Math.Sin(t * 1.6 + cx) * F(4);
                float r = F(2 + hsh % 4);
                using (var p = new Pen(Color.FromArgb(110, 0xd8, 0xf4, 0xff), Math.Max(1f, F(1)))) g.DrawEllipse(p, x - r, y - r, r * 2, r * 2);
                using (var b = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillEllipse(b, x - r * 0.5f, y - r * 0.6f, r * 0.6f, r * 0.6f);
            }
        }

        /// <summary>Kaminfeuer: warmes Flackern von unten, ein paar Funken steigen auf.</summary>
        private static void DrawFire(Graphics g, Rectangle area, Point origin, double t)
        {
            float flick = (float)(0.75 + 0.15 * Math.Sin(t * 7.3) + 0.1 * Math.Sin(t * 13.1 + 1));
            var glow = new Rectangle(area.X, area.Y, area.Width, area.Height);
            using (var b = new LinearGradientBrush(new Rectangle(glow.X, glow.Y - 1, glow.Width, glow.Height + 2), Color.FromArgb(0, 0xff, 0x70, 0x20), Color.FromArgb((int)(85 * flick), 0xff, 0x6a, 0x18), LinearGradientMode.Vertical))
                g.FillRectangle(b, glow);
            float span = Math.Max(F(1600), origin.X + area.Right);
            for (int i = 0; i < 14; i++)
            {
                float fx = (float)(i / 14.0 * span) - origin.X + (float)Math.Sin(t * 2 + i) * F(8);
                float fh = F(80) * (0.6f + 0.4f * (float)Math.Abs(Math.Sin(t * (3 + i % 4) + i * 1.9)));
                var e = new RectangleF(fx - F(40), area.Bottom - fh, F(80), fh * 1.6f);
                if (e.Right < area.X || e.X > area.Right) continue;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(e);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(110, 0xff, 0xb0, 0x40), SurroundColors = new[] { Color.FromArgb(0, 0xff, 0x40, 0x10) } }) g.FillPath(pb, path);
                }
            }
            int cell = Math.Max(30, Px(60));
            int x0 = (origin.X + area.X) / cell - 1, x1 = (origin.X + area.Right) / cell + 1;
            float hh = Math.Max(area.Height, Px(400)) * 0.7f;
            for (int cx = x0; cx <= x1; cx++)
            {
                uint hsh = (uint)(cx * 40503u + 977u); hsh ^= hsh >> 11; hsh *= 0x9e3779b1;
                if (hsh % 2 != 0) continue;
                float speed = F(30 + hsh % 40);
                float life = (float)((t * speed + hsh % 641) % hh);
                float y = area.Bottom - life;
                float x = cx * cell + (hsh >> 9) % (uint)cell - origin.X + (float)Math.Sin(t * 2.2 + cx) * F(6);
                int a = (int)(220 * (1 - life / hh));
                if (a < 6) continue;
                using (var b = new SolidBrush(Color.FromArgb(a, 0xff, 0xc0, 0x50))) g.FillEllipse(b, x, y, F(2.2f), F(2.2f));
            }
        }

        private const string MatrixChars = "ｱｲｳｴｵｶｷｸｹｺ0123456789ABCDEFﾊﾋﾌﾍﾎ<>=+*";
        /// <summary>Tempo des Matrix-Regens (1 = ruhig, hoeher bei Musik).</summary>
        public static float MatrixSpeed = 1f;
        private static double _matrixPos, _matrixLastT = -1;

        /// <summary>Matrix-Regen: gruene Zeichen fallen in Spalten, schneller bei Musik.</summary>
        private static void DrawMatrix(Graphics g, Rectangle area, Point origin, double t)
        {
            if (_matrixLastT < 0 || t < _matrixLastT) _matrixLastT = t;
            _matrixPos += (t - _matrixLastT) * MatrixSpeed;
            _matrixLastT = t;
            float cw = Math.Max(10f, F(14)), chh = Math.Max(12f, F(16));
            var font = Mono(12.5f);
            int c0 = (int)Math.Floor((origin.X + area.X) / cw) - 1, c1 = (int)Math.Ceiling((origin.X + area.Right) / cw) + 1;
            int rows = (int)Math.Ceiling(Math.Max(area.Bottom + origin.Y, Px(500)) / chh) + 1;
            for (int c = c0; c <= c1; c++)
            {
                uint hsh = (uint)(c * 2246822519u + 3266489917u); hsh ^= hsh >> 15;
                if (hsh % 7 == 0) continue;
                double speed = 4 + hsh % 7;
                int len = 6 + (int)(hsh % 12);
                double head = (_matrixPos * speed + hsh % 211) % (rows + len);
                float x = c * cw - origin.X;
                for (int k = 0; k < len; k++)
                {
                    int row = (int)head - k;
                    if (row < 0 || row >= rows) continue;
                    float y = row * chh - origin.Y;
                    if (y + chh < area.Y || y > area.Bottom) continue;
                    int a = k == 0 ? 235 : (int)(190 * (1 - k / (float)len));
                    if (a < 6) continue;
                    char ch = MatrixChars[(int)((hsh + (uint)(row * 31) + (uint)(_matrixPos * 2) * (uint)(k == 0 ? 7 : 0)) % (uint)MatrixChars.Length)];
                    using (var b = new SolidBrush(k == 0 ? Color.FromArgb(a, 0xc8, 0xff, 0xd0) : Color.FromArgb(a, 0x2c, 0xe0, 0x5a)))
                        g.DrawString(ch.ToString(), font, b, x, y);
                }
            }
        }
    }
}
