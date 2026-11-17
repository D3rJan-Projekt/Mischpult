using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.12: Pegel-Farben, LCD-Stile (gruenes LCD, rote LED, Nixie, VFD) und Siebensegment-Ziffern (Studio-Uhr, LED-Anzeigen).
    /// </summary>
    internal static partial class Theme
    {
        // ---------------------------------------------------------------- Pegel-Farben

        /// <summary>0 = klassisch (gruen/gelb/rot), 1 = blau, 2 = bernstein, 3 = in der Kanalfarbe. Oben bleibt es immer rot.</summary>
        public static int MeterStyle;
        public static readonly string[] MeterStyleNames = { "Klassisch", "Blau", "Bernstein", "Kanalfarbe", "Eigene" };

        private static readonly Color BlueLow = Color.FromArgb(0x3d, 0x9b, 0xff), BlueMid = Color.FromArgb(0x9a, 0xe2, 0xff);
        private static readonly Color AmberLow = Color.FromArgb(0xff, 0x9a, 0x1f), AmberMid = Color.FromArgb(0xff, 0xd6, 0x5a);
        private static readonly Color ClassicLow = Color.FromArgb(0x39, 0xd6, 0x6b), ClassicMid = Color.FromArgb(0xf2, 0xd1, 0x3a);
        /// <summary>Gruen fuer Zustaende (offen, unter der Schwelle) – unabhaengig von den Pegel-Farben.</summary>
        public static Color StatusGreen => ClassicLow;

        /// <summary>Helligkeit 0..1 (fuer "genug Leuchtkraft" bei dunklen Kanalfarben).</summary>
        public static float Luma(Color c) => (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

        /// <summary>Kanalfarbe so aufhellen, dass sie als leuchtende LED wirkt (dunkle Farben wuerden im Pegel absaufen).</summary>
        private static Color Luminous(Color ch)
        {
            float l = Luma(ch);
            return l >= 0.5f ? ch : Lighten(ch, Math.Min(0.6f, (0.5f - l) * 1.4f));
        }

        public static Color MeterLow(Color ch)
        {
            switch (MeterStyle)
            {
                case 1: return BlueLow;
                case 2: return AmberLow;
                case 3: return Luminous(ch);
                case 4: return CustomLow;   // 3.2
            }
            return ClassicLow;
        }

        public static Color MeterMid(Color ch)
        {
            switch (MeterStyle)
            {
                case 1: return BlueMid;
                case 2: return AmberMid;
                case 3: return Lighten(Luminous(ch), 0.45f);
                case 4: return CustomMid;   // 3.2
            }
            return ClassicMid;
        }

        /// <summary>Farbe eines Pegel-Segments (frac 0..1 von unten). Bis lowTo "unten", bis midTo "Mitte", darueber rot.</summary>
        public static Color SegColor(float frac, Color ch, float lowTo = 0.55f, float midTo = 0.85f) =>
            NixieBar ? NixieSeg(frac, midTo)   // 4.0: Nixie-Balken
            : MeterStyle == 4 ? LedLevel(frac <= CustomMidFrac ? CustomLow : frac <= CustomTopFrac ? CustomMid : CustomTop)   // 3.2: eigene Farben und Grenzen
                            : LedLevel(frac <= lowTo ? MeterLow(ch) : frac <= midTo ? MeterMid(ch) : MeterRed);

        // ---------------------------------------------------------------- LCD-Stile

        /// <summary>0 = passend zum Design, 1 = gruenes LCD, 2 = rote LED, 3 = Nixie-Roehren, 4 = VFD (tuerkis).</summary>
        public static int LcdStyle;
        public static readonly string[] LcdStyleNames = { "Passend", "Grünes LCD", "Rote LED", "Nixie", "VFD", "Punktmatrix" };

        /// <summary>Tatsaechlich benutzter Stil: "passend" heisst beim Design Vintage Nixie-Roehren.</summary>
        public static int EffLcd => LcdStyle != 0 ? LcdStyle : Vintage || Brass ? 3 : Carbon ? 2 : SynthDesign ? 4 : RetroDesign ? 5 : SteamDesign ? 3 : 0;   // 4.2: Synthwave VFD, Retro Punktmatrix

        /// <summary>Hintergrund und Schrift der Zahlen-Anzeigen (auch fuer die kleinen dB-Kaestchen).</summary>
        public static void LcdColors(out Color bg, out Color fg)
        {
            LcdColorsRaw(out bg, out fg);
            if (WarmLevel < 0.999f) fg = Mix(bg, WarmColor(fg), 0.25f + 0.75f * WarmLevel);   // 3.3: gluehen langsam auf
        }

        private static void LcdColorsRaw(out Color bg, out Color fg)
        {
            switch (EffLcd)
            {
                case 1: bg = Color.FromArgb(0x9d, 0xb5, 0x4a); fg = Color.FromArgb(0x1b, 0x27, 0x0c); return;
                case 2: bg = Color.FromArgb(0x07, 0x02, 0x02); fg = Color.FromArgb(0xff, 0x33, 0x22); return;
                case 3: bg = Color.FromArgb(0x10, 0x08, 0x05); fg = Color.FromArgb(0xff, 0x8c, 0x2e); return;
                case 4: bg = Color.FromArgb(0x03, 0x10, 0x0e); fg = Color.FromArgb(0x4c, 0xf2, 0xd0); return;
                case 5: bg = Color.FromArgb(0x0a, 0x07, 0x02); fg = Color.FromArgb(0xff, 0xb0, 0x1a); return;   // 3.3 Punktmatrix
            }
            bg = LcdBg; fg = LcdText;
        }

        /// <summary>"Kein Wert"-Farbe in den kleinen Kaestchen (zwischen Grund und Schrift).</summary>
        public static Color LcdOffText()
        {
            LcdColors(out var bg, out var fg);
            return EffLcd == 0 ? Darken(fg, 0.55f) : Mix(bg, fg, EffLcd == 1 ? 0.45f : 0.35f);
        }

        /// <summary>Ziffern als "unbeleuchtete Segmente" (8 statt Ziffer, Rest leer) – fuer den Geister-Effekt bei LED/VFD.</summary>
        private static string GhostText(string s)
        {
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++) c[i] = char.IsDigit(c[i]) ? '8' : c[i] == '.' || c[i] == ':' ? c[i] : ' ';
            return new string(c);
        }

        /// <summary>LCD im gewaehlten Stil (statt passend zum Design). dim = Zug ist stumm/abgedunkelt.</summary>
        private static void DrawLcdStyled(Graphics g, RectangleF r, string text, bool dim, Color? custom = null)
        {
            LcdColors(out var bg, out var fg);
            if (custom.HasValue && EffLcd != 1) fg = custom.Value;   // z. B. Sleep-Timer in Blau (nicht auf dem hellen gruenen LCD)
            if (dim) fg = Mix(bg, fg, 0.5f);
            float rad = F(3);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var font = Mono(11.5f);
            switch (EffLcd)
            {
                case 1:   // gruenes LCD: hinterleuchtet, dunkle Ziffern mit leichtem Schatten, feines Pixelraster
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(bg, 0.1f), Darken(bg, 0.08f), LinearGradientMode.Vertical))
                        FillRound(g, b, r, rad);
                    using (var path = Round(RectangleF.Inflate(r, -F(1), -F(1)), rad))
                    {
                        var st = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        g.SmoothingMode = SmoothingMode.None;
                        using (var pen = new Pen(Color.FromArgb(16, 0, 0, 0), 1f))
                        {
                            float step = Math.Max(2f, F(2));
                            for (float x = r.X + 1; x < r.Right; x += step) g.DrawLine(pen, x, r.Y, x, r.Bottom);
                            for (float y = r.Y + 1; y < r.Bottom; y += step) g.DrawLine(pen, r.X, y, r.Right, y);
                        }
                        g.Restore(st);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                    }
                    using (var b = new SolidBrush(Alpha(fg, 45))) Str(g, text, font, b, new RectangleF(r.X + F(1), r.Y + F(1), r.Width, r.Height), Center);
                    using (var b = new SolidBrush(fg)) Str(g, text, font, b, r, Center);
                    break;
                case 2:   // rote LED: schwarz, unbeleuchtete Segmente schimmern dunkelrot, Ziffern gluehen
                case 4:   // VFD: dunkles Glas, feines Punktraster, tuerkis leuchtend
                    FillRound(g, bg, r, rad);
                    if (EffLcd == 4)
                    {
                        var st = g.Save();
                        using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
                        g.SmoothingMode = SmoothingMode.None;
                        using (var b = new SolidBrush(Alpha(fg, 14)))
                        {
                            float step = Math.Max(2f, F(2));
                            for (float y = r.Y + 1; y < r.Bottom; y += step)
                                for (float x = r.X + 1; x < r.Right; x += step) g.FillRectangle(b, x, y, 1, 1);
                        }
                        g.Restore(st);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                    }
                    using (var b = new SolidBrush(Mix(bg, fg, 0.11f))) Str(g, GhostText(text), font, b, r, Center);
                    using (var glow = new SolidBrush(Alpha(fg, 34)))
                    {
                        float d = F(1.2f);
                        Str(g, text, font, glow, new RectangleF(r.X - d, r.Y, r.Width, r.Height), Center);
                        Str(g, text, font, glow, new RectangleF(r.X + d, r.Y, r.Width, r.Height), Center);
                        Str(g, text, font, glow, new RectangleF(r.X, r.Y - d, r.Width, r.Height), Center);
                        Str(g, text, font, glow, new RectangleF(r.X, r.Y + d, r.Width, r.Height), Center);
                    }
                    using (var b = new SolidBrush(fg)) Str(g, text, font, b, r, Center);
                    break;
                case 3:   // Nixie: dunkles Rauchglas mit Gitter, warm gluehende Ziffern
                    FillRound(g, bg, r, rad);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(r.X + r.Width * 0.1f, r.Y - r.Height * 0.2f, r.Width * 0.8f, r.Height * 1.4f);
                        using (var pb = new PathGradientBrush(gp) { CenterColor = Alpha(fg, dim ? 20 : 46), SurroundColors = new[] { Alpha(fg, 0) } })
                        using (var face = Round(r, rad))
                            g.FillPath(pb, face);
                    }
                    {
                        var st = g.Save();
                        using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
                        using (var pen = new Pen(Color.FromArgb(46, 0, 0, 0), 1f))
                        {
                            float step = Math.Max(3f, F(3.5f));
                            for (float x = r.X - r.Height; x < r.Right; x += step)
                            {
                                g.DrawLine(pen, x, r.Bottom, x + r.Height, r.Y);
                                g.DrawLine(pen, x, r.Y, x + r.Height, r.Bottom);
                            }
                        }
                        g.Restore(st);
                    }
                    using (var glow = new SolidBrush(Alpha(fg, 40)))
                    {
                        float d = F(1.4f);
                        foreach (var o in new[] { new PointF(-d, 0), new PointF(d, 0), new PointF(0, -d), new PointF(0, d), new PointF(-d, -d), new PointF(d, d) })
                            Str(g, text, font, glow, new RectangleF(r.X + o.X, r.Y + o.Y, r.Width, r.Height), Center);
                    }
                    using (var b = new SolidBrush(dim ? fg : Mix(fg, Color.FromArgb(0xff, 0xe2, 0xa8), 0.45f))) Str(g, text, font, b, r, Center);
                    break;
                case 5:   // 3.3 Punktmatrix: bernsteinfarbene Leuchtpunkte, dazwischen dunkel
                    DrawDotMatrix(g, r, text, bg, fg, rad, font);
                    break;
            }
            // Glas: Spiegelung oben, Innenschatten, Rahmen
            using (var path = Round(r, rad))
            {
                float sh = Math.Min(1f, F(5) / Math.Max(1f, r.Height));
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(EffLcd == 1 ? 60 : 110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0) },
                        Positions = new[] { 0f, sh, 1f },
                    };
                    g.FillPath(b, path);
                }
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(EffLcd == 1 ? 34 : 18, 255, 255, 255), Color.FromArgb(4, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                        Positions = new[] { 0f, 0.42f, 0.43f, 1f },
                    };
                    g.FillPath(b, path);
                }
            }
            StrokeRound(g, EffLcd == 1 ? Darken(bg, 0.55f) : Color.FromArgb(0x02, 0x02, 0x03), F(1), r, rad);
            using (var pen = new Pen(Color.FromArgb(24, 255, 255, 255), F(1)))
                g.DrawLine(pen, r.X + rad, r.Bottom + F(0.5f), r.Right - rad, r.Bottom + F(0.5f));
            g.SmoothingMode = sm;
        }

        // ---------------------------------------------------------------- Siebensegment-Ziffern

        //  Segmente:  a oben, b oben rechts, c unten rechts, d unten, e unten links, f oben links, g Mitte
        private static readonly Dictionary<char, string> Seg7 = new Dictionary<char, string>
        {
            ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abged", ['3'] = "abgcd", ['4'] = "fgbc", ['5'] = "afgcd",
            ['6'] = "afgedc", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg", ['-'] = "g", ['−'] = "g", [' '] = "",
        };

        /// <summary>Breite einer Ziffer bei Hoehe h (Doppelpunkt/Punkt sind schmaler).</summary>
        public static float Seg7Width(string text, float h)
        {
            float w = 0f;
            foreach (char ch in text) w += ch == ':' || ch == '.' ? h * 0.28f : h * 0.56f;
            return w;
        }

        /// <summary>
        /// Text aus Siebensegment-Ziffern zeichnen (0-9, -, :, ., Leerzeichen), linke obere Ecke (x, y), Ziffernhoehe h.
        /// ghost = Farbe der unbeleuchteten Segmente (oder leer).
        /// </summary>
        public static void DrawSeg7(Graphics g, string text, float x, float y, float h, Color on, Color ghost, bool glow = true)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = h * 0.46f, t = Math.Max(1f, h * 0.12f), gap = Math.Max(0.5f, t * 0.18f), slant = h * 0.06f;
            foreach (char ch in text)
            {
                if (ch == ':' || ch == '.')
                {
                    float cw = h * 0.28f, dot = t * 1.05f;
                    using (var b = new SolidBrush(on))
                    {
                        if (ch == ':')
                        {
                            g.FillEllipse(b, x + cw / 2f - dot / 2f + slant * 0.5f, y + h * 0.3f - dot / 2f, dot, dot);
                            g.FillEllipse(b, x + cw / 2f - dot / 2f - slant * 0.2f, y + h * 0.72f - dot / 2f, dot, dot);
                        }
                        else g.FillEllipse(b, x + cw / 2f - dot / 2f, y + h - dot, dot, dot);
                    }
                    x += cw;
                    continue;
                }
                Seg7.TryGetValue(ch, out string lit);
                lit = lit ?? "";
                foreach (char seg in "abcdefg")
                {
                    bool isOn = lit.IndexOf(seg) >= 0;
                    if (!isOn && ghost.A == 0) continue;
                    var pts = SegPoly(seg, x + (h * 0.56f - w) / 2f, y, w, h, t, gap, slant);
                    if (isOn && glow)
                        using (var pen = new Pen(Alpha(on, 46), t * 1.6f) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, pts);
                    using (var b = new SolidBrush(isOn ? on : ghost)) g.FillPolygon(b, pts);
                }
                x += h * 0.56f;
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Ein Segment als spitz zulaufendes Sechseck (leicht kursiv wie echte LED-Anzeigen).</summary>
        private static PointF[] SegPoly(char seg, float x, float y, float w, float h, float t, float gap, float slant)
        {
            float half = h / 2f, ht = t / 2f;
            PointF[] P(float x1, float y1, float x2, float y2, bool horiz)
            {
                // Segment von (x1,y1) nach (x2,y2), Dicke t, Enden spitz
                if (horiz)
                    return new[] { new PointF(x1 + gap, y1), new PointF(x1 + gap + ht, y1 - ht), new PointF(x2 - gap - ht, y2 - ht),
                                   new PointF(x2 - gap, y2), new PointF(x2 - gap - ht, y2 + ht), new PointF(x1 + gap + ht, y1 + ht) };
                return new[] { new PointF(x1, y1 + gap), new PointF(x1 + ht, y1 + gap + ht), new PointF(x2 + ht, y2 - gap - ht),
                               new PointF(x2, y2 - gap), new PointF(x2 - ht, y2 - gap - ht), new PointF(x1 - ht, y1 + gap + ht) };
            }
            PointF[] pts;
            float l = x + ht, r = x + w - ht, top = y + ht, mid = y + half, bot = y + h - ht;
            switch (seg)
            {
                case 'a': pts = P(l, top, r, top, true); break;
                case 'b': pts = P(r, top, r, mid, false); break;
                case 'c': pts = P(r, mid, r, bot, false); break;
                case 'd': pts = P(l, bot, r, bot, true); break;
                case 'e': pts = P(l, mid, l, bot, false); break;
                case 'f': pts = P(l, top, l, mid, false); break;
                default: pts = P(l, mid, r, mid, true); break;
            }
            // kursiv: oben etwas nach rechts
            for (int i = 0; i < pts.Length; i++) pts[i].X += slant * (1f - (pts[i].Y - y) / h);
            return pts;
        }
    
        // ---------------------------------------------------------------- Studio-Uhr

        /// <summary>Wann das Mischpult gestartet wurde (fuer die Laufzeit der Studio-Uhr).</summary>
        public static readonly DateTime AppStart = DateTime.Now;
        private static readonly System.Diagnostics.Stopwatch _runClock = System.Diagnostics.Stopwatch.StartNew();   // Laufzeit (unabhaengig von Uhr-Umstellungen)

        private static readonly Color ClockRed = Color.FromArgb(0xff, 0x2e, 0x1e), ClockGhost = Color.FromArgb(0x30, 0x0a, 0x06);

        /// <summary>Laufzeit seit dem Start als "1:23" (Stunden:Minuten).</summary>
        public static string RunTimeText()
        {
            var t = _runClock.Elapsed;
            return ((int)t.TotalHours) + ":" + t.Minutes.ToString("00");
        }

        /// <summary>
        /// Studio-Uhr wie in der Regie: Kranz aus 60 roten Sekunden-Punkten, in der Mitte HH:MM, darunter die Laufzeit.
        /// staticPart = nur Gehaeuse und unbeleuchtete Punkte (fuer den Bild-Cache), sonst das Leuchtende.
        /// </summary>
        public static void DrawStudioClock(Graphics g, RectangleF r, bool staticPart)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(r.Width, r.Height);
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            float R = size / 2f - F(6), dot = Math.Max(1.6f, size * 0.026f);
            var now = DateTime.Now;
            if (staticPart)
            {
                FillRound(g, Color.FromArgb(0x06, 0x05, 0x05), r, F(5));
                StrokeRound(g, Color.FromArgb(0x24, 0x22, 0x22), F(1), r, F(5));
                using (var b = new SolidBrush(ClockGhost))
                    for (int i = 0; i < 60; i++)
                    {
                        double a = (i * 6 - 90) * Math.PI / 180.0;
                        float d = i % 5 == 0 ? dot * 1.25f : dot;
                        g.FillEllipse(b, c.X + (float)Math.Cos(a) * R - d / 2f, c.Y + (float)Math.Sin(a) * R - d / 2f, d, d);
                    }
            }
            else
            {
                // Stundenmarken immer an (schwach), Sekunden bis jetzt hell
                using (var hour = new SolidBrush(Alpha(ClockRed, 110)))
                using (var on = new SolidBrush(ClockRed))
                using (var glow = new SolidBrush(Alpha(ClockRed, 50)))
                    for (int i = 0; i < 60; i++)
                    {
                        bool lit = i <= now.Second;
                        if (!lit && i % 5 != 0) continue;
                        double a = (i * 6 - 90) * Math.PI / 180.0;
                        float d = i % 5 == 0 ? dot * 1.25f : dot;
                        float x = c.X + (float)Math.Cos(a) * R, y = c.Y + (float)Math.Sin(a) * R;
                        if (lit) g.FillEllipse(glow, x - d, y - d, d * 2, d * 2);
                        g.FillEllipse(lit ? on : hour, x - d / 2f, y - d / 2f, d, d);
                    }
                // HH:MM gross, darunter Laufzeit klein
                string t = now.Hour.ToString("00") + ":" + now.Minute.ToString("00");
                float h = Math.Max(8f, size * 0.185f);
                float tw = Seg7Width(t, h);
                DrawSeg7(g, t, c.X - tw / 2f, c.Y - h * 0.78f, h, ClockRed, ClockGhost);
                string run = RunTimeText();
                float h2 = Math.Max(6f, size * 0.11f), rw = Seg7Width(run, h2);
                DrawSeg7(g, run, c.X - rw / 2f, c.Y + h * 0.4f, h2, Alpha(ClockRed, 210), Color.Empty, false);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Kleine Studio-Uhr fuer die Kopfleiste: HH:MM in roten Segmenten, rechts darunter die Laufzeit.</summary>
        public static void DrawStudioClockSmall(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, Color.FromArgb(0x06, 0x05, 0x05), r, F(3));
            StrokeRound(g, Color.FromArgb(0x02, 0x02, 0x02), F(1), r, F(3));
            var now = DateTime.Now;
            string t = now.Hour.ToString("00") + (now.Second % 2 == 0 ? ":" : " ") + now.Minute.ToString("00");
            float h = (float)Math.Round(r.Height * 0.58f), tw = Seg7Width("88:88", h);
            float x = r.X + F(6), y = r.Y + (r.Height - h) / 2f;
            DrawSeg7(g, t, x, y, h, ClockRed, ClockGhost);
            string run = RunTimeText();
            float h2 = (float)Math.Round(r.Height * 0.32f), rw = Seg7Width(run, h2);
            DrawSeg7(g, run, r.Right - F(5) - rw, r.Bottom - F(4) - h2, h2, Alpha(ClockRed, 170), Color.Empty, false);
            g.SmoothingMode = sm;
        }
}
}
