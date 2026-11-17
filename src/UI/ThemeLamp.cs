using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// Pultlampe: kleine Schwanenhals-Lampe ueber den Kanaelen, die einen warmen Lichtkegel aufs Pult wirft.
    /// Jede Flaeche (Zuege, Hintergrund, Trennfuge …) legt das Licht passend zu ihrer Lage im Fenster darueber –
    /// so entsteht ein durchgehender Lichtfleck. Leuchtendes (Pegel, LEDs, Anzeigen) bleibt davon unberuehrt.
    /// </summary>
    internal static partial class Theme
    {
        /// <summary>Lampe leuchtet gerade.</summary>
        public static bool LampOn;
        /// <summary>Mitte der Lampen-Oeffnung in Fenster-Koordinaten.</summary>
        public static PointF LampAt;
        /// <summary>Mitte des Lichtflecks (etwas unter der Lampe, wo der Kegel aufs Pult trifft).</summary>
        public static PointF LampPool;
        /// <summary>Halbachsen des Lichtflecks.</summary>
        public static float LampReachX = 300f, LampReachY = 500f;
        /// <summary>Staerke: 0 schwach, 1 mittel, 2 hell.</summary>
        public static int LampStrength = 1;
        /// <summary>Zaehlt hoch, wenn sich Lage oder Staerke aendern (dann neu zeichnen).</summary>
        public static int LampVersion;

        public static readonly string[] LampModeNames = { "Aus", "An", "Wenn das Pult dunkler ist" };
        public static readonly string[] LampPosNames = { "Links", "Mitte", "Rechts" };
        public static readonly string[] LampStrengthNames = { "Schwach", "Mittel", "Hell" };

        /// <summary>Warmweisses Licht (Gluehlampe).</summary>
        public static readonly Color LampColor = Color.FromArgb(0xff, 0xc8, 0x84);

        /// <summary>Lage eines Steuerelements im Fenster (ohne Windows-Aufruf).</summary>
        public static Point FormOrigin(Control c)
        {
            int x = 0, y = 0;
            for (var p = c; p != null && !(p is Form); p = p.Parent) { x += p.Left; y += p.Top; }
            return new Point(x, y);
        }

        /// <summary>Wie stark das Licht in der Mitte ist (0..255).</summary>
        private static int LampAlpha()
        {
            float a = IsLight ? 38f : 56f;
            a *= LampStrength <= 0 ? 0.6f : LampStrength >= 2 ? 1.5f : 1f;
            if (NightOn) a += NightDim * (IsLight ? 70f : 95f);   // abends hebt die Lampe das Dunkel teilweise auf
            return (int)Math.Max(0, Math.Min(150, a));
        }

        /// <summary>Licht der Lampe auf eine Flaeche legen (origin = Lage der Flaeche im Fenster).</summary>
        public static void LampLight(Graphics g, RectangleF area, Point origin)
        {
            if (!LampOn || area.Width <= 0 || area.Height <= 0 || LampReachX < 2 || LampReachY < 2) return;
            float cx = LampPool.X - origin.X, cy = LampPool.Y - origin.Y;
            var ell = new RectangleF(cx - LampReachX, cy - LampReachY, LampReachX * 2, LampReachY * 2);
            if (!ell.IntersectsWith(area)) return;
            int alpha = LampAlpha();
            if (alpha <= 0) return;
            var st = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ell);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(cx, cy);
                    b.CenterColor = Color.FromArgb(alpha, LampColor);
                    b.SurroundColors = new[] { Color.FromArgb(0, LampColor) };
                    // weicher Rand: aussen flach auslaufend, innen gleichmaessig hell
                    b.Blend = new Blend { Factors = new[] { 0f, 0.12f, 0.42f, 0.78f, 0.95f, 1f }, Positions = new[] { 0f, 0.15f, 0.38f, 0.65f, 0.85f, 1f } };
                    g.FillPath(b, path);
                }
            }
            g.Restore(st);
        }

        /// <summary>
        /// Die Lampe selbst: Schwanenhals von oben, Schirm mit leuchtender Oeffnung (Mitte unten = p), Lichtschein darunter.
        /// </summary>
        public static void DrawLamp(Graphics g, PointF p, float neckFromX)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = F(34), h = F(13), top = p.Y - h;
            // Schwanenhals: von oben (Halterung an der Kante) in einem Bogen zum Schirm
            using (var pen = new Pen(Color.FromArgb(0x1a, 0x1b, 0x1e), F(3.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawBezier(pen, neckFromX, -F(2), neckFromX, top - F(2), p.X + F(10), top - F(5), p.X + F(3), top + F(1));
            }
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), F(1)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, neckFromX - F(0.8f), -F(2), neckFromX - F(0.8f), top - F(2), p.X + F(9), top - F(6), p.X + F(2), top);
            // Schirm (Kegel): oben schmal, unten breit
            using (var shade = new GraphicsPath())
            {
                shade.AddLine(p.X - F(6), top, p.X + F(6), top);
                shade.AddLine(p.X + F(6), top, p.X + w / 2, p.Y);
                shade.AddArc(p.X - w / 2, p.Y - F(2.5f), w, F(5), 0, 180);
                shade.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(p.X - w / 2, top - 1, w, h + 4), Color.FromArgb(0x5a, 0x5e, 0x66), Color.FromArgb(0x1c, 0x1e, 0x22), 20f))
                    g.FillPath(b, shade);
                using (var pen = new Pen(Color.FromArgb(0x0c, 0x0d, 0x0f), F(1))) g.DrawPath(pen, shade);
            }
            // Glanzlicht auf dem Schirm
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), F(1)))
                g.DrawLine(pen, p.X - F(3), top + F(1.5f), p.X - w / 2 + F(3.5f), p.Y - F(1.5f));
            // leuchtende Oeffnung
            var open = new RectangleF(p.X - w / 2 + F(1.5f), p.Y - F(1.8f), w - F(3), F(3.6f));
            if (LampOn)
            {
                using (var b = new SolidBrush(Color.FromArgb(0xff, 0xf4, 0xd8))) g.FillEllipse(b, open);
                using (var pen = new Pen(Color.FromArgb(0xff, 0xc8, 0x84), F(1))) g.DrawEllipse(pen, open);
            }
            else using (var b = new SolidBrush(Color.FromArgb(0x30, 0x2c, 0x26))) g.FillEllipse(b, open);
            g.SmoothingMode = sm;
        }

        /// <summary>Heller Schein direkt unter der Lampe (auf dem Hintergrund, vor den Zuegen).</summary>
        public static void DrawLampGlow(Graphics g, PointF p)
        {
            if (!LampOn) return;
            var r = new RectangleF(p.X - F(40), p.Y - F(10), F(80), F(34));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(p.X, p.Y);
                    b.CenterColor = Color.FromArgb(Math.Min(200, LampAlpha() + 60), LampColor);
                    b.SurroundColors = new[] { Color.FromArgb(0, LampColor) };
                    var sm = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(b, path);
                    g.SmoothingMode = sm;
                }
            }
        }
    }
}
