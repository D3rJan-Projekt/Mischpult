using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>2.20: eigene Farben (Design-Editor), Schraeglage/3D-Licht und Armauflage vorne am Pult.</summary>
    internal static partial class Theme
    {
        // ================================================================ Design-Editor

        public static readonly string[] CustomSlots = { "body", "panel", "keys", "disp", "text", "accent", "bar", "menu" };
        public static readonly string[] CustomSlotNames = { "Pult-Grund", "Kanalzüge", "Tasten", "Anzeigen und Felder", "Schrift", "Akzent (Werte, Haken)", "Kopfleiste", "Menüs und Fenster" };

        /// <summary>Eigene Farben je Bereich (null = keine) – kommen nach Farbschema und Design obendrauf.</summary>
        public static Dictionary<string, Color> CustomColors;

        /// <summary>Vorschlaege je Bereich (Flaechen, Schrift, Akzent).</summary>
        public static Color[] CustomPalette(string slot)
        {
            Color c(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb));
            if (slot == "text") return new[] { c(0xffffff), c(0xe8ebef), c(0xd9c4a0), c(0xf3e6c8), c(0xc8d6e6), c(0xb8f0c8), c(0xffd9a0), c(0x9aa4b0), c(0x202428), c(0x3a2a1a) };
            if (slot == "accent") return new[] { c(0xffb547), c(0x5ee08a), c(0x3cd6f0), c(0xff5fa2), c(0xff4d4d), c(0xb48cff), c(0x4d8dff), c(0xffffff), c(0xe8c25a), c(0xff8a3d) };
            return new[] { c(0x131519), c(0x1d2025), c(0x2c3037), c(0x11151f), c(0x1a2a22), c(0x2a1418), c(0x2a1d12), c(0x0a0a0a), c(0x3a3f47), c(0xd8dbe0) };
        }

        private static void ApplyCustom()
        {
            var cc = CustomColors;
            if (cc == null || cc.Count == 0) return;
            if (cc.TryGetValue("body", out var body))
            {
                Body = body; WindowTop = Lighten(body, 0.06f); WindowBottom = Darken(body, 0.3f);
                ScrollTrack = Lighten(body, 0.06f); AddHoverBg = Lighten(body, 0.05f);
            }
            if (cc.TryGetValue("panel", out var panel))
            {
                PanelTop = Lighten(panel, 0.1f); PanelBottom = panel; PanelHighlight = Lighten(panel, 0.32f);
                BadgeBg = Darken(panel, 0.1f); Chip = Lighten(panel, 0.08f);
            }
            if (cc.TryGetValue("keys", out var keys))
            {
                KeyTop = Lighten(keys, 0.12f); KeyBottom = Darken(keys, 0.22f); KeyHoverTop = Lighten(keys, 0.24f); KeyHoverBottom = Darken(keys, 0.08f);
                BtnTop = Lighten(keys, 0.06f); BtnBottom = Darken(keys, 0.2f); BtnHoverTop = Lighten(keys, 0.18f); BtnHoverBottom = Darken(keys, 0.08f);
                PadTop = KeyTop; PadBottom = KeyBottom; PadHoverTop = KeyHoverTop; PadHoverBottom = KeyHoverBottom;
            }
            if (cc.TryGetValue("disp", out var disp))
            {
                DispBg = disp; DispHover = Lighten(disp, 0.07f); Well = Darken(disp, 0.08f); WellHover = Lighten(disp, 0.04f);
                LcdBg = Darken(disp, 0.35f); Input = disp; InputFocus = Lighten(disp, 0.12f); Deep = Darken(disp, 0.4f); Inset = Lighten(disp, 0.05f);
            }
            if (cc.TryGetValue("text", out var text))
            {
                Text = text; TextHover = Lighten(text, 0.3f);
                TextDim = Mix(text, PanelBottom, 0.42f); TextFaint = Mix(text, PanelBottom, 0.62f);
                ScaleLabel = Mix(text, PanelBottom, 0.48f); ScaleTick = Mix(text, PanelBottom, 0.6f);
            }
            if (cc.TryGetValue("accent", out var acc)) { Accent = acc; LcdText = acc; ValueText = acc; }
            if (cc.TryGetValue("bar", out var bar)) BarBg = bar;
            if (cc.TryGetValue("menu", out var menu))
            {
                MenuBg = menu; MenuHover = Lighten(menu, 0.1f); MenuBorder = Lighten(menu, 0.18f); PopupBg = Darken(menu, 0.08f);
                PopupHover = Lighten(menu, 0.05f); MenuSep = Lighten(menu, 0.15f); MenuSepLight = menu;
            }
        }

        /// <summary>Kontrast (WCAG) zwischen zwei Farben – fuer den Hinweis im Design-Editor.</summary>
        public static double Contrast(Color a, Color b)
        {
            double L(Color c)
            {
                double f(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
                return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
            }
            double x = L(a), y = L(b);
            if (x < y) { var t = x; x = y; y = t; }
            return (x + 0.05) / (y + 0.05);
        }

        // ================================================================ Schraeglage / 3D

        /// <summary>Schraeglage: 0 aus, 1 leicht, 2 stark – oben heller (dem Licht zugewandt), vorne dunkler.</summary>
        public static int Tilt;
        /// <summary>Hoehe des Pult-Fensters (Licht-Verlauf ueber die ganze Hoehe).</summary>
        public static int TiltWindowH = 800;
        public static bool TiltOn => Tilt > 0;
        public static readonly string[] TiltNames = { "Aus", "Leicht", "Stark" };

        /// <summary>Licht der Schraeglage auf die Flaeche 'area' (Lage im Fenster: origin).</summary>
        public static void TiltShade(Graphics g, RectangleF area, Point origin)
        {
            SunLight(g, area, origin);   // 3.4: Licht nach Uhrzeit (eigener Schalter)
            if (Tilt <= 0 || area.Width <= 0 || area.Height <= 0) return;
            float h = Math.Max(100, TiltWindowH);
            float y0 = -origin.Y, y1 = h - origin.Y;   // Fenster-Oberkante/-Unterkante in eigenen Koordinaten
            int top = Tilt >= 2 ? 34 : 18, bottom = Tilt >= 2 ? 110 : 60;
            if (IsLight) { top = top * 2 / 3; bottom = bottom * 2 / 3; }
            var r = new RectangleF(area.X, y0 - 1, area.Width, Math.Max(2, y1 - y0 + 2));
            using (var b = new LinearGradientBrush(r, Color.Black, Color.Black, LinearGradientMode.Vertical))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(top, 255, 250, 240), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(bottom, 0, 0, 0) },
                    Positions = new[] { 0f, 0.3f, 0.45f, 1f },
                };
                var st = g.Save();
                g.SetClip(area, CombineMode.Intersect);
                g.FillRectangle(b, area);
                g.Restore(st);
            }
        }

        // ================================================================ Armauflage

        /// <summary>Armauflage vorne: 0 keine, 1 Leder, 2 Holz, 3 Gummi, 4 Alu.</summary>
        public static int ArmRest;
        public static readonly string[] ArmRestNames = { "Keine", "Leder (gepolstert)", "Holz", "Gummi", "Alu" };

        public static void DrawArmRest(Graphics g, Rectangle r, int style)
        {
            if (style <= 0 || r.Width <= 0 || r.Height <= 0) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            EnsureTextures();
            switch (style)
            {
                case 1:   // Leder: dunkel, gepolstert, mit Ziernaht
                    using (var b = new SolidBrush(Color.FromArgb(0x24, 0x1d, 0x1a))) g.FillRectangle(b, r);
                    _leatherBrush.ResetTransform();
                    g.FillRectangle(_leatherBrush, r);
                    break;
                case 2:   // Holz
                    using (var b = new SolidBrush(Color.FromArgb(0x5a, 0x34, 0x1c))) g.FillRectangle(b, r);
                    var st = g.Save();
                    g.TranslateTransform(r.X, r.Bottom);
                    g.RotateTransform(-90);
                    DrawWoodCheek(g, new Rectangle(0, 0, r.Height, r.Width), true);
                    g.Restore(st);
                    break;
                case 3:   // Gummi: matt schwarz mit feinen Rillen
                    using (var b = new SolidBrush(Color.FromArgb(0x16, 0x17, 0x19))) g.FillRectangle(b, r);
                    using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                        for (float y = r.Y + F(5); y < r.Bottom - F(3); y += F(4)) g.DrawLine(p, r.X, y, r.Right, y);
                    break;
                default:  // Alu
                    using (var b = new SolidBrush(Color.FromArgb(0x8a, 0x8f, 0x97))) g.FillRectangle(b, r);
                    FillBrushed(g, r);
                    break;
            }
            // Woelbung: oben Glanzkante, unten Schatten
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.Black, Color.Black, LinearGradientMode.Vertical))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(90, 0, 0, 0), Color.FromArgb(style == 3 ? 30 : 70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(130, 0, 0, 0) },
                    Positions = new[] { 0f, 0.12f, 0.35f, 0.6f, 1f },
                };
                g.FillRectangle(b, r);
            }
            if (style == 1)
                using (var p = new Pen(Color.FromArgb(110, 0xc8, 0xb0, 0x90), Math.Max(1f, F(1))) { DashPattern = new[] { 3f, 2f } })
                {
                    g.DrawLine(p, r.X, r.Y + F(4), r.Right, r.Y + F(4));
                    g.DrawLine(p, r.X, r.Bottom - F(5), r.Right, r.Bottom - F(5));
                }
            using (var p = new Pen(Color.FromArgb(160, 0, 0, 0), 1f)) g.DrawLine(p, r.X, r.Y, r.Right, r.Y);   // Fuge zum Pult
            g.SmoothingMode = sm;
        }
    }
}
