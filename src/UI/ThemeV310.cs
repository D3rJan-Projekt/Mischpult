using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.1: analoge Studio-Uhr (Zeiger) – Farben passend zum VU-Meter (Skala, Zeiger, roter Sekundenzeiger).</summary>
    internal static partial class Theme
    {
        /// <summary>Uhr mit Zeigern statt Ziffern (Einstellung "Art der Uhr" = Analog).</summary>
        public static bool AnalogClock;

        private static PointF ClockPolar(PointF c, float r, double deg)
        {
            double a = (deg - 90) * Math.PI / 180.0;
            return new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r);
        }

        /// <summary>
        /// Analoge Studio-Uhr. staticPart = Gehaeuse, Zifferblatt, Striche und Ziffern (fuer den Bild-Cache),
        /// sonst Stunden-, Minuten- und roter Sekundenzeiger (springt jede Sekunde, wie bei einer Funkuhr im Studio).
        /// </summary>
        public static void DrawAnalogClock(Graphics g, RectangleF r, bool staticPart) => DrawAnalogClock(g, r, staticPart, DateTime.Now);

        public static void DrawAnalogClock(Graphics g, RectangleF r, bool staticPart, DateTime now)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            VuColors(out var face, out var edge, out var ink, out var red, out var needle, out var hood);
            float size = Math.Min(r.Width, r.Height);
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            float R = size / 2f - F(2);
            bool small = size < F(40);
            if (staticPart)
            {
                // Gehaeuse: dunkler Ring mit Metallkante
                var outer = new RectangleF(c.X - R, c.Y - R, R * 2, R * 2);
                using (var b = new LinearGradientBrush(outer, Lighten(hood, 0.35f), Darken(hood, 0.4f), 60f))
                    g.FillEllipse(b, outer);
                float bez = Math.Max(F(1.5f), R * 0.09f);
                var dial = RectangleF.Inflate(outer, -bez, -bez);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(dial);
                    using (var pb = new PathGradientBrush(path) { CenterColor = face, SurroundColors = new[] { Mix(face, edge, 0.55f) } })
                        g.FillEllipse(pb, dial);
                }
                using (var p = new Pen(Color.FromArgb(70, 0, 0, 0), Math.Max(1f, F(0.8f)))) g.DrawEllipse(p, dial);
                float dr = dial.Width / 2f;
                // Striche: 60 Minuten, jede 5. dicker
                for (int i = 0; i < 60; i++)
                {
                    bool hour = i % 5 == 0;
                    if (small && !hour) continue;
                    float len = hour ? dr * (small ? 0.2f : 0.14f) : dr * 0.06f;
                    float w = hour ? Math.Max(1f, dr * (small ? 0.09f : 0.045f)) : Math.Max(0.8f, dr * 0.016f);
                    using (var p = new Pen(i == 0 ? red : ink, w))
                        g.DrawLine(p, ClockPolar(c, dr * 0.94f, i * 6), ClockPolar(c, dr * 0.94f - len, i * 6));
                }
                // Ziffern 12 / 3 / 6 / 9
                if (!small)
                {
                    var f = Ui(Math.Max(6f, (float)Math.Round(dr * 0.2f / S)), FontStyle.Bold);   // aus dem Schrift-Speicher, nicht freigeben
                    using (var b = new SolidBrush(ink))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        for (int h = 3; h <= 12; h += 3)
                        {
                            var p = ClockPolar(c, dr * 0.66f, h * 30);
                            g.DrawString(h.ToString(), f, b, p.X, p.Y + F(0.5f), sf);
                        }
                }
                // Glas: heller Bogen oben
                using (var path = new GraphicsPath())
                {
                    var gl = new RectangleF(dial.X + dial.Width * 0.12f, dial.Y + dial.Height * 0.04f, dial.Width * 0.76f, dial.Height * 0.5f);
                    path.AddEllipse(gl);
                    using (var b = new LinearGradientBrush(gl, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillPath(b, path);
                }
            }
            else
            {
                float dr = R - Math.Max(F(1.5f), R * 0.09f);
                double sec = now.Second, min = now.Minute + sec / 60.0, hr = (now.Hour % 12) + min / 60.0;
                var sh = Color.FromArgb(60, 0, 0, 0);
                float off = Math.Max(0.8f, dr * 0.03f);
                // Schatten, dann Zeiger
                DrawHand(g, c, hr * 30, dr * 0.52f, dr * 0.12f, Math.Max(1.6f, dr * 0.085f), sh, off);
                DrawHand(g, c, min * 6, dr * 0.8f, dr * 0.14f, Math.Max(1.2f, dr * 0.06f), sh, off);
                DrawHand(g, c, hr * 30, dr * 0.52f, dr * 0.12f, Math.Max(1.6f, dr * 0.085f), needle, 0);
                DrawHand(g, c, min * 6, dr * 0.8f, dr * 0.14f, Math.Max(1.2f, dr * 0.06f), needle, 0);
                // Sekundenzeiger rot mit Gegengewicht
                using (var p = new Pen(red, Math.Max(0.9f, dr * 0.022f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, ClockPolar(c, -dr * 0.2f, sec * 6), ClockPolar(c, dr * 0.88f, sec * 6));
                if (!small)
                {
                    var cw = ClockPolar(c, -dr * 0.16f, sec * 6);
                    float cr = dr * 0.055f;
                    using (var b = new SolidBrush(red)) g.FillEllipse(b, cw.X - cr, cw.Y - cr, cr * 2, cr * 2);
                }
                float k = Math.Max(F(1.6f), dr * 0.07f);
                using (var b = new SolidBrush(red)) g.FillEllipse(b, c.X - k, c.Y - k, k * 2, k * 2);
                float k2 = k * 0.45f;
                using (var b = new SolidBrush(Lighten(red, 0.5f))) g.FillEllipse(b, c.X - k2, c.Y - k2, k2 * 2, k2 * 2);
            }
            g.SmoothingMode = sm;
        }

        private static void DrawHand(Graphics g, PointF c, double deg, float len, float tail, float width, Color col, float off)
        {
            var a = ClockPolar(c, -tail, deg); var b = ClockPolar(c, len, deg);
            if (off > 0) { a.X += off; a.Y += off; b.X += off; b.Y += off; }
            using (var p = new Pen(col, width) { StartCap = LineCap.Round, EndCap = LineCap.Triangle })
                g.DrawLine(p, a, b);
        }

        /// <summary>Kleine analoge Uhr fuer die Kopfleiste (rund, ohne Ziffern).</summary>
        public static void DrawAnalogClockSmall(Graphics g, RectangleF r)
        {
            float d = Math.Min(r.Width, r.Height);
            var rr = new RectangleF(r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            DrawAnalogClock(g, rr, true);
            DrawAnalogClock(g, rr, false);
        }
    
        // ================================================================ 3.1: Stereo-Doppelzeiger-VU

        /// <summary>VU-Meter zeigt links und rechts als zwei Zeiger in einem Instrument (L in Zeigerfarbe, R rot).</summary>
        public static bool VuDual;

        /// <summary>Doppelzeiger, danach Abdeckung und Glas.</summary>
        public static void DrawVuNeedle2(Graphics g, RectangleF r, float pL, float pR, bool dim, int led = 0)
        {
            DrawVuPointer2(g, r, pL, pR, dim, led);
            DrawVuCover(g, r);
        }

        /// <summary>Zwei Zeiger (R hinten, rot; L vorne, in Zeigerfarbe), PEAK-Leuchte, kleine Legende L/R.</summary>
        public static void DrawVuPointer2(Graphics g, RectangleF r, float pL, float pR, bool dim, int led = 0)
        {
            if (VuKind == 3) { DrawEye(g, r, Math.Max(pL, pR), dim, led); return; }   // 3.3: Magisches Auge (lauterer Kanal)
            VuColors(out _, out _, out var ink, out var red, out var needle, out _);
            if (dim) { needle = Darken(needle, 0.3f); red = Darken(red, 0.3f); }
            Color second = Mix(red, needle, 0.12f);
            VuGeometry(r, out var pv, out float R);
            var state = g.Save();
            using (var path = Round(r, VuRad)) g.SetClip(path, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (led > 0)
            {
                var lr = VuLedRect(r);
                if (led == 2 && !dim)
                {
                    for (int i = 3; i >= 1; i--)
                        using (var b = new SolidBrush(Color.FromArgb(34, red))) g.FillEllipse(b, RectangleF.Inflate(lr, F(i * 1.2f), F(i * 1.2f)));
                    using (var b = new SolidBrush(red)) g.FillEllipse(b, lr);
                }
                else using (var b = new SolidBrush(Mix(Color.FromArgb(0x24, 0x10, 0x10), red, 0.22f))) g.FillEllipse(b, lr);
                using (var pen = new Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1f, F(0.7f)))) g.DrawEllipse(pen, lr);
            }
            // Legende unten links: L (Zeigerfarbe) und R (rot)
            var f = Ui(Math.Max(6f, (float)Math.Round(Math.Min(9f, r.Height / F(1) * 0.085f))), FontStyle.Bold);
            float lx = r.X + r.Width * 0.07f, ly = r.Bottom - r.Height * 0.2f;
            using (var b = new SolidBrush(Alpha(needle, 230))) g.DrawString("L", f, b, lx, ly);
            using (var b = new SolidBrush(Alpha(second, 230))) g.DrawString("R", f, b, lx + f.Size * 1.05f, ly);
            DrawVuHand(g, r, pv, R, pR, second, dim);
            DrawVuHand(g, r, pv, R, pL, needle, dim);
            g.Restore(state);
        }

        private static void DrawVuHand(Graphics g, RectangleF r, PointF pv, float R, float p, Color col, bool dim)
        {
            p = Math.Max(-0.015f, Math.Min(1.06f, p));
            float a = VuAngle(p);
            float tipR = R + F(7.5f), baseR = R * 0.25f;
            double ar = a * Math.PI / 180.0;
            float nx = (float)Math.Cos(ar), ny = (float)Math.Sin(ar);
            Func<float, float, PointF[]> shape = (dx, dy) =>
            {
                var tip = Polar(pv, tipR, a); var bas = Polar(pv, baseR, a);
                float wb = F(0.95f), wt = F(0.3f);
                return new[]
                {
                    new PointF(bas.X - nx * wb + dx, bas.Y - ny * wb + dy), new PointF(tip.X - nx * wt + dx, tip.Y - ny * wt + dy),
                    new PointF(tip.X + nx * wt + dx, tip.Y + ny * wt + dy), new PointF(bas.X + nx * wb + dx, bas.Y + ny * wb + dy),
                };
            };
            bool darkFace = VuFace == 2 || (VuFace == 0 && (Style == StyleModern || Style == StyleNeon));
            using (var b = new SolidBrush(darkFace ? Color.FromArgb(110, 0, 0, 0) : Color.FromArgb(40, 0, 0, 0)))
                g.FillPolygon(b, shape(F(1.6f), F(1.2f)));
            using (var b = new SolidBrush(col)) g.FillPolygon(b, shape(0, 0));
        }

        /// <summary>3.1: Doppelzeiger auf der Grafikkarte (Abdeckung und Glas kommen als Bild darueber).</summary>
        public static void DrawVuPointer2Gpu(Mischpult.Native.D2DCanvas c, RectangleF r, float pL, float pR, bool dim, int led)
        {
            VuColors(out _, out _, out _, out var red, out var needle, out _);
            if (dim) { needle = Darken(needle, 0.3f); red = Darken(red, 0.3f); }
            Color second = Mix(red, needle, 0.12f);
            VuGeometry(r, out var pv, out float R);
            c.PushClip(r);
            if (led > 0)
            {
                var lr = VuLedRect(r);
                c.FillEllipse(lr, led == 2 && !dim ? red : Mix(Color.FromArgb(0x24, 0x10, 0x10), red, 0.22f));
            }
            float tipR = R + F(7.5f), baseR = R * 0.25f;
            foreach (var hand in new[] { new KeyValuePair<float, Color>(pR, second), new KeyValuePair<float, Color>(pL, needle) })
            {
                float a = VuAngle(Math.Max(-0.015f, Math.Min(1.06f, hand.Key)));
                var tip = Polar(pv, tipR, a); var bas = Polar(pv, baseR, a); var mid = Polar(pv, (baseR + tipR) / 2f, a);
                c.Line(new PointF(bas.X + F(1.6f), bas.Y + F(1.2f)), new PointF(tip.X + F(1.6f), tip.Y + F(1.2f)), Color.FromArgb(50, 0, 0, 0), F(1.4f));
                c.Line(bas, mid, hand.Value, F(1.75f));
                c.Line(mid, tip, hand.Value, F(1.0f));
            }
            c.PopClip();
        }

        // ================================================================ 3.1: Tasten-Farben und Tasten-Form

        /// <summary>Eigene Leuchtfarben je Taste (Beschriftung → Farbe); null = aus (Farben wie im Design).</summary>
        public static Dictionary<string, Color> KeyColorMap;
        /// <summary>Tasten-Form: 0 wie im Design, 1 eckig, 2 rund, 3 Leuchtring, 4 Kippschalter, 5 Bakelit (3.3).</summary>
        public static int KeyShape;
        public static readonly string[] KeyShapeNames = { "Wie im Design", "Eckig", "Rund", "Leuchtring", "Kippschalter", "Bakelit", "Pads (leuchten von innen)" };
        /// <summary>Tasten, deren Farbe man waehlen kann (Beschriftung).</summary>
        public static readonly string[] ColorKeys = { "MUTE", "SOLO", "FADE", "EQ", "COMP", "AUTO", "SIG", "PEAK", "DIM", "LOUD" };

        /// <summary>Leuchtfarbe einer Taste – eigene Farbe, falls gewaehlt.</summary>
        public static Color KeyLit(string key, Color def)
        {
            var m = KeyColorMap;
            if (m == null || key == null) return def;
            return m.TryGetValue(key, out var c) ? c : def;
        }

        /// <summary>Ecken der Tasten je nach Form (def = wie im Design).</summary>
        public static float KeyRad(float def, RectangleF r) => KeyShape == 1 ? F(1f) : KeyShape == 2 ? Math.Max(def, r.Height / 2f) : def;

        /// <summary>Leuchtring: Taste bleibt dunkel, nur ein leuchtender Rand und die Schrift in der Farbe.</summary>
        private static void RingOverlay(Graphics g, RectangleF r, string text, Color lit, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 3; i >= 1; i--) StrokeRound(g, Alpha(lit, 26), F(1.2f * i + 1f), RectangleF.Inflate(r, F(0.4f * i), F(0.4f * i)), rad + F(0.4f * i));
            StrokeRound(g, Lighten(lit, 0.15f), F(1.6f), RectangleF.Inflate(r, -F(0.8f), -F(0.8f)), Math.Max(0f, rad - F(0.8f)));
            Label(g, text, r, Center, Lighten(lit, 0.25f), KeyPx(g, text, r.Width));
            g.SmoothingMode = sm;
        }

        // ================================================================ 3.1: Faderkappen-Form

        /// <summary>Kappen-Form: 0 wie im Design, 1 kurz, 2 lang, 3 rund, 4 flach.</summary>
        public static int CapShape;
        public static readonly string[] CapShapeNames = { "Wie im Design", "Kurz", "Lang", "Rund", "Flach", "Griffmulde" };   // 3.4.6: Griffmulde

        /// <summary>Hoehe der Faderkappe (logische Pixel) je nach Form.</summary>
        public static float CapHeightPx => CapShape == 1 ? 15f : CapShape == 2 ? 28f : CapShape == 4 ? 11f : CapShape == 5 ? 22f : 20f;

        /// <summary>Ecken der Faderkappe: rund = Halbkreise, sonst wie im Design.</summary>
        public static float CapRad(float def, RectangleF r) => CapShape == 3 ? Math.Min(r.Width, r.Height) / 2f : def;

        /// <summary>Wie viele Griffrillen ober- und unterhalb der Mittellinie Platz haben.</summary>
        public static int CapRidges(RectangleF r) => r.Height >= F(26) ? 3 : r.Height >= F(18) ? 2 : r.Height >= F(13) ? 1 : 0;

        // ================================================================ 3.1: LED-Helligkeit

        /// <summary>Helligkeit der leuchtenden Pegel-LEDs (1 = wie im Design, 0,4 gedimmt … 1,5 grell).</summary>
        public static float LedBright = 1f;

        /// <summary>Leuchtende LED-Farbe nach der LED-Helligkeit.</summary>
        public static Color LedLevel(Color c)
        {
            if (WarmLevel < 0.999f) c = WarmColor(c);   // 3.3: Aufwaermen beim Start
            float b = LedBright;
            if (Math.Abs(b - 1f) < 0.01f) return c;
            if (b < 1f)
            {
                float f = Math.Max(0.3f, b);
                return Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
            }
            return Lighten(c, Math.Min(0.6f, (b - 1f) * 0.6f));
        }
}
}
