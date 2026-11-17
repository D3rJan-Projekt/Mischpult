using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.3 – Optik: weitere Zeigerinstrumente (BBC, DIN, Magisches Auge), Kippschalter und Bakelit-Tasten, Praegeband,
    /// Symbole im Namensschild, Gebrauchsspuren, Roehrengluehen, Wand hinter dem Pult, bewegter Hintergrund,
    /// Fallblatt- und Nixie-Uhr, Punktmatrix, LED-Kranz um die Knoepfe, Aufwaermen beim Start.
    /// </summary>
    internal static partial class Theme
    {
        // ================================================================ Zeigerinstrumente

        /// <summary>0 VU, 1 BBC-Spitzenpegel, 2 DIN-Spitzenpegel, 3 Magisches Auge.</summary>
        public static int VuKind;
        public static readonly string[] VuKindNames = { "VU-Meter", "BBC-Spitzenpegel", "DIN-Spitzenpegel", "Magisches Auge" };

        private static readonly Color EyeGreen = Color.FromArgb(0x5d, 0xff, 0x8f), EyeDeep = Color.FromArgb(0x0c, 0x4a, 0x24);

        /// <summary>Mittelpunkt und Radius des Auges im Instrument.</summary>
        private static void EyeGeometry(RectangleF r, out PointF c, out float rad)
        {
            rad = Math.Max(F(6), Math.Min(r.Width, r.Height - F(12)) / 2f - F(4));   // unten Platz fuer die Beschriftung
            c = new PointF(r.X + r.Width / 2f, r.Y + (r.Height - F(10)) / 2f);
        }

        /// <summary>Gehaeuse des Magischen Auges: schwarzes Blech, runde Fassung, dunkler Glaskolben.</summary>
        private static void DrawEyeFace(Graphics g, RectangleF r, bool dim)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var frame = RectangleF.Inflate(r, F(2.5f), F(2.5f));
            using (var b = new LinearGradientBrush(new RectangleF(frame.X, frame.Y - 1, frame.Width, frame.Height + 2), Color.FromArgb(0x1a, 0x1b, 0x1e), Color.FromArgb(0x06, 0x06, 0x07), LinearGradientMode.Vertical))
                FillRound(g, b, frame, F(4));
            FillRound(g, Color.FromArgb(0x0b, 0x0b, 0x0c), r, F(3));
            EyeGeometry(r, out var c, out float rad);
            // Fassung (Chromring)
            using (var b = new LinearGradientBrush(new RectangleF(c.X - rad - F(3), c.Y - rad - F(3), (rad + F(3)) * 2, (rad + F(3)) * 2), Color.FromArgb(0xc8, 0xcc, 0xd2), Color.FromArgb(0x3a, 0x3d, 0x42), LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(b, c.X - rad - F(3), c.Y - rad - F(3), (rad + F(3)) * 2, (rad + F(3)) * 2);
            using (var b = new SolidBrush(Color.FromArgb(0x03, 0x06, 0x04))) g.FillEllipse(b, c.X - rad, c.Y - rad, rad * 2, rad * 2);
            using (var pen = new Pen(Color.FromArgb(dim ? 40 : 70, EyeGreen), Math.Max(1f, F(0.8f))))
                g.DrawEllipse(pen, c.X - rad * 0.92f, c.Y - rad * 0.92f, rad * 1.84f, rad * 1.84f);
            var f = Ui(Math.Max(6f, Math.Min(8f, r.Height / S * 0.09f)), FontStyle.Bold);
            using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                Str(g, "TUNING", f, b, new RectangleF(r.X, r.Bottom - F(11), r.Width, F(10)), Center);
            g.SmoothingMode = sm;
        }

        /// <summary>Leuchtender Faecher: je lauter, desto kleiner der dunkle Spalt oben (wie bei alten Radios).</summary>
        private static void DrawEye(Graphics g, RectangleF r, float p, bool dim, int led)
        {
            EyeGeometry(r, out var c, out float rad);
            p = Math.Max(0f, Math.Min(1.06f, p));
            float shadow = 100f * (1f - Math.Min(1f, p)) + 4f;   // Spalt in Grad
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float ir = rad * 0.94f;
            var rect = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            using (var path = new GraphicsPath())
            {
                path.AddPie(rect.X, rect.Y, rect.Width, rect.Height, -90f + shadow / 2f, 360f - shadow);
                using (var pb = new PathGradientBrush(path) { CenterPoint = c, CenterColor = dim ? Darken(EyeGreen, 0.5f) : Lighten(EyeGreen, 0.35f), SurroundColors = new[] { dim ? Darken(EyeDeep, 0.4f) : EyeDeep } })
                    g.FillPath(pb, path);
            }
            // Kante des Spalts leuchtet heller
            using (var pen = new Pen(Color.FromArgb(dim ? 90 : 190, EyeGreen), Math.Max(1f, F(1.1f))))
            {
                double a1 = (-90f + shadow / 2f) * Math.PI / 180.0, a2 = (-90f - shadow / 2f) * Math.PI / 180.0;
                g.DrawLine(pen, c, new PointF(c.X + (float)Math.Cos(a1) * ir, c.Y + (float)Math.Sin(a1) * ir));
                g.DrawLine(pen, c, new PointF(c.X + (float)Math.Cos(a2) * ir, c.Y + (float)Math.Sin(a2) * ir));
            }
            // Kathodenkappe in der Mitte
            float cr = rad * 0.3f;
            using (var b = new SolidBrush(Color.FromArgb(0x08, 0x0a, 0x09))) g.FillEllipse(b, c.X - cr, c.Y - cr, cr * 2, cr * 2);
            using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, F(0.6f)))) g.DrawArc(pen, c.X - cr, c.Y - cr, cr * 2, cr * 2, 200, 120);
            if (led > 0)
            {
                var lr = VuLedRect(r);
                using (var b = new SolidBrush(led == 2 && !dim ? Color.FromArgb(0xff, 0x40, 0x30) : Color.FromArgb(0x40, 0x14, 0x10))) g.FillEllipse(b, lr);
            }
            g.SmoothingMode = sm;
        }

        private static void DrawEyeGlass(Graphics g, RectangleF r)
        {
            EyeGeometry(r, out var c, out float rad);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - rad * 0.8f, c.Y - rad * 0.95f, rad * 1.2f, rad * 0.9f);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(55, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillPath(pb, path);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Kippschalter und Bakelit-Tasten

        /// <summary>
        /// Kippschalter als beleuchtete Wippe (wie an einer Steckdosenleiste): Chromrahmen, die Wippe kippt –
        /// an = oben gedrueckt und leuchtet von innen in der Leuchtfarbe, aus = unten gedrueckt, dunkel.
        /// </summary>
        private static void DrawToggleKey(Graphics g, RectangleF r, string text, bool on, Color lit, bool hover, bool half)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(F(3), r.Height / 4f);
            FillRound(g, Color.FromArgb(110, 0, 0, 0), new RectangleF(r.X, r.Y + F(1.2f), r.Width, r.Height), rad);
            // Rahmen (Chrom)
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0xd8, 0xdb, 0xe0), Color.FromArgb(0x50, 0x54, 0x5a), LinearGradientMode.Vertical))
                FillRound(g, b, r, rad);
            var face = RectangleF.Inflate(r, -Math.Max(1.5f, F(1.8f)), -Math.Max(1.5f, F(1.8f)));
            float fr = Math.Max(0f, rad - F(1));
            Color top, bottom;
            if (on)
            {
                // leuchtet, oben gedrueckt: oben dunkler (Schatten der Kante), unten hell
                top = Darken(lit, 0.25f); bottom = Lighten(lit, 0.3f);
            }
            else
            {
                Color baseC = hover ? Color.FromArgb(0x3a, 0x3d, 0x43) : Color.FromArgb(0x2a, 0x2c, 0x31);
                top = Lighten(baseC, 0.12f); bottom = Darken(baseC, 0.35f);
                if (half) { top = Mix(top, lit, 0.25f); bottom = Mix(bottom, lit, 0.15f); }
            }
            using (var b = new LinearGradientBrush(new RectangleF(face.X, face.Y - 1, face.Width, face.Height + 2), top, bottom, LinearGradientMode.Vertical))
                FillRound(g, b, face, fr);
            // Knick der Wippe: Mittellinie, die gedrueckte Haelfte liegt tiefer
            float mid = face.Y + face.Height * (on ? 0.42f : 0.58f);
            using (var pen = new Pen(Color.FromArgb(on ? 70 : 110, 0, 0, 0), 1f)) g.DrawLine(pen, face.X + F(1), mid, face.Right - F(1), mid);
            using (var pen = new Pen(Color.FromArgb(on ? 90 : 40, 255, 255, 255), 1f)) g.DrawLine(pen, face.X + F(1), mid + 1, face.Right - F(1), mid + 1);
            var raised = on ? new RectangleF(face.X, mid, face.Width, face.Bottom - mid) : new RectangleF(face.X, face.Y, face.Width, mid - face.Y);
            using (var b = new SolidBrush(Color.FromArgb(on ? 40 : 22, 255, 255, 255))) g.FillRectangle(b, raised.X + F(1), raised.Y + F(0.5f), raised.Width - F(2), Math.Max(1f, raised.Height * 0.4f));
            if (on)
                for (int i = 3; i >= 1; i--)
                    StrokeRound(g, Alpha(lit, 22), F(1.1f * i), RectangleF.Inflate(r, F(0.5f * i), F(0.5f * i)), rad + F(0.5f * i));
            Label(g, text, face, Center, on ? Color.FromArgb(0xff, 0xff, 0xff) : hover ? Color.FromArgb(0xf0, 0xf0, 0xf0) : Color.FromArgb(0xc0, 0xc4, 0xca), KeyPx(g, text, face.Width + F(4)));
            g.SmoothingMode = sm;
        }

        /// <summary>Bakelit-Drucktaste: dunkelbraun glaenzend, cremefarbene Schrift; an = warmes Licht von innen in der Leuchtfarbe.</summary>
        private static void DrawBakeliteKey(Graphics g, RectangleF r, string text, bool on, Color lit, bool hover, bool half)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Math.Min(r.Height / 2f, F(6));
            FillRound(g, Color.FromArgb(120, 0, 0, 0), new RectangleF(r.X, r.Y + F(on ? 0.6f : 1.6f), r.Width, r.Height), rad);
            Color top = on ? Mix(Color.FromArgb(0x5a, 0x34, 0x1c), lit, 0.35f) : hover ? Color.FromArgb(0x5a, 0x36, 0x20) : Color.FromArgb(0x4a, 0x2b, 0x18);
            Color bottom = on ? Mix(Color.FromArgb(0x2a, 0x16, 0x0b), lit, 0.2f) : Color.FromArgb(0x22, 0x12, 0x09);
            var kr = on ? new RectangleF(r.X, r.Y + F(0.8f), r.Width, r.Height - F(0.8f)) : r;   // gedrueckt: etwas tiefer
            using (var b = new LinearGradientBrush(new RectangleF(kr.X, kr.Y - 1, kr.Width, kr.Height + 2), top, bottom, LinearGradientMode.Vertical))
                FillRound(g, b, kr, rad);
            if (on)
                using (var path = Round(kr, rad))
                using (var pb = new PathGradientBrush(path) { CenterPoint = new PointF(kr.X + kr.Width / 2f, kr.Y + kr.Height / 2f), CenterColor = Alpha(Lighten(lit, 0.3f), 150), SurroundColors = new[] { Alpha(lit, 0) } })
                    g.FillPath(pb, path);
            // Glanz oben
            using (var b = new LinearGradientBrush(new RectangleF(kr.X, kr.Y, kr.Width, kr.Height * 0.5f + 1), Color.FromArgb(70, 255, 240, 220), Color.FromArgb(0, 255, 240, 220), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(kr.X + F(1.5f), kr.Y + F(1), kr.Width - F(3), kr.Height * 0.45f), Math.Max(0f, rad - F(1)));
            StrokeRound(g, Color.FromArgb(0x12, 0x09, 0x04), F(1), kr, rad);
            if (half && !on) StrokeRound(g, Alpha(lit, 150), F(1.2f), RectangleF.Inflate(kr, -F(1.5f), -F(1.5f)), Math.Max(0f, rad - F(1.5f)));
            Label(g, text, kr, Center, on ? Color.FromArgb(0xff, 0xf3, 0xd6) : Color.FromArgb(0xe8, 0xdc, 0xbf), KeyPx(g, text, kr.Width));
            g.SmoothingMode = sm;
        }

        // ================================================================ Praegeband (Namensschild)

        public static bool DymoPlates;

        /// <summary>Praegeband in der Kanalfarbe: glaenzendes Kunststoffband mit Rillen, Schrift weiss erhaben (zeichnet der Zug).</summary>
        private static void DrawDymoPlate(Graphics g, RectangleF r, Color bg)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color tape = DymoTape(bg);
            // dunkler Untergrund (Schild), darauf ein schmaleres Band
            FillRound(g, Color.FromArgb(0x16, 0x17, 0x1a), r, F(3));
            float bh = (float)Math.Round(r.Height * 0.74f);
            var band = new RectangleF(r.X + F(3), r.Y + (r.Height - bh) / 2f, r.Width - F(6), bh);
            FillRound(g, Color.FromArgb(90, 0, 0, 0), new RectangleF(band.X, band.Y + F(1), band.Width, band.Height), F(1.5f));
            using (var b = new LinearGradientBrush(new RectangleF(band.X, band.Y - 1, band.Width, band.Height + 2), Lighten(tape, 0.18f), Darken(tape, 0.25f), LinearGradientMode.Vertical))
                FillRound(g, b, band, F(1.5f));
            // feine Laengsrillen und schraeg abgeschnittene Enden
            using (var pen = new Pen(Color.FromArgb(28, 0, 0, 0), 1f))
                for (float y = band.Y + F(2); y < band.Bottom - F(1); y += Math.Max(2f, F(2.5f))) g.DrawLine(pen, band.X + F(3), y, band.Right - F(3), y);
            using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                g.FillRectangle(b, band.X + F(2), band.Y + F(1), band.Width - F(4), Math.Max(1f, F(1)));
            g.SmoothingMode = sm;
        }

        /// <summary>Farbe des Praegebands: Kanalfarbe, so dunkel, dass weisse Schrift gut lesbar ist.</summary>
        public static Color DymoTape(Color bg) => Luma(bg) > 0.55f ? Darken(bg, 0.6f) : Luma(bg) > 0.35f ? Darken(bg, 0.3f) : Darken(bg, 0.08f);

        // ================================================================ Symbole im Namensschild

        public static readonly string[] SymbolNames = { "Automatisch", "Controller", "Note", "Sprechblase", "Film", "Globus", "Zahnrad", "Kopfhörer", "Stern", "Herz", "Lautsprecher" };

        /// <summary>Symbol nach dem Kanalnamen (fuer "automatisch").</summary>
        public static int SymbolFor(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            if (n.Contains("SPIEL") || n.Contains("GAME") || n.Contains("ZOCK")) return 1;
            if (n.Contains("MUSIK") || n.Contains("MUSIC") || n.Contains("SPOTIFY") || n.Contains("SONG")) return 2;
            if (n.Contains("CHAT") || n.Contains("DISCORD") || n.Contains("VOICE") || n.Contains("TEAMSPEAK") || n.Contains("SKYPE")) return 3;
            if (n.Contains("VIDEO") || n.Contains("FILM") || n.Contains("NETFLIX") || n.Contains("YOUTUBE") || n.Contains("TWITCH")) return 4;
            if (n.Contains("BROWSER") || n.Contains("WEB") || n.Contains("INTERNET")) return 5;
            if (n.Contains("SYSTEM")) return 6;
            return 0;
        }

        /// <summary>Kleines Strich-Symbol (1..10) in r, Farbe c.</summary>
        public static void DrawSymbol(Graphics g, RectangleF r, int sym, Color c)
        {
            if (sym <= 0 || sym > 10) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            var q = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            float w = Math.Max(1f, s * 0.11f);
            float cx = q.X + s / 2f, cy = q.Y + s / 2f;
            using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(c))
            {
                switch (sym)
                {
                    case 1:   // Controller
                        using (var path = Round(new RectangleF(q.X + s * 0.06f, q.Y + s * 0.28f, s * 0.88f, s * 0.44f), s * 0.2f)) g.DrawPath(pen, path);
                        g.DrawLine(pen, q.X + s * 0.22f, cy, q.X + s * 0.38f, cy);
                        g.DrawLine(pen, q.X + s * 0.30f, cy - s * 0.08f, q.X + s * 0.30f, cy + s * 0.08f);
                        g.FillEllipse(b, q.X + s * 0.62f, cy - s * 0.1f, s * 0.08f, s * 0.08f);
                        g.FillEllipse(b, q.X + s * 0.72f, cy + s * 0.02f, s * 0.08f, s * 0.08f);
                        break;
                    case 2:   // Note
                        g.FillEllipse(b, q.X + s * 0.18f, q.Y + s * 0.62f, s * 0.3f, s * 0.22f);
                        g.DrawLine(pen, q.X + s * 0.46f, q.Y + s * 0.72f, q.X + s * 0.46f, q.Y + s * 0.14f);
                        g.DrawBezier(pen, q.X + s * 0.46f, q.Y + s * 0.14f, q.X + s * 0.62f, q.Y + s * 0.24f, q.X + s * 0.78f, q.Y + s * 0.3f, q.X + s * 0.72f, q.Y + s * 0.5f);
                        break;
                    case 3:   // Sprechblase
                        using (var path = Round(new RectangleF(q.X + s * 0.08f, q.Y + s * 0.14f, s * 0.84f, s * 0.54f), s * 0.16f)) g.DrawPath(pen, path);
                        g.DrawLines(pen, new[] { new PointF(q.X + s * 0.3f, q.Y + s * 0.68f), new PointF(q.X + s * 0.24f, q.Y + s * 0.88f), new PointF(q.X + s * 0.46f, q.Y + s * 0.68f) });
                        break;
                    case 4:   // Film
                        g.DrawRectangle(pen, q.X + s * 0.14f, q.Y + s * 0.12f, s * 0.72f, s * 0.76f);
                        for (int i = 0; i < 4; i++)
                        {
                            float y = q.Y + s * (0.2f + i * 0.18f);
                            g.FillRectangle(b, q.X + s * 0.2f, y, s * 0.08f, s * 0.08f);
                            g.FillRectangle(b, q.X + s * 0.72f, y, s * 0.08f, s * 0.08f);
                        }
                        break;
                    case 5:   // Globus
                        g.DrawEllipse(pen, q.X + s * 0.1f, q.Y + s * 0.1f, s * 0.8f, s * 0.8f);
                        g.DrawEllipse(pen, cx - s * 0.18f, q.Y + s * 0.1f, s * 0.36f, s * 0.8f);
                        g.DrawLine(pen, q.X + s * 0.1f, cy, q.Right - s * 0.1f, cy);
                        break;
                    case 6:   // Zahnrad
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, cx + (float)Math.Cos(a) * s * 0.28f, cy + (float)Math.Sin(a) * s * 0.28f, cx + (float)Math.Cos(a) * s * 0.44f, cy + (float)Math.Sin(a) * s * 0.44f);
                        }
                        g.DrawEllipse(pen, cx - s * 0.28f, cy - s * 0.28f, s * 0.56f, s * 0.56f);
                        g.DrawEllipse(pen, cx - s * 0.1f, cy - s * 0.1f, s * 0.2f, s * 0.2f);
                        break;
                    case 7:   // Kopfhoerer
                        g.DrawArc(pen, q.X + s * 0.14f, q.Y + s * 0.12f, s * 0.72f, s * 0.72f, 180, 180);
                        using (var p2 = Round(new RectangleF(q.X + s * 0.1f, cy, s * 0.16f, s * 0.3f), s * 0.05f)) g.FillPath(b, p2);
                        using (var p3 = Round(new RectangleF(q.Right - s * 0.26f, cy, s * 0.16f, s * 0.3f), s * 0.05f)) g.FillPath(b, p3);
                        break;
                    case 8:   // Stern
                        {
                            var pts = new PointF[10];
                            for (int i = 0; i < 10; i++)
                            {
                                double a = -Math.PI / 2 + i * Math.PI / 5;
                                float rr = i % 2 == 0 ? s * 0.44f : s * 0.18f;
                                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
                            }
                            g.FillPolygon(b, pts);
                        }
                        break;
                    case 9:   // Herz
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(cx, q.Y + s * 0.86f, q.X + s * 0.02f, q.Y + s * 0.5f, q.X + s * 0.18f, q.Y + s * 0.06f, cx, q.Y + s * 0.3f);
                            path.AddBezier(cx, q.Y + s * 0.3f, q.Right - s * 0.18f, q.Y + s * 0.06f, q.Right - s * 0.02f, q.Y + s * 0.5f, cx, q.Y + s * 0.86f);
                            g.FillPath(b, path);
                        }
                        break;
                    case 10:  // Lautsprecher
                        g.FillPolygon(b, new[] { new PointF(q.X + s * 0.1f, cy - s * 0.14f), new PointF(q.X + s * 0.28f, cy - s * 0.14f), new PointF(q.X + s * 0.5f, cy - s * 0.34f),
                                                new PointF(q.X + s * 0.5f, cy + s * 0.34f), new PointF(q.X + s * 0.28f, cy + s * 0.14f), new PointF(q.X + s * 0.1f, cy + s * 0.14f) });
                        g.DrawArc(pen, cx - s * 0.12f, cy - s * 0.2f, s * 0.4f, s * 0.4f, -50, 100);
                        g.DrawArc(pen, cx - s * 0.2f, cy - s * 0.34f, s * 0.68f, s * 0.68f, -50, 100);
                        break;
                }
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Gebrauchsspuren

        /// <summary>0 aus, 1 leicht, 2 stark.</summary>
        public static int Wear;
        public static readonly string[] WearNames = { "Aus", "Leicht", "Stark" };

        /// <summary>Feine Kratzer, Abrieb und blasse Stellen auf dem Zug (immer gleich fuer denselben Zug).</summary>
        public static void DrawWear(Graphics g, RectangleF r, int seed)
        {
            if (Wear <= 0 || r.Width < 10) return;
            var rnd = new Random(seed);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int n = Wear == 1 ? 18 : 44;
            bool light = IsLight;
            for (int i = 0; i < n; i++)
            {
                float x = r.X + (float)rnd.NextDouble() * r.Width, y = r.Y + (float)rnd.NextDouble() * r.Height;
                float len = F(4f + (float)rnd.NextDouble() * (Wear == 1 ? 10f : 22f));
                double a = (rnd.NextDouble() - 0.5) * 1.2 + (rnd.Next(2) == 0 ? 0 : Math.PI / 2);
                int alpha = (Wear == 1 ? 18 : 26) + rnd.Next(Wear == 1 ? 16 : 30);
                using (var pen = new Pen(light ? Color.FromArgb(alpha, 0, 0, 0) : Color.FromArgb(alpha, 255, 255, 255), Math.Max(0.6f, F(0.6f))))
                    g.DrawLine(pen, x, y, x + (float)Math.Cos(a) * len, y + (float)Math.Sin(a) * len);
            }
            // abgegriffene Stellen (heller) dort, wo die Hand oft liegt: unten um den Fader
            int spots = Wear == 1 ? 2 : 5;
            for (int i = 0; i < spots; i++)
            {
                float w = F(10 + rnd.Next(14)), h = F(6 + rnd.Next(10));
                float x = r.X + (float)rnd.NextDouble() * (r.Width - w), y = r.Y + r.Height * (0.55f + 0.4f * (float)rnd.NextDouble()) - h;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(x, y, w, h);
                    using (var pb = new PathGradientBrush(path) { CenterColor = light ? Color.FromArgb(Wear * 16, 0, 0, 0) : Color.FromArgb(Wear * 14, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                        g.FillPath(pb, path);
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Abgegriffene Faderkappe: in der Mitte heller, Kanten matt.</summary>
        public static void DrawCapWear(Graphics g, RectangleF cap)
        {
            if (Wear <= 0) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cap.X + cap.Width * 0.2f, cap.Y + cap.Height * 0.15f, cap.Width * 0.6f, cap.Height * 0.7f);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(Wear * 22, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillPath(pb, path);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Roehrengluehen

        public static bool TubeGlow;
        /// <summary>Wie hell die Roehren gerade gluehen (0..1, folgt dem Ausgang).</summary>
        public static float TubeLevel;

        /// <summary>Lueftungsschlitze im Seitenteil, dahinter orange gluehende Roehren.</summary>
        public static void DrawTubeVents(Graphics g, RectangleF r)
        {
            if (!TubeGlow || r.Width < 8 || r.Height < 60) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sw = r.Width * 0.5f, sh = Math.Max(2f, F(3)), gap = F(4);
            int n = 9;
            float top = r.Y + r.Height * 0.12f;
            float glow = 0.25f + 0.75f * Math.Max(0f, Math.Min(1f, TubeLevel));
            var area = new RectangleF(r.X + (r.Width - sw) / 2f, top, sw, n * (sh + gap));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(area.X - area.Width, area.Y - F(10), area.Width * 3, area.Height + F(20));
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(120 * glow), 0xff, 0x8a, 0x2a), SurroundColors = new[] { Color.FromArgb(0, 0xff, 0x8a, 0x2a) } })
                    g.FillRectangle(pb, RectangleF.Inflate(area, area.Width, F(10)));
            }
            for (int i = 0; i < n; i++)
            {
                var slot = new RectangleF(area.X, top + i * (sh + gap), sw, sh);
                FillRound(g, Color.FromArgb(0x08, 0x05, 0x03), slot, sh / 2f);
                using (var b = new SolidBrush(Color.FromArgb((int)(60 + 170 * glow), 0xff, (int)(0x70 + 0x40 * glow), 0x20)))
                    FillRound(g, b, RectangleF.Inflate(slot, -F(0.8f), -F(0.6f)), sh / 2f);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Wand hinter dem Pult

        public static readonly string[] WallNames = { "Keine", "Akustik-Schaumstoff", "Ziegel", "Holzlamellen", "Samtvorhang", "Sichtbeton", "Kacheln", "Graffiti", "Bücherregal", "Serverraum" };   // 4.2: 4–6, 4.3: 7–9

        /// <summary>Wand in Fenstergroesse (abgedunkelt), als Hintergrund des Pults.</summary>
        public static Bitmap MakeWall(int w, int h, int kind)
        {
            if (w < 2 || h < 2 || kind <= 0) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rnd = new Random(42);
                if (kind == 1)
                {
                    // Schaumstoff: Pyramiden-Noppen, dunkelgrau
                    float cell = Math.Max(14f, F(22));
                    g.Clear(Color.FromArgb(0x1a, 0x1b, 0x1e));
                    for (float y = 0; y < h; y += cell)
                        for (float x = 0; x < w; x += cell)
                        {
                            var c = new PointF(x + cell / 2f, y + cell / 2f);
                            var tl = new PointF(x, y); var tr = new PointF(x + cell, y); var br = new PointF(x + cell, y + cell); var bl = new PointF(x, y + cell);
                            using (var b = new SolidBrush(Color.FromArgb(0x2b, 0x2d, 0x31))) g.FillPolygon(b, new[] { tl, tr, c });
                            using (var b = new SolidBrush(Color.FromArgb(0x22, 0x24, 0x27))) g.FillPolygon(b, new[] { tl, bl, c });
                            using (var b = new SolidBrush(Color.FromArgb(0x16, 0x17, 0x1a))) g.FillPolygon(b, new[] { tr, br, c });
                            using (var b = new SolidBrush(Color.FromArgb(0x10, 0x11, 0x13))) g.FillPolygon(b, new[] { bl, br, c });
                        }
                }
                else if (kind == 2)
                {
                    // Ziegel: versetzte Reihen, Fugen, etwas unterschiedliche Steine
                    float bh = Math.Max(12f, F(18)), bw = bh * 2.6f, mortar = Math.Max(1.5f, F(2.5f));
                    g.Clear(Color.FromArgb(0x2a, 0x25, 0x22));
                    int row = 0;
                    for (float y = 0; y < h; y += bh, row++)
                        for (float x = row % 2 == 0 ? 0 : -bw / 2f; x < w; x += bw)
                        {
                            int v = rnd.Next(-14, 15);
                            var col = Color.FromArgb(Clamp255(0x7a + v), Clamp255(0x3a + v / 2), Clamp255(0x2a + v / 3));
                            var br = new RectangleF(x + mortar / 2f, y + mortar / 2f, bw - mortar, bh - mortar);
                            using (var b = new LinearGradientBrush(new RectangleF(br.X, br.Y - 1, br.Width, br.Height + 2), Lighten(col, 0.08f), Darken(col, 0.18f), LinearGradientMode.Vertical))
                                g.FillRectangle(b, br);
                        }
                }
                else if (kind >= 7) MakeWall430(g, w, h, kind, rnd);   // 4.3: Graffiti, Buecherregal, Serverraum
                else if (kind >= 4) MakeWall420(g, w, h, kind, rnd);   // 4.2: Samtvorhang, Sichtbeton, Kacheln
                else
                {
                    // Holzlamellen: senkrechte Leisten mit dunklen Fugen
                    float sw = Math.Max(14f, F(20)), gapW = Math.Max(3f, F(5));
                    g.Clear(Color.FromArgb(0x0c, 0x08, 0x05));
                    for (float x = 0; x < w; x += sw + gapW)
                    {
                        int v = rnd.Next(-10, 11);
                        var col = Color.FromArgb(Clamp255(0x6b + v), Clamp255(0x42 + v / 2), Clamp255(0x22 + v / 3));
                        var lr = new RectangleF(x, 0, sw, h);
                        using (var b = new LinearGradientBrush(new RectangleF(lr.X - 1, 0, lr.Width + 2, 10), Lighten(col, 0.12f), Darken(col, 0.22f), LinearGradientMode.Horizontal))
                            g.FillRectangle(b, lr);
                        using (var pen = new Pen(Color.FromArgb(26, 0, 0, 0), 1f))
                            for (int k = 0; k < 6; k++) { float gx = x + (float)rnd.NextDouble() * sw; g.DrawLine(pen, gx, 0, gx + F(2), h); }
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 0, 0, w, h);   // abgedunkelt: das Pult bleibt im Vordergrund
                WallLights430(g, kind);   // 4.3: LEDs im Serverraum bleiben hell
            }
            return bmp;
        }

        private static int Clamp255(int v) => Math.Max(0, Math.Min(255, v));

        // ================================================================ Bewegter Hintergrund

        /// <summary>0 aus, 1 Sterne, 2 Farbverlauf, 3 Buehnennebel.</summary>
        public static int LiveBg;
        public static readonly string[] LiveBgNames = { "Aus", "Sternenhimmel", "Farbverlauf", "Bühnennebel", "Regen am Fenster", "Schneefall", "Nordlicht", "Unterwasser", "Kaminfeuer", "Matrix-Regen", "Gewitter", "Sternenwirbel", "Glühwürmchen" };   // 3.4.6: Regen, Schnee; 4.2: 6–9; 4.3: 10–12
        private static readonly System.Diagnostics.Stopwatch LiveClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Bewegtes ueber den Pult-Grund (nur in den freien Flaechen – die Zuege liegen darueber).</summary>
        public static void DrawLiveBg(Graphics g, Rectangle area, Point origin)
        {
            if (LiveBg <= 0 || area.Width < 4 || area.Height < 4) return;
            double t = LiveClock.Elapsed.TotalSeconds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (LiveBg == 1)
            {
                // Sterne: feste Lage im Fenster, funkeln langsam
                int cell = Math.Max(20, Px(28));
                int x0 = (origin.X + area.X) / cell - 1, y0 = (origin.Y + area.Y) / cell - 1;
                int x1 = (origin.X + area.Right) / cell + 1, y1 = (origin.Y + area.Bottom) / cell + 1;
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        uint hsh = (uint)(cx * 73856093 ^ cy * 19349663);
                        hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                        if (hsh % 3 != 0) continue;
                        float fx = cx * cell + (hsh >> 4) % (uint)cell - origin.X, fy = cy * cell + (hsh >> 12) % (uint)cell - origin.Y;
                        double tw = 0.5 + 0.5 * Math.Sin(t * (0.6 + (hsh % 7) * 0.15) + (hsh % 100));
                        int a = (int)(60 + 180 * tw);
                        float d = Math.Max(1.5f, F(1.2f + (hsh / 3 % 3) * 0.7f));
                        if (tw > 0.8 && hsh % 2 == 0)
                            using (var gb = new SolidBrush(Color.FromArgb((int)(60 * tw), 0xc8, 0xd8, 0xff))) g.FillEllipse(gb, fx - d * 1.6f, fy - d * 1.6f, d * 3.2f, d * 3.2f);
                        using (var b = new SolidBrush(Color.FromArgb(a, 0xe8, 0xee, 0xff))) g.FillEllipse(b, fx - d / 2f, fy - d / 2f, d, d);
                    }
            }
            else if (LiveBg == 2)
            {
                // langsamer Farbverlauf (eine Runde etwa alle 40 s), sehr zurueckhaltend
                float hue = (float)(t * 9 % 360);
                Color c1 = Hue(hue), c2 = Hue((hue + 120) % 360);
                var full = new Rectangle(-origin.X, -origin.Y, Math.Max(area.Width, 1600), Math.Max(area.Height, 900));
                using (var b = new LinearGradientBrush(full, Color.FromArgb(34, c1), Color.FromArgb(34, c2), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(b, area);
            }
            else if (LiveBg == 4 || LiveBg == 5) DrawRainSnow(g, area, origin, t, LiveBg == 5);   // 3.4.6
            else if (LiveBg >= 10) DrawLiveBg430(g, area, origin, t);   // 4.3
            else if (LiveBg >= 6) DrawLiveBg420(g, area, origin, t);   // 4.2
            else
            {
                // Buehnennebel: weiche Schwaden unten, ziehen langsam vorbei (Lage im Fenster, damit es ueber alle Flaechen passt)
                float span = Math.Max(F(1600), origin.X + area.Right + F(300));
                float baseY = area.Bottom;
                for (int i = 0; i < 7; i++)
                {
                    float w = F(260 + i * 40), hh = F(90 + (i % 3) * 30);
                    float fx = (float)((t * (8 + i * 3) + i * 237) % (span + w)) - w;   // Fenster-Koordinaten
                    var e = new RectangleF(fx - origin.X, baseY - hh * 0.7f, w, hh);
                    if (e.Right < area.X || e.X > area.Right) continue;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(e);
                        using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(26, 0xd0, 0xd8, 0xe8), SurroundColors = new[] { Color.FromArgb(0, 0xd0, 0xd8, 0xe8) } })
                            g.FillEllipse(pb, e);
                    }
                }
            }
            g.SmoothingMode = sm;
        }

        private static Color Hue(float h)
        {
            float x = 1f - Math.Abs(h / 60f % 2f - 1f);
            float r, gg, b;
            if (h < 60) { r = 1; gg = x; b = 0; } else if (h < 120) { r = x; gg = 1; b = 0; } else if (h < 180) { r = 0; gg = 1; b = x; }
            else if (h < 240) { r = 0; gg = x; b = 1; } else if (h < 300) { r = x; gg = 0; b = 1; } else { r = 1; gg = 0; b = x; }
            return Color.FromArgb(255, (int)(r * 255), (int)(gg * 255), (int)(b * 255));
        }

        // ================================================================ Fallblatt- und Nixie-Uhr

        /// <summary>Art der Uhr: 0 Studio, 1 LCD, 2 analog, 3 Fallblatt, 4 Nixie.</summary>
        public static int ClockKind;

        /// <summary>Fallblatt-Uhr: zwei Karten fuer Stunden, zwei fuer Minuten, mit Trennspalt in der Mitte. staticPart = nur das Gehaeuse.</summary>
        public static void DrawFlipClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, F(5));
                StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), F(1), r, F(5));
                g.SmoothingMode = sm;
                return;
            }
            var now = DateTime.Now;
            string t = now.ToString("HHmm");
            float pad = F(5), gapMid = F(7);
            float ch = r.Height - pad * 2 - F(10), cw = Math.Min(ch * 0.7f, (r.Width - pad * 2 - gapMid - F(6)) / 4f);
            float total = cw * 4 + F(3) * 2 + gapMid;
            float x = r.X + (r.Width - total) / 2f, y = r.Y + pad;
            var font = Ui(Math.Max(8f, ch / S * 0.72f), FontStyle.Bold);
            for (int i = 0; i < 4; i++)
            {
                var card = new RectangleF(x, y, cw, ch);
                using (var b = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(0x30, 0x32, 0x36), Color.FromArgb(0x16, 0x17, 0x1a), LinearGradientMode.Vertical))
                    FillRound(g, b, card, F(2.5f));
                using (var b = new SolidBrush(Color.FromArgb(0xf2, 0xf2, 0xee)))
                    Str(g, t[i].ToString(), font, b, card, Center);
                // Spalt in der Mitte und Glanz oben
                using (var pen = new Pen(Color.FromArgb(0x05, 0x05, 0x06), Math.Max(1f, F(1.2f)))) g.DrawLine(pen, card.X, card.Y + card.Height / 2f, card.Right, card.Y + card.Height / 2f);
                using (var b = new SolidBrush(Color.FromArgb(22, 255, 255, 255))) g.FillRectangle(b, card.X + F(1), card.Y + F(1), card.Width - F(2), card.Height / 2f - F(1.5f));
                x += cw + (i == 1 ? gapMid : F(3));
            }
            using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                Str(g, now.ToString("ss") + " s", Ui(7.5f, FontStyle.Bold), b, new RectangleF(r.X, r.Bottom - F(12), r.Width, F(10)), Center);
            g.SmoothingMode = sm;
        }

        /// <summary>Nixie-Uhr: vier Roehren mit orange gluehenden Ziffern hinter einem Gitter, Doppelpunkt aus Glimmlampen.</summary>
        public static void DrawNixieClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float pad = F(5), colon = F(8);
            float th = r.Height - pad * 2, tw = Math.Min(th * 0.56f, (r.Width - pad * 2 - colon - F(9)) / 4f);
            float total = tw * 4 + F(3) * 2 + colon;
            float x0 = r.X + (r.Width - total) / 2f, y = r.Y + pad;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0x0b, 0x0a, 0x09), r, F(5));
                StrokeRound(g, Color.FromArgb(0x2a, 0x24, 0x1e), F(1), r, F(5));
                float x = x0;
                for (int i = 0; i < 4; i++)
                {
                    var tube = new RectangleF(x, y, tw, th);
                    FillRound(g, Color.FromArgb(0x12, 0x0b, 0x07), tube, tw / 2.2f);
                    using (var pen = new Pen(Color.FromArgb(40, 255, 220, 180), Math.Max(1f, F(0.8f)))) g.DrawPath(pen, Round(tube, tw / 2.2f));
                    x += tw + (i == 1 ? colon : F(3));
                }
                g.SmoothingMode = sm;
                return;
            }
            var now = DateTime.Now;
            string t = now.ToString("HHmm");
            var font = Ui(Math.Max(8f, th / S * 0.62f));
            Color glow = Color.FromArgb(0xff, 0x8c, 0x2e);
            float xx = x0;
            for (int i = 0; i < 4; i++)
            {
                var tube = new RectangleF(xx, y, tw, th);
                using (var path = Round(tube, tw / 2.2f))
                {
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, glow), SurroundColors = new[] { Color.FromArgb(0, glow) } })
                        g.FillPath(pb, path);
                }
                using (var b = new SolidBrush(Color.FromArgb(50, glow)))
                    foreach (var o in new[] { new PointF(-F(1.2f), 0), new PointF(F(1.2f), 0), new PointF(0, -F(1.2f)), new PointF(0, F(1.2f)) })
                        Str(g, t[i].ToString(), font, b, new RectangleF(tube.X + o.X, tube.Y + o.Y, tube.Width, tube.Height), Center);
                using (var b = new SolidBrush(Mix(glow, Color.FromArgb(0xff, 0xe2, 0xa8), 0.45f))) Str(g, t[i].ToString(), font, b, tube, Center);
                // Gitter
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                    for (float gx = tube.X + F(2); gx < tube.Right - F(1); gx += Math.Max(2f, F(2.5f))) g.DrawLine(pen, gx, tube.Y + F(3), gx, tube.Bottom - F(3));
                xx += tw + (i == 1 ? colon : F(3));
                if (i == 1)
                {
                    float cx = xx - colon / 2f - F(1.5f), d = F(3);
                    bool on = now.Millisecond < 500 || true;
                    using (var b = new SolidBrush(on ? Color.FromArgb(0xff, 0x9a, 0x40) : Color.FromArgb(0x40, 0x22, 0x10)))
                    {
                        g.FillEllipse(b, cx - d / 2f, y + th * 0.33f - d / 2f, d, d);
                        g.FillEllipse(b, cx - d / 2f, y + th * 0.67f - d / 2f, d, d);
                    }
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Kleine Fallblatt- bzw. Nixie-Uhr fuer die Kopfleiste.</summary>
        public static void DrawClockSmall(Graphics g, RectangleF r, int kind)
        {
            if (kind == 6) { DrawWordClockSmall(g, r); return; }     // 4.0
            if (kind == 7) { DrawBinaryClockSmall(g, r); return; }   // 4.0
            if (kind == 8) { DrawScopeClockSmall(g, r); return; }    // 4.0 finale
            if (kind == 5) { DrawRollClock(g, r, true, true); DrawRollClock(g, r, false, true); }   // 3.4
            else if (kind == 3) { DrawFlipClock(g, r, true); DrawFlipClockCompact(g, r); }
            else { DrawNixieClock(g, r, true); DrawNixieClock(g, r, false); }
        }

        private static void DrawFlipClockCompact(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var now = DateTime.Now;
            string t = now.ToString("HHmm");
            float pad = F(2.5f), gapMid = F(5);
            float ch = r.Height - pad * 2, cw = Math.Min(ch * 0.68f, (r.Width - pad * 2 - gapMid - F(3)) / 4f);
            float total = cw * 4 + F(1.5f) * 2 + gapMid;
            float x = r.X + (r.Width - total) / 2f, y = r.Y + pad;
            var font = Ui(Math.Max(7f, ch / S * 0.7f), FontStyle.Bold);
            for (int i = 0; i < 4; i++)
            {
                var card = new RectangleF(x, y, cw, ch);
                FillRound(g, Color.FromArgb(0x26, 0x28, 0x2c), card, F(1.5f));
                using (var b = new SolidBrush(Color.FromArgb(0xf2, 0xf2, 0xee))) Str(g, t[i].ToString(), font, b, card, Center);
                using (var pen = new Pen(Color.FromArgb(0x05, 0x05, 0x06), 1f)) g.DrawLine(pen, card.X, card.Y + card.Height / 2f, card.Right, card.Y + card.Height / 2f);
                x += cw + (i == 1 ? gapMid : F(1.5f));
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ LED-Kranz um die Drehknoepfe

        public static bool KnobRing;

        /// <summary>Kranz aus 15 kleinen LED-Segmenten (wie an Digitalpulten) – leuchtet bis zur Stellung in der Leuchtfarbe.</summary>
        public static void DrawKnobRing(Graphics g, PointF c, float radius, float value, Color led, bool bipolar, float ringOffset)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int dots = 15;
            float r0 = radius + F(ringOffset) - F(1.6f), r1 = r0 + F(3.6f);
            int centerIdx = dots / 2;
            int valueIdx = (int)Math.Round((bipolar ? (value + 1f) / 2f : value) * (dots - 1));
            for (int i = 0; i < dots; i++)
            {
                double ang = (-135 + 270.0 * i / (dots - 1) - 90) * Math.PI / 180.0;
                float cs = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
                bool lit = bipolar ? (i >= Math.Min(centerIdx, valueIdx) && i <= Math.Max(centerIdx, valueIdx)) : i <= valueIdx;
                var p1 = new PointF(c.X + cs * r0, c.Y + sn * r0);
                var p2 = new PointF(c.X + cs * r1, c.Y + sn * r1);
                if (lit)
                {
                    using (var pen = new Pen(Alpha(led, 70), Math.Max(2f, F(3.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, p1, p2);
                    using (var pen = new Pen(Lighten(led, 0.2f), Math.Max(1.2f, F(1.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, p1, p2);
                }
                else using (var pen = new Pen(Color.FromArgb(0x16, 0x17, 0x1a), Math.Max(1.2f, F(1.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, p1, p2);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Aufwaermen beim Start

        /// <summary>Wie weit Lampen und LEDs schon gluehen (0..1; 1 = voll).</summary>
        public static float WarmLevel = 1f;

        /// <summary>Farbe nach dem Aufwaermen (dunkler und waermer, solange es noch nicht voll gluehen).</summary>
        public static Color WarmColor(Color c)
        {
            float w = WarmLevel;
            if (w >= 0.999f) return c;
            float f = 0.12f + 0.88f * w;
            var d = Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f * (0.75f + 0.25f * w)), (int)(c.B * f * (0.6f + 0.4f * w)));
            return d;
        }
    }
}

namespace Mischpult.UI
{
    internal static partial class Theme
    {
        /// <summary>3.3: Zahlen als echte Punktmatrix – die Schrift wird in ein grobes Raster zerlegt, jede Zelle ist ein Leuchtpunkt.</summary>
        private static void DrawDotMatrix(Graphics g, RectangleF r, string text, Color bg, Color fg, float rad, Font font)
        {
            FillRound(g, bg, r, rad);
            float p = Math.Max(2.2f, F(2.4f));
            int cols = Math.Max(4, (int)((r.Width - F(4)) / p)), rows = Math.Max(5, (int)((r.Height - F(3)) / p));
            bool[] lit = DotCells(text ?? "", cols, rows);
            float x0 = r.X + (r.Width - cols * p) / 2f, y0 = r.Y + (r.Height - rows * p) / 2f;
            float dot = Math.Max(1.2f, p * 0.72f);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var off = new SolidBrush(Alpha(fg, 24)))
            using (var on = new SolidBrush(fg))
            using (var glow = new SolidBrush(Alpha(fg, 50)))
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        float cx = x0 + x * p + (p - dot) / 2f, cy = y0 + y * p + (p - dot) / 2f;
                        if (lit[y * cols + x])
                        {
                            g.FillEllipse(glow, cx - dot * 0.35f, cy - dot * 0.35f, dot * 1.7f, dot * 1.7f);
                            g.FillEllipse(on, cx, cy, dot, dot);
                        }
                        else g.FillEllipse(off, cx, cy, dot, dot);
                    }
            g.SmoothingMode = sm;
        }

        private static readonly Dictionary<string, bool[]> DotCache = new Dictionary<string, bool[]>();

        /// <summary>Welche Rasterzellen leuchten (Schrift fein gezeichnet, dann pro Zelle gemittelt) – gemerkt pro Text und Groesse.</summary>
        private static bool[] DotCells(string text, int cols, int rows)
        {
            string key = text + "\u0001" + cols + "x" + rows;
            if (DotCache.TryGetValue(key, out var hit)) return hit;
            const int k = 4;
            var cells = new bool[cols * rows];
            using (var bmp = new Bitmap(cols * k, rows * k, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    float px = rows * k * 0.92f;
                    using (var f = new Font(FontFamily.GenericMonospace, px, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        var sz = g.MeasureString(text, f, int.MaxValue, sf);
                        float scale = sz.Width > bmp.Width * 0.98f ? bmp.Width * 0.98f / Math.Max(1f, sz.Width) : 1f;
                        Font use = f;
                        if (scale < 1f) use = new Font(FontFamily.GenericMonospace, px * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                        try { g.DrawString(text, use, Brushes.White, new RectangleF(0, k * 0.4f, bmp.Width, bmp.Height), sf); }
                        finally { if (!ReferenceEquals(use, f)) use.Dispose(); }
                    }
                }
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var px = new int[bmp.Width * bmp.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                    for (int cy = 0; cy < rows; cy++)
                        for (int cx = 0; cx < cols; cx++)
                        {
                            int sum = 0;
                            for (int yy = 0; yy < k; yy++)
                                for (int xx = 0; xx < k; xx++)
                                    sum += (px[(cy * k + yy) * bmp.Width + cx * k + xx] >> 8) & 255;   // gruen reicht (weiss auf schwarz)
                            cells[cy * cols + cx] = sum > k * k * 255 * 0.32f;
                        }
                }
                finally { bmp.UnlockBits(data); }
            }
            if (DotCache.Count > 256) DotCache.Clear();
            DotCache[key] = cells;
            return cells;
        }
    }
}

namespace Mischpult.UI
{
    internal static partial class Theme
    {
        /// <summary>3.3: ON-AIR-Leuchtkasten wie ueber der Studiotuer – an: rot leuchtend mit Schein, aus: dunkles Glas.</summary>
        public static void DrawOnAir(Graphics g, RectangleF r, bool on)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = F(3);
            if (on)
                using (var path = new GraphicsPath())
                {
                    var glow = RectangleF.Inflate(r, F(6), F(5));
                    path.AddEllipse(glow);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(90, 0xff, 0x30, 0x20), SurroundColors = new[] { Color.FromArgb(0, 0xff, 0x30, 0x20) } })
                        g.FillPath(pb, path);
                }
            FillRound(g, Color.FromArgb(0x10, 0x10, 0x12), RectangleF.Inflate(r, F(1), F(1)), rad + F(1));
            var face = RectangleF.Inflate(r, -F(1.5f), -F(1.5f));
            using (var b = new LinearGradientBrush(new RectangleF(face.X, face.Y - 1, face.Width, face.Height + 2),
                on ? Color.FromArgb(0xff, 0x4a, 0x36) : Color.FromArgb(0x3a, 0x0e, 0x0b), on ? Color.FromArgb(0xc4, 0x16, 0x10) : Color.FromArgb(0x22, 0x07, 0x06), LinearGradientMode.Vertical))
                FillRound(g, b, face, rad);
            using (var b = new LinearGradientBrush(new RectangleF(face.X, face.Y, face.Width, face.Height * 0.5f + 1), Color.FromArgb(on ? 70 : 30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(face.X + F(1), face.Y + F(0.5f), face.Width - F(2), face.Height * 0.45f), rad);
            var font = Ui(Math.Max(7f, Math.Min(10f, face.Height / S * 0.5f)), FontStyle.Bold);
            using (var b = new SolidBrush(on ? Color.FromArgb(0xff, 0xf4, 0xee) : Color.FromArgb(0x7a, 0x3a, 0x34)))
                Str(g, "ON AIR", font, b, face, Center);
            g.SmoothingMode = sm;
        }
    }
}
