using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// "Studio-Hardware": gebuerstetes Metall, Raendelknoepfe mit Alu-Kappe, Schrauben, hinterleuchtete Namensschilder,
    /// Glas ueber Pegeln. Alles Statische landet im Bild-Cache der Zuege – nur das Glas ueber den Pegeln wird laufend gezeichnet.
    /// </summary>
    internal static partial class Theme
    {
        private static TextureBrush _brushed;

        /// <summary>Feine waagerechte Schliffspuren (neutral hell/dunkel, passt zu jedem Farbschema).</summary>
        private static TextureBrush BrushedBrush
        {
            get
            {
                if (_brushed != null) return _brushed;
                const int W = 512, H = 256;
                var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                var px = new int[W * H];
                var rnd = new Random(1234);
                double row = 0;
                for (int y = 0; y < H; y++)
                {
                    row = row * 0.5 + (rnd.NextDouble() - 0.5) * 0.9;     // Helligkeit der Zeile
                    double run = 0;
                    for (int x = 0; x < W; x++)
                    {
                        run = run * 0.93 + (rnd.NextDouble() - 0.5) * 0.32; // weich entlang des Schliffs
                        double v = row + run;
                        int a = (int)Math.Min(60, Math.Abs(v) * 20);
                        int c = v > 0 ? 255 : 0;
                        px[y * W + x] = (a << 24) | (c << 16) | (c << 8) | c;
                    }
                }
                var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(px, 0, data.Scan0, px.Length);
                bmp.UnlockBits(data);
                _brushed = new TextureBrush(bmp, WrapMode.Tile);
                return _brushed;
            }
        }

        /// <summary>Gebuerstete Oberflaeche ueber eine Flaeche legen (Muster beginnt an der linken oberen Ecke).</summary>
        public static void FillBrushed(Graphics g, GraphicsPath path, RectangleF r)
        {
            var tb = BrushedBrush;
            tb.ResetTransform();
            tb.TranslateTransform((float)Math.Round(r.X), (float)Math.Round(r.Y));
            g.FillPath(tb, path);
        }

        public static void FillBrushed(Graphics g, RectangleF r)
        {
            var tb = BrushedBrush;
            tb.ResetTransform();
            tb.TranslateTransform((float)Math.Round(r.X), (float)Math.Round(r.Y));
            g.FillRectangle(tb, r);
        }

        /// <summary>Drehknopf: geriffelter Gummigriff, gedrehte Alu-Kappe, eingravierter Zeiger. pos = 0..1.</summary>
        public static void DrawKnobBody(Graphics g, PointF c, float radius, float pos, bool hover)
        {
            // Schatten
            FillEllipse(g, Color.FromArgb(70, 0, 0, 0), c.X + F(0.6f), c.Y + F(2.6f), radius + F(1.4f));
            FillEllipse(g, Color.FromArgb(90, 0, 0, 0), c.X, c.Y + F(1.4f), radius + F(0.6f));

            // Griff (Gummi)
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, Color.FromArgb(0x3a, 0x3e, 0x45), Color.FromArgb(0x0d, 0x0e, 0x10), 70f))
                g.FillEllipse(b, outer);
            // Riffelung
            int ridges = Math.Max(18, Math.Min(40, (int)(radius * 2.4f)));
            using (var light = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, F(0.8f))))
            using (var dark = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1f, F(0.8f))))
            {
                for (int i = 0; i < ridges; i++)
                {
                    double a = 2 * Math.PI * i / ridges;
                    float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    var p1 = new PointF(c.X + cs * radius * 0.80f, c.Y + sn * radius * 0.80f);
                    var p2 = new PointF(c.X + cs * radius * 0.97f, c.Y + sn * radius * 0.97f);
                    // oben links Licht, unten rechts Schatten
                    bool lit = (cs * -0.6f + sn * -0.8f) > -0.2f;
                    g.DrawLine(i % 2 == 0 ? (lit ? light : dark) : dark, p1, p2);
                }
            }
            using (var pen = new Pen(Color.FromArgb(0x05, 0x05, 0x06), F(1))) g.DrawEllipse(pen, outer);

            // Kappe: gedrehtes Aluminium
            float ir = radius * 0.72f;
            var inner = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            Color hi = hover ? Color.FromArgb(0xd6, 0xda, 0xe0) : Color.FromArgb(0xbd, 0xc2, 0xc9);
            Color lo = hover ? Color.FromArgb(0x5e, 0x63, 0x6b) : Color.FromArgb(0x4c, 0x51, 0x58);
            using (var b = new LinearGradientBrush(inner, hi, lo, 55f))
                g.FillEllipse(b, inner);
            // Drehrillen (konzentrisch)
            for (int k = 1; k <= 4; k++)
            {
                float rr = ir * k / 5f;
                using (var pen = new Pen(Color.FromArgb(k % 2 == 0 ? 22 : 16, k % 2 == 0 ? 255 : 0, k % 2 == 0 ? 255 : 0, k % 2 == 0 ? 255 : 0), Math.Max(1f, F(0.7f))))
                    g.DrawEllipse(pen, c.X - rr, c.Y - rr, rr * 2, rr * 2);
            }
            // Glanzpunkt
            using (var path = new GraphicsPath())
            {
                float gr = ir * 0.7f;
                var gl = new RectangleF(c.X - ir * 0.55f - gr / 2, c.Y - ir * 0.55f - gr / 2, gr, gr);
                path.AddEllipse(gl);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(110, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillEllipse(pb, gl);
            }
            // Kante der Kappe
            using (var pen = new Pen(Color.FromArgb(150, 0x10, 0x11, 0x13), F(1))) g.DrawEllipse(pen, inner);
            using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, F(0.7f))))
                g.DrawArc(pen, inner.X + F(0.8f), inner.Y + F(0.8f), inner.Width - F(1.6f), inner.Height - F(1.6f), 190, 80);

            // Zeiger: eingraviert, laeuft bis in den Griff
            double a2 = (-135 + 270.0 * Math.Max(0f, Math.Min(1f, pos)) - 90) * Math.PI / 180.0;
            float ca = (float)Math.Cos(a2), sa = (float)Math.Sin(a2);
            var q1 = new PointF(c.X + ca * ir * 0.18f, c.Y + sa * ir * 0.18f);
            var q2 = new PointF(c.X + ca * (radius - F(1.2f)), c.Y + sa * (radius - F(1.2f)));
            using (var pen = new Pen(Color.FromArgb(0x16, 0x17, 0x1a), F(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, q1, q2);
            // helle Markierung auf dem Griff
            var m1 = new PointF(c.X + ca * ir * 1.02f, c.Y + sa * ir * 1.02f);
            using (var pen = new Pen(Color.FromArgb(0xf4, 0xf6, 0xf8), F(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, m1, q2);
        }

        /// <summary>Senkkopfschraube (Kreuzschlitz).</summary>
        public static void DrawScrew(Graphics g, float cx, float cy, float r, float angleDeg)
        {
            FillEllipse(g, Color.FromArgb(120, 0, 0, 0), cx, cy + F(0.6f), r + F(0.8f));
            var rc = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var b = new LinearGradientBrush(rc, Color.FromArgb(0xa9, 0xae, 0xb6), Color.FromArgb(0x3c, 0x40, 0x46), 50f))
                g.FillEllipse(b, rc);
            using (var pen = new Pen(Color.FromArgb(0x0a, 0x0b, 0x0c), Math.Max(1f, F(0.7f)))) g.DrawEllipse(pen, rc);
            double a = angleDeg * Math.PI / 180;
            float s = r * 0.62f;
            using (var dark = new Pen(Color.FromArgb(0x16, 0x17, 0x19), Math.Max(1f, F(1.1f))))
            using (var light = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, F(0.6f))))
            {
                for (int k = 0; k < 2; k++)
                {
                    double b = a + k * Math.PI / 2;
                    float dx = (float)Math.Cos(b) * s, dy = (float)Math.Sin(b) * s;
                    g.DrawLine(dark, cx - dx, cy - dy, cx + dx, cy + dy);
                    g.DrawLine(light, cx - dx + F(0.6f), cy - dy + F(0.6f), cx + dx + F(0.6f), cy + dy + F(0.6f));
                }
            }
        }

        /// <summary>Hinterleuchtetes Namensschild in Kanalfarbe (Text zeichnet der Zug danach).</summary>
        public static void DrawNamePlate(Graphics g, RectangleF r, Color bg)
        {
            if (DymoPlates) { DrawDymoPlate(g, r, bg); return; }   // 3.3
            if (TapePlates) { DrawTapePlate(g, r, bg); return; }   // 2.18
            if (DigitalPlates) { DrawDigitalPlate(g, r, bg); return; }
            if (BigConsole) { DrawConsolePlate(g, r, bg); return; }   // 4.0 finale: weisser Beschriftungsstreifen
            if (Style != StyleStudio) { StyledNamePlate(g, r, bg); return; }
            float rad = F(3.5f);
            using (var path = Round(r, rad))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(bg, 0.14f), Darken(bg, 0.2f), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                // Licht von hinten: heller Kern
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterPoint = new PointF(r.X + r.Width / 2, r.Y + r.Height * 0.55f);
                    pb.CenterColor = Alpha(Lighten(bg, 0.35f), 120);
                    pb.SurroundColors = new[] { Alpha(bg, 0) };
                    pb.FocusScales = new PointF(0.55f, 0.2f);
                    g.FillPath(pb, path);
                }
            }
            // Glas obendrauf
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f + 1), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(r.X + F(1), r.Y + F(1), r.Width - F(2), r.Height * 0.46f), rad - 1);
            StrokeRound(g, Alpha(Lighten(bg, 0.45f), 80), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
        }

        /// <summary>Vertiefte Fassung um die Pegelanzeige (im Bild-Cache).</summary>
        public static void DrawMeterBezel(Graphics g, RectangleF r)
        {
            if (NixieBar) { DrawNixieBezel(g, r); return; }   // 4.0
            if (Style != StyleStudio) { StyledMeterBezel(g, r); return; }
            FillRound(g, Hole, r, F(2.5f));
            using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), F(1)))
                g.DrawLine(pen, r.X + F(2), r.Y + F(0.5f), r.Right - F(2), r.Y + F(0.5f));
            using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), F(1)))
            {
                g.DrawLine(pen, r.X + F(2), r.Bottom + F(0.5f), r.Right - F(2), r.Bottom + F(0.5f));
                g.DrawLine(pen, r.Right + F(0.5f), r.Y + F(2), r.Right + F(0.5f), r.Bottom - F(2));
            }
        }

        /// <summary>Glasscheibe ueber den Pegeln (wird nach den LEDs gezeichnet).</summary>
        public static void DrawMeterGlass(Graphics g, RectangleF r)
        {
            if (NixieBar) { DrawNixieGlass(g, r); return; }   // 4.0
            if (Style != StyleStudio || r.Width <= 0 || r.Height <= 0) return;
            float h = r.Height * 0.38f;
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, h + 2), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillRectangle(b, r.X, r.Y, r.Width, h);
            using (var b = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
                g.FillRectangle(b, r.X, r.Y, Math.Max(1f, F(1.2f)), r.Height);
        }
    }
}
