using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.5: Roehren, Geraete-Bilder, Lauflicht.</summary>
    internal static partial class Theme
    {
        /// <summary>Verstaerkerroehre: Glaskolben, Anode, Heizfaden, der mit glow (0..1) orange gluehen.</summary>
        public static void DrawTube(Graphics g, RectangleF r, float glow)
        {
            if (r.Width < 8 || r.Height < 16) return;
            glow = Math.Max(0f, Math.Min(1.2f, glow));
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float baseH = Math.Max(F(5), r.Height * 0.16f);
            var glass = new RectangleF(r.X + r.Width * 0.08f, r.Y, r.Width * 0.84f, r.Height - baseH);
            var socket = new RectangleF(r.X, r.Bottom - baseH, r.Width, baseH);
            // Schein hinter dem Kolben
            var hot = Color.FromArgb(0xff, 0x8a, 0x2a);
            if (glow > 0.05f)
                using (var path = new GraphicsPath())
                {
                    var halo = RectangleF.Inflate(glass, glass.Width * 0.5f, glass.Height * 0.2f);
                    path.AddEllipse(halo);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)Math.Min(120, 110 * glow), hot), SurroundColors = new[] { Color.FromArgb(0, hot) } })
                        g.FillPath(pb, path);
                }
            // Kolben (oben rund)
            using (var path = new GraphicsPath())
            {
                float rad = glass.Width / 2f;
                path.AddArc(glass.X, glass.Y, glass.Width, glass.Width, 180, 180);
                path.AddLine(glass.Right, glass.Y + rad, glass.Right, glass.Bottom);
                path.AddLine(glass.Right, glass.Bottom, glass.X, glass.Bottom);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(glass, Color.FromArgb(70, 0x9a, 0xa8, 0xb8), Color.FromArgb(30, 0x50, 0x58, 0x66), LinearGradientMode.Horizontal))
                    g.FillPath(b, path);
                // Anode (graues Blech) und Heizfaden
                var plate = new RectangleF(glass.X + glass.Width * 0.22f, glass.Y + glass.Height * 0.3f, glass.Width * 0.56f, glass.Height * 0.55f);
                using (var b = new LinearGradientBrush(plate, Color.FromArgb(0x55, 0x5a, 0x62), Color.FromArgb(0x2a, 0x2d, 0x33), LinearGradientMode.Horizontal))
                    g.FillRectangle(b, plate);
                var fil = new RectangleF(plate.X + plate.Width * 0.38f, plate.Y + plate.Height * 0.08f, plate.Width * 0.24f, plate.Height * 0.84f);
                Color fc = Mix(Color.FromArgb(0x50, 0x24, 0x10), Color.FromArgb(0xff, 0xc0, 0x60), Math.Min(1f, glow));
                using (var b = new SolidBrush(fc)) g.FillRectangle(b, fil);
                if (glow > 0.1f)
                    using (var p2 = new GraphicsPath())
                    {
                        var gl = RectangleF.Inflate(fil, plate.Width * 0.5f, plate.Height * 0.15f);
                        p2.AddEllipse(gl);
                        using (var pb = new PathGradientBrush(p2) { CenterColor = Color.FromArgb((int)Math.Min(200, 190 * glow), 0xff, 0xa0, 0x40), SurroundColors = new[] { Color.FromArgb(0, 0xff, 0x80, 0x20) } })
                            g.FillPath(pb, p2);
                    }
                // Spitze oben und Glanz
                using (var b = new SolidBrush(Color.FromArgb(90, 0xc8, 0xd0, 0xd8))) g.FillEllipse(b, glass.X + glass.Width * 0.44f, glass.Y - F(1.5f), glass.Width * 0.12f, F(3));
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, F(1.2f))))
                    g.DrawLine(pen, glass.X + glass.Width * 0.2f, glass.Y + glass.Width * 0.45f, glass.X + glass.Width * 0.2f, glass.Bottom - F(3));
                using (var pen = new Pen(Color.FromArgb(110, 0xa0, 0xaa, 0xb6), Math.Max(1f, F(1))))
                    g.DrawPath(pen, path);
            }
            // Sockel
            using (var b = new LinearGradientBrush(socket, Color.FromArgb(0x30, 0x22, 0x18), Color.FromArgb(0x12, 0x0c, 0x08), LinearGradientMode.Vertical))
                FillRound(g, b, socket, F(2));
            g.SmoothingMode = sm;
        }

        /// <summary>Geraete-Bild: 1 Lautsprecher, 3 Kopfhoerer, 5 Headset, 8/9 Bildschirm (HDMI/SPDIF), sonst Lautsprecher. Bluetooth = kleines Zeichen unten rechts.</summary>
        public static void DrawDeviceIcon(Graphics g, RectangleF r, int formFactor, bool bluetooth, bool usb, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height) / 16f, x = r.X + (r.Width - 16 * s) / 2f, y = r.Y + (r.Height - 16 * s) / 2f;
            PointF P(float px, float py) => new PointF(x + px * s, y + py * s);
            using (var pen = new Pen(c, Math.Max(1.2f, 1.5f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(c))
            {
                switch (formFactor)
                {
                    case 3:   // Kopfhoerer
                    case 5:   // Headset (mit Mikrofon-Buegel)
                        g.DrawArc(pen, x + 2.5f * s, y + 2 * s, 11 * s, 11 * s, 180, 180);
                        g.FillRectangle(b, x + 1.5f * s, y + 8 * s, 3 * s, 5.5f * s);
                        g.FillRectangle(b, x + 11.5f * s, y + 8 * s, 3 * s, 5.5f * s);
                        if (formFactor == 5)
                        {
                            g.DrawBezier(pen, P(3, 13.2f), P(3.5f, 15.5f), P(6, 15.6f), P(8, 15.2f));
                            g.FillEllipse(b, x + 7.6f * s, y + 14.2f * s, 2 * s, 2 * s);
                        }
                        break;
                    case 8:
                    case 9:   // Bildschirm (HDMI, DisplayPort, SPDIF)
                        g.DrawRectangle(pen, x + 1.5f * s, y + 2.5f * s, 13 * s, 8.5f * s);
                        g.DrawLine(pen, P(8, 11), P(8, 13.5f));
                        g.DrawLine(pen, P(5, 13.8f), P(11, 13.8f));
                        break;
                    default:  // Lautsprecher (Box mit zwei Membranen)
                        g.DrawRectangle(pen, x + 3.5f * s, y + 1.5f * s, 9 * s, 13 * s);
                        g.DrawEllipse(pen, x + 6.5f * s, y + 3.3f * s, 3 * s, 3 * s);
                        g.DrawEllipse(pen, x + 5.3f * s, y + 7.6f * s, 5.4f * s, 5.4f * s);
                        break;
                }
            }
            if (bluetooth || usb)
            {
                var badge = new RectangleF(x + 10 * s, y + 9.5f * s, 7 * s, 7 * s);
                FillRound(g, bluetooth ? Color.FromArgb(0x2a, 0x6c, 0xd8) : Color.FromArgb(0x55, 0x5c, 0x66), badge, 2 * s);
                using (var pen = new Pen(Color.White, Math.Max(1f, 0.9f * s)) { LineJoin = LineJoin.Round })
                {
                    float bx = badge.X, by = badge.Y, k = badge.Width / 7f;
                    if (bluetooth)
                        g.DrawLines(pen, new[] { new PointF(bx + 2 * k, by + 2.2f * k), new PointF(bx + 5 * k, by + 4.6f * k), new PointF(bx + 3.5f * k, by + 5.8f * k),
                            new PointF(bx + 3.5f * k, by + 1.2f * k), new PointF(bx + 5 * k, by + 2.4f * k), new PointF(bx + 2 * k, by + 4.8f * k) });
                    else
                    {
                        g.DrawLine(pen, bx + 3.5f * k, by + 1.4f * k, bx + 3.5f * k, by + 5.8f * k);
                        g.DrawLine(pen, bx + 3.5f * k, by + 4.2f * k, bx + 2 * k, by + 3f * k);
                        g.DrawLine(pen, bx + 3.5f * k, by + 3.6f * k, bx + 5 * k, by + 2.5f * k);
                    }
                }
            }
            g.SmoothingMode = sm;
        }

        public static string DeviceKindName(int formFactor, bool bluetooth, bool usb)
        {
            string k = formFactor == 3 ? "Kopfhörer" : formFactor == 5 ? "Headset" : formFactor == 9 ? "Bildschirm (HDMI/DisplayPort)" : formFactor == 8 ? "Digital (S/PDIF)"
                     : formFactor == 1 ? "Lautsprecher" : formFactor == 2 ? "Line-Ausgang" : formFactor < 0 ? "unbekannt" : "Ausgang";
            return k + (bluetooth ? " · Bluetooth" : usb ? " · USB" : "");
        }

        /// <summary>Lauflicht: ein heller Streifen faehrt ueber den Zug (t 0..1 = links nach rechts, ausserhalb = nichts).</summary>
        public static void DrawSweep(Graphics g, Rectangle r, float t, Color c)
        {
            if (t <= -0.3f || t >= 1.3f) return;
            float w = r.Width * 0.9f, cx = r.X - w / 2f + t * (r.Width + w);
            var band = new RectangleF(cx - w / 2f, r.Y, w, r.Height);
            using (var b = new LinearGradientBrush(new RectangleF(band.X - 1, band.Y, band.Width + 2, band.Height), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                var blend = new ColorBlend { Colors = new[] { Color.FromArgb(0, c), Color.FromArgb(70, c), Color.FromArgb(120, Color.White), Color.FromArgb(70, c), Color.FromArgb(0, c) }, Positions = new[] { 0f, 0.35f, 0.5f, 0.65f, 1f } };
                b.InterpolationColors = blend;
                g.FillRectangle(b, band);
            }
        }
    }
}
