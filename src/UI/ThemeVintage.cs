using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// Design "Vintage" (2.12): zeichnet wie "Hell", aber warm – cremefarbene Platten, Nussbaum-Holz an den Seiten,
    /// Bakelit-Knoepfe, Nixie-Anzeigen. Ausserdem: Kanalfarbe auf Faderkappen und Knoepfen (alle Designs).
    /// </summary>
    internal static partial class Theme
    {
        /// <summary>Design Vintage aktiv (Style ist dann StyleLight).</summary>
        public static bool Vintage;

        /// <summary>Schrift direkt auf dem Pult-Grund (Bereichs-Titel "KANÄLE"): auf Holz hell.</summary>
        public static Color BodyText => Vintage ? Color.FromArgb(0xd9, 0xc4, 0xa0) : TextFaint;

        private static void VintageColors(int scheme)
        {
            Color cream, creamDark, edge, wood;
            switch (scheme)
            {
                case 1: cream = C(0xece6d6); creamDark = C(0xdcd3bd); edge = C(0x7c6f58); wood = C(0x2a2018); break;   // heller, kuehler
                case 2: cream = C(0xe8dcc4); creamDark = C(0xd6c6a6); edge = C(0x76634a); wood = C(0x24180f); break;   // dunkler, kraeftiger
                default: cream = C(0xf0e6cf); creamDark = C(0xe0d2b4); edge = C(0x7e6a4e); wood = C(0x2e1f14); break;
            }
            Body = wood; WindowTop = Lighten(cream, 0.1f); WindowBottom = cream; BarBg = Darken(cream, 0.04f);
            PanelTop = cream; PanelBottom = creamDark; PanelHighlight = Lighten(cream, 0.6f); PanelEdge = edge;
            Text = C(0x2b2118); TextDim = C(0x5c4c3b); TextFaint = C(0x8a7862); TextHover = Color.Black;
            MenuBg = Lighten(cream, 0.45f); MenuHover = Darken(MenuBg, 0.08f); MenuBorder = Darken(MenuBg, 0.28f);
            MenuSep = Darken(MenuBg, 0.14f); MenuSepLight = MenuBg; CheckBg = Darken(MenuBg, 0.1f); CheckSelBg = Darken(MenuBg, 0.2f);
            Well = Lighten(cream, 0.45f); WellHover = Lighten(cream, 0.6f); WellEdge = Darken(creamDark, 0.22f);
            PlateRim = Darken(creamDark, 0.3f); Hole = C(0x1a1410); Slot = Darken(creamDark, 0.35f); SegOff = C(0x1f1a15);
            Chip = Darken(creamDark, 0.08f); Disabled = C(0xa89880); RingOff = Darken(creamDark, 0.2f);
            ScaleTick = C(0x9a876c); ScaleLabel = C(0x6e5c46); EngraveText = Darken(creamDark, 0.25f);
            KeyTop = C(0xfbf5e6); KeyBottom = Darken(cream, 0.04f); KeyHoverTop = Color.White; KeyHoverBottom = Lighten(cream, 0.35f); KeyEdge = Darken(creamDark, 0.32f);
            DispBg = Lighten(cream, 0.5f); DispHover = Lighten(cream, 0.7f); DispEdge = Darken(creamDark, 0.22f); DispEdgeHover = Darken(creamDark, 0.45f);
            BtnTop = Lighten(cream, 0.55f); BtnBottom = Darken(cream, 0.03f); BtnHoverTop = Color.White; BtnHoverBottom = Lighten(cream, 0.3f); BtnEdge = Darken(creamDark, 0.3f);
            BadgeBg = Darken(cream, 0.08f); BadgeEdge = Darken(cream, 0.22f); LogoCap = C(0x6e5a44);
            // Holz-Grund: Bedienelemente darauf hell
            ScrollTrack = Lighten(wood, 0.08f); ScrollThumb = C(0x8a6a48); ScrollThumbHover = C(0xc49a66);
            AddIdle = C(0x9c8264); AddHoverBg = Lighten(wood, 0.06f);
            DivDark = Darken(wood, 0.5f); DivLight = Lighten(wood, 0.14f);
            PopupBg = Lighten(cream, 0.4f); PopupHeadTop = Lighten(cream, 0.6f); PopupHeadBottom = cream; PopupHover = Darken(PopupBg, 0.05f); PopupLine = Darken(PopupBg, 0.12f);
            Deep = Darken(creamDark, 0.1f); Grid = Darken(creamDark, 0.2f); GridZero = Darken(creamDark, 0.36f);
            Input = Lighten(cream, 0.6f); InputFocus = C(0xf6ecd4); Inset = Darken(MenuBg, 0.03f);
            Button = Darken(MenuBg, 0.07f); ButtonHover = Darken(MenuBg, 0.14f); ButtonEdge = Darken(MenuBg, 0.28f); ButtonText = Text;
            Primary = C(0x7a5a2e); PrimaryHover = C(0x8e6a36);
            Track = Darken(MenuBg, 0.15f); HoverFill = Darken(PopupBg, 0.1f);
            PadTop = Lighten(cream, 0.5f); PadBottom = Darken(MenuBg, 0.06f); PadHoverTop = Color.White; PadHoverBottom = Darken(MenuBg, 0.02f);
            FootBtn = Darken(PopupBg, 0.05f); FootBtnHover = Darken(PopupBg, 0.12f); FootBtnEdge = Darken(PopupBg, 0.25f);
            LcdBg = C(0x120a06);
            ValueText = Darken(Accent, 0.4f);
        }

        /// <summary>Bakelit-Knopf: dunkelbraun glaenzend, geriffelter Rand, cremefarbener Strich.</summary>
        private static void VintageKnobBody(Graphics g, PointF c, float radius, float ca, float sa, bool hover)
        {
            FillEllipse(g, Color.FromArgb(70, 0, 0, 0), c.X, c.Y + F(1.6f), radius + F(0.5f));
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, hover ? C(0x5a3a26) : C(0x4a2e1d), C(0x150b06), 70f))
                g.FillEllipse(b, outer);
            // Riffelung am Rand
            using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), Math.Max(1f, F(0.7f))))
                for (int i = 0; i < 24; i++)
                {
                    double a = i * Math.PI * 2 / 24;
                    float x1 = c.X + (float)Math.Cos(a) * radius * 0.82f, y1 = c.Y + (float)Math.Sin(a) * radius * 0.82f;
                    float x2 = c.X + (float)Math.Cos(a) * radius * 0.98f, y2 = c.Y + (float)Math.Sin(a) * radius * 0.98f;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            // glaenzende Kappe
            float ir = radius * 0.72f;
            var inner = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            using (var b = new LinearGradientBrush(inner, hover ? C(0x6a4630) : C(0x5a3a26), C(0x1c0f08), 95f))
                g.FillEllipse(b, inner);
            using (var b = new SolidBrush(Color.FromArgb(70, 255, 240, 220)))
                g.FillEllipse(b, c.X - ir * 0.62f, c.Y - ir * 0.82f, ir * 0.9f, ir * 0.55f);
            using (var pen = new Pen(Color.FromArgb(0x0c, 0x06, 0x03), F(1))) g.DrawEllipse(pen, outer);
            using (var pen = new Pen(C(0xf1e3c4), F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X + ca * radius * 0.28f, c.Y + sa * radius * 0.28f, c.X + ca * radius * 0.86f, c.Y + sa * radius * 0.86f);
        }

        // ---------------------------------------------------------------- Holz

        private static Bitmap _wood;

        /// <summary>Nussbaum-Maserung (einmal erzeugt, senkrecht kachelbar).</summary>
        private static Bitmap WoodTexture()
        {
            if (_wood != null) return _wood;
            const int W = 96, H = 512;
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var px = new int[W * H];
            var rnd = new Random(1970);
            var phase = new double[6];
            for (int i = 0; i < phase.Length; i++) phase[i] = rnd.NextDouble() * Math.PI * 2;
            for (int y = 0; y < H; y++)
            {
                double v = y / (double)H * Math.PI * 2;
                for (int x = 0; x < W; x++)
                {
                    // Maserung: Linien entlang y, leicht wellig; Ringe enger und weiter
                    double xx = x + 4.0 * Math.Sin(v * 2 + phase[0]) + 2.0 * Math.Sin(v * 5 + phase[1] + x * 0.05);
                    double ring = Math.Sin(xx * 0.55 + 3.0 * Math.Sin(xx * 0.07 + phase[2]) + Math.Sin(v + phase[3]));
                    double fine = Math.Sin(xx * 2.3 + phase[4]) * 0.25 + (rnd.NextDouble() - 0.5) * 0.18;
                    double t = 0.5 + 0.32 * ring + 0.12 * fine;
                    t = Math.Max(0, Math.Min(1, t));
                    int r = (int)(0x3a + t * (0x7a - 0x3a)), gg = (int)(0x22 + t * (0x4c - 0x22)), b = (int)(0x12 + t * (0x2a - 0x12));
                    px[y * W + x] = unchecked((int)0xff000000) | (r << 16) | (gg << 8) | b;
                }
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            _wood = bmp;
            return bmp;
        }

        /// <summary>Holz-Seitenteil (left = links): Maserung, Lack-Glanz, abgerundete Kante zum Pult hin.</summary>
        public static void DrawWoodCheek(Graphics g, Rectangle r, bool left)
        {
            using (var tb = new TextureBrush(WoodTexture(), WrapMode.TileFlipX))
            {
                tb.TranslateTransform(left ? 0 : 37, 0);
                g.FillRectangle(tb, r);
            }
            // Lack: oben heller, Rundung zur Seite (innen dunkler, aussen Glanzkante)
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.Black, Color.Black, LinearGradientMode.Horizontal))
            {
                var outerC = Color.FromArgb(70, 255, 230, 200);
                var innerC = Color.FromArgb(110, 0, 0, 0);
                b.InterpolationColors = new ColorBlend
                {
                    Colors = left ? new[] { Color.FromArgb(40, 0, 0, 0), outerC, Color.FromArgb(0, 0, 0, 0), innerC }
                                  : new[] { innerC, Color.FromArgb(0, 0, 0, 0), outerC, Color.FromArgb(40, 0, 0, 0) },
                    Positions = new[] { 0f, left ? 0.22f : 0.3f, left ? 0.7f : 0.78f, 1f },
                };
                g.FillRectangle(b, r);
            }
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(36, 255, 240, 220), Color.FromArgb(50, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
        }

        // ---------------------------------------------------------------- Kanalfarbe auf Fadern und Knoepfen

        /// <summary>Farbige Einlage in der Faderkappe (wie farbige Kappen am Pult).</summary>
        public static void DrawCapInsert(Graphics g, RectangleF cap, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Math.Max(3f, F(4.2f));
            var r = new RectangleF(cap.X + F(2.5f), cap.Y + cap.Height / 2f - h / 2f, cap.Width - F(5f), h);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(c, 0.25f), Darken(c, 0.15f), LinearGradientMode.Vertical))
                FillRound(g, b, r, F(1.5f));
            StrokeRound(g, Darken(c, 0.5f), Math.Max(1f, F(0.7f)), r, F(1.5f));
            using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                g.DrawLine(pen, r.X + F(2), r.Y + 1, r.Right - F(2), r.Y + 1);
            g.SmoothingMode = sm;
        }

        /// <summary>Farbige Kappe oben auf einem Drehknopf.</summary>
        public static void DrawKnobCap(Graphics g, PointF c, float knobRadius, Color col)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = knobRadius * 0.42f;
            var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            using (var b = new LinearGradientBrush(rect, Lighten(col, 0.3f), Darken(col, 0.25f), 70f)) g.FillEllipse(b, rect);
            using (var pen = new Pen(Darken(col, 0.55f), Math.Max(1f, F(0.7f)))) g.DrawEllipse(pen, rect);
            using (var b = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillEllipse(b, c.X - r * 0.55f, c.Y - r * 0.7f, r * 0.8f, r * 0.5f);
            g.SmoothingMode = sm;
        }
    }
}
