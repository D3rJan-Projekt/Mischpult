using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// Scharfe Schrift (ClearType, ganze Pixel), SIG/PEAK-Leuchten und analoges VU-Meter.
    /// </summary>
    internal static partial class Theme
    {
        // ---------------------------------------------------------------- Scharfe Schrift

        /// <summary>Schriftglaettung: ClearType, wenn Windows es nutzt – sonst Graustufen. Wird beim Start gesetzt.</summary>
        public static TextRenderingHint TextHint = TextRenderingHint.AntiAliasGridFit;

        /// <summary>Beim Start: ClearType nur, wenn es in Windows eingeschaltet ist (sonst farbige Raender).</summary>
        public static void DetectTextHint()
        {
            TextHint = TextRenderingHint.AntiAliasGridFit;
            if (!Native.Win32.IsWindows) return;
            try
            {
                if (System.Windows.Forms.SystemInformation.IsFontSmoothingEnabled &&
                    System.Windows.Forms.SystemInformation.FontSmoothingType == 2)
                    TextHint = TextRenderingHint.ClearTypeGridFit;
            }
            catch { }
        }

        // Nach Eigenschaften (nicht nach Objekt) zwischengespeichert: es gibt nur wenige Kombinationen,
        // auch wenn Aufrufer bei jedem Zeichnen ein neues StringFormat anlegen.
        private static readonly Dictionary<long, StringFormat> _nearLine = new Dictionary<long, StringFormat>();

        /// <summary>Gleiches Format, aber oben ausgerichtet (die senkrechte Mitte rechnen wir selbst auf ganze Pixel).</summary>
        private static StringFormat NearLine(StringFormat f)
        {
            long key = (long)f.Alignment | ((long)f.Trimming << 4) | ((long)f.HotkeyPrefix << 8) | ((long)(uint)f.FormatFlags << 16);
            if (_nearLine.TryGetValue(key, out var n)) return n;
            n = new StringFormat(f.FormatFlags)
            {
                Alignment = f.Alignment, LineAlignment = StringAlignment.Near,
                Trimming = f.Trimming, HotkeyPrefix = f.HotkeyPrefix,
            };
            _nearLine[key] = n;
            return n;
        }

        private static readonly StringFormat _plain = new StringFormat();

        /// <summary>
        /// Text ueber die Windows-eigene Textausgabe (GDI) zeichnen – genau so scharf wie in normalen Windows-Fenstern
        /// (ClearType mit voller Anpassung an die Pixel). GDI+ (DrawString) wirkt dagegen weicher.
        /// Nur unter Windows; MISCHPULT_GDI_TEXT=0/1 schaltet um (zum Testen).
        /// </summary>
        public static bool GdiText = DetectGdiText();

        /// <summary>
        /// &gt; 0, solange in ein GDI+-Bitmap gezeichnet wird: dort keine Windows-Schrift. GDI mischt ClearType-Kanten in einem
        /// solchen Bitmap gegen Schwarz statt gegen den echten Hintergrund (dunkle Schrift wird klobig, helle bekommt bunte Raender).
        /// Die Zug-Zwischenspeicher sind darum unter Windows echte Windows-Zeichenflaechen (PaintCache).
        /// </summary>
        public static int NoGdiText;

        private static bool UseGdi => GdiText && NoGdiText == 0;

        private static bool DetectGdiText()
        {
            string env = Environment.GetEnvironmentVariable("MISCHPULT_GDI_TEXT");
            if (env == "0") return false;
            if (env == "1") return true;
            return Native.Win32.IsWindows;
        }

        /// <summary>Nur verschoben (nicht gedreht/skaliert)? dx/dy = Verschiebung.</summary>
        private static bool OnlyTranslate(Graphics g, out float dx, out float dy)
        {
            using (var m = g.Transform)
            {
                var e = m.Elements;
                dx = e[4]; dy = e[5];
                return Math.Abs(e[0] - 1f) < 1e-5f && Math.Abs(e[1]) < 1e-5f && Math.Abs(e[2]) < 1e-5f && Math.Abs(e[3] - 1f) < 1e-5f;
            }
        }

        private static TextFormatFlags GdiFlags(StringFormat sf, string s, bool point)
        {
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            if (point) return fl | TextFormatFlags.SingleLine;
            switch (sf.Alignment)
            {
                case StringAlignment.Center: fl |= TextFormatFlags.HorizontalCenter; break;
                case StringAlignment.Far: fl |= TextFormatFlags.Right; break;
            }
            bool wrap = (sf.FormatFlags & StringFormatFlags.NoWrap) == 0;
            if (wrap) fl |= TextFormatFlags.WordBreak;
            else fl |= TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;   // kleine Felder: Unterlaengen nicht abschneiden
            if (!wrap || s.IndexOf('\n') < 0)
            {
                if (sf.LineAlignment == StringAlignment.Center) fl |= TextFormatFlags.VerticalCenter;
                else if (sf.LineAlignment == StringAlignment.Far) fl |= TextFormatFlags.Bottom;
            }
            if (sf.Trimming != StringTrimming.None) fl |= TextFormatFlags.EndEllipsis;
            return fl;
        }

        /// <summary>Kann dieser Text ueber GDI gezeichnet werden? (deckende Farbe, nicht gedreht)</summary>
        private static bool CanGdi(Graphics g, Brush b, out Color c)
        {
            c = Color.Empty;
            if (!UseGdi || !(b is SolidBrush sb) || sb.Color.A < 255) return false;
            c = sb.Color;
            return true;
        }

        /// <summary>
        /// Text zeichnen – scharf: unter Windows mit der Windows-Textausgabe (GDI), sonst ohne halben Pixelversatz
        /// auf ganze Pixel gerundet und senkrecht exakt mittig.
        /// </summary>
        public static void Str(Graphics g, string s, Font f, Brush b, RectangleF r, StringFormat fmt = null)
        {
            if (string.IsNullOrEmpty(s)) return;
            var sf = fmt ?? _plain;
            bool plain = OnlyTranslate(g, out float dx, out float dy);
            if (plain && CanGdi(g, b, out Color gc))
            {
                // auf ganze Bildschirmpixel
                int x0 = (int)Math.Round(r.X + dx), y0 = (int)Math.Round(r.Y + dy);
                int x1 = (int)Math.Round(r.Right + dx), y1 = (int)Math.Round(r.Bottom + dy);
                var rr = new Rectangle(x0 - (int)Math.Round(dx), y0 - (int)Math.Round(dy), Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
                try { TextRenderer.DrawText(g, s, f, rr, gc, GdiFlags(sf, s, false)); return; }
                catch { /* z. B. Zeichenflaeche ohne GDI-Zugriff: unten mit GDI+ */ }
            }
            var pom = g.PixelOffsetMode; var trh = g.TextRenderingHint;
            if (!plain)
            {
                // gedrehter Text (z. B. "KANAL HINZUFÜGEN"): Graustufen, keine Rundung
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(s, f, b, r, sf);
                g.TextRenderingHint = trh;
                return;
            }
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.TextRenderingHint = TextHint;
            // auf ganze Bildschirmpixel runden (auch wenn die Flaeche verschoben ist, z. B. beim Scrollen)
            float x = (float)Math.Round(r.X + dx) - dx, y = (float)Math.Round(r.Y + dy) - dy;
            float w = (float)Math.Round(r.Right + dx) - dx - x, h = (float)Math.Round(r.Bottom + dy) - dy - y;
            if (sf.LineAlignment == StringAlignment.Center && s.IndexOf('\n') < 0 && (sf.FormatFlags & StringFormatFlags.NoWrap) != 0)
            {
                float lh = f.GetHeight(g);
                float ty = (float)Math.Round(y + dy + (h - lh) / 2f) - dy;
                g.DrawString(s, f, b, new RectangleF(x, ty, w, (float)Math.Ceiling(lh) + 1f), NearLine(sf));
            }
            else g.DrawString(s, f, b, new RectangleF(x, y, w, h), sf);
            g.PixelOffsetMode = pom; g.TextRenderingHint = trh;
        }

        public static void Str(Graphics g, string s, Font f, Brush b, float x, float y, StringFormat fmt = null)
        {
            if (string.IsNullOrEmpty(s)) return;
            bool plain = OnlyTranslate(g, out float dx, out float dy);
            if (plain && CanGdi(g, b, out Color gc))
            {
                var pt = new Point((int)Math.Round(x + dx) - (int)Math.Round(dx), (int)Math.Round(y + dy) - (int)Math.Round(dy));
                try { TextRenderer.DrawText(g, s, f, pt, gc, GdiFlags(fmt ?? _plain, s, true)); return; }
                catch { }
            }
            var pom = g.PixelOffsetMode; var trh = g.TextRenderingHint;
            if (!plain)
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(s, f, b, x, y, fmt ?? _plain);
                g.TextRenderingHint = trh;
                return;
            }
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.TextRenderingHint = TextHint;
            g.DrawString(s, f, b, (float)Math.Round(x + dx) - dx, (float)Math.Round(y + dy) - dy, fmt ?? _plain);
            g.PixelOffsetMode = pom; g.TextRenderingHint = trh;
        }

        /// <summary>Breite eines Textes (einzeilig) – passend zur Art, wie er gezeichnet wird.</summary>
        public static float TextWidth(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (UseGdi && OnlyTranslate(g, out _, out _))   // gedrehter Text wird mit GDI+ gezeichnet – dann auch so messen
                try { return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width; }
                catch { }
            return g.MeasureString(s, f, 100000, StringFormat.GenericTypographic).Width;
        }

        /// <summary>Hoehe eines umbrochenen Textes bei gegebener Breite – passend zur Zeichenart.</summary>
        public static float TextHeight(Graphics g, string s, Font f, float width)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            int w = (int)Math.Max(20, Math.Floor(width));
            if (UseGdi && OnlyTranslate(g, out _, out _))   // gedrehter Text wird mit GDI+ gezeichnet – dann auch so messen
                try { return TextRenderer.MeasureText(g, s, f, new Size(w, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak).Height; }
                catch { }
            return (float)Math.Ceiling(g.MeasureString(s, f, w - 3).Height);
        }

        public static void Str(Graphics g, string s, Font f, Brush b, PointF p, StringFormat fmt = null) => Str(g, s, f, b, p.X, p.Y, fmt);

        // ---------------------------------------------------------------- SIG / PEAK

        /// <summary>
        /// Leuchtfeld mit Aufschrift (SIG gruen, PEAK rot): aus = dunkel getoent, an = hell mit Schein.
        /// Gross genug, dass man es auch aus dem Augenwinkel sieht.
        /// </summary>
        public static void DrawLens(Graphics g, RectangleF r, string text, Color c, bool on, float fontPx = 8.5f)
        {
            float rad = KeyRad(Style == StyleModern || Style == StyleLight ? F(4) : F(3), r);   // 3.1: Tasten-Form
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (on)
            {
                // Schein um das Feld
                for (int i = 3; i >= 1; i--)
                    FillRound(g, Alpha(c, 22 + (3 - i) * 14), RectangleF.Inflate(r, F(i * 1.2f), F(i * 1.2f)), rad + F(i * 1.2f));
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(c, 0.38f), c, LinearGradientMode.Vertical))
                    FillRound(g, b, r, rad);
                // Glanz oben
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    FillRound(g, b, new RectangleF(r.X + F(1.5f), r.Y + F(1), r.Width - F(3), r.Height * 0.38f), rad * 0.7f);
            }
            else
            {
                FillRound(g, Mix(Color.FromArgb(0x0a, 0x0b, 0x0d), c, 0.2f), r, rad);
                using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
                    FillRound(g, b, new RectangleF(r.X + F(1.5f), r.Y + F(1), r.Width - F(3), r.Height * 0.35f), rad * 0.7f);
            }
            StrokeRound(g, on ? Darken(c, 0.35f) : Color.FromArgb(0x03, 0x03, 0x04), F(1), r, rad);
            g.SmoothingMode = sm;
            Color tc = on ? Color.FromArgb(0x0c, 0x0d, 0x0f) : Mix(Color.FromArgb(0x0a, 0x0b, 0x0d), c, 0.62f);
            using (var b = new SolidBrush(tc))
                Str(g, text, Ui(fontPx, FontStyle.Bold), b, r, Center);
        }

        // ---------------------------------------------------------------- Analoges VU-Meter

        /// <summary>Farben je Stil: Skala hell (Mitte, vom Laempchen beleuchtet), Skala Rand, Striche/Schrift, Rot, Zeiger, Abdeckung.</summary>
        /// <summary>Farbe der VU-Skala: 0 = passend zum Design, 1 = Creme (beleuchtet), 2 = Schwarz, 3 = Weiss.</summary>
        public static int VuFace;

        /// <summary>
        /// Zahlen auf der VU-Skala: true = echte Pegel in dB passend zur Empfindlichkeit (z. B. −60 … −40),
        /// false = klassisch wie am Studio-VU-Meter (20 … 0 … +3).
        /// </summary>
        public static bool VuDbScale = true;

        /// <summary>dB-Skala: ab hier rot (laut).</summary>
        public const float VuRedDb = -6f;
        /// <summary>dB-Skala: Zeigerstellung am linken Skalenende (davor liegt noch ein Stueck bis zum Anschlag, wie beim echten VU).</summary>
        private const float VuDbP0 = 0.07f;

        /// <summary>dB-Skala: Zeigerstellung (0..1) fuer einen Pegel – gleichmaessig von "reagiert ab" (links) bis 0 dB (rechts).</summary>
        public static float VuDbPos(float db)
        {
            float s0 = StripBase.VuStartDb;
            return VuDbP0 + (1f - VuDbP0) * (db - s0) / -s0;
        }

        /// <summary>dB-Skala: Abstand der Zahlen – 10 dB, bei langer Skala (ueber 50 dB) 20 dB, sonst wird es zu eng.</summary>
        public static float VuDbStep(float startDb) => -startDb > 50f ? 20f : 10f;

        /// <summary>dB-Skala: Abstand der kleinen Striche (grosse Striche alle 10 dB).</summary>
        public static float VuDbMinor(float startDb) => -startDb > 40f ? 5f : 2f;

        /// <summary>dB-Skala: Zahlen von 0 abwaerts im Abstand VuDbStep, dazu ganz links der Startwert (ohne Gedraenge davor).</summary>
        public static List<float> VuDbLabels(float startDb)
        {
            float step = VuDbStep(startDb), minGap = -startDb * 0.2f;   // ca. 18 Grad Abstand zur Startzahl
            var l = new List<float>();
            for (float v = 0f; v - startDb >= minGap - 0.01f; v -= step) l.Add(v);
            l.Add((float)Math.Round(startDb));
            return l;
        }

        /// <summary>Pegel als kurze Zahl mit echtem Minuszeichen ("−60").</summary>
        private static string DbNum(float db) => ((int)Math.Round(db)).ToString(System.Globalization.CultureInfo.InvariantCulture).Replace("-", "−");
        public static readonly string[] VuFaceNames = { "Passend zum Design", "Creme (beleuchtet)", "Schwarz", "Weiß" };

        private static void VuColors(out Color lamp, out Color edge, out Color ink, out Color red, out Color needle, out Color hood)
        {
            VuColorsBase(out lamp, out edge, out ink, out red, out needle, out hood);
            ApplyVuLight(ref lamp, ref edge, ref ink);   // gewaehlte Lichtfarbe hinter der Skala
            if (VuKind == 1)
            {
                // 3.3: BBC-Spitzenpegel – schwarzes Instrument, weisse Zahlen, weisser Zeiger
                lamp = Color.FromArgb(0x26, 0x26, 0x28); edge = Color.FromArgb(0x07, 0x07, 0x08);
                ink = Color.FromArgb(0xf2, 0xf2, 0xf0); red = Color.FromArgb(0xff, 0x5a, 0x4a); needle = Color.FromArgb(0xf6, 0xf6, 0xf2); hood = Color.Black;
            }
            else if (VuKind == 2)
            {
                // 3.3: DIN-Spitzenpegel – hellgraue Skala, schwarze Schrift, rot ab 0
                lamp = Color.FromArgb(0xf3, 0xf1, 0xea); edge = Color.FromArgb(0xc6, 0xc3, 0xba);
                ink = Color.FromArgb(0x14, 0x14, 0x14); red = Color.FromArgb(0xd4, 0x2a, 0x20); needle = Color.FromArgb(0x10, 0x10, 0x10); hood = Color.FromArgb(0x2a, 0x2a, 0x2a);
            }
        }

        private static void VuColorsBase(out Color lamp, out Color edge, out Color ink, out Color red, out Color needle, out Color hood)
        {
            switch (VuFace)
            {
                case 1:   // Creme, warm hinterleuchtet (wie bei alten Studio-Geraeten)
                    lamp = Color.FromArgb(0xff, 0xf2, 0xc4); edge = Color.FromArgb(0xc9, 0xa2, 0x55);
                    ink = Color.FromArgb(0x1e, 0x18, 0x0d); red = Color.FromArgb(0xc4, 0x1e, 0x16); needle = Color.FromArgb(0x12, 0x0f, 0x0a); hood = Color.FromArgb(0x14, 0x13, 0x12);
                    return;
                case 2:   // Schwarz: dunkle Skala, helle Schrift, heller Zeiger
                    lamp = Color.FromArgb(0x2c, 0x29, 0x24); edge = Color.FromArgb(0x08, 0x08, 0x09);
                    ink = Color.FromArgb(0xee, 0xea, 0xe0); red = Color.FromArgb(0xff, 0x4d, 0x3d); needle = Color.FromArgb(0xf6, 0xf2, 0xe8); hood = Color.FromArgb(0x02, 0x02, 0x03);
                    return;
                case 3:   // Weiss
                    lamp = Color.White; edge = Color.FromArgb(0xdc, 0xdf, 0xe4);
                    ink = Color.FromArgb(0x1d, 0x20, 0x26); red = Color.FromArgb(0xd8, 0x2b, 0x22); needle = Color.FromArgb(0x14, 0x16, 0x1a); hood = Color.FromArgb(0x2a, 0x2d, 0x33);
                    return;
            }
            switch (Style)
            {
                case StyleModern:
                    lamp = Color.FromArgb(0x2a, 0x2f, 0x38); edge = Color.FromArgb(0x13, 0x15, 0x19);
                    ink = Color.FromArgb(0xd0, 0xd5, 0xdc); red = Color.FromArgb(0xff, 0x5a, 0x4f); needle = Accent; hood = Color.FromArgb(0x0b, 0x0c, 0x0e); break;
                case StyleNeon:
                    lamp = Color.FromArgb(0x10, 0x16, 0x22); edge = Color.Black;
                    ink = Lighten(Accent, 0.25f); red = Color.FromArgb(0xff, 0x3b, 0x6b); needle = Color.White; hood = Color.Black; break;
                case StyleLight when Vintage || Hifi:   // Vintage, Retro-Hi-Fi: warm beleuchtete Creme-Skala
                    lamp = Color.FromArgb(0xff, 0xf2, 0xc4); edge = Color.FromArgb(0xc9, 0xa2, 0x55);
                    ink = Color.FromArgb(0x1e, 0x18, 0x0d); red = Color.FromArgb(0xc4, 0x1e, 0x16); needle = Color.FromArgb(0x12, 0x0f, 0x0a); hood = Color.FromArgb(0x14, 0x13, 0x12); break;
                case StyleLight:
                    lamp = Color.White; edge = Color.FromArgb(0xe2, 0xe4, 0xe8);
                    ink = Color.FromArgb(0x1d, 0x20, 0x26); red = Color.FromArgb(0xd8, 0x2b, 0x22); needle = Color.FromArgb(0x14, 0x16, 0x1a); hood = Color.FromArgb(0x2a, 0x2d, 0x33); break;
                default: // Studio, Klassisch: warm beleuchtete, leicht vergilbte Skala wie bei alten Geraeten
                    lamp = Color.FromArgb(0xff, 0xf2, 0xc4); edge = Color.FromArgb(0xc9, 0xa2, 0x55);
                    ink = Color.FromArgb(0x1e, 0x18, 0x0d); red = Color.FromArgb(0xc4, 0x1e, 0x16); needle = Color.FromArgb(0x12, 0x0f, 0x0a); hood = Color.FromArgb(0x14, 0x13, 0x12); break;
            }
        }

        /// <summary>VU-Skala: Zeigerwert (0..1, linear in der Spannung) fuer einen VU-Wert.</summary>
        public static float VuPos(float vu) => (float)Math.Pow(10, (vu - 3f) / 20f);

        private const float VuSweep = 48f;

        /// <summary>Winkel (Grad, 0 = senkrecht) fuer eine Zeigerstellung 0..1.</summary>
        private static float VuAngle(float p) => -VuSweep + 2f * VuSweep * p;

        /// <summary>Drehpunkt (unter der Abdeckung, knapp unter dem Fenster) und Radius des Skalenbogens.</summary>
        private static void VuGeometry(RectangleF r, out PointF pivot, out float radius)
        {
            float sin = (float)Math.Sin(VuSweep * Math.PI / 180.0);
            radius = Math.Max(F(10), Math.Min(r.Height * 1.05f, (r.Width / 2f - F(12)) / sin));
            float arcTop = r.Y + F(15) + Math.Max(0f, (r.Height - F(56)) * 0.35f);
            pivot = new PointF(r.X + r.Width / 2f, arcTop + radius);
        }

        private static PointF Polar(PointF c, float rad, float deg)
        {
            double a = deg * Math.PI / 180.0;
            return new PointF(c.X + (float)(Math.Sin(a) * rad), c.Y - (float)(Math.Cos(a) * rad));
        }

        private static float VuRad => Style == StyleModern || Style == StyleLight ? F(4) : F(2);

        /// <summary>Schwarze Abdeckung unten in der Mitte, unter der der Zeiger gelagert ist.</summary>
        private static RectangleF VuHood(RectangleF r)
        {
            float w = r.Width * 0.46f, h = Math.Max(F(7), r.Height * 0.14f);
            return new RectangleF(r.X + (r.Width - w) / 2f, r.Bottom - h, w, h);
        }

        /// <summary>Gehaeuse, beleuchtete Skala und Beschriftung (ohne Zeiger) – gehoert in den Bild-Cache.</summary>
        public static void DrawVuFace(Graphics g, RectangleF r, bool dim)
        {
            if (VuKind == 3) { DrawEyeFace(g, r, dim); return; }   // 3.3: Magisches Auge
            VuColors(out var lamp, out var edge, out var ink, out var red, out _, out _);
            if (dim) { lamp = Darken(lamp, 0.5f); edge = Darken(edge, 0.5f); ink = Darken(ink, 0.15f); red = Darken(red, 0.4f); }
            float rad = VuRad;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Gehaeuse: schwarzer Rahmen mit leichter Kante (oben dunkel, unten Lichtkante)
            var frame = RectangleF.Inflate(r, F(2.5f), F(2.5f));
            Color fr1 = Style == StyleLight ? Darken(PanelBottom, 0.45f) : Color.FromArgb(0x02, 0x02, 0x03);
            Color fr2 = Style == StyleLight ? Darken(PanelBottom, 0.25f) : Color.FromArgb(0x2a, 0x2c, 0x30);
            using (var b = new LinearGradientBrush(new RectangleF(frame.X, frame.Y - 1, frame.Width, frame.Height + 2), fr1, fr2, LinearGradientMode.Vertical))
                FillRound(g, b, frame, rad + F(2.5f));

            // Skala: von hinten unten durch ein Laempchen beleuchtet – Mitte hell, Ecken dunkler
            FillRound(g, edge, r, rad);
            var state = g.Save();
            using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
            using (var glowPath = new GraphicsPath())
            using (var facePath = Round(r, rad))
            {
                glowPath.AddEllipse(r.X - r.Width * 0.25f, r.Y - r.Height * 0.55f, r.Width * 1.5f, r.Height * 2.3f);
                using (var pb = new PathGradientBrush(glowPath)
                {
                    CenterPoint = new PointF(r.X + r.Width / 2f, r.Bottom - r.Height * 0.15f),
                    CenterColor = lamp,
                    SurroundColors = new[] { edge },
                })
                {
                    pb.FocusScales = new PointF(0.35f, 0.3f);
                    g.FillPath(pb, facePath);   // geglaettete Ecken (Clipping waere treppig)
                }
            }

            VuGeometry(r, out var pv, out float R);
            bool dbs = VuDbScale || VuKind == 2;   // 3.3: DIN zeigt immer dB
            bool bbc = VuKind == 1;                // 3.3: BBC – Marken 1 bis 7, kein rotes Band
            float startDb = StripBase.VuStartDb;
            // dB-Skala: von "reagiert ab" bis 0 dB, rot ab -6 dB. Klassisch: -20..+3 VU, rot ab 0 VU.
            float a0 = VuAngle(dbs ? VuDbPos(startDb) : VuPos(-20)), aZero = VuAngle(dbs ? VuDbPos(VuRedDb) : VuPos(0)), a3 = VuAngle(1f);
            float thin = Math.Max(1f, F(0.85f));

            // Prozent-Skala innen (fein, ohne Zahlen)
            float Rp = R - F(5);
            if (VuKind == 0)
            using (var pen = new Pen(Alpha(ink, 120), Math.Max(0.8f, F(0.6f))))
            {
                g.DrawArc(pen, pv.X - Rp, pv.Y - Rp, Rp * 2, Rp * 2, -90 + VuAngle(0f), VuAngle(1f) - VuAngle(0f));
                for (int i = 0; i <= 10; i++)
                    g.DrawLine(pen, Polar(pv, Rp, VuAngle(i / 10f)), Polar(pv, Rp - F(i % 5 == 0 ? 3f : 1.8f), VuAngle(i / 10f)));
            }

            // Hauptbogen -20..0 (schwarz) und breites rotes Band 0..+3
            if (bbc)
            {
                // 3.3 BBC: ein durchgehender Bogen ohne rotes Band
                using (var pen = new Pen(ink, thin))
                    g.DrawArc(pen, pv.X - R, pv.Y - R, R * 2, R * 2, -90 + VuAngle(0f), VuAngle(1f) - VuAngle(0f));
            }
            else
            {
                using (var pen = new Pen(ink, thin))
                    g.DrawArc(pen, pv.X - R, pv.Y - R, R * 2, R * 2, -90 + a0, aZero - a0);
                float Rr = R + F(1.6f);
                using (var pen = new Pen(red, F(3.2f)))
                    g.DrawArc(pen, pv.X - Rr, pv.Y - Rr, Rr * 2, Rr * 2, -90 + aZero, a3 - aZero);
                using (var pen = new Pen(red, thin))
                    g.DrawArc(pen, pv.X - R, pv.Y - R, R * 2, R * 2, -90 + aZero, a3 - aZero);
            }

            // Striche: grosse mit Zahl, kleine dazwischen
            var tickPos = new List<float>(); var tickBig = new List<bool>(); var tickRed = new List<bool>();
            if (bbc)
            {
                for (int m = 0; m <= 12; m++) { tickPos.Add(m / 12f); tickBig.Add(m % 2 == 0); tickRed.Add(false); }   // 1 bis 7, dazwischen halbe Marken
            }
            else if (dbs)
            {
                float minor = VuDbMinor(startDb);
                for (float v = 0f; v >= startDb - 0.01f; v -= minor)
                {
                    tickPos.Add(VuDbPos(v));
                    tickBig.Add(Math.Abs(v / 10f - (float)Math.Round(v / 10f)) < 0.01f);   // grosse Striche alle 10 dB
                    tickRed.Add(v > VuRedDb + 0.01f);
                }
                tickPos.Add(VuDbPos(startDb)); tickBig.Add(true); tickRed.Add(false);
            }
            else
            {
                foreach (float t in new float[] { -20, -15, -10, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3 })
                {
                    tickPos.Add(VuPos(t));
                    tickBig.Add(t == -20 || t == -10 || t == -7 || t == -5 || t == -3 || t == 0 || t == 3);
                    tickRed.Add(t > 0);
                }
            }
            for (int i = 0; i < tickPos.Count; i++)
            {
                float a = VuAngle(tickPos[i]);
                bool big = tickBig[i];
                var p1 = Polar(pv, R, a);
                var p2 = Polar(pv, R + F(big ? 6f : 3.8f), a);
                using (var pen = new Pen(tickRed[i] ? red : ink, Math.Max(1f, F(big ? 1.15f : 0.8f))))
                    g.DrawLine(pen, p1, p2);
            }

            // Zahlen ueber dem Bogen
            var font = Ui(7.5f, FontStyle.Bold);
            float Rl = R + F(11.5f);
            if (bbc)
            {
                var bf = Ui(8.5f, FontStyle.Bold);
                for (int m = 1; m <= 7; m++)
                {
                    var p = Polar(pv, Rl, VuAngle((m - 1) / 6f));
                    using (var b = new SolidBrush(ink)) Str(g, m.ToString(), bf, b, new RectangleF(p.X - F(8), p.Y - F(6), F(16), F(12)), Center);
                }
            }
            else if (dbs)
            {
                // echte Pegel: links "reagiert ab", rechts 0 dB, dazwischen alle 5 bzw. 10 dB
                foreach (float v in VuDbLabels(startDb))
                {
                    var p = Polar(pv, Rl, VuAngle(VuDbPos(v)));
                    using (var b = new SolidBrush(v > VuRedDb + 0.01f ? red : ink))
                        Str(g, DbNum(v), font, b, new RectangleF(p.X - F(13), p.Y - F(6), F(26), F(12)), Center);
                }
            }
            else
            {
                foreach (var kv in new[] { new KeyValuePair<float, string>(-20, "20"), new KeyValuePair<float, string>(-10, "10"),
                                           new KeyValuePair<float, string>(-7, "7"), new KeyValuePair<float, string>(-5, "5"),
                                           new KeyValuePair<float, string>(-3, "3"), new KeyValuePair<float, string>(0, "0"),
                                           new KeyValuePair<float, string>(3, "3") })
                {
                    var p = Polar(pv, Rl, VuAngle(VuPos(kv.Key)));
                    using (var b = new SolidBrush(kv.Key > 0 ? red : ink))
                        Str(g, kv.Value, font, b, new RectangleF(p.X - F(8), p.Y - F(6), F(16), F(12)), Center);
                }
                // Vorzeichen in den oberen Ecken, wie auf echten Skalen
                var signFont = Ui(10f, FontStyle.Bold);
                using (var b = new SolidBrush(ink)) Str(g, "–", signFont, b, new RectangleF(r.X + F(3), r.Y + F(1), F(12), F(12)), Center);
                using (var b = new SolidBrush(red)) Str(g, "+", signFont, b, new RectangleF(r.Right - F(15), r.Y + F(1), F(12), F(12)), Center);
            }

            // "VU" (bzw. "dB") ueber der Abdeckung
            var hood = VuHood(r);
            float vuSize = Math.Min(13f, Math.Max(9f, r.Height / S * 0.21f));
            using (var b = new SolidBrush(ink))
                Str(g, VuKind == 1 ? "PPM" : VuKind == 2 ? "dB" : VuDbScale ? "dB" : "VU", Ui(vuSize, FontStyle.Bold), b, new RectangleF(r.X, hood.Y - F(vuSize + 4), r.Width, F(vuSize + 3)), Center);

            g.Restore(state);
            g.SmoothingMode = sm;
        }

        /// <summary>PEAK-Leuchte im VU-Meter: unten rechts im Instrument.</summary>
        public static RectangleF VuLedRect(RectangleF r)
        {
            float d = Math.Max(4f, F(5.5f));
            return new RectangleF(r.Right - F(7.5f) - d / 2f, r.Bottom - F(7.5f) - d / 2f, d, d);
        }

        /// <summary>
        /// Zeiger (live), danach Abdeckung und Glas darueber. p = 0..~1.06 (Anschlag).
        /// peakP &gt;= 0: Schleppzeiger (duenn, rot) an dieser Stelle. led: 0 = keine PEAK-Leuchte, 1 = aus, 2 = an.
        /// </summary>
        public static void DrawVuNeedle(Graphics g, RectangleF r, float p, bool dim, float peakP = -1f, int led = 0)
        {
            DrawVuPointer(g, r, p, dim, peakP, led);
            DrawVuCover(g, r);
        }

        /// <summary>3.0: nur Zeiger, Schleppzeiger und PEAK-Leuchte (ohne Abdeckung und Glas).</summary>
        public static void DrawVuPointer(Graphics g, RectangleF r, float p, bool dim, float peakP = -1f, int led = 0)
        {
            if (VuKind == 3) { DrawEye(g, r, p, dim, led); return; }   // 3.3: Magisches Auge
            VuColors(out _, out _, out _, out var red, out var needle, out var hoodCol);
            if (dim) needle = Darken(needle, 0.3f);
            float rad = VuRad;
            p = Math.Max(-0.015f, Math.Min(1.06f, p));
            VuGeometry(r, out var pv, out float R);
            float a = VuAngle(p);
            var state = g.Save();
            using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // PEAK-Leuchte (unten rechts): aus = dunkles Glas, an = leuchtend rot mit Lichthof
            if (led > 0)
            {
                var lr = VuLedRect(r);
                if (led == 2 && !dim)
                {
                    for (int i = 3; i >= 1; i--)
                        using (var b = new SolidBrush(Color.FromArgb(34, red))) g.FillEllipse(b, RectangleF.Inflate(lr, F(i * 1.2f), F(i * 1.2f)));
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(lr);
                        using (var pb = new PathGradientBrush(path) { CenterColor = Lighten(red, 0.55f), SurroundColors = new[] { red } })
                            g.FillPath(pb, path);
                    }
                }
                else using (var b = new SolidBrush(Mix(Color.FromArgb(0x24, 0x10, 0x10), red, 0.22f))) g.FillEllipse(b, lr);
                using (var pen = new Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1f, F(0.7f)))) g.DrawEllipse(pen, lr);
                using (var b = new SolidBrush(Color.FromArgb(led == 2 ? 170 : 70, 255, 255, 255)))
                    g.FillEllipse(b, lr.X + lr.Width * 0.25f, lr.Y + lr.Height * 0.15f, lr.Width * 0.35f, lr.Height * 0.3f);
            }

            // Zeiger: schmal zulaufend, mit weichem Schatten auf der Skala
            float tipR = R + F(7.5f), baseR = R * 0.25f;

            // Schleppzeiger: duenn und rot, unter dem Zeiger – zeigt kurz, wie weit es zuletzt hochging
            if (peakP >= 0f && peakP > p + 0.006f)
            {
                float ap = VuAngle(Math.Min(1.06f, peakP));
                using (var pen = new Pen(Alpha(dim ? Darken(red, 0.4f) : red, 215), Math.Max(1f, F(0.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, Polar(pv, R * 0.5f, ap), Polar(pv, tipR - F(0.5f), ap));
            }
            double ar = a * Math.PI / 180.0;
            float nx = (float)Math.Cos(ar), ny = (float)Math.Sin(ar);   // quer zum Zeiger
            Func<float, float, PointF[]> needleShape = (dx, dy) =>
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
            if (Style != StyleNeon || VuFace != 0)
                using (var b = new SolidBrush(darkFace ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(45, 0, 0, 0)))
                    g.FillPolygon(b, needleShape(F(1.6f), F(1.2f)));
            else
                using (var pen = new Pen(Alpha(Accent, 100), F(3.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, Polar(pv, baseR, a), Polar(pv, tipR, a));
            using (var b = new SolidBrush(needle)) g.FillPolygon(b, needleShape(0, 0));
            g.Restore(state);
        }

        /// <summary>3.0: Abdeckung ueber dem Drehpunkt und Glas – kommt ueber den Zeiger (aendert sich nicht).</summary>
        public static void DrawVuCover(Graphics g, RectangleF r)
        {
            if (VuKind == 3) { DrawEyeGlass(g, r); return; }   // 3.3: Magisches Auge
            VuColors(out _, out _, out _, out _, out _, out var hoodCol);
            float rad = VuRad;
            var state = g.Save();
            using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Abdeckung ueber dem Drehpunkt (dunkle Kappe mit leichter Lichtkante)
            var hood = VuHood(r);
            using (var hp = new GraphicsPath())
            {
                // flache Kuppel: oberer Teil einer Ellipse
                var ell = new RectangleF(hood.X, hood.Y, hood.Width, hood.Height * 2f);
                hp.AddArc(ell, 180, 180);
                hp.AddLine(hood.Right, hood.Bottom + F(2), hood.X, hood.Bottom + F(2));
                hp.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(hood.X, hood.Y - 1, hood.Width, hood.Height + 2), Lighten(hoodCol, 0.16f), hoodCol, LinearGradientMode.Vertical))
                    g.FillPath(b, hp);
                using (var pen = new Pen(Style == StyleNeon ? Alpha(Accent, 140) : Color.FromArgb(70, 255, 255, 255), Math.Max(1f, F(0.7f))))
                    g.DrawArc(pen, RectangleF.Inflate(ell, -F(0.7f), -F(0.7f)), 200, 140);
            }

            // Glas: weicher Lichtschein oben links (ohne harte Kante), Schatten unter dem Rahmen oben
            g.Restore(state);
            // Glas: weicher Lichtschein oben links, Schatten unter dem Rahmen oben – als Flaechen im runden Fenster (glatte Ecken)
            using (var face = Round(r, rad))
            {
                using (var glare = new GraphicsPath())
                {
                    glare.AddEllipse(r.X - r.Width * 0.35f, r.Y - r.Height * 0.95f, r.Width * 1.0f, r.Height * 1.6f);
                    using (var pb = new PathGradientBrush(glare)
                    {
                        CenterColor = Color.FromArgb(Style == StyleLight ? 80 : 46, 255, 255, 255),
                        SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
                    })
                    {
                        // der Verlauf malt nur innerhalb seiner Ellipse; die Fensterkante bleibt die geglaettete Kante des Pfads
                        var sm2 = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillPath(pb, face);
                        g.SmoothingMode = sm2;
                    }
                }
                float sh = Math.Min(1f, F(6) / Math.Max(1f, r.Height));
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(Style == StyleLight ? 50 : 110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0) },
                        Positions = new[] { 0f, sh, 1f },
                    };
                    var sm2 = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(b, face);
                    g.SmoothingMode = sm2;
                }
            }
            // innerer Rand (Glas sitzt vertieft im Rahmen)
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            StrokeRound(g, Color.FromArgb(110, 0, 0, 0), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
            g.SmoothingMode = sm;
        }
    }
}
