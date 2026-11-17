using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.15 Optik: Material der Zuege, Seitenteile, Knoepfe und Faderkappen waehlbar, digitale Namensschilder,
    /// LED-Form und -Groesse, Nachgluehen, Lichtschein, VU-Lichtfarbe, eigenes Hintergrundbild.
    /// Alles einzeln einstellbar; "Passend" bzw. 0 = wie das Design es vorsieht.
    /// </summary>
    internal static partial class Theme
    {
        // ================================================================ Einstellungen

        public static readonly string[] MaterialNames = { "Passend", "Carbon", "Alu gebürstet", "Holz", "Leder" };
        public static readonly string[] CheekNames = { "Passend", "Keine", "Holz", "Carbon", "Alu" };
        public static readonly string[] KnobStyleNames = { "Passend", "Rändel-Metall", "Chrom", "Gummi", "Bakelit" };
        public static readonly string[] CapStyleNames = { "Passend", "Chrom", "Schwarz", "Weiß", "Holz", "Glas (innen Kanalfarbe)" };
        public static readonly string[] PlateStyleNames = { "Farbig", "Digital (LCD)", "Klebeband", "Prägeband" };
        public static readonly string[] LedShapeNames = { "Eckig", "Rund", "Striche" };
        public static readonly string[] LedSizeNames = { "Fein", "Normal", "Grob" };
        public static readonly string[] VuLightNames = { "Warm", "Blau", "Grün", "Weiß" };

        /// <summary>Material der Kanalzuege (0 = wie das Design).</summary>
        public static int PanelMaterial;
        /// <summary>Seitenteile: 0 passend (Vintage: Holz), 1 keine, 2 Holz, 3 Carbon, 4 Alu.</summary>
        public static int CheekStyle;
        public static int KnobStyle;
        public static int CapStyle;
        /// <summary>Namensschilder als beleuchtete LCD-Schilder.</summary>
        public static bool DigitalPlates;
        /// <summary>Form der Pegel-LEDs: 0 eckig, 1 rund, 2 Striche.</summary>
        public static int LedShape;
        /// <summary>Groesse der Pegel-LEDs: 0 fein, 1 normal, 2 grob.</summary>
        public static int LedSize = 1;
        /// <summary>Ausgehende LEDs gluehen kurz nach.</summary>
        public static bool LedAfterglow = true;
        /// <summary>Leuchtende LEDs werfen einen weichen Schein.</summary>
        public static bool LedHalo = true;
        /// <summary>Licht hinter der VU-Skala: 0 warm, 1 blau, 2 gruen, 3 weiss.</summary>
        public static int VuLight;

        /// <summary>Seitenteil, das gerade gezeigt wird (0 = keins, 2 Holz, 3 Carbon, 4 Alu).</summary>
        public static int EffCheek => CheekStyle == 0 ? (Vintage || Hifi || Brass ? 2 : Carbon ? 3 : 0) : CheekStyle == 1 ? 0 : CheekStyle;

        // ================================================================ Material der Zuege

        private static Bitmap _carbonTile, _woodTile, _leatherTile;
        private static TextureBrush _carbonBrush, _woodBrush, _leatherBrush;
        private static float _texScale;
        private static bool _texLight;

        private static void EnsureTextures()
        {
            if (_texScale == S && _texLight == IsLight && _carbonBrush != null) return;
            _texScale = S; _texLight = IsLight;
            foreach (var d in new IDisposable[] { _carbonBrush, _woodBrush, _leatherBrush, _carbonTile, _woodTile, _leatherTile }) d?.Dispose();
            _carbonTile = MakeCarbon(Math.Max(4, (int)Math.Round(F(5))), IsLight);
            _woodTile = MakeWood(Math.Max(64, (int)Math.Round(F(150))), Math.Max(128, (int)Math.Round(F(420))));
            _leatherTile = MakeLeather(Math.Max(64, (int)Math.Round(F(110))), IsLight);
            _carbonBrush = new TextureBrush(_carbonTile, WrapMode.Tile);
            _woodBrush = new TextureBrush(_woodTile, WrapMode.Tile);
            _leatherBrush = new TextureBrush(_leatherTile, WrapMode.Tile);
        }

        private static void SetPixels(Bitmap bmp, int[] px)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(px, y * bmp.Width, data.Scan0 + y * data.Stride, bmp.Width);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static int Argb(int a, int r, int g, int b) => (Math.Max(0, Math.Min(255, a)) << 24) | (Math.Max(0, Math.Min(255, r)) << 16) | (Math.Max(0, Math.Min(255, g)) << 8) | Math.Max(0, Math.Min(255, b));

        /// <summary>Carbon: 2x2-Koeper-Gewebe – Licht und Schatten wechseln je Faden (als durchscheinende Schicht).</summary>
        private static Bitmap MakeCarbon(int cell, bool light)
        {
            int n = cell * 4;
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            var px = new int[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cx = x / cell, cy = y / cell;
                    bool horiz = ((cx + cy) / 2) % 2 == 0;   // Koeper: versetzte Doppelfaeden
                    double u = horiz ? (y % cell) / (double)cell : (x % cell) / (double)cell;
                    double v = Math.Sin(u * Math.PI);          // Faden gewoelbt: Mitte hell
                    double shade = horiz ? 0.25 : -0.15;
                    double l = v * 0.9 + shade - 0.45;
                    float k = light ? 0.6f : 1f;   // helle Designs: zarteres Gewebe
                    px[y * n + x] = l > 0 ? Argb((int)(l * 34 * k), 255, 255, 255) : Argb((int)(-l * 70 * k), 0, 0, 0);
                }
            SetPixels(bmp, px);
            return bmp;
        }

        // einfacher Wert-Rauschen (glatt), fuer Holz und Leder
        private static double Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (double)0x7fffffff;
            }
        }

        private static double Noise(double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = Hash(x0, y0), b = Hash(x0 + 1, y0), c = Hash(x0, y0 + 1), d = Hash(x0 + 1, y0 + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        /// <summary>Holz-Furnier: senkrechte Maserung mit Jahresringen, braun lasiert (durchscheinend).</summary>
        private static Bitmap MakeWood(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            double sx = 150.0 / w, sy = 420.0 / h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double X = x * sx, Y = y * sy;
                    // kachelbar: Rauschen an den Raendern ineinander blenden
                    double warp = Noise(X * 0.03, Y * 0.006) * 9 + Noise(X * 0.1, Y * 0.02) * 2.5;
                    double ring = Math.Sin((X + warp) * 0.42 + Noise(X * 0.02, Y * 0.004) * 6);
                    double fine = Noise(X * 0.9, Y * 0.04) - 0.5;
                    double dark = 0.5 + 0.5 * ring;
                    int a = (int)(95 + dark * 55 + fine * 20);
                    int r = (int)(0x6a - dark * 26 + fine * 10), g = (int)(0x40 - dark * 18 + fine * 6), b = (int)(0x22 - dark * 10 + fine * 4);
                    px[y * w + x] = Argb(a, r, g, b);
                }
            SetPixels(bmp, px);
            return bmp;
        }

        /// <summary>Leder: genarbte Oberflaeche, dunkel getoent (durchscheinend).</summary>
        private static Bitmap MakeLeather(int n, bool light)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            var px = new int[n * n];
            double s = 110.0 / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double X = x * s, Y = y * s;
                    double pebble = Noise(X * 0.45, Y * 0.45);           // Narben
                    double crease = Math.Abs(Noise(X * 0.12, Y * 0.12) - 0.5);   // feine Falten
                    double grain = Hash(x, y) - 0.5;
                    double l = (pebble - 0.5) * 0.9 - (crease < 0.04 ? 0.35 : 0) + grain * 0.25;
                    if (light)
                    {
                        // helles, sandfarbenes Leder
                        int la = (int)(40 + Math.Abs(l) * 45);
                        px[y * n + x] = l > 0 ? Argb(la, 0xff, 0xf2, 0xe0) : Argb(la, 0x9a, 0x76, 0x52);
                        continue;
                    }
                    int a = (int)(70 + Math.Abs(l) * 60);
                    int c = l > 0 ? 0x5a : 0x0c;
                    px[y * n + x] = Argb(a, c + 0x10, (int)(c * 0.75), (int)(c * 0.55));
                }
            SetPixels(bmp, px);
            return bmp;
        }

        /// <summary>Material ueber die Flaeche des Zugs legen (nach der Grundfarbe, vor den Bedienelementen).</summary>
        public static void DrawPanelMaterial(Graphics g, RectangleF r, float rad)
        {
            if (EffMaterial == 0) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inner = RectangleF.Inflate(r, -F(1), -F(1));
            using (var path = Round(inner, Math.Max(1f, rad - F(1))))
            {
                switch (EffMaterial)
                {
                    case 1: // Carbon
                    {
                        EnsureTextures();
                        _carbonBrush.ResetTransform();
                        _carbonBrush.TranslateTransform((float)Math.Round(r.X), (float)Math.Round(r.Y));
                        g.FillPath(_carbonBrush, path);
                        // Klarlack: Glanz von oben
                        using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                        {
                            b.Blend = new Blend { Factors = new[] { 0f, 1f, 0.6f, 1f }, Positions = new[] { 0f, 0.08f, 0.4f, 1f } };
                            g.FillPath(b, path);
                        }
                        break;
                    }
                    case 2: // Alu gebuerstet
                        FillBrushed(g, path, r);
                        using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.Black, Color.Black, LinearGradientMode.Horizontal))
                        {
                            b.InterpolationColors = new ColorBlend
                            {
                                Colors = new[] { Color.FromArgb(10, 255, 255, 255), Color.FromArgb(30, 255, 255, 255), Color.FromArgb(4, 255, 255, 255), Color.FromArgb(22, 0, 0, 0) },
                                Positions = new[] { 0f, 0.3f, 0.65f, 1f },
                            };
                            g.FillPath(b, path);
                        }
                        break;
                    case 3: // Holz
                        EnsureTextures();
                        _woodBrush.ResetTransform();
                        _woodBrush.TranslateTransform((float)Math.Round(r.X), (float)Math.Round(r.Y));
                        g.FillPath(_woodBrush, path);
                        using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(26, 255, 230, 190), Color.FromArgb(30, 0, 0, 0), LinearGradientMode.Vertical))
                            g.FillPath(b, path);
                        break;
                    case 4: // Leder mit Naht
                        EnsureTextures();
                        _leatherBrush.ResetTransform();
                        _leatherBrush.TranslateTransform((float)Math.Round(r.X), (float)Math.Round(r.Y));
                        g.FillPath(_leatherBrush, path);
                        var seam = RectangleF.Inflate(r, -F(4), -F(4));
                        using (var pen = new Pen(Color.FromArgb(IsLight ? 150 : 110, IsLight ? Color.FromArgb(0x5a, 0x40, 0x2a) : Color.FromArgb(0xc8, 0xb4, 0x96)), Math.Max(1f, F(1)))
                               { DashStyle = DashStyle.Custom, DashPattern = new[] { 3f, 2.2f } })
                        using (var sp = Round(seam, Math.Max(1f, rad - F(3))))
                            g.DrawPath(pen, sp);
                        break;
                }
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Seitenteile

        /// <summary>Seitenteil links/rechts im gewaehlten Material.</summary>
        public static void DrawCheek(Graphics g, Rectangle r, bool left)
        {
            int st = EffCheek;
            if (st == 2 || st == 0) { DrawWoodCheek(g, r, left); return; }
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (st == 3)
            {
                using (var b = new SolidBrush(Color.FromArgb(0x16, 0x17, 0x1a))) g.FillRectangle(b, r);
                EnsureTextures();
                _carbonBrush.ResetTransform();
                g.FillRectangle(_carbonBrush, r);
            }
            else
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.FromArgb(0x9a, 0x9f, 0xa7), Color.FromArgb(0x4a, 0x4e, 0x55), LinearGradientMode.Horizontal))
                    g.FillRectangle(b, r);
                FillBrushed(g, new RectangleF(r.X, r.Y, r.Width, r.Height));
            }
            // Kanten: aussen Licht, innen Schatten (wie eine abgerundete Wange)
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.Black, Color.Black, LinearGradientMode.Horizontal))
            {
                var cols = new[] { Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(120, 0, 0, 0) };
                if (!left) Array.Reverse(cols);
                b.InterpolationColors = new ColorBlend { Colors = cols, Positions = new[] { 0f, 0.25f, 0.7f, 1f } };
                g.FillRectangle(b, r);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Knoepfe

        /// <summary>Knopf im gewaehlten Stil (ueber den des Designs). pos = 0..1.</summary>
        public static void DrawChosenKnobBody(Graphics g, PointF c, float radius, float pos, bool hover)
        {
            double ang = (-135 + 270.0 * Math.Max(0f, Math.Min(1f, pos)) - 90) * Math.PI / 180.0;
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            switch (EffKnob)
            {
                case 5: GoldKnobBody(g, c, radius, ca, sa, hover); break;
                case 1: DrawKnobBody(g, c, radius, pos, hover); break;
                case 2: ChromeKnobBody(g, c, radius, ca, sa, hover); break;
                case 3: RubberKnobBody(g, c, radius, ca, sa, hover); break;
                case 4: VintageKnobBody(g, c, radius, ca, sa, hover); break;
            }
        }

        /// <summary>Chrom: spiegelnde Kappe mit hellen und dunklen Reflexen, schwarzer Zeiger.</summary>
        private static void ChromeKnobBody(Graphics g, PointF c, float radius, float ca, float sa, bool hover)
        {
            FillEllipse(g, Color.FromArgb(90, 0, 0, 0), c.X, c.Y + F(1.6f), radius + F(0.6f));
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, Color.White, Color.Black, 60f))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { C(0xf4f6f8), C(0x9aa0a8), C(0xe8ebef), C(0x5c626a), C(0xc8ccd2), C(0x3a3e44) },
                    Positions = new[] { 0f, 0.22f, 0.42f, 0.6f, 0.8f, 1f },
                };
                g.FillEllipse(b, outer);
            }
            float ir = radius * 0.74f;
            var inner = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            using (var b = new LinearGradientBrush(inner, hover ? Color.White : C(0xe6e9ed), C(0x6e747c), 240f))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { hover ? Color.White : C(0xeef0f3), C(0xb8bdc4), C(0xf6f7f9), C(0x80868e) },
                    Positions = new[] { 0f, 0.4f, 0.55f, 1f },
                };
                g.FillEllipse(b, inner);
            }
            using (var pen = new Pen(Color.FromArgb(160, 0x20, 0x22, 0x26), F(1))) g.DrawEllipse(pen, outer);
            using (var pen = new Pen(Color.FromArgb(90, 0x30, 0x33, 0x38), F(0.8f))) g.DrawEllipse(pen, inner);
            using (var pen = new Pen(C(0x15, 0x16, 0x18), F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X + ca * ir * 0.2f, c.Y + sa * ir * 0.2f, c.X + ca * radius * 0.92f, c.Y + sa * radius * 0.92f);
        }

        /// <summary>Gummi (Soft-Touch): matt schwarz, weicher Glanz oben, weisser Zeiger.</summary>
        private static void RubberKnobBody(Graphics g, PointF c, float radius, float ca, float sa, bool hover)
        {
            FillEllipse(g, Color.FromArgb(80, 0, 0, 0), c.X, c.Y + F(1.4f), radius + F(0.6f));
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, hover ? C(0x3a3c40) : C(0x2c2e31), C(0x111214), 80f))
                g.FillEllipse(b, outer);
            // Griffmulden am Rand
            int n = Math.Max(10, Math.Min(20, (int)(radius * 1.4f)));
            using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), Math.Max(1f, F(1.1f))))
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2 / n;
                    float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    g.DrawLine(pen, c.X + cs * radius * 0.84f, c.Y + sn * radius * 0.84f, c.X + cs * radius * 0.98f, c.Y + sn * radius * 0.98f);
                }
            // weicher Glanz
            using (var path = new GraphicsPath())
            {
                var gl = new RectangleF(c.X - radius * 0.7f, c.Y - radius * 0.85f, radius * 1.4f, radius * 0.9f);
                path.AddEllipse(gl);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(40, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillEllipse(pb, gl);
            }
            using (var pen = new Pen(Color.FromArgb(200, 0x05, 0x05, 0x06), F(1))) g.DrawEllipse(pen, outer);
            using (var pen = new Pen(C(0xf0, 0xf1, 0xf2), F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X + ca * radius * 0.3f, c.Y + sa * radius * 0.3f, c.X + ca * radius * 0.82f, c.Y + sa * radius * 0.82f);
        }

        // ================================================================ Faderkappen

        /// <summary>Faderkappe im gewaehlten Material (MASTER bleibt rot, wie es sich gehoert).</summary>
        public static void DrawChosenCap(Graphics g, RectangleF r, bool hover, Color accent)
        {
            if (EffCap == 6) { DrawGlassCap(g, r, hover, accent); return; }   // 4.0 finale: Glas
            float rad = CapRad(F(3), r);
            FillRound(g, Color.FromArgb(100, 0, 0, 0), new RectangleF(r.X + F(1), r.Y + F(2.5f), r.Width, r.Height), rad + F(1));
            Color[] cols; float[] pos;
            Color lineDark, lineLight, mid;
            switch (EffCap)
            {
                case 5: // Gold (Design Schwarz-Gold)
                    cols = new[] { C(0xfbeab0), C(0xc9993c), C(0xf3d98b), C(0x8e6a26), C(0xe0bb66), C(0x6e4c14) };
                    pos = new[] { 0f, 0.3f, 0.47f, 0.53f, 0.75f, 1f };
                    lineDark = Color.FromArgb(80, 0x3a, 0x26, 0x05); lineLight = Color.FromArgb(120, 255, 245, 210); mid = C(0x1a, 0x12, 0x05); break;
                case 1: // Chrom
                    cols = new[] { C(0xfafbfc), C(0xa4aab2), C(0xe9ecf0), C(0x6a7078), C(0xd0d4d9), C(0x8a9098) };
                    pos = new[] { 0f, 0.3f, 0.47f, 0.53f, 0.75f, 1f };
                    lineDark = Color.FromArgb(70, 0, 0, 0); lineLight = Color.FromArgb(120, 255, 255, 255); mid = C(0x15, 0x16, 0x18); break;
                case 2: // Schwarz
                    cols = new[] { C(0x4a4d52), C(0x26282c), C(0x18191c), C(0x18191c), C(0x222428), C(0x0c0d0f) };
                    pos = new[] { 0f, 0.35f, 0.48f, 0.52f, 0.7f, 1f };
                    lineDark = Color.FromArgb(120, 0, 0, 0); lineLight = Color.FromArgb(30, 255, 255, 255); mid = C(0xf2, 0xf3, 0xf5); break;
                case 3: // Weiss
                    cols = new[] { Color.White, C(0xeef0f2), C(0xd6d9dd), C(0xd6d9dd), C(0xe6e8eb), C(0xb8bcc2) };
                    pos = new[] { 0f, 0.35f, 0.48f, 0.52f, 0.7f, 1f };
                    lineDark = Color.FromArgb(40, 0, 0, 0); lineLight = Color.FromArgb(160, 255, 255, 255); mid = C(0x22, 0x24, 0x28); break;
                default: // Holz
                    cols = new[] { C(0xa06a3e), C(0x7a4a26), C(0x5e3519), C(0x5e3519), C(0x6e4222), C(0x3c2010) };
                    pos = new[] { 0f, 0.35f, 0.48f, 0.52f, 0.7f, 1f };
                    lineDark = Color.FromArgb(80, 0x20, 0x10, 0x05); lineLight = Color.FromArgb(40, 255, 220, 180); mid = C(0xf0, 0xe2, 0xc8); break;
            }
            if (hover) for (int i = 0; i < cols.Length; i++) cols[i] = Lighten(cols[i], 0.08f);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), cols[0], cols[cols.Length - 1], LinearGradientMode.Vertical))
            {
                b.InterpolationColors = new ColorBlend { Colors = cols, Positions = pos };
                FillRound(g, b, r, rad);
            }
            if (EffCap == 4)
            {
                // Maserung
                var st = g.Save();
                using (var path = Round(r, rad)) g.SetClip(path, CombineMode.Intersect);
                using (var pen = new Pen(Color.FromArgb(45, 0x2a, 0x14, 0x06), Math.Max(1f, F(0.8f))))
                    for (int i = 0; i < 4; i++)
                    {
                        float y = r.Y + r.Height * (0.18f + i * 0.22f);
                        g.DrawBezier(pen, r.X, y, r.X + r.Width * 0.35f, y - F(1.5f), r.X + r.Width * 0.65f, y + F(1.5f), r.Right, y);
                    }
                g.Restore(st);
            }
            StrokeRound(g, Color.FromArgb(0x0c, 0x0d, 0x0f), F(1), r, rad);
            using (var dark = new Pen(lineDark, F(1)))
            using (var light = new Pen(lineLight, F(1)))
                for (int i = -CapRidges(r); i <= CapRidges(r); i++)
                {
                    if (i == 0) continue;
                    float y = r.Y + r.Height / 2 + i * F(2.6f);
                    g.DrawLine(dark, r.X + F(4), y, r.Right - F(4), y);
                    g.DrawLine(light, r.X + F(4), y + F(1), r.Right - F(4), y + F(1));
                }
            float cy = r.Y + r.Height / 2;
            using (var pen = new Pen(mid, F(2))) g.DrawLine(pen, r.X + F(2), cy, r.Right - F(2), cy);
            using (var b = new SolidBrush(accent))
            {
                g.FillRectangle(b, r.X + F(1.5f), cy - F(1), F(3), F(2));
                g.FillRectangle(b, r.Right - F(4.5f), cy - F(1), F(3), F(2));
            }
        }

        // ================================================================ Digitale Namensschilder

        public static readonly Color DigitalPlateBg = Color.FromArgb(0x0a, 0x0c, 0x0f);

        /// <summary>Schrift auf dem digitalen Schild: die Kanalfarbe, hell genug zum Lesen.</summary>
        public static Color DigitalInk(Color bg)
        {
            float l = Luma(bg);
            return l < 0.45f ? Lighten(bg, 0.45f - l + 0.15f) : l > 0.85f ? Color.FromArgb(0xe8, 0xeb, 0xef) : Lighten(bg, 0.12f);
        }

        /// <summary>Hinterleuchtetes LCD-Schild: dunkel, feines Pixelraster, Kanalfarbe als Rand und Schein.</summary>
        public static void DrawDigitalPlate(Graphics g, RectangleF r, Color bg)
        {
            float rad = F(3);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, DigitalPlateBg, r, rad);
            // Schein der Hintergrundbeleuchtung in Kanalfarbe
            using (var path = Round(r, rad))
            using (var pb = new PathGradientBrush(path))
            {
                pb.CenterPoint = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                pb.CenterColor = Color.FromArgb(46, bg);
                pb.SurroundColors = new[] { Color.FromArgb(10, bg) };
                pb.FocusScales = new PointF(0.6f, 0.2f);
                g.FillPath(pb, path);
            }
            // Pixelraster
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                for (float y = r.Y + F(2); y < r.Bottom - F(1.5f); y += Math.Max(2f, F(2)))
                    g.DrawLine(pen, r.X + F(2), y, r.Right - F(2), y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            StrokeRound(g, Color.FromArgb(150, bg), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
            // Glas
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f + 1), Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(r.X + F(1), r.Y + F(1), r.Width - F(2), r.Height * 0.45f), rad - 1);
            g.SmoothingMode = sm;
        }

        // ================================================================ Pegel-LEDs

        /// <summary>Hoehe und Abstand der Segmente in den Zuegen.</summary>
        public static void StripSegSize(out int h, out int gap)
        {
            switch (LedSize)
            {
                case 0: h = Math.Max(2, Px(2)); gap = Math.Max(1, Px(1)); break;
                case 2: h = Math.Max(4, Px(5)); gap = Math.Max(2, Px(2)); break;
                default: h = Math.Max(2, Px(3)); gap = Math.Max(1, Px(1)); break;
            }
        }

        /// <summary>Hoehe und Abstand der Segmente in der Meterbridge.</summary>
        public static void BridgeSegSize(out int h, out int gap)
        {
            switch (LedSize)
            {
                case 0: h = Math.Max(1, Px(1)); gap = Math.Max(1, Px(1)); break;
                case 2: h = Math.Max(3, Px(4)); gap = Math.Max(2, Px(2)); break;
                default: h = Math.Max(2, Px(2)); gap = Math.Max(1, Px(1)); break;
            }
        }

        /// <summary>Ein Pegel-Segment in der gewaehlten Form.</summary>
        public static void FillSeg(Graphics g, SolidBrush b, float x, float y, float w, float h)
        {
            switch (LedShape)
            {
                case 1:
                {
                    var sm = g.SmoothingMode;
                    var po = g.PixelOffsetMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;   // Pixel x deckt [x, x+1] – die Form bleibt im Segment
                    float d = Math.Min(w, h);
                    if (w <= h * 1.8f)
                        g.FillEllipse(b, x + (w - d) / 2f, y + (h - d) / 2f, d, d);
                    else
                    {
                        // Kapsel aus zwei Kreisen und einem Rechteck (ohne Pfad – jedes Bild viele Male, das spart Zeit)
                        g.FillEllipse(b, x, y, d, d);
                        g.FillEllipse(b, x + w - d, y, d, d);
                        g.SmoothingMode = SmoothingMode.None;
                        g.FillRectangle(b, x + d / 2f, y, w - d, h);
                    }
                    g.SmoothingMode = sm;
                    g.PixelOffsetMode = po;
                    break;
                }
                case 2:
                {
                    float lh = Math.Max(1f, (float)Math.Round(h * 0.45f));
                    g.FillRectangle(b, x, y + (float)Math.Round((h - lh) / 2f), w, lh);
                    break;
                }
                default:
                    g.FillRectangle(b, x, y, w, h);
                    break;
            }
        }

        private static readonly int[] HaloAlpha = { 14, 22, 34 };
        private static readonly float[] HaloGrowBase = { 4.5f, 3f, 1.5f };
        private static readonly SolidBrush HaloBrush = new SolidBrush(Color.Black);

        /// <summary>Weicher Lichtschein um den leuchtenden Teil eines Pegels.</summary>
        public static void DrawLedHalo(Graphics g, RectangleF litPart, Color c)
        {
            if (!LedHalo || litPart.Height < 1 || litPart.Width < 1) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            // Drei weiche Schichten; die Ecken als Stufe (je ein breites und ein hohes Stueck, ohne Ueberlappung) –
            // sieht bei dem schwachen Licht aus wie abgerundet, kostet aber nur Rechtecke statt geglaetteter Pfade.
            for (int i = 0; i < 3; i++)
            {
                float gx = F(HaloGrowBase[i]), gy = gx * 0.7f, cut = Math.Max(1f, (float)Math.Round(gx * 0.55f));
                var rr = RectangleF.Inflate(litPart, gx, gy);
                HaloBrush.Color = Color.FromArgb(HaloAlpha[i], c);
                g.FillRectangle(HaloBrush, rr.X, rr.Y + cut, rr.Width, rr.Height - 2 * cut);
                g.FillRectangle(HaloBrush, rr.X + cut, rr.Y, rr.Width - 2 * cut, cut);
                g.FillRectangle(HaloBrush, rr.X + cut, rr.Bottom - cut, rr.Width - 2 * cut, cut);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ VU-Licht

        /// <summary>Lichtfarbe hinter der VU-Skala anwenden (warm = wie das Design).</summary>
        private static void ApplyVuLight(ref Color lamp, ref Color edge, ref Color ink)
        {
            if (VuLight == 0) return;
            Color l, e, i;
            switch (VuLight)
            {
                case 1: l = Color.FromArgb(0xa6, 0xd6, 0xff); e = Color.FromArgb(0x1c, 0x44, 0x86); i = Color.FromArgb(0x08, 0x16, 0x30); break;   // blau wie alte Hi-Fi-Verstaerker
                case 2: l = Color.FromArgb(0xc8, 0xff, 0xb4); e = Color.FromArgb(0x2c, 0x6c, 0x2a); i = Color.FromArgb(0x0a, 0x22, 0x0c); break;   // gruen
                default: l = Color.FromArgb(0xf6, 0xf9, 0xff); e = Color.FromArgb(0x9c, 0xa8, 0xba); i = Color.FromArgb(0x14, 0x18, 0x20); break; // weiss (kalt)
            }
            if (Luma(lamp) < 0.4f)
            {
                // dunkle Skala: nur ein Hauch der Lichtfarbe
                lamp = Mix(lamp, l, 0.28f);
                edge = Mix(edge, e, 0.3f);
                return;
            }
            lamp = l; edge = e; ink = i;
        }

        // ================================================================ Eigenes Hintergrundbild

        /// <summary>Hintergrundbild (schon auf Fenstergroesse gebracht und abgedunkelt) oder null.</summary>
        public static Bitmap BgImage;
        public static int BgVersion;

        /// <summary>Pult-Grund fuellen: Farbe oder der passende Ausschnitt des Hintergrundbilds (origin = Lage im Fenster).</summary>
        public static void FillBody(Graphics g, RectangleF area, Point origin, Color body)
        {
            var bg = BgImage;
            if (bg == null)
            {
                using (var b = new SolidBrush(body)) g.FillRectangle(b, area);
                return;
            }
            var src = new RectangleF(area.X + origin.X, area.Y + origin.Y, area.Width, area.Height);
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            var po = g.PixelOffsetMode;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var b = new SolidBrush(body)) g.FillRectangle(b, area);   // falls das Bild kleiner ist
            g.DrawImage(bg, area, src, GraphicsUnit.Pixel);
            g.InterpolationMode = im;
            g.PixelOffsetMode = po;
            if (NightOn) DimOverlay(g, area, 0f);
        }
    }
}
