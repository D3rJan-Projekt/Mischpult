using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Mischpult.UI
{
    internal static partial class Theme
    {
        public static float S = 1f;
        public static int Px(float v) => (int)Math.Round(v * S);
        public static float F(float v) => v * S;

        /// <summary>Etwas mit anderer Skalierung zeichnen (z. B. das grosse VU-Meter) – danach wieder wie vorher.</summary>
        public static void WithScale(float s, Action draw)
        {
            float old = S;
            S = s;
            try { draw(); }
            finally { S = old; }
        }

        // ---------------------------------------------------------------- Farben
        public static Color WindowTop = Color.FromArgb(0x17, 0x19, 0x1d);
        public static Color WindowBottom = Color.FromArgb(0x0e, 0x10, 0x13);
        public static Color BarBg = Color.FromArgb(0x0c, 0x0d, 0x10);
        public static Color PanelTop = Color.FromArgb(0x2c, 0x30, 0x37);
        public static Color PanelBottom = Color.FromArgb(0x1d, 0x20, 0x25);
        public static Color PanelEdge = Color.FromArgb(0x07, 0x08, 0x0a);
        public static Color PanelHighlight = Color.FromArgb(0x48, 0x4e, 0x58);
        public static Color Text = Color.FromArgb(0xe8, 0xeb, 0xef);
        public static Color TextDim = Color.FromArgb(0x8f, 0x98, 0xa4);
        public static Color TextFaint = Color.FromArgb(0x5a, 0x62, 0x6d);
        public static readonly Color Groove = Color.FromArgb(0x07, 0x08, 0x0a);
        public static Color LcdBg = Color.FromArgb(0x0a, 0x0c, 0x0f);
        public static Color LcdText = Color.FromArgb(0xff, 0xb5, 0x47);
        public static Color Accent = Color.FromArgb(0xff, 0xb5, 0x47);
        public static readonly Color MuteRed = Color.FromArgb(0xff, 0x4b, 0x3e);
        public static readonly Color SoloAmber = Color.FromArgb(0xff, 0xc2, 0x33);
        public static readonly Color LedGreen = Color.FromArgb(0x3b, 0xdb, 0x6f);
        /// <summary>Pegel unten/Mitte – je nach gewaehlten Pegel-Farben (OPTIONEN → Aussehen). Rot oben bleibt immer rot.</summary>
        public static Color MeterGreen => MeterLow(Accent);
        public static Color MeterYellow => MeterMid(Accent);
        public static readonly Color MeterRed = Color.FromArgb(0xff, 0x4a, 0x3a);
        /// <summary>Grau fuer den Pegel vor Fader/Stumm ("da laeuft eigentlich was"). Helligkeit einstellbar.</summary>
        public static Color MeterGhost = Color.FromArgb(0x46, 0x4b, 0x53);
        /// <summary>Grauer Pegel ueberhaupt anzeigen.</summary>
        public static bool GhostOn = true;

        /// <summary>Schnellwahl im Menue (Helligkeit in %).</summary>
        public static readonly int[] GhostPresets = { 0, 15, 30, 50, 75 };
        public static readonly string[] GhostNames = { "Aus", "Sehr dunkel", "Dunkel", "Mittel", "Hell" };
        public const int GhostDefault = MixerSettings.GhostDefault;
        private static readonly Color GhostDark = Color.FromArgb(0x1c, 0x1f, 0x24), GhostLight = Color.FromArgb(0xc4, 0xca, 0xd2);

        /// <summary>Grau fuer eine Helligkeit 0..100 % (0 = aus).</summary>
        public static Color GhostColor(int pct) => Mix(GhostDark, GhostLight, Math.Max(0, Math.Min(100, pct)) / 100f);

        public static void ApplyGhost(int pct)
        {
            GhostOn = pct > 0;
            if (GhostOn) MeterGhost = GhostColor(pct);
        }
        public static Color MenuBg = Color.FromArgb(0x22, 0x25, 0x2b);
        public static Color MenuHover = Color.FromArgb(0x35, 0x3a, 0x43);
        public static Color MenuBorder = Color.FromArgb(0x3b, 0x40, 0x49);
        public static Color Body = Color.FromArgb(0x13, 0x15, 0x19);

        public static readonly string[] SchemeNames = { "Dunkel", "Mitternacht", "Graphit" };
        public static readonly string[] AccentNames = { "Bernstein", "Grün", "Cyan", "Pink", "Weiß" };
        private static readonly Color[] AccentColors =
        {
            Color.FromArgb(0xff, 0xb5, 0x47), Color.FromArgb(0x5b, 0xe0, 0x8a), Color.FromArgb(0x4f, 0xd6, 0xe8),
            Color.FromArgb(0xff, 0x6f, 0xae), Color.FromArgb(0xe8, 0xec, 0xf2),
        };

        /// <summary>Farbschema und Akzentfarbe setzen, Design-Stil bleibt (danach alles neu zeichnen).</summary>
        public static void ApplyScheme(int scheme, int accent) => ApplyScheme(scheme, accent, Style);

        /// <summary>Farbschema, Akzentfarbe und Design-Stil setzen (danach alles neu zeichnen).</summary>
        public static void ApplyScheme(int scheme, int accent, int style)
        {
            Design = Math.Max(0, Math.Min(LastDesign, style));   // 4.0: 9 Carbon, 10 Holz & Messing
            Vintage = Design == StyleVintage;
            Style = BaseStyleOf(Design);
            Color c(int r, int g, int b) => Color.FromArgb(r, g, b);
            switch (scheme)
            {
                case 1: // Mitternacht (blau)
                    WindowTop = c(0x15, 0x19, 0x24); WindowBottom = c(0x0c, 0x0f, 0x17); BarBg = c(0x0a, 0x0c, 0x13);
                    PanelTop = c(0x29, 0x31, 0x43); PanelBottom = c(0x1a, 0x20, 0x2e); PanelHighlight = c(0x45, 0x51, 0x69);
                    Body = c(0x11, 0x15, 0x1f); MenuBg = c(0x1e, 0x24, 0x31); MenuHover = c(0x30, 0x3a, 0x4f); MenuBorder = c(0x37, 0x42, 0x58);
                    break;
                case 2: // Graphit (heller)
                    WindowTop = c(0x26, 0x27, 0x2a); WindowBottom = c(0x1c, 0x1d, 0x20); BarBg = c(0x18, 0x19, 0x1b);
                    PanelTop = c(0x40, 0x42, 0x47); PanelBottom = c(0x2d, 0x2f, 0x33); PanelHighlight = c(0x5e, 0x61, 0x68);
                    Body = c(0x22, 0x23, 0x26); MenuBg = c(0x2c, 0x2e, 0x32); MenuHover = c(0x3e, 0x41, 0x47); MenuBorder = c(0x48, 0x4b, 0x52);
                    break;
                default:
                    WindowTop = c(0x17, 0x19, 0x1d); WindowBottom = c(0x0e, 0x10, 0x13); BarBg = c(0x0c, 0x0d, 0x10);
                    PanelTop = c(0x2c, 0x30, 0x37); PanelBottom = c(0x1d, 0x20, 0x25); PanelHighlight = c(0x48, 0x4e, 0x58);
                    Body = c(0x13, 0x15, 0x19); MenuBg = c(0x22, 0x25, 0x2b); MenuHover = c(0x35, 0x3a, 0x43); MenuBorder = c(0x3b, 0x40, 0x49);
                    break;
            }
            var a = AccentColors[Math.Max(0, Math.Min(AccentColors.Length - 1, accent))];
            Accent = a;
            LcdText = a;
            ValueText = a;
            DarkTokens();
            ApplyStyleColors(Math.Max(0, Math.Min(2, scheme)), Math.Max(0, Math.Min(AccentColors.Length - 1, accent)));
            if (Vintage) VintageColors(Math.Max(0, Math.Min(2, scheme)));
            DesignColors(Math.Max(0, Math.Min(2, scheme)));   // 2.16: Schwarz-Gold, Broadcast-Blau, Retro-Hi-Fi
            Designs400(Math.Max(0, Math.Min(2, scheme)));     // 4.0: Carbon, Holz & Messing
            Designs400c(Math.Max(0, Math.Min(2, scheme)));    // 4.0 finale: Grosskonsole, Glas & Licht, Industrial, Weltraum
            Designs420(Math.Max(0, Math.Min(2, scheme)));     // 4.2: Synthwave, Retro-Game, Cockpit
            Designs430(Math.Max(0, Math.Min(2, scheme)));     // 4.3: Steampunk, Arktis, Japan
            ApplyCustom();                                    // 2.20: eigene Farben (Design-Editor)
            ApplyAccentOverride();                            // 3.2: Akzent von Windows oder vom Album-Cover
            if (NixieBar) SegOff = NixieOff;                  // 4.0: Nixie-Balken (unbeleuchtete Kathode)
            if (StrongText) StrongTextColors();               // 4.0: kraeftige Schrift (Barrierefreiheit)
        }

        public static Color AccentPreview(int i) => AccentColors[Math.Max(0, Math.Min(AccentColors.Length - 1, i))];

        // ---------------------------------------------------------------- Schriften
        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        public static Font Ui(float px, FontStyle style = FontStyle.Regular) =>
            UiFamily != null ? Get(UiFamily, px, style, "Segoe UI", style) : Get("Segoe UI", px, style);
        public static Font UiSemi(float px) =>
            UiFamily != null ? Get(UiFamily, px, FontStyle.Bold, "Segoe UI", FontStyle.Bold) : Get("Segoe UI Semibold", px, FontStyle.Regular, "Segoe UI", FontStyle.Bold);
        public static Font Mono(float px, FontStyle style = FontStyle.Bold) => Get("Consolas", px, style);

        private static Font Get(string family, float px, FontStyle style, string fallback = null, FontStyle fallbackStyle = FontStyle.Regular)
        {
            string key = family + "|" + px + "|" + style + "|" + S;   // S gehoert dazu: das grosse VU-Fenster zeichnet mit anderer Skalierung
            if (_fonts.TryGetValue(key, out var f)) return f;
            // sehr kleine Schriften etwas groesser: unter ~9 px verschwimmen die Buchstaben
            if (px < 9f) px += (9f - px) * 0.35f;
            try
            {
                f = new Font(family, px * S, style, GraphicsUnit.Pixel);
                if (fallback != null && !string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    f.Dispose();
                    f = new Font(fallback, px * S, fallbackStyle, GraphicsUnit.Pixel);
                }
            }
            catch
            {
                f = new Font(FontFamily.GenericSansSerif, px * S, style, GraphicsUnit.Pixel);
            }
            _fonts[key] = f;
            return f;
        }

        /// <summary>Groesste Schrift (px) zwischen maxPx und minPx, in der der Text in die Breite passt.</summary>
        public static Font Fit(Graphics g, string text, float width, float maxPx, float minPx, FontStyle style = FontStyle.Bold)
        {
            for (float px = maxPx; px > minPx; px -= 0.5f)
            {
                var f = Ui(px, style);
                if (TextWidth(g, text, f) <= width) return f;
            }
            return Ui(minPx, style);
        }

        public static readonly StringFormat Center = new StringFormat
        {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
        };
        public static readonly StringFormat Left = new StringFormat
        {
            Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
        };
        public static readonly StringFormat Right = new StringFormat
        {
            Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap
        };
        public static readonly StringFormat Wrap = new StringFormat
        {
            Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord
        };

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextHint;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // ---------------------------------------------------------------- Farbhelfer
        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        public static Color Darken(Color c, float f) => Mix(c, Color.Black, f);
        public static Color Lighten(Color c, float f) => Mix(c, Color.White, f);

        public static Color TextOn(Color bg)
        {
            double l = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return l > 0.58 ? Color.FromArgb(0x10, 0x11, 0x14) : Color.White;
        }

        // ---------------------------------------------------------------- Formen
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var b = new SolidBrush(c)) FillRound(g, b, r, rad);
        }

        /// <summary>
        /// Lichtkante oben am Zug, die nach unten ausläuft. Nur im oberen Bereich gezeichnet: Ein Verlaufspinsel wiederholt sich
        /// unter Windows nach seiner Höhe (Kachel-Modus) – über den ganzen Zug gezogen wären die Seitenkanten sonst gestrichelt.
        /// </summary>
        public static void DrawTopHighlight(Graphics g, RectangleF hl, float rad, int alpha, float fadeH)
        {
            var st = g.Save();
            float top = hl.Y - F(2), bottom = hl.Y - 1 + fadeH - 0.5f;
            g.SetClip(new RectangleF(hl.X - F(2), top, hl.Width + F(4), Math.Max(1f, bottom - top)), CombineMode.Intersect);
            using (var b = new LinearGradientBrush(new RectangleF(hl.X, hl.Y - 1, hl.Width, fadeH), Alpha(PanelHighlight, alpha), Alpha(PanelHighlight, 0), LinearGradientMode.Vertical))
            using (var pen = new Pen(b, F(1)))
            using (var p = Round(hl, rad))
                g.DrawPath(pen, p);
            g.Restore(st);
        }

        public static void StrokeRound(Graphics g, Color c, float width, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void FillVertical(Graphics g, RectangleF r, Color top, Color bottom, float rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                FillRound(g, b, r, rad);
        }

        /// <summary>Gehaeuse-Panel eines Kanalzugs: eloxiertes, gebuerstetes Aluminium mit Fase.</summary>
        public static void DrawPanel(Graphics g, RectangleF r, float rad, Color? tint = null)
        {
            if (Style != StyleStudio) { StyledPanel(g, r, rad, tint); DrawPanelMaterial(g, r, rad); PanelDeco400(g, r, rad, tint); return; }
            // weicher Schatten
            for (int i = 4; i >= 1; i--)
            {
                var sr = new RectangleF(r.X - i * 0.5f, r.Y + i, r.Width + i, r.Height + i * 0.5f);
                FillRound(g, Color.FromArgb(26, 0, 0, 0), sr, rad + i);
            }
            using (var path = Round(r, rad))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), PanelTop, PanelBottom, LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                if (!CockpitDesign && !JapanDesign) FillBrushed(g, path, r);   // 4.2: Cockpit ist matt lackiert, 4.3: Japan glaenzender Lack
                // Glanzband wie bei Metall (Licht von links), rechts etwas dunkler
                using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), Color.Black, Color.Black, LinearGradientMode.Horizontal))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(6, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(26, 0, 0, 0) },
                        Positions = new[] { 0f, 0.3f, 0.68f, 1f },
                    };
                    g.FillPath(b, path);
                }
                // nach unten etwas tiefer
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(46, 0, 0, 0) },
                        Positions = new[] { 0f, 0.7f, 1f },
                    };
                    g.FillPath(b, path);
                }
            }
            float e = F(1) / 2f;
            StrokeRound(g, PanelEdge, F(1), RectangleF.Inflate(r, -e, -e), rad - e);
            // Fase: oben und links Licht, rechts Schatten
            var hl = RectangleF.Inflate(r, -F(1) - e, -F(1) - e);
            DrawTopHighlight(g, hl, rad - 1, 230, F(46));
            using (var pen = new Pen(Alpha(PanelHighlight, 70), F(1)))
                g.DrawLine(pen, hl.X, hl.Y + rad, hl.X, hl.Bottom - rad);
            using (var pen = new Pen(Color.FromArgb(110, 0, 0, 0), F(1)))
                g.DrawLine(pen, hl.Right, hl.Y + rad, hl.Right, hl.Bottom - rad);
            DrawPanelMaterial(g, r, rad);
            PanelDeco400(g, r, rad, tint);   // 4.0 finale
        }

        /// <summary>Gravierte Trennlinie (dunkel + hell darunter).</summary>
        public static void Engrave(Graphics g, float x1, float x2, float y)
        {
            if (Style != StyleStudio && Style != StyleClassic) { StyledEngrave(g, x1, x2, y); return; }
            using (var dark = new Pen(Color.FromArgb(150, 0, 0, 0), F(1)))
            using (var light = new Pen(Color.FromArgb(22, 255, 255, 255), F(1)))
            {
                g.DrawLine(dark, x1, y, x2, y);
                g.DrawLine(light, x1, y + F(1), x2, y + F(1));
            }
        }

        public static void Label(Graphics g, string text, RectangleF r, StringFormat fmt, Color color, float px = 9f)
        {
            using (var b = new SolidBrush(color)) Theme.Str(g, text, UiSemi(px), b, r, fmt);
        }

        /// <summary>Schriftgroesse fuer Tastenbeschriftung: 9,5 px, bei schmalen Tasten (z. B. SOLO | FADE | MUTE) etwas kleiner.</summary>
        public static float KeyPx(Graphics g, string text, float width)
        {
            float px = 9.5f;
            while (px > 8f && TextWidth(g, text, UiSemi(px)) > width - F(6)) px -= 0.5f;
            return px;
        }

        /// <summary>Gummitaste (MUTE/SOLO ...) mit LED: aus = dunkle Taste mit LED-Fenster in der Leuchtfarbe, an = leuchtet von innen.</summary>
        public static void DrawKey(Graphics g, RectangleF r, string text, bool on, Color litColor, bool hover, bool half = false)
        {
            litColor = KeyLit(text, litColor);   // 3.1: eigene Tasten-Farben
            if (KeyShape == 6)
            {
                // 4.0 finale: Pads (leuchten von innen)
                if (on && LitRedraw != null) LitRedraw.Add(() => DrawKey(g, r, text, true, litColor, hover, half));
                DrawPadKey(g, r, text, on, litColor, hover, half);
                return;
            }
            if (IndustrialDesign && KeyShape == 0 && text == "MUTE")
            {
                // 4.0 finale: Industrial – MUTE als Kippschalter mit Schutzkappe
                if (on && LitRedraw != null) LitRedraw.Add(() => DrawKey(g, r, text, true, litColor, hover, half));
                DrawToggleKey(g, r, text, on, litColor, hover, half);
                DrawSafetyCover(g, r, on);
                return;
            }
            if (KeyShape == 4 || KeyShape == 5)
            {
                // 3.3: Kippschalter oder Bakelit-Taste
                if (on && LitRedraw != null) LitRedraw.Add(() => DrawKey(g, r, text, true, litColor, hover, half));
                if (KeyShape == 4) DrawToggleKey(g, r, text, on, litColor, hover, half);
                else DrawBakeliteKey(g, r, text, on, litColor, hover, half);
                return;
            }
            if (on && KeyShape == 3)
            {
                // 3.1 Leuchtring: dunkle Taste, leuchtender Rand
                DrawKey(g, r, "", false, litColor, hover, false);
                float rr = KeyRad(Style == StyleStudio ? F(3.5f) : Style == StyleClassic ? F(3) : F(4.5f), r);
                if (LitRedraw != null) LitRedraw.Add(() => RingOverlay(g, r, text, litColor, rr));
                RingOverlay(g, r, text, litColor, rr);
                return;
            }
            if (on && LitRedraw != null) LitRedraw.Add(() => DrawKey(g, r, text, true, litColor, hover, half));   // abends: hell nachzeichnen
            if (Style != StyleStudio) { StyledKey(g, r, text, on, litColor, hover, half); return; }
            float rad = KeyRad(F(3.5f), r);
            bool window = r.Height >= F(19);   // Platz fuer das LED-Fenster oben
            if (on)
            {
                // Lichthof
                for (int i = 5; i >= 1; i--)
                    FillRound(g, Alpha(litColor, 13), RectangleF.Inflate(r, F(i * 1.1f), F(i * 1.1f)), rad + F(i));
                using (var path = Round(r, rad))
                {
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(litColor, 0.26f), Darken(litColor, 0.2f), LinearGradientMode.Vertical))
                        g.FillPath(b, path);
                    // leuchtet von innen: heller Kern
                    using (var pb = new PathGradientBrush(path))
                    {
                        pb.CenterPoint = new PointF(r.X + r.Width / 2, r.Y + r.Height * 0.45f);
                        pb.CenterColor = Alpha(Lighten(litColor, 0.55f), 150);
                        pb.SurroundColors = new[] { Alpha(litColor, 0) };
                        pb.FocusScales = new PointF(0.35f, 0.1f);
                        g.FillPath(pb, path);
                    }
                }
                StrokeRound(g, Darken(litColor, 0.5f), F(1), r, rad);
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height / 2 + 1), Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    FillRound(g, b, new RectangleF(r.X + F(1), r.Y + F(1), r.Width - F(2), r.Height * 0.45f), rad - 1);
                if (window)
                    FillRound(g, Color.FromArgb(220, 255, 255, 255), new RectangleF(r.X + r.Width / 2 - F(7), r.Y + F(2), F(14), F(1.8f)), F(0.9f));
                Label(g, text, window ? new RectangleF(r.X, r.Y + F(1), r.Width, r.Height) : r, Center, TextOn(litColor), KeyPx(g, text, r.Width));
            }
            else
            {
                // steht aus dem Pult hervor: Schlagschatten
                FillRound(g, Color.FromArgb(120, 0, 0, 0), new RectangleF(r.X, r.Y + F(1.4f), r.Width, r.Height), rad);
                Color top = hover ? KeyHoverTop : KeyTop;
                Color bottom = hover ? KeyHoverBottom : KeyBottom;
                FillVertical(g, r, top, bottom, rad);
                StrokeRound(g, KeyEdge, F(1), r, rad);
                using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), F(1)))
                    g.DrawLine(pen, r.X + rad, r.Y + F(1.5f), r.Right - rad, r.Y + F(1.5f));
                if (window)
                {
                    // LED aus – zeigt schon, in welcher Farbe die Taste leuchtet
                    var led = new RectangleF(r.X + r.Width / 2 - F(7), r.Y + F(2.2f), F(14), F(1.8f));
                    FillRound(g, half ? Darken(litColor, 0.15f) : Darken(litColor, 0.62f), led, F(0.9f));
                    if (half) FillRound(g, Alpha(litColor, 60), RectangleF.Inflate(led, F(2), F(1.5f)), F(2));
                }
                Color tc = half ? Mix(TextDim, litColor, 0.75f) : hover ? Text : TextDim;
                if (half && !window) StrokeRound(g, Alpha(litColor, 170), F(1.2f), RectangleF.Inflate(r, -F(1.5f), -F(1.5f)), rad - 1);
                Label(g, text, window ? new RectangleF(r.X, r.Y + F(1), r.Width, r.Height) : r, Center, tc, KeyPx(g, text, r.Width));
            }
        }

        /// <summary>Dunkles "Display"-Feld mit Text (z. B. Auswahlknoepfe).</summary>
        public static void DrawDisplayButton(Graphics g, RectangleF r, string text, bool hover, Color textColor, bool chevron = true, string icon = null)
        {
            float rad = Style == StyleModern || Style == StyleLight ? F(4.5f) : F(3);
            FillRound(g, hover ? DispHover : DispBg, r, rad);
            Color edge = hover ? DispEdgeHover : DispEdge;
            if (Style == StyleNeon && hover) edge = Alpha(Accent, 200);
            StrokeRound(g, edge, F(1), r, rad);
            if (Style == StyleStudio || Style == StyleClassic || Style == StyleLight)
                using (var pen = new Pen(Style == StyleLight ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(16, 255, 255, 255), F(1)))
                    g.DrawLine(pen, r.X + rad, r.Bottom + F(0.5f), r.Right - rad, r.Bottom + F(0.5f));
            float left = r.X + F(5);
            float right = r.Right - (chevron ? F(13) : F(4));
            if (chevron)
            {
                float cx = r.Right - F(8), cy = r.Y + r.Height / 2 + F(1);
                using (var b = new SolidBrush(hover ? Text : TextDim))
                    g.FillPolygon(b, new[] { new PointF(cx - F(3), cy - F(2)), new PointF(cx + F(3), cy - F(2)), new PointF(cx, cy + F(1.5f)) });
            }
            if (icon != null)
            {
                Label(g, icon, new RectangleF(left - F(1), r.Y, F(12), r.Height), Left, AccentText, 9f);
                left += F(11);
            }
            Label(g, text, new RectangleF(left, r.Y, right - left, r.Height), Left, textColor, 9f);
        }

        /// <summary>LCD-Anzeige hinter Glas: leuchtende Ziffern, feines Raster, Spiegelung.</summary>
        public static void DrawLcd(Graphics g, RectangleF r, string text, Color? color = null)
        {
            if (LitRedraw != null) { LitRedraw.Add(() => DrawLcd(g, r, text, color)); return; }   // Anzeige leuchtet: abends hell (nach dem Abdunkeln)
            if (EffLcd != 0)   // gewaehlter LCD-Stil (OPTIONEN → Aussehen), bei Vintage Nixie
            {
                bool dim = color.HasValue && color.Value.ToArgb() == Darken(LcdText, 0.45f).ToArgb();
                DrawLcdStyled(g, r, text, dim, dim ? null : color);
                return;
            }
            if (Style != StyleStudio) { StyledLcd(g, r, text, color ?? LcdText); return; }
            float rad = F(3);
            Color c = color ?? LcdText;
            FillRound(g, LcdBg, r, rad);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(26, c.R, c.G, c.B), Color.FromArgb(4, c.R, c.G, c.B), LinearGradientMode.Vertical))
                FillRound(g, b, r, rad);
            // Raster (unter den Ziffern, damit die Schrift scharf bleibt) – an den runden Ecken eingerueckt statt abgeschnitten
            using (var pen = new Pen(Color.FromArgb(30, 0, 0, 0), 1f))
                for (float y = r.Y + 1; y < r.Bottom; y += Math.Max(2f, F(2)))
                {
                    float dy = Math.Min(y - r.Y, r.Bottom - y);
                    float ins = dy >= rad ? 0f : rad - (float)Math.Sqrt(Math.Max(0, rad * rad - (rad - dy) * (rad - dy)));
                    g.DrawLine(pen, r.X + ins, y, r.Right - ins, y);
                }
            // Ziffern mit leichtem Leuchten
            var font = Mono(11.5f);
            using (var glow = new SolidBrush(Alpha(c, 20)))
            {
                float d = F(1f);
                Theme.Str(g, text, font, glow, new RectangleF(r.X - d, r.Y, r.Width, r.Height), Center);
                Theme.Str(g, text, font, glow, new RectangleF(r.X + d, r.Y, r.Width, r.Height), Center);
            }
            using (var b = new SolidBrush(c)) Theme.Str(g, text, font, b, r, Center);
            // Innenschatten oben und Glas-Spiegelung als geglaettete Flaeche (kein hartes Clipping an den Ecken)
            using (var path = Round(r, rad))
            {
                float sh = Math.Min(1f, F(6) / Math.Max(1f, r.Height));
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(120, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0) },
                        Positions = new[] { 0f, sh, 1f },
                    };
                    g.FillPath(b, path);
                }
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(16, 255, 255, 255), Color.FromArgb(4, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                        Positions = new[] { 0f, 0.42f, 0.43f, 1f },
                    };
                    g.FillPath(b, path);
                }
            }
            StrokeRound(g, Color.FromArgb(0x02, 0x02, 0x03), F(1), r, rad);
            using (var pen = new Pen(Color.FromArgb(24, 255, 255, 255), F(1)))
                g.DrawLine(pen, r.X + rad, r.Bottom + F(0.5f), r.Right - rad, r.Bottom + F(0.5f));
        }

        /// <summary>Drehregler mit LED-Kranz.</summary>
        public static void DrawKnob(Graphics g, PointF c, float radius, float value, Color led, bool bipolar, bool hover, float ringOffset = 6f, int dots = 15)
        {
            if (Style != StyleStudio && Style != StyleClassic)
            {
                if (KnobRing) DrawKnobRing(g, c, radius, value, led, bipolar, ringOffset);   // 3.3: LED-Kranz statt Bogen
                StyledKnob(g, c, radius, value, led, bipolar, hover, ringOffset);
                if (EffKnob != 0) DrawChosenKnobBody(g, c, radius, bipolar ? (value + 1f) / 2f : value, hover);   // gewaehlter Knopf
                return;
            }
            // LED-Kranz: Punkte von -135 bis +135 Grad
            float ringR = radius + F(ringOffset);
            int centerIdx = dots / 2;
            int valueIdx = (int)Math.Round((bipolar ? (value + 1f) / 2f : value) * (dots - 1));
            if (KnobRing) { DrawKnobRing(g, c, radius, value, led, bipolar, ringOffset); dots = 0; }   // 3.3
            for (int i = 0; i < dots; i++)
            {
                double ang = (-135 + 270.0 * i / (dots - 1) - 90) * Math.PI / 180.0;
                float x = c.X + (float)Math.Cos(ang) * ringR, y = c.Y + (float)Math.Sin(ang) * ringR;
                bool lit = bipolar
                    ? (i >= Math.Min(centerIdx, valueIdx) && i <= Math.Max(centerIdx, valueIdx))
                    : i <= valueIdx;
                float dr = F(dots > 13 ? 1.6f : 1.5f);
                if (lit)
                {
                    FillEllipse(g, Alpha(led, 60), x, y, dr * 2.2f);
                    FillEllipse(g, Lighten(led, 0.15f), x, y, dr);
                }
                else FillEllipse(g, RingOff, x, y, dr * 0.85f);
            }

            if (EffKnob != 0) DrawChosenKnobBody(g, c, radius, bipolar ? (value + 1f) / 2f : value, hover);
            else if (Style == StyleClassic) ClassicKnobBody(g, c, radius, bipolar ? (value + 1f) / 2f : value, hover);
            else DrawKnobBody(g, c, radius, bipolar ? (value + 1f) / 2f : value, hover);
        }

        public static void FillEllipse(Graphics g, Color c, float cx, float cy, float r)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        /// <summary>Faderkappe. master = rote Kappe.</summary>
        public static void DrawFaderCap(Graphics g, RectangleF r, bool master, bool hover, Color accent)
        {
            if (CapShape == 5) { DrawDishCap(g, r, master, hover, accent); return; }   // 3.4.6: Griffmulde
            if (EffCap != 0 && !master) { DrawChosenCap(g, r, hover, accent); return; }   // gewaehlte Kappe (MASTER bleibt rot)
            if (Style != StyleStudio && Style != StyleClassic) { StyledFaderCap(g, r, master, hover, accent); return; }
            float rad = CapRad(F(2.5f), r);   // 3.1: Kappen-Form
            // Schatten
            FillRound(g, Color.FromArgb(110, 0, 0, 0), new RectangleF(r.X + F(1), r.Y + F(3), r.Width, r.Height), rad + F(1));
            FillRound(g, Color.FromArgb(50, 0, 0, 0), new RectangleF(r.X - F(1), r.Y + F(5), r.Width + F(2), r.Height), rad + F(2));

            Color c1, c2, c3;
            if (master)
            {
                c1 = Color.FromArgb(0xf0, 0x6a, 0x5c); c2 = Color.FromArgb(0xa8, 0x2a, 0x22); c3 = Color.FromArgb(0xd8, 0x4d, 0x40);
            }
            else
            {
                c1 = Color.FromArgb(0xe4, 0xe7, 0xeb); c2 = Color.FromArgb(0x86, 0x8c, 0x95); c3 = Color.FromArgb(0xc3, 0xc7, 0xcd);
            }
            if (hover) { c1 = Lighten(c1, 0.08f); c2 = Lighten(c2, 0.08f); c3 = Lighten(c3, 0.08f); }

            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), c1, c3, LinearGradientMode.Vertical))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { c1, c3, c2, c2, c3, Darken(c3, 0.25f) },
                    Positions = new[] { 0f, 0.35f, 0.48f, 0.52f, 0.7f, 1f }
                };
                b.InterpolationColors = blend;
                FillRound(g, b, r, rad);
            }
            StrokeRound(g, Color.FromArgb(0x0c, 0x0d, 0x0f), F(1), r, rad);

            // Griffrillen
            using (var dark = new Pen(Color.FromArgb(master ? 90 : 70, 0, 0, 0), F(1)))
            using (var light = new Pen(Color.FromArgb(master ? 60 : 110, 255, 255, 255), F(1)))
            {
                int nr = CapRidges(r);   // 3.1: flache Kappen haben weniger Rillen
                for (int i = -nr; i <= nr; i++)
                {
                    if (i == 0) continue;
                    float y = r.Y + r.Height / 2 + i * F(2.6f);
                    g.DrawLine(dark, r.X + F(4), y, r.Right - F(4), y);
                    g.DrawLine(light, r.X + F(4), y + F(1), r.Right - F(4), y + F(1));
                }
            }
            // Mittellinie
            float cy = r.Y + r.Height / 2;
            using (var pen = new Pen(master ? Color.White : Color.FromArgb(0x10, 0x11, 0x13), F(2)))
                g.DrawLine(pen, r.X + F(2), cy, r.Right - F(2), cy);
            if (!master)
            {
                using (var b = new SolidBrush(accent))
                {
                    g.FillRectangle(b, r.X + F(1.5f), cy - F(1), F(3), F(2));
                    g.FillRectangle(b, r.Right - F(4.5f), cy - F(1), F(3), F(2));
                }
            }
        }

        /// <summary>Kleines LED-Symbol.</summary>
        public static void DrawLed(Graphics g, float cx, float cy, float r, Color c, bool on)
        {
            if (on)
            {
                FillEllipse(g, Alpha(c, 55), cx, cy, r * 2.3f);
                FillEllipse(g, c, cx, cy, r);
                FillEllipse(g, Color.FromArgb(150, 255, 255, 255), cx - r * 0.3f, cy - r * 0.35f, r * 0.35f);
            }
            else
            {
                FillEllipse(g, Darken(c, 0.72f), cx, cy, r);
            }
        }

        public static void DrawSpaced(Graphics g, string text, Font font, Color color, float x, float y, float spacing)
        {
            using (var b = new SolidBrush(color))
            {
                foreach (char ch in text)
                {
                    string s = ch.ToString();
                    Theme.Str(g, s, font, b, x, y, StringFormat.GenericTypographic);
                    float w = ch == ' ' ? font.Size * 0.3f : TextWidth(g, s, font);
                    x += w + spacing;
                }
            }
        }

        public static float MeasureSpaced(Graphics g, string text, Font font, float spacing)
        {
            float x = 0;
            foreach (char ch in text)
            {
                string s = ch.ToString();
                x += (ch == ' ' ? font.Size * 0.3f : TextWidth(g, s, font)) + spacing;
            }
            return x - spacing;
        }
    }
}
