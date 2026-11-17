using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 (finale) Optik: vier Designs (Grosskonsole, Glas &amp; Licht, Industrial, Weltraum), Tastenform "Pads",
    /// Faderkappe "Glas", Oszilloskop-Uhr, Himmel nach Tageszeit, Weltraum-Hintergrund, Neon-Schild.
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleConsole = 11, StyleGlass = 12, StyleIndustrial = 13, StyleSpace = 14;
        public static bool BigConsole => Design == StyleConsole;
        public static bool GlassDesign => Design == StyleGlass;
        public static bool IndustrialDesign => Design == StyleIndustrial;
        public static bool SpaceDesign => Design == StyleSpace;
        /// <summary>Das Design bringt einen eigenen Hintergrund mit (Weltraum).</summary>
        public static bool DesignBg => SpaceDesign || SynthDesign;   // 4.2: Synthwave

        private static void Designs400c(int scheme)
        {
            if (BigConsole) ConsoleColors(scheme);
            else if (GlassDesign) GlassColors(scheme);
            else if (IndustrialDesign) IndustrialColors(scheme);
            else if (SpaceDesign) SpaceColors(scheme);
        }

        /// <summary>Grosskonsole: graublaues Metall wie im grossen Studio, weisse Schrift, dunkle Tasten.</summary>
        private static void ConsoleColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x161c24); top = C(0x4f5b69); bottom = C(0x3c4653); break;
                case 2: body = C(0x262c33); top = C(0x6a7582); bottom = C(0x55606c); break;
                default: body = C(0x1c2229); top = C(0x5c6875); bottom = C(0x48535f); break;
            }
            Body = body; WindowTop = Lighten(body, 0.06f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.25f); PanelEdge = Darken(body, 0.45f);
            Text = C(0xf4, 0xf6, 0xf8); TextDim = C(0xd6, 0xdc, 0xe2); TextFaint = C(0xa8, 0xb2, 0xbc);
            MenuBg = Darken(bottom, 0.25f); MenuHover = Lighten(MenuBg, 0.1f); MenuBorder = Lighten(MenuBg, 0.18f); MenuSep = Lighten(MenuBg, 0.12f); MenuSepLight = MenuBg;
            KeyTop = C(0x3c4450); KeyBottom = C(0x262c34); KeyHoverTop = C(0x4a5360); KeyHoverBottom = C(0x2e353e); KeyEdge = C(0x14181d);
            Well = Darken(bottom, 0.45f); WellHover = Darken(bottom, 0.3f); WellEdge = Darken(body, 0.3f);
            BadgeBg = Darken(bottom, 0.2f); BadgeEdge = Lighten(bottom, 0.2f); LogoCap = C(0xe8, 0x44, 0x3a);
            ScaleTick = C(0xc8, 0xd0, 0xd8); ScaleLabel = C(0xe6, 0xea, 0xee); EngraveText = C(0xb8, 0xc2, 0xcc);
            DispBg = Darken(bottom, 0.5f); DispHover = Darken(bottom, 0.35f);
            Primary = C(0x2f, 0x6f, 0xb8); PrimaryHover = C(0x3a, 0x80, 0xcc);
            LcdBg = C(0x06, 0x0a, 0x0c); LcdText = C(0x7c, 0xe8, 0xff); ValueText = C(0x9c, 0xee, 0xff);
            Slot = Darken(bottom, 0.6f); RingOff = Darken(bottom, 0.3f);
        }

        /// <summary>Glas &amp; Licht: mattes Acryl ueber tiefem Blauschwarz, Kanten leuchten in der Kanalfarbe.</summary>
        private static void GlassColors(int scheme)
        {
            switch (scheme)
            {
                case 1: ModernFrom(C(0x070a12), C(0x0d1220), C(0x1a2132)); break;
                case 2: ModernFrom(C(0x12151b), C(0x191d26), C(0x272d39)); break;
                default: ModernFrom(C(0x0a0d13), C(0x10141c), C(0x1f2530)); break;
            }
            PanelTop = Lighten(PanelBottom, 0.08f);
            Text = C(0xf2, 0xf5, 0xfa); TextDim = C(0xb4, 0xbd, 0xca); TextFaint = C(0x7a, 0x84, 0x93);
            KeyTop = KeyBottom = Color.FromArgb(0x2a, 0x31, 0x3d); KeyHoverTop = KeyHoverBottom = Color.FromArgb(0x35, 0x3d, 0x4b);
            LcdBg = C(0x05, 0x07, 0x0c); LogoCap = Accent;
        }

        /// <summary>Industrial: Stahl, gelb-schwarze Warnstreifen, Nieten.</summary>
        private static void IndustrialColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x131416); top = C(0x4a4e54); bottom = C(0x34373c); break;
                case 2: body = C(0x222326); top = C(0x666a70); bottom = C(0x4c5056); break;
                default: body = C(0x18191b); top = C(0x575b61); bottom = C(0x3e4146); break;
            }
            Body = body; WindowTop = Lighten(body, 0.06f); WindowBottom = body; BarBg = Darken(body, 0.25f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.3f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xf2, 0xf1, 0xea); TextDim = C(0xcc, 0xca, 0xc0); TextFaint = C(0x9a, 0x98, 0x8e);
            MenuBg = Darken(bottom, 0.3f); MenuHover = Mix(MenuBg, HazardYellow, 0.25f); MenuBorder = Lighten(MenuBg, 0.2f); MenuSep = Lighten(MenuBg, 0.12f); MenuSepLight = MenuBg;
            KeyTop = C(0x2e3034); KeyBottom = C(0x1c1d20); KeyHoverTop = C(0x3a3d42); KeyHoverBottom = C(0x24262a); KeyEdge = C(0x0c0c0d);
            BadgeBg = Darken(bottom, 0.25f); BadgeEdge = HazardYellow; LogoCap = HazardYellow;
            ScaleTick = C(0xb8, 0xb6, 0xac); ScaleLabel = C(0xe0, 0xde, 0xd4); EngraveText = C(0xa8, 0xa6, 0x9c);
            DispEdgeHover = HazardYellow; ScrollThumbHover = HazardYellow;
            Primary = C(0xb8, 0x8a, 0x10); PrimaryHover = C(0xd0, 0x9c, 0x14);
            LcdBg = C(0x0c, 0x0a, 0x02); LcdText = HazardYellow; ValueText = C(0xff, 0xd8, 0x50);
        }

        public static readonly Color HazardYellow = Color.FromArgb(0xf2, 0xc2, 0x1e);

        /// <summary>Weltraum: Nebel und Sterne im Hintergrund, dunkle Zuege mit blau gluehenden Kanten.</summary>
        private static void SpaceColors(int scheme)
        {
            switch (scheme)
            {
                case 1: ModernFrom(C(0x030409), C(0x070a14), C(0x0f1424)); break;
                case 2: ModernFrom(C(0x0a0b12), C(0x10121c), C(0x1a1e2e)); break;
                default: ModernFrom(C(0x05060c), C(0x0a0c18), C(0x121829)); break;
            }
            PanelTop = Lighten(PanelBottom, 0.05f);
            Text = C(0xe8, 0xf0, 0xff); TextDim = C(0xa6, 0xb8, 0xd8); TextFaint = C(0x6c, 0x7c, 0x9c);
            LcdBg = C(0x02, 0x04, 0x0a); LcdText = SpaceBlue; ValueText = C(0x8c, 0xd0, 0xff);
            LogoCap = SpaceBlue;
        }

        public static readonly Color SpaceBlue = Color.FromArgb(0x4a, 0xa8, 0xff);

        // ================================================================ Zuege: Deko je Design (nach DrawPanel)

        private static void PanelDeco400(Graphics g, RectangleF r, float rad, Color? tint)
        {
            if (GlassDesign) GlassEdge(g, r, rad, tint ?? Accent);
            else if (SpaceDesign) { GlassEdge(g, r, rad, SpaceBlue); }
            else if (IndustrialDesign) IndustrialDeco(g, r, rad);
            else if (BigConsole) ConsoleDeco(g, r, rad);
            else PanelDeco420(g, r, rad, tint);   // 4.2
        }

        private static void GlassEdge(Graphics g, RectangleF r, float rad, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, rad))
            {
                // mattes Acryl: oben etwas heller (Licht faellt hinein)
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(GlassDesign ? 22 : 10, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                // Kante leuchtet: von innen
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterColor = Color.FromArgb(0, c);
                    pb.SurroundColors = new[] { Color.FromArgb(GlassDesign ? 46 : 34, c) };
                    pb.FocusScales = new PointF(Math.Max(0f, 1f - F(18) / Math.Max(1f, r.Width)), Math.Max(0f, 1f - F(18) / Math.Max(1f, r.Height)));
                    g.FillPath(pb, path);
                }
            }
            for (int i = 3; i >= 1; i--)
                StrokeRound(g, Color.FromArgb(16 + 8 * (3 - i), c), F(1.0f + i * 1.1f), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
            StrokeRound(g, Color.FromArgb(210, Lighten(c, 0.35f)), Math.Max(1f, F(1.1f)), RectangleF.Inflate(r, -F(0.8f), -F(0.8f)), Math.Max(0f, rad - F(0.8f)));
            g.SmoothingMode = sm;
        }

        private static void IndustrialDeco(Graphics g, RectangleF r, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Warnstreifen unten
            var band = new RectangleF(r.X + F(4), r.Bottom - F(9), r.Width - F(8), F(5));
            var st = g.Save();
            g.SetClip(band, CombineMode.Intersect);
            using (var b = new SolidBrush(HazardYellow)) g.FillRectangle(b, band);
            using (var b = new SolidBrush(Color.FromArgb(0x18, 0x18, 0x18)))
                for (float x = band.X - band.Height * 2; x < band.Right + band.Height; x += F(8))
                    g.FillPolygon(b, new[] { new PointF(x, band.Bottom), new PointF(x + F(4), band.Bottom), new PointF(x + F(4) + band.Height, band.Y), new PointF(x + band.Height, band.Y) });
            g.Restore(st);
            using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawRectangle(p, band.X, band.Y, band.Width, band.Height);
            // Nieten an den Ecken und am Rand
            float m = F(5.5f), rr = F(2.2f);
            var pts = new List<PointF> { new PointF(r.X + m, r.Y + m + F(4)), new PointF(r.Right - m, r.Y + m + F(4)), new PointF(r.X + m, r.Bottom - m - F(8)), new PointF(r.Right - m, r.Bottom - m - F(8)) };
            for (float y = r.Y + F(130); y < r.Bottom - F(80); y += F(150)) { pts.Add(new PointF(r.X + m, y)); pts.Add(new PointF(r.Right - m, y)); }
            foreach (var p in pts)
            {
                FillEllipse(g, Color.FromArgb(90, 0, 0, 0), p.X + F(0.6f), p.Y + F(0.8f), rr);
                var rc = new RectangleF(p.X - rr, p.Y - rr, rr * 2, rr * 2);
                using (var b = new LinearGradientBrush(rc, Lighten(PanelTop, 0.45f), Darken(PanelBottom, 0.35f), 45f)) g.FillEllipse(b, rc);
                using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1f, F(0.5f)))) g.DrawEllipse(pen, rc);
            }
            g.SmoothingMode = sm;
        }

        private static void ConsoleDeco(Graphics g, RectangleF r, float rad)
        {
            // Modul-Fugen links und rechts (wie einzelne Einschuebe)
            using (var dark = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
            using (var light = new Pen(Color.FromArgb(35, 255, 255, 255), 1f))
            {
                g.DrawLine(dark, r.X + F(2.5f), r.Y + rad + F(2), r.X + F(2.5f), r.Bottom - rad - F(2));
                g.DrawLine(light, r.X + F(3.5f), r.Y + rad + F(2), r.X + F(3.5f), r.Bottom - rad - F(2));
                g.DrawLine(dark, r.Right - F(3.5f), r.Y + rad + F(2), r.Right - F(3.5f), r.Bottom - rad - F(2));
            }
            // Schrauben oben und unten
            DrawScrew(g, r.X + r.Width / 2f, r.Bottom - F(6), F(2.6f), 30f);
        }

        // ================================================================ Grosskonsole: Namensstreifen, farbige Knopfkappen

        /// <summary>Weisser Beschriftungsstreifen mit Kanalfarbe am Rand (Text dunkel, siehe PlateText).</summary>
        private static void DrawConsolePlate(Graphics g, RectangleF r, Color bg)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = F(1.5f);
            FillRound(g, Color.FromArgb(70, 0, 0, 0), new RectangleF(r.X + F(0.5f), r.Y + F(1), r.Width, r.Height), rad);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), C(0xfb, 0xfb, 0xf7), C(0xe2, 0xe3, 0xde), LinearGradientMode.Vertical))
                FillRound(g, b, r, rad);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r.X + F(1), r.Y + F(1), F(4), r.Height - F(2));
            using (var pen = new Pen(Color.FromArgb(22, 0x30, 0x30, 0x30), 1f))
                for (float y = r.Y + F(4); y < r.Bottom - F(2); y += F(4)) g.DrawLine(pen, r.X + F(7), y, r.Right - F(3), y);
            StrokeRound(g, Color.FromArgb(120, 0x80, 0x84, 0x88), 1f, r, rad);
            g.SmoothingMode = sm;
        }

        public static readonly Color ConsoleInk = Color.FromArgb(0x16, 0x19, 0x1e);

        /// <summary>Farbe der Knopfkappe je Funktion (wie an der grossen Konsole): HI rot, MID gruen, LO blau, PAN grau, STREAM lila.</summary>
        public static Color ConsoleCapColor(string fn)
        {
            switch (fn)
            {
                case "HI": return C(0xd8, 0x3a, 0x34);
                case "MID": return C(0x2e, 0xa0, 0x5a);
                case "LO": return C(0x2c, 0x6e, 0xd0);
                case "STREAM": return C(0x9a, 0x6c, 0xe8);
                default: return C(0x9a, 0xa2, 0xac);
            }
        }

        // ================================================================ 254 Tastenform Pads, Faderkappe Glas

        /// <summary>Pad (wie an Drum-Controllern): weiches Silikon, leuchtet von innen.</summary>
        private static void DrawPadKey(Graphics g, RectangleF r, string text, bool on, Color lit, bool hover, bool half)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(F(4), r.Height / 3f);
            FillRound(g, Color.FromArgb(120, 0, 0, 0), new RectangleF(r.X, r.Y + F(1.2f), r.Width, r.Height), rad);
            using (var path = Round(r, rad))
            {
                Color baseC = on ? lit : Mix(C(0x22, 0x24, 0x28), lit, half ? 0.3f : hover ? 0.16f : 0.1f);
                using (var b = new SolidBrush(baseC)) g.FillPath(b, path);
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                    pb.CenterColor = on ? Color.FromArgb(230, Lighten(lit, 0.65f)) : Color.FromArgb(half ? 90 : 40, lit);
                    pb.SurroundColors = new[] { on ? Color.FromArgb(255, Darken(lit, 0.15f)) : Color.FromArgb(0, lit) };
                    pb.FocusScales = new PointF(0.25f, 0.15f);
                    g.FillPath(pb, path);
                }
                // Silikon: feiner Glanz oben
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height / 2f + 1), Color.FromArgb(on ? 60 : 30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    FillRound(g, b, new RectangleF(r.X + F(1), r.Y + F(1), r.Width - F(2), r.Height * 0.45f), Math.Max(0f, rad - F(1)));
            }
            if (on)
                for (int i = 3; i >= 1; i--)
                    StrokeRound(g, Alpha(lit, 26), F(1.2f * i), RectangleF.Inflate(r, F(0.6f * i), F(0.6f * i)), rad + F(0.6f * i));
            StrokeRound(g, on ? Darken(lit, 0.45f) : C(0x0a, 0x0a, 0x0c), F(1), r, rad);
            Label(g, text, r, Center, on ? TextOn(Lighten(lit, 0.3f)) : hover ? Text : Mix(TextDim, lit, half ? 0.6f : 0.15f), KeyPx(g, text, r.Width));
            g.SmoothingMode = sm;
        }

        /// <summary>Glas-Kappe: durchsichtiger Block, innen leuchtet die Kanalfarbe.</summary>
        private static void DrawGlassCap(Graphics g, RectangleF r, bool hover, Color accent)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = CapRad(F(3), r);
            FillRound(g, Color.FromArgb(90, 0, 0, 0), new RectangleF(r.X + F(1), r.Y + F(2.5f), r.Width, r.Height), rad + F(1));
            for (int i = 3; i >= 1; i--) FillRound(g, Color.FromArgb(14, accent), RectangleF.Inflate(r, F(i * 1.3f), F(i * 1.3f)), rad + F(i));
            using (var path = Round(r, rad))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(hover ? 120 : 95, 0xd8, 0xe6, 0xf2), Color.FromArgb(hover ? 110 : 80, 0x40, 0x4c, 0x58), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                    pb.CenterColor = Color.FromArgb(230, Lighten(accent, 0.35f));
                    pb.SurroundColors = new[] { Color.FromArgb(40, accent) };
                    pb.FocusScales = new PointF(0.6f, 0.05f);
                    g.FillPath(pb, path);
                }
            }
            // Glanz und Kante
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.45f + 1), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(r.X + F(1.5f), r.Y + F(1), r.Width - F(3), r.Height * 0.4f), Math.Max(0f, rad - F(1)));
            StrokeRound(g, Color.FromArgb(200, 0xe8, 0xf0, 0xf8), Math.Max(1f, F(0.9f)), r, rad);
            float cy = r.Y + r.Height / 2f;
            using (var pen = new Pen(Color.FromArgb(240, Lighten(accent, 0.55f)), F(1.6f))) g.DrawLine(pen, r.X + F(3), cy, r.Right - F(3), cy);
            g.SmoothingMode = sm;
        }

        /// <summary>Industrial: Schutzkappe ueber dem Kippschalter (aufgeklappt, wenn an).</summary>
        private static void DrawSafetyCover(Graphics g, RectangleF r, bool on)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(F(3), r.Height / 4f);
            var cov = on ? new RectangleF(r.X + F(1), r.Y - r.Height * 0.32f, r.Width - F(2), r.Height * 0.32f) : RectangleF.Inflate(r, F(1), F(1));
            using (var b = new SolidBrush(Color.FromArgb(on ? 120 : 70, 0xe0, 0x30, 0x24))) FillRound(g, b, cov, rad);
            StrokeRound(g, Color.FromArgb(on ? 220 : 170, 0xff, 0x5a, 0x48), Math.Max(1f, F(1)), cov, rad);
            // Scharnier
            using (var pen = new Pen(Color.FromArgb(200, 0x30, 0x30, 0x30), Math.Max(1f, F(1.4f)))) g.DrawLine(pen, r.X + F(2), r.Y, r.Right - F(2), r.Y);
            g.SmoothingMode = sm;
        }

        // ================================================================ 255 Oszilloskop-Uhr

        private static readonly Color ScopeGreen = Color.FromArgb(0x5c, 0xff, 0x8a);

        /// <summary>Vektor-Uhr: runder Schirm, gruener Leuchtstrahl (Zeiger und Ziffern als Linien mit Nachleuchten).</summary>
        public static void DrawScopeClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(r.Height, r.Width) - F(4);
            var face = new RectangleF(r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            var c = new PointF(face.X + d / 2f, face.Y + d / 2f);
            float R = d / 2f;
            if (staticPart)
            {
                // Roehre: dunkler gruener Schirm mit Glas-Woelbung und Raster
                using (var b = new SolidBrush(C(0x12, 0x14, 0x16))) g.FillEllipse(b, RectangleF.Inflate(face, F(2), F(2)));
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(face);
                    using (var pb = new PathGradientBrush(path) { CenterColor = C(0x0c, 0x24, 0x16), SurroundColors = new[] { C(0x02, 0x08, 0x05) } }) g.FillPath(pb, path);
                }
                using (var pen = new Pen(Color.FromArgb(28, ScopeGreen), 1f))
                {
                    g.DrawLine(pen, face.X + F(4), c.Y, face.Right - F(4), c.Y);
                    g.DrawLine(pen, c.X, face.Y + F(4), c.X, face.Bottom - F(4));
                }
                using (var pen = new Pen(Color.FromArgb(150, 0x50, 0x54, 0x5a), Math.Max(1f, F(1.4f)))) g.DrawEllipse(pen, RectangleF.Inflate(face, F(1.5f), F(1.5f)));
                g.SmoothingMode = sm;
                return;
            }
            var now = DateTime.Now;
            var lines = new List<PointF[]>();
            for (int i = 0; i < 60; i++)
            {
                double a = i * Math.PI / 30;
                float o = R * 0.9f, ii = i % 5 == 0 ? R * 0.76f : R * 0.85f;
                lines.Add(new[] { new PointF(c.X + (float)Math.Sin(a) * ii, c.Y - (float)Math.Cos(a) * ii), new PointF(c.X + (float)Math.Sin(a) * o, c.Y - (float)Math.Cos(a) * o) });
            }
            double sec = now.Second, min = now.Minute + sec / 60.0, hr = (now.Hour % 12) + min / 60.0;
            Func<double, float, PointF[]> hand = (ang, len) => new[] { c, new PointF(c.X + (float)Math.Sin(ang) * len, c.Y - (float)Math.Cos(ang) * len) };
            var hh = hand(hr * Math.PI / 6, R * 0.48f); var mm = hand(min * Math.PI / 30, R * 0.68f); var ss = hand(sec * Math.PI / 30, R * 0.74f);
            // Nachleuchten des Sekundenzeigers (Phosphor)
            for (int k = 3; k >= 1; k--)
            {
                var tr = hand((sec - k * 0.35) * Math.PI / 30, R * 0.74f);
                using (var pen = new Pen(Color.FromArgb(30 / k, ScopeGreen), F(1.2f))) g.DrawLine(pen, tr[0], tr[1]);
            }
            BeamLines(g, lines, F(0.9f));
            BeamLines(g, new List<PointF[]> { hh }, F(2.4f));
            BeamLines(g, new List<PointF[]> { mm }, F(1.7f));
            BeamLines(g, new List<PointF[]> { ss }, F(1.0f));
            FillEllipse(g, Color.FromArgb(220, Lighten(ScopeGreen, 0.5f)), c.X, c.Y, F(1.8f));
            g.SmoothingMode = sm;
        }

        private static void BeamLines(Graphics g, List<PointF[]> lines, float w)
        {
            using (var glow = new Pen(Color.FromArgb(46, ScopeGreen), w * 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var mid = new Pen(Color.FromArgb(150, ScopeGreen), w * 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var core = new Pen(Color.FromArgb(240, Lighten(ScopeGreen, 0.55f)), Math.Max(0.8f, w * 0.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                foreach (var pen in new[] { glow, mid, core })
                    foreach (var l in lines) g.DrawLine(pen, l[0], l[1]);
        }

        public static void DrawScopeClockSmall(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, C(0x04, 0x0c, 0x07), r, F(3));
            StrokeRound(g, Color.FromArgb(120, 0x50, 0x54, 0x5a), 1f, r, F(3));
            // Ziffern als Leuchtstrahl (einfache Vektor-Ziffern)
            string t = DateTime.Now.ToString("HH:mm");
            using (var path = new GraphicsPath())
            {
                float px = Math.Max(8f, r.Height * 0.62f);
                FontFamily ff;
                try { ff = new FontFamily("Consolas"); } catch { ff = new FontFamily(System.Drawing.Text.GenericFontFamilies.Monospace); }
                using (ff)
                    path.AddString(t, ff, (int)FontStyle.Regular, px, new RectangleF(r.X, r.Y + (r.Height - px * 1.2f) / 2f, r.Width, px * 1.3f), Center);
                using (var glow = new Pen(Color.FromArgb(60, ScopeGreen), F(2.6f)) { LineJoin = LineJoin.Round }) g.DrawPath(glow, path);
                using (var core = new Pen(Color.FromArgb(240, Lighten(ScopeGreen, 0.4f)), Math.Max(0.8f, F(0.9f))) { LineJoin = LineJoin.Round }) g.DrawPath(core, path);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ 225 Himmel nach Tageszeit, Weltraum-Hintergrund

        /// <summary>Mondphase 0..1 (0 Neumond, 0,5 Vollmond) – synodischer Monat ab dem Neumond am 6.1.2000.</summary>
        public static double MoonPhase(DateTime utc)
        {
            const double Synodic = 29.530588853;
            double days = (utc - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays;
            double p = days / Synodic % 1.0;
            return p < 0 ? p + 1 : p;
        }

        public static readonly string[] MoonNames = { "Neumond", "zunehmende Sichel", "erstes Viertel", "zunehmender Mond", "Vollmond", "abnehmender Mond", "letztes Viertel", "abnehmende Sichel" };
        public static string MoonName(double phase) => MoonNames[(int)Math.Floor((phase * 8 + 0.5)) % 8];

        /// <summary>Tageszeit-Abschnitt fuer den Himmel: 0 Nacht, 1 Morgenrot, 2 Tag, 3 Sonnenuntergang.</summary>
        public static int SkyPhase(double hour) => hour < 5.5 || hour >= 21.5 ? 0 : hour < 8 ? 1 : hour < 18 ? 2 : 3;

        private static Color Lerp3(double t, Color a, Color b) => Mix(a, b, (float)Math.Max(0, Math.Min(1, t)));

        /// <summary>Himmel fuer diese Uhrzeit (Farbverlauf, Sonne bzw. Mond mit echter Phase, Sterne, Wolken), leicht abgedunkelt.</summary>
        public static Bitmap MakeSky(int w, int h, DateTime local, double moonPhase, int dimPct)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            double hr = local.Hour + local.Minute / 60.0;
            // Farben oben/unten je nach Zeit (weich ueberblendet)
            Color nightT = C(0x05, 0x08, 0x18), nightB = C(0x12, 0x1a, 0x36);
            Color dawnT = C(0x2a, 0x2e, 0x5e), dawnB = C(0xf0, 0x88, 0x5a);
            Color dayT = C(0x2a, 0x6c, 0xc8), dayB = C(0x9c, 0xcc, 0xf0);
            Color duskT = C(0x2c, 0x22, 0x5a), duskB = C(0xf4, 0x6e, 0x3a);
            Color top, bottom;
            if (hr < 5) { top = nightT; bottom = nightB; }
            else if (hr < 6.5) { double t = (hr - 5) / 1.5; top = Lerp3(t, nightT, dawnT); bottom = Lerp3(t, nightB, dawnB); }
            else if (hr < 9) { double t = (hr - 6.5) / 2.5; top = Lerp3(t, dawnT, dayT); bottom = Lerp3(t, dawnB, dayB); }
            else if (hr < 17.5) { top = dayT; bottom = dayB; }
            else if (hr < 19.5) { double t = (hr - 17.5) / 2.0; top = Lerp3(t, dayT, duskT); bottom = Lerp3(t, dayB, duskB); }
            else if (hr < 21.5) { double t = (hr - 19.5) / 2.0; top = Lerp3(t, duskT, nightT); bottom = Lerp3(t, duskB, nightB); }
            else { top = nightT; bottom = nightB; }
            double dark = hr < 5 || hr >= 21.5 ? 1 : hr < 7 ? 1 - (hr - 5) / 2 : hr >= 19.5 ? (hr - 19.5) / 2 : 0;
            var rnd = new Random(1234);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), top, bottom, LinearGradientMode.Vertical)) g.FillRectangle(b, 0, 0, w, h);
                // Sterne
                if (dark > 0.05)
                {
                    int n = Math.Max(40, w * h / 2600);
                    for (int i = 0; i < n; i++)
                    {
                        float x = (float)(rnd.NextDouble() * w), y = (float)(rnd.NextDouble() * h * 0.85);
                        double br = rnd.NextDouble();
                        int a = (int)(dark * (60 + 170 * br * br));
                        float s = br > 0.93 ? 1.6f : br > 0.7 ? 1.1f : 0.8f;
                        using (var sb = new SolidBrush(Color.FromArgb(Math.Min(255, a), 0xf4, 0xf6, 0xff))) g.FillEllipse(sb, x - s / 2, y - s / 2, s, s);
                    }
                }
                // Sonne bzw. Mond auf einem Bogen ueber den Himmel
                bool sunUp = hr >= 6 && hr < 20.5;
                double u = sunUp ? (hr - 6) / 14.5 : ((hr >= 20.5 ? hr - 20.5 : hr + 3.5) / 9.5);
                float bx = (float)(w * (0.08 + 0.84 * u)), by = (float)(h * (0.62 - 0.5 * Math.Sin(Math.PI * Math.Max(0, Math.Min(1, u)))));
                float br0 = Math.Max(10f, Math.Min(w, h) * 0.05f);
                if (sunUp)
                {
                    Color sc = hr < 8 || hr > 18 ? C(0xff, 0xa8, 0x60) : C(0xff, 0xf2, 0xc8);
                    for (int i = 6; i >= 1; i--) { float rr = br0 * (1 + i * 0.6f); using (var sb = new SolidBrush(Color.FromArgb(10, sc))) g.FillEllipse(sb, bx - rr, by - rr, rr * 2, rr * 2); }
                    using (var sb = new SolidBrush(sc)) g.FillEllipse(sb, bx - br0, by - br0, br0 * 2, br0 * 2);
                }
                else DrawMoon(g, new PointF(bx, by), br0 * 0.85f, moonPhase);
                // Wolken (Tag und Sonnenuntergang)
                double cloud = hr >= 6.5 && hr < 20.5 ? 1 : 0.15;
                Color cc = hr < 8 || hr > 17.5 ? C(0xff, 0xc4, 0xa8) : C(0xff, 0xff, 0xff);
                for (int k = 0; k < Math.Max(3, w / 320); k++)
                {
                    float cx = (float)(rnd.NextDouble() * w), cy = (float)(h * (0.12 + rnd.NextDouble() * 0.45)), cw = (float)(w * (0.08 + rnd.NextDouble() * 0.1));
                    for (int j = 0; j < 7; j++)
                    {
                        float ex = cx + (float)((rnd.NextDouble() - 0.5) * cw), ey = cy + (float)((rnd.NextDouble() - 0.5) * cw * 0.18), er = (float)(cw * (0.18 + rnd.NextDouble() * 0.2));
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(ex - er, ey - er * 0.6f, er * 2, er * 1.2f);
                            using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(70 * cloud), cc), SurroundColors = new[] { Color.FromArgb(0, cc) } }) g.FillPath(pb, path);
                        }
                    }
                }
                int dim = Math.Max(0, Math.Min(90, dimPct + (int)Math.Round((1 - dark) * 22)));   // tagsueber kraeftiger abdunkeln, damit die Schrift lesbar bleibt
                using (var db = new SolidBrush(Color.FromArgb(dim * 255 / 100, 0, 0, 0))) g.FillRectangle(db, 0, 0, w, h);
            }
            return bmp;
        }

        /// <summary>Mond mit Phase (hell beleuchteter Teil; Schatten als Ellipse).</summary>
        public static void DrawMoon(Graphics g, PointF c, float r, double phase)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color lit = C(0xf4, 0xf0, 0xdc), dark = Color.FromArgb(200, 0x1c, 0x22, 0x34);
            for (int i = 4; i >= 1; i--) { float rr = r * (1 + i * 0.35f); using (var b = new SolidBrush(Color.FromArgb(8, lit))) g.FillEllipse(b, c.X - rr, c.Y - rr, rr * 2, rr * 2); }
            using (var b = new SolidBrush(dark)) g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
            // beleuchteter Teil: Halbkreis + Ellipse (Terminator)
            double k = Math.Cos(phase * 2 * Math.PI);   // 1 Neumond … −1 Vollmond
            bool waxing = phase < 0.5;
            using (var path = new GraphicsPath())
            {
                var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
                path.AddArc(rect, waxing ? -90 : 90, 180);   // rechte Haelfte zunehmend, linke abnehmend
                float ew = (float)(r * Math.Abs(k));
                var er = new RectangleF(c.X - ew, c.Y - r, Math.Max(0.01f, ew * 2), r * 2);
                // Terminator: bei Sichel nach innen, bei Dreiviertel nach aussen
                if (k > 0) path.AddArc(er, waxing ? 90 : -90, -180);
                else path.AddArc(er, waxing ? 90 : -90, 180);
                path.CloseFigure();
                using (var b = new SolidBrush(lit)) g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(30, 0x80, 0x78, 0x60)))
            {
                g.FillEllipse(b, c.X - r * 0.35f, c.Y - r * 0.2f, r * 0.3f, r * 0.26f);
                g.FillEllipse(b, c.X + r * 0.1f, c.Y + r * 0.25f, r * 0.24f, r * 0.2f);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Weltraum: Nebel in Violett/Blau, viele Sterne, einige mit Strahlenkreuz.</summary>
        public static Bitmap MakeSpaceBg(int w, int h)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            var rnd = new Random(77);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), C(0x04, 0x05, 0x0c), C(0x0a, 0x0c, 0x1e), LinearGradientMode.Vertical)) g.FillRectangle(b, 0, 0, w, h);
                Color[] neb = { C(0x6a, 0x2c, 0xa8), C(0x1e, 0x4c, 0xc0), C(0x1c, 0x9c, 0xb0), C(0xa0, 0x2c, 0x7c) };
                for (int i = 0; i < 9; i++)
                {
                    float cx = (float)(rnd.NextDouble() * w), cy = (float)(rnd.NextDouble() * h), rx = (float)(w * (0.15 + rnd.NextDouble() * 0.25)), ry = rx * (float)(0.4 + rnd.NextDouble() * 0.5);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                        var c = neb[i % neb.Length];
                        using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(46, c), SurroundColors = new[] { Color.FromArgb(0, c) } }) g.FillPath(pb, path);
                    }
                }
                int n = Math.Max(80, w * h / 1500);
                for (int i = 0; i < n; i++)
                {
                    float x = (float)(rnd.NextDouble() * w), y = (float)(rnd.NextDouble() * h);
                    double br = rnd.NextDouble();
                    float s = br > 0.97 ? 2.2f : br > 0.8 ? 1.3f : 0.8f;
                    Color sc = br > 0.9 ? C(0xc8, 0xdc, 0xff) : C(0xff, 0xff, 0xff);
                    using (var b = new SolidBrush(Color.FromArgb((int)(70 + 185 * br * br), sc))) g.FillEllipse(b, x - s / 2, y - s / 2, s, s);
                    if (br > 0.985)
                        using (var p = new Pen(Color.FromArgb(70, sc), 1f)) { g.DrawLine(p, x - s * 3, y, x + s * 3, y); g.DrawLine(p, x, y - s * 3, x, y + s * 3); }
                }
            }
            return bmp;
        }

        // ================================================================ 224 Neon-Schild

        private static GraphicsPath _neonPath;
        private static string _neonKey;

        /// <summary>Schriftzug als Pfad, passend in r (zwischengespeichert, solange Text und Platz gleich bleiben).</summary>
        private static GraphicsPath NeonPath(string text, RectangleF r)
        {
            string key = text + "|" + r.X + "|" + r.Y + "|" + r.Width + "|" + r.Height + "|" + S;
            if (_neonPath != null && key == _neonKey) return _neonPath;
            var path = new GraphicsPath();
            FontFamily ff;
            try { ff = new FontFamily("Segoe Script"); } catch { ff = new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif); }
            using (ff)
            {
                float px = r.Height * 0.78f;
                path.AddString(text, ff, (int)FontStyle.Bold, px, new PointF(0, 0), StringFormat.GenericTypographic);
                var b0 = path.GetBounds();
                if (b0.Width > 0 && b0.Height > 0)
                {
                    float k = Math.Min(1f, Math.Min((r.Width - F(10)) / b0.Width, (r.Height - F(4)) / b0.Height));
                    using (var m = new Matrix())
                    {
                        m.Translate(r.X + (r.Width - b0.Width * k) / 2f - b0.X * k, r.Y + (r.Height - b0.Height * k) / 2f - b0.Y * k);
                        m.Scale(k, k);
                        path.Transform(m);
                    }
                }
            }
            _neonPath?.Dispose();
            _neonPath = path; _neonKey = key;
            return path;
        }

        /// <summary>Neon-Schrift: mehrere weiche Leuchtschichten, heller Kern; on = 0..1 (Flackern), pulse = 0..1 (zur Musik).</summary>
        public static void DrawNeon(Graphics g, RectangleF r, string text, Color c, float on, float pulse)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 10 || r.Height < 8) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            {
                var path = NeonPath(text, r);
                float lvl = Math.Max(0f, Math.Min(1f, on)) * (0.75f + 0.25f * Math.Max(0f, Math.Min(1f, pulse)));
                // ausgeschaltete Roehre: dunkles Glas
                using (var tube = new Pen(Color.FromArgb(120, Darken(c, 0.6f)), F(2.2f)) { LineJoin = LineJoin.Round }) g.DrawPath(tube, path);
                if (lvl > 0.01f)
                {
                    float[] widths = { 14f, 9f, 5.5f, 3.2f };
                    int[] alphas = { 16, 26, 52, 120 };
                    for (int i = 0; i < widths.Length; i++)
                        using (var pen = new Pen(Color.FromArgb((int)(alphas[i] * lvl), c), F(widths[i])) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawPath(pen, path);
                    using (var core = new Pen(Color.FromArgb((int)(255 * lvl), Lighten(c, 0.7f)), F(1.5f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawPath(core, path);
                }
            }
            g.SmoothingMode = sm;
        }
    }
}
