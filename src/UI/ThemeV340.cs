using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4 – Optik: Rollzaehlwerk-Uhr, Licht nach Uhrzeit, Typenschild, Aufkleber, Studio-Boxen, Staubschutzhaube,
    /// PC-Auslastung als Zeigerinstrument, zweifarbige Namensschilder.
    /// </summary>
    internal static partial class Theme
    {
        // ================================================================ Rollzaehlwerk (Art der Uhr 5)

        /// <summary>Rollzaehlwerk: Ziffernrollen wie am Bandzaehler; beim Sekundenwechsel rollt die letzte Rolle sichtbar weiter.</summary>
        public static void DrawRollClock(Graphics g, RectangleF r, bool staticPart, bool small = false)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (staticPart)
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0x30, 0x32, 0x36), Color.FromArgb(0x14, 0x15, 0x18), LinearGradientMode.Vertical))
                    FillRound(g, b, r, F(small ? 3 : 5));
                StrokeRound(g, Color.FromArgb(0x08, 0x08, 0x09), F(1), r, F(small ? 3 : 5));
                g.SmoothingMode = sm;
                return;
            }
            var now = DateTime.Now;
            string t = small ? now.ToString("HHmm") : now.ToString("HHmmss");
            // Bruchteil der laufenden Sekunde: die Sekunden-Einer rollen in den letzten 0,25 s weiter
            float frac = now.Millisecond >= 750 ? (now.Millisecond - 750) / 250f : 0f;
            int n = t.Length;
            float pad = F(small ? 2.5f : 5), gap = F(small ? 1.2f : 2), groupGap = F(small ? 3 : 6);
            int groups = n / 2;
            float wheelH = r.Height - pad * 2 - (small ? 0 : F(10));
            float wheelW = Math.Min(wheelH * 0.62f, (r.Width - pad * 2 - gap * (n - 1) - groupGap * (groups - 1)) / n);
            float total = wheelW * n + gap * (n - groups) + groupGap * (groups - 1);
            float x = r.X + (r.Width - total) / 2f, y = r.Y + pad;
            var font = Ui(Math.Max(7f, wheelH / S * 0.62f), FontStyle.Bold);
            for (int i = 0; i < n; i++)
            {
                var wr = new RectangleF(x, y, wheelW, wheelH);
                var st = g.Save();
                g.SetClip(wr, CombineMode.Intersect);
                // Rolle: in der Mitte hell, oben und unten dunkler (Woelbung)
                using (var b = new LinearGradientBrush(new RectangleF(wr.X, wr.Y - 1, wr.Width, wr.Height + 2), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0x5a, 0x5c, 0x60), Color.FromArgb(0xee, 0xec, 0xe4), Color.FromArgb(0xee, 0xec, 0xe4), Color.FromArgb(0x4a, 0x4c, 0x50) },
                        Positions = new[] { 0f, 0.3f, 0.7f, 1f }
                    };
                    g.FillRectangle(b, wr);
                }
                int d = t[i] - '0';
                bool rolling = i == n - 1 && frac > 0f && !small;
                // naechste Ziffer kommt von unten nach oben
                float off = rolling ? frac * wheelH : 0f;
                using (var b = new SolidBrush(Color.FromArgb(0x14, 0x14, 0x16)))
                {
                    Str(g, d.ToString(), font, b, new RectangleF(wr.X, wr.Y - off, wr.Width, wr.Height), Center);
                    if (rolling) Str(g, ((d + 1) % 10).ToString(), font, b, new RectangleF(wr.X, wr.Y + wheelH - off, wr.Width, wr.Height), Center);
                }
                // Schatten oben/unten ueber die Ziffern (Rolle verschwindet im Gehaeuse)
                using (var b = new LinearGradientBrush(new RectangleF(wr.X, wr.Y - 1, wr.Width, wr.Height + 2), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0) },
                        Positions = new[] { 0f, 0.28f, 0.72f, 1f }
                    };
                    g.FillRectangle(b, wr);
                }
                g.Restore(st);
                StrokeRound(g, Color.FromArgb(0x06, 0x06, 0x07), 1f, wr, F(1));
                x += wheelW + ((i % 2 == 1 && i < n - 1) ? groupGap : gap);
            }
            if (!small)
                using (var b = new SolidBrush(Color.FromArgb(0x8a, 0x90, 0x98)))
                    Str(g, now.ToString("ddd d. MMM", System.Globalization.CultureInfo.GetCultureInfo("de-DE")).ToUpperInvariant(), Ui(7f, FontStyle.Bold), b,
                        new RectangleF(r.X, r.Bottom - F(12), r.Width, F(10)), Center);
            g.SmoothingMode = sm;
        }

        // ================================================================ Licht nach Uhrzeit

        public static bool SunOn;
        public static int SunWindowW = 1200;
        /// <summary>Nur Vorschau/Test: feste Stunde (-1 = jetzt).</summary>
        public static float SunHourOverride = -1f;

        /// <summary>Licht nach Uhrzeit: Farbe, Richtung (0 = von links, 1 = von oben, 2 = von rechts) und Staerke.</summary>
        public static void SunState(float hour, out Color col, out float fromX, out int alpha)
        {
            if (hour >= 6f && hour < 10f) { col = Color.FromArgb(0xff, 0xc8, 0x8a); fromX = 0f; alpha = 30; }          // Morgen: warm von links
            else if (hour >= 10f && hour < 16f) { col = Color.FromArgb(0xff, 0xf6, 0xe0); fromX = 0.5f; alpha = 22; }   // Mittag: hell von oben
            else if (hour >= 16f && hour < 20f) { col = Color.FromArgb(0xff, 0x9a, 0x4a); fromX = 1f; alpha = 34; }     // Abend: orange von rechts
            else { col = Color.FromArgb(0x6a, 0x8a, 0xd8); fromX = 0.5f; alpha = 18; }                                 // Nacht: kuehles Mondlicht
        }

        public static float SunHour => SunHourOverride >= 0f ? SunHourOverride : (float)DateTime.Now.TimeOfDay.TotalHours;

        /// <summary>Schraeges Licht ueber das Pult (Lage im Fenster: origin), heller auf der Seite, von der es kommt.</summary>
        public static void SunLight(Graphics g, RectangleF area, Point origin)
        {
            if (!SunOn || area.Width <= 0 || area.Height <= 0) return;
            SunState(SunHour, out var col, out float fromX, out int alpha);
            float w = Math.Max(200, SunWindowW), h = Math.Max(200, TiltWindowH);
            float x0 = -origin.X, y0 = -origin.Y;
            var st = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            if (Math.Abs(fromX - 0.5f) < 0.01f)
            {
                using (var b = new LinearGradientBrush(new RectangleF(x0, y0 - 1, w, h + 2), Color.FromArgb(alpha, col), Color.FromArgb(0, col), LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(alpha, col), Color.FromArgb(alpha / 3, col), Color.FromArgb(0, col) }, Positions = new[] { 0f, 0.45f, 1f } };
                    g.FillRectangle(b, area);
                }
            }
            else
            {
                // schraeg von oben links bzw. oben rechts
                var rect = new RectangleF(x0 - 1, y0 - 1, w + 2, h + 2);
                float angle = fromX < 0.5f ? 25f : 155f;
                using (var b = new LinearGradientBrush(rect, Color.FromArgb(alpha, col), Color.FromArgb(0, col), angle))
                {
                    b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(alpha, col), Color.FromArgb(alpha / 2, col), Color.FromArgb(0, col), Color.FromArgb(alpha / 3, 0, 0, 0) }, Positions = new[] { 0f, 0.3f, 0.7f, 1f } };
                    g.FillRectangle(b, area);
                }
            }
            g.Restore(st);
        }

        // ================================================================ Typenschild

        /// <summary>Graviertes Metallschild mit vier Schrauben (z. B. "Jans Studio").</summary>
        public static void DrawTypePlate(Graphics g, RectangleF r, string text)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 30 || r.Height < 10) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, Color.FromArgb(90, 0, 0, 0), new RectangleF(r.X + F(1), r.Y + F(1.5f), r.Width, r.Height), F(3));
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0xe4, 0xe6, 0xea), Color.FromArgb(0x8e, 0x93, 0x9a), LinearGradientMode.Vertical))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0xf0, 0xf2, 0xf4), Color.FromArgb(0xc4, 0xc8, 0xce), Color.FromArgb(0xd8, 0xdb, 0xe0), Color.FromArgb(0x9a, 0x9e, 0xa6) },
                    Positions = new[] { 0f, 0.45f, 0.55f, 1f }
                };
                FillRound(g, b, r, F(3));
            }
            // gebuerstete Linien
            var st = g.Save();
            using (var path = Round(r, F(3))) g.SetClip(path, CombineMode.Intersect);
            using (var pen = new Pen(Color.FromArgb(20, 255, 255, 255), 1f))
                for (float y = r.Y + 1; y < r.Bottom; y += 2f) g.DrawLine(pen, r.X, y, r.Right, y);
            g.Restore(st);
            StrokeRound(g, Color.FromArgb(0x5a, 0x5e, 0x64), F(1), r, F(3));
            // Schrauben
            float sr = Math.Max(1.5f, Math.Min(F(2.6f), r.Height * 0.14f));
            foreach (var p in new[] { new PointF(r.X + sr * 2.2f, r.Y + r.Height / 2f), new PointF(r.Right - sr * 2.2f, r.Y + r.Height / 2f) })
            {
                using (var b = new LinearGradientBrush(new RectangleF(p.X - sr, p.Y - sr, sr * 2, sr * 2), Color.FromArgb(0xfa, 0xfa, 0xfa), Color.FromArgb(0x60, 0x64, 0x6a), LinearGradientMode.ForwardDiagonal))
                    g.FillEllipse(b, p.X - sr, p.Y - sr, sr * 2, sr * 2);
                using (var pen = new Pen(Color.FromArgb(0x40, 0x44, 0x4a), Math.Max(1f, F(0.7f)))) g.DrawLine(pen, p.X - sr * 0.7f, p.Y + sr * 0.3f, p.X + sr * 0.7f, p.Y - sr * 0.3f);
            }
            // gravierte Schrift: dunkel mit heller Kante darunter
            var tr = new RectangleF(r.X + sr * 4.5f, r.Y, r.Width - sr * 9f, r.Height);
            var font = Fit(g, text, tr.Width, Math.Max(7f, Math.Min(13f, r.Height / S * 0.55f)), 6.5f, FontStyle.Bold);
            using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) Str(g, text, font, b, new RectangleF(tr.X, tr.Y + 1, tr.Width, tr.Height), Center);
            using (var b = new SolidBrush(Color.FromArgb(0x2a, 0x2c, 0x30))) Str(g, text, font, b, tr, Center);
            g.SmoothingMode = sm;
        }

        // ================================================================ Aufkleber

        public static readonly string[] StickerNames = { "Stern", "Blitz", "Herz", "ON AIR", "Smiley", "Daumen hoch", "Note", "GG", "Flamme" };

        /// <summary>Aufkleber (gestanzt mit weissem Rand und leichtem Schatten), Mitte c, Groesse s, gedreht.</summary>
        public static void DrawSticker(Graphics g, int kind, PointF c, float s, float angle)
        {
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            using (var path = StickerPath(kind, s))
            {
                // Schatten
                var mx = new Matrix(); mx.Translate(F(1), F(1.5f));
                using (var sh = (GraphicsPath)path.Clone()) { sh.Transform(mx); using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillPath(b, sh); }
                // weisser Stanzrand
                using (var pen = new Pen(Color.FromArgb(0xfa, 0xfa, 0xf6), Math.Max(2f, s * 0.12f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                Color fill = StickerColor(kind);
                using (var b = new LinearGradientBrush(new RectangleF(-s / 2f, -s / 2f - 1, s, s + 2), Lighten(fill, 0.2f), Darken(fill, 0.12f), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
            }
            DrawStickerDetail(g, kind, s);
            // Glanz
            using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillEllipse(b, -s * 0.35f, -s * 0.45f, s * 0.5f, s * 0.25f);
            g.Restore(st);
        }

        private static Color StickerColor(int kind)
        {
            switch (kind)
            {
                case 0: return Color.FromArgb(0xff, 0xc8, 0x2a);
                case 1: return Color.FromArgb(0xff, 0xd4, 0x1e);
                case 2: return Color.FromArgb(0xe8, 0x2c, 0x48);
                case 3: return Color.FromArgb(0xd8, 0x1e, 0x18);
                case 4: return Color.FromArgb(0xff, 0xd8, 0x34);
                case 5: return Color.FromArgb(0x2c, 0x9c, 0xff);
                case 6: return Color.FromArgb(0x8a, 0x5c, 0xf0);
                case 7: return Color.FromArgb(0x1c, 0x1e, 0x22);
                default: return Color.FromArgb(0xff, 0x6a, 0x1a);
            }
        }

        private static GraphicsPath StickerPath(int kind, float s)
        {
            var p = new GraphicsPath();
            float h = s / 2f;
            switch (kind)
            {
                case 0:   // Stern
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5;
                            float rr = i % 2 == 0 ? h : h * 0.45f;
                            pts[i] = new PointF((float)Math.Cos(a) * rr, (float)Math.Sin(a) * rr);
                        }
                        p.AddPolygon(pts);
                    }
                    break;
                case 1:   // Blitz
                    p.AddPolygon(new[] { new PointF(h * 0.15f, -h), new PointF(-h * 0.6f, h * 0.1f), new PointF(-h * 0.05f, h * 0.1f), new PointF(-h * 0.25f, h),
                                          new PointF(h * 0.6f, -h * 0.15f), new PointF(h * 0.05f, -h * 0.15f) });
                    break;
                case 2:   // Herz
                    p.AddBezier(0, h * 0.85f, -h * 1.05f, h * 0.05f, -h * 0.75f, -h * 0.95f, 0, -h * 0.4f);
                    p.AddBezier(0, -h * 0.4f, h * 0.75f, -h * 0.95f, h * 1.05f, h * 0.05f, 0, h * 0.85f);
                    p.CloseFigure();
                    break;
                case 3:   // ON AIR (Rechteck)
                case 7:   // GG
                    p.AddPath(Round(new RectangleF(-h * 1.15f, -h * 0.5f, h * 2.3f, h), h * 0.22f), false);
                    break;
                case 6:   // Note: Kreis
                case 4:   // Smiley
                case 5:   // Daumen: Kreis
                    p.AddEllipse(-h, -h, s, s);
                    break;
                default:  // Flamme
                    p.AddBezier(0, -h, h * 0.9f, -h * 0.1f, h * 0.8f, h * 0.9f, 0, h);
                    p.AddBezier(0, h, -h * 0.8f, h * 0.9f, -h * 0.9f, -h * 0.1f, 0, -h);
                    p.CloseFigure();
                    break;
            }
            return p;
        }

        private static void DrawStickerDetail(Graphics g, int kind, float s)
        {
            float h = s / 2f;
            switch (kind)
            {
                case 3:
                case 7:
                    using (var b = new SolidBrush(kind == 3 ? Color.White : Color.FromArgb(0x4a, 0xf0, 0x7a)))
                        Str(g, kind == 3 ? "ON AIR" : "GG", Ui(Math.Max(5f, h / S * (kind == 3 ? 0.62f : 0.95f)), FontStyle.Bold), b, new RectangleF(-h * 1.15f, -h * 0.5f, h * 2.3f, h), Center);
                    break;
                case 4:   // Smiley
                    using (var b = new SolidBrush(Color.FromArgb(0x3a, 0x2a, 0x10)))
                    {
                        g.FillEllipse(b, -h * 0.4f, -h * 0.35f, h * 0.2f, h * 0.28f);
                        g.FillEllipse(b, h * 0.2f, -h * 0.35f, h * 0.2f, h * 0.28f);
                    }
                    using (var pen = new Pen(Color.FromArgb(0x3a, 0x2a, 0x10), Math.Max(1.2f, s * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, -h * 0.55f, -h * 0.4f, h * 1.1f, h * 1.05f, 30, 120);
                    break;
                case 5:   // Daumen hoch
                    using (var b = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(b, -h * 0.55f, -h * 0.05f, h * 0.28f, h * 0.6f);
                        using (var path = Round(new RectangleF(-h * 0.2f, -h * 0.1f, h * 0.75f, h * 0.65f), h * 0.12f)) g.FillPath(b, path);
                        using (var path = Round(new RectangleF(-h * 0.12f, -h * 0.62f, h * 0.3f, h * 0.62f), h * 0.12f)) g.FillPath(b, path);
                    }
                    break;
                case 6:   // Note
                    using (var b = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(b, -h * 0.45f, h * 0.15f, h * 0.45f, h * 0.35f);
                        g.FillRectangle(b, -h * 0.06f, -h * 0.55f, h * 0.12f, h * 0.9f);
                        g.FillPolygon(b, new[] { new PointF(h * 0.06f, -h * 0.55f), new PointF(h * 0.45f, -h * 0.35f), new PointF(h * 0.45f, -h * 0.15f), new PointF(h * 0.06f, -h * 0.3f) });
                    }
                    break;
                case 8:   // Flamme: heller Kern
                    using (var b = new SolidBrush(Color.FromArgb(0xff, 0xd6, 0x4a)))
                    {
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(0, -h * 0.3f, h * 0.5f, h * 0.2f, h * 0.4f, h * 0.85f, 0, h * 0.85f);
                            path.AddBezier(0, h * 0.85f, -h * 0.4f, h * 0.85f, -h * 0.5f, h * 0.2f, 0, -h * 0.3f);
                            g.FillPath(b, path);
                        }
                    }
                    break;
            }
        }

        // ================================================================ Studio-Boxen

        /// <summary>Studio-Box: Gehaeuse, Hochtoener oben, Tieftoener unten – die Membran schwingt mit level (0..1).</summary>
        public static void DrawSpeaker(Graphics g, Rectangle r, float level, bool left)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(r.X + F(5), r.Y + F(8), r.Width - F(10), r.Height - F(16));
            FillRound(g, Color.FromArgb(120, 0, 0, 0), new RectangleF(box.X + F(2), box.Y + F(3), box.Width, box.Height), F(5));
            using (var b = new LinearGradientBrush(new RectangleF(box.X - 1, box.Y, box.Width + 2, box.Height), Color.FromArgb(0x26, 0x27, 0x2a), Color.FromArgb(0x12, 0x13, 0x15), left ? LinearGradientMode.Horizontal : LinearGradientMode.Horizontal))
                FillRound(g, b, box, F(5));
            StrokeRound(g, Color.FromArgb(0x3a, 0x3c, 0x40), F(1), box, F(5));
            float cx = box.X + box.Width / 2f;
            float tw = Math.Min(box.Width * 0.42f, F(26));
            float wf = Math.Min(box.Width * 0.86f, box.Height * 0.45f);
            // Hochtoener
            var tc = new PointF(cx, box.Y + F(10) + tw / 2f);
            DrawDriver(g, tc, tw / 2f, level * 0.3f, true);
            // Tieftoener (unten), darueber ein Bassreflex-Rohr
            var wc = new PointF(cx, box.Bottom - F(12) - wf / 2f);
            DrawDriver(g, wc, wf / 2f, level, false);
            float portY = tc.Y + tw / 2f + (wc.Y - wf / 2f - tc.Y - tw / 2f) / 2f;
            float pw = Math.Min(box.Width * 0.5f, F(22)), ph = Math.Max(F(5), pw * 0.35f);
            using (var b = new SolidBrush(Color.FromArgb(0x05, 0x05, 0x06))) g.FillEllipse(b, cx - pw / 2f, portY - ph / 2f, pw, ph);
            using (var pen = new Pen(Color.FromArgb(0x3a, 0x3c, 0x40), 1f)) g.DrawEllipse(pen, cx - pw / 2f, portY - ph / 2f, pw, ph);
            // kleines Logo-Licht
            using (var b = new SolidBrush(level > 0.05f ? Color.FromArgb(0x4a, 0xd8, 0x7a) : Color.FromArgb(0x1a, 0x3a, 0x24)))
                g.FillEllipse(b, cx - F(2), box.Bottom - F(7), F(4), F(4));
            g.SmoothingMode = sm;
        }

        private static void DrawDriver(Graphics g, PointF c, float rad, float level, bool tweeter)
        {
            level = Math.Max(0f, Math.Min(1f, level));
            // Korb/Ring
            using (var b = new LinearGradientBrush(new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2), Color.FromArgb(0x5a, 0x5e, 0x64), Color.FromArgb(0x18, 0x19, 0x1b), LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(b, c.X - rad, c.Y - rad, rad * 2, rad * 2);
            float sur = rad * 0.86f;
            using (var b = new SolidBrush(Color.FromArgb(0x1c, 0x1c, 0x1e))) g.FillEllipse(b, c.X - sur, c.Y - sur, sur * 2, sur * 2);
            // Membran: je lauter, desto weiter "raus" (groesser, heller in der Mitte)
            float cone = rad * (0.74f + 0.06f * level);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - cone, c.Y - cone, cone * 2, cone * 2);
                using (var pb = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(c.X - cone * 0.15f, c.Y - cone * 0.15f),
                    CenterColor = tweeter ? Color.FromArgb(0xc8, 0xcc, 0xd2) : Color.FromArgb((int)(0x4a + 0x30 * level), (int)(0x4c + 0x30 * level), (int)(0x50 + 0x30 * level)),
                    SurroundColors = new[] { Color.FromArgb(0x10, 0x10, 0x12) }
                })
                    g.FillPath(pb, path);
            }
            // Staubkappe
            float cap = rad * (tweeter ? 0.35f : 0.28f) * (1f + 0.12f * level);
            using (var b = new LinearGradientBrush(new RectangleF(c.X - cap, c.Y - cap, cap * 2, cap * 2), Color.FromArgb(0x7a, 0x7e, 0x84), Color.FromArgb(0x1a, 0x1b, 0x1d), LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(b, c.X - cap, c.Y - cap, cap * 2, cap * 2);
            // Schraubenkranz
            using (var b = new SolidBrush(Color.FromArgb(0x8a, 0x8e, 0x94)))
                for (int i = 0; i < 4; i++)
                {
                    double a = Math.PI / 4 + i * Math.PI / 2;
                    float sx = c.X + (float)Math.Cos(a) * rad * 0.93f, sy = c.Y + (float)Math.Sin(a) * rad * 0.93f;
                    g.FillEllipse(b, sx - F(1.2f), sy - F(1.2f), F(2.4f), F(2.4f));
                }
        }

        // ================================================================ Staubschutzhaube

        /// <summary>Durchsichtige Plastikhaube: getoent, mit Spiegelungen und Kante.</summary>
        public static void DrawDustCover(Graphics g, Rectangle r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0x5a, 0x66, 0x78), Color.FromArgb(0x24, 0x2a, 0x34), LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
            // breite Spiegelung schraeg
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new PointF(r.X + r.Width * 0.12f, r.Y), new PointF(r.X + r.Width * 0.32f, r.Y), new PointF(r.X + r.Width * 0.12f, r.Bottom), new PointF(r.X - r.Width * 0.08f, r.Bottom) });
                using (var b = new SolidBrush(Color.FromArgb(46, 255, 255, 255))) g.FillPath(b, path);
            }
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new PointF(r.X + r.Width * 0.38f, r.Y), new PointF(r.X + r.Width * 0.43f, r.Y), new PointF(r.X + r.Width * 0.23f, r.Bottom), new PointF(r.X + r.Width * 0.18f, r.Bottom) });
                using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillPath(b, path);
            }
            // Kante oben (abgerundete Haube)
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, F(18)), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillRectangle(b, r.X, r.Y, r.Width, F(18));
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), F(2))) g.DrawRectangle(pen, r.X + F(1), r.Y + F(1), r.Width - F(2), r.Height - F(2));
            using (var b = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
                Str(g, "Maus bewegen – die Haube geht auf", Ui(10f), b, new RectangleF(r.X, r.Bottom - F(40), r.Width, F(24)), Center);
            g.SmoothingMode = sm;
        }

        // ================================================================ PC-Auslastung

        /// <summary>Kleines Zeigerinstrument 0–100 % (staticPart = Skala), rot ab 85 %.</summary>
        public static void DrawLoadGauge(Graphics g, RectangleF r, string label, float value, bool staticPart, bool known = true)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = new PointF(r.X + r.Width / 2f, r.Bottom - r.Height * 0.18f);
            float rad = Math.Min(r.Width * 0.46f, r.Height * 0.72f);
            const float a0 = 200f, sweep = 140f;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0xe9, 0xe2, 0xc8), r, F(3));
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(40, 255, 240, 200), Color.FromArgb(30, 0, 0, 0), LinearGradientMode.Vertical))
                    FillRound(g, b, r, F(3));
                StrokeRound(g, Color.FromArgb(0x2a, 0x2c, 0x30), F(1), r, F(3));
                using (var pen = new Pen(Color.FromArgb(0x2a, 0x26, 0x20), Math.Max(1f, F(1))))
                    g.DrawArc(pen, c.X - rad, c.Y - rad, rad * 2, rad * 2, a0, sweep);
                using (var pen = new Pen(Color.FromArgb(0xc8, 0x2a, 0x1e), Math.Max(2f, F(2.4f))))
                    g.DrawArc(pen, c.X - rad, c.Y - rad, rad * 2, rad * 2, a0 + sweep * 0.85f, sweep * 0.15f);
                var f = Ui(Math.Max(5.5f, Math.Min(7.5f, r.Height / S * 0.11f)), FontStyle.Bold);
                using (var pen = new Pen(Color.FromArgb(0x2a, 0x26, 0x20), 1f))
                using (var b = new SolidBrush(Color.FromArgb(0x2a, 0x26, 0x20)))
                    for (int i = 0; i <= 10; i++)
                    {
                        double a = (a0 + sweep * i / 10f) * Math.PI / 180.0;
                        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        float l = i % 5 == 0 ? F(5) : F(3);
                        g.DrawLine(pen, c.X + ca * rad, c.Y + sa * rad, c.X + ca * (rad - l), c.Y + sa * (rad - l));
                        if (i % 5 == 0)
                            Str(g, (i * 10).ToString(), f, b, new RectangleF(c.X + ca * (rad - F(13)) - F(10), c.Y + sa * (rad - F(13)) - F(5), F(20), F(10)), Center);
                    }
                using (var b = new SolidBrush(Color.FromArgb(0x2a, 0x26, 0x20)))
                    Str(g, label, Ui(Math.Max(6f, Math.Min(8.5f, r.Height / S * 0.13f)), FontStyle.Bold), b, new RectangleF(r.X + F(4), r.Y + F(2), r.Width - F(8), F(11)), Left);   // oben links: der Zeiger verdeckt es nie
                g.SmoothingMode = sm;
                return;
            }
            float v = Math.Max(0f, Math.Min(1f, value / 100f));
            double ang = (a0 + sweep * v) * Math.PI / 180.0;
            var tip = new PointF(c.X + (float)Math.Cos(ang) * (rad - F(2)), c.Y + (float)Math.Sin(ang) * (rad - F(2)));
            using (var pen = new Pen(known ? Color.FromArgb(0x1a, 0x18, 0x14) : Color.FromArgb(90, 0x1a, 0x18, 0x14), Math.Max(1f, F(1.3f))) { EndCap = LineCap.Round })
                g.DrawLine(pen, c, tip);
            using (var b = new SolidBrush(Color.FromArgb(0x2a, 0x2c, 0x30))) g.FillEllipse(b, c.X - F(2.5f), c.Y - F(2.5f), F(5), F(5));
            string txt = known ? ((int)Math.Round(value)).ToString() + " %" : "–";
            using (var b = new SolidBrush(v > 0.85f && known ? Color.FromArgb(0xc8, 0x2a, 0x1e) : Color.FromArgb(0x2a, 0x26, 0x20)))
                Str(g, txt, Ui(Math.Max(6f, Math.Min(8.5f, r.Height / S * 0.13f)), FontStyle.Bold), b, new RectangleF(r.X, r.Bottom - F(12), r.Width, F(11)), Center);
            g.SmoothingMode = sm;
        }

        // ================================================================ Zweifarbige Namensschilder

        public static bool TwoTone;

        /// <summary>Zweite Kanalfarbe als Verlauf ueber das Namensschild (links Kanalfarbe, rechts zweite Farbe).</summary>
        public static void DrawTwoToneOverlay(Graphics g, RectangleF r, Color c2, float rad)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, rad))
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.FromArgb(0, c2), Color.FromArgb(235, c2), LinearGradientMode.Horizontal))
            {
                b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, c2), Color.FromArgb(0, c2), Color.FromArgb(235, c2) }, Positions = new[] { 0f, 0.25f, 1f } };
                g.FillPath(b, path);
            }
            g.SmoothingMode = sm;
        }
    }
}

namespace Mischpult.UI
{
    using System.Drawing.Drawing2D;

    internal static partial class Theme
    {
        /// <summary>3.4: Farbige Einlage der Faderkappe in zwei Farben (Verlauf links → rechts).</summary>
        public static void DrawCapInsert2(Graphics g, RectangleF cap, Color c1, Color c2)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Math.Max(3f, F(4.2f));
            var r = new RectangleF(cap.X + F(2.5f), cap.Y + cap.Height / 2f - h / 2f, cap.Width - F(5f), h);
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Lighten(c1, 0.15f), Lighten(c2, 0.15f), LinearGradientMode.Horizontal))
                FillRound(g, b, r, F(1.5f));
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(50, 255, 255, 255), Color.FromArgb(50, 0, 0, 0), LinearGradientMode.Vertical))
                FillRound(g, b, r, F(1.5f));
            StrokeRound(g, Darken(Mix(c1, c2, 0.5f), 0.5f), Math.Max(1f, F(0.7f)), r, F(1.5f));
            g.SmoothingMode = sm;
        }
    }
}
