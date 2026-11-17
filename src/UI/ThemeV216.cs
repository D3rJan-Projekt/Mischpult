using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// 2.16 Designs: Schwarz-Gold (schwarz eloxiertes Metall, goldene Knoepfe und Anzeigen), Broadcast-Blau (ruhiges
    /// Blaugrau wie im Sendestudio) und Retro-Hi-Fi (gebuerstetes Alu, Holz, Chrom, bernsteinfarbene Anzeigen).
    /// Wie Vintage bauen sie auf einem der Grund-Stile auf und bringen eigene Farben und passende Teile mit.
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleGold = 6, StyleBroadcast = 7, StyleHifi = 8;
        /// <summary>Gewaehltes Design (0 … 8) – Style ist der Grund-Stil, nach dem gezeichnet wird.</summary>
        public static int Design;
        public static bool Gold => Design == StyleGold;
        public static bool Broadcast => Design == StyleBroadcast;
        public static bool Hifi => Design == StyleHifi;

        /// <summary>Grund-Stil, auf dem ein Design aufbaut.</summary>
        public static int BaseStyleOf(int design)
        {
            switch (design)
            {
                case StyleVintage: case StyleHifi: return StyleLight;
                case StyleGold: case StyleCarbon: case StyleBrass: case StyleConsole: case StyleIndustrial: case StyleCockpit: return StyleStudio;   // 4.0, 4.2
                case StyleGlass: case StyleSpace: case StyleSynth: return StyleModern;   // 4.0 finale, 4.2
                case StyleRetro: return StyleClassic;   // 4.2
                case StyleBroadcast: return StyleModern;
                default: return Math.Max(0, Math.Min(StyleLight, design));
            }
        }

        /// <summary>Oberflaeche der Zuege: gewaehlt oder passend zum Design (Retro-Hi-Fi: gebuerstetes Alu).</summary>
        public static int EffMaterial => PanelMaterial != 0 ? PanelMaterial : Hifi || IndustrialDesign ? 2 : Carbon ? 1 : Brass ? 3 : 0;
        /// <summary>Knoepfe: gewaehlt oder passend (Retro-Hi-Fi Chrom, Schwarz-Gold Gold, Broadcast Gummi). 5 = Gold.</summary>
        public static int EffKnob => KnobStyle != 0 ? KnobStyle : Hifi || SynthDesign ? 2 : Gold || Brass ? 5 : Broadcast || RetroDesign || CockpitDesign ? 3 : Carbon || BigConsole || IndustrialDesign ? 1 : 0;
        /// <summary>Faderkappen: gewaehlt oder passend (Retro-Hi-Fi Chrom, Schwarz-Gold Gold, Broadcast schwarz). 5 = Gold.</summary>
        public static int EffCap => CapStyle == 5 ? 6 : CapStyle != 0 ? CapStyle : Hifi ? 1 : Gold || Brass ? 5 : Broadcast || Carbon || IndustrialDesign || RetroDesign || CockpitDesign ? 2 : GlassDesign || SpaceDesign || SynthDesign ? 6 : 0;   // 4.0 finale: 6 = Glas

        private static readonly Color GoldLight = C(0xf3, 0xd9, 0x8b), GoldMid = C(0xd4, 0xa8, 0x4a), GoldDark = C(0x7a, 0x58, 0x1c);

        /// <summary>Farben fuers Design nach dem Grund-Stil (nach ApplyStyleColors).</summary>
        private static void DesignColors(int scheme)
        {
            if (Gold) GoldColors(scheme);
            else if (Broadcast) BroadcastColors(scheme);
            else if (Hifi) HifiColors(scheme);
        }

        /// <summary>Schwarz eloxiert, warme Lichter, goldene Schrift in den Anzeigen.</summary>
        private static void GoldColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x0a0b0e); top = C(0x24262c); bottom = C(0x121317); break;   // kuehler
                case 2: body = C(0x141210); top = C(0x302b25); bottom = C(0x1b1814); break;   // heller
                default: body = C(0x0c0b0a); top = C(0x2a2622); bottom = C(0x15130f); break;
            }
            Body = body; WindowTop = Lighten(body, 0.06f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Mix(Lighten(top, 0.22f), GoldDark, 0.35f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xf1, 0xe7, 0xd0); TextDim = C(0xb3, 0xa4, 0x87); TextFaint = C(0x7a, 0x6e, 0x58);
            MenuBg = Lighten(bottom, 0.05f); MenuHover = Mix(MenuBg, GoldDark, 0.35f); MenuBorder = Mix(Lighten(bottom, 0.2f), GoldDark, 0.5f);
            MenuSep = Lighten(bottom, 0.14f); MenuSepLight = MenuBg;
            KeyTop = Lighten(top, 0.08f); KeyBottom = Darken(bottom, 0.15f); KeyHoverTop = Lighten(top, 0.16f); KeyHoverBottom = bottom; KeyEdge = Darken(body, 0.4f);
            BtnTop = Lighten(top, 0.04f); BtnBottom = bottom; BtnHoverTop = Lighten(top, 0.12f); BtnHoverBottom = Lighten(bottom, 0.06f);
            BadgeBg = Lighten(bottom, 0.06f); BadgeEdge = GoldDark; LogoCap = GoldMid;
            ScaleTick = Mix(C(0x55, 0x50, 0x46), GoldDark, 0.4f); ScaleLabel = C(0xa8, 0x92, 0x66); EngraveText = C(0x6a, 0x5a, 0x3c);
            DispEdgeHover = GoldDark; ScrollThumbHover = GoldMid;
            Primary = C(0x8a, 0x6a, 0x22); PrimaryHover = C(0xa3, 0x7e, 0x2a);
            LcdBg = C(0x0d, 0x0a, 0x05); LcdText = GoldLight; ValueText = GoldMid;
        }

        /// <summary>Blaugrau, flach und ruhig, Anzeigen in hellem Cyan – wie im Sendestudio.</summary>
        private static void BroadcastColors(int scheme)
        {
            switch (scheme)
            {
                case 1: ModernFrom(C(0x111a26), C(0x152030), C(0x1e2b3d)); break;   // tiefer
                case 2: ModernFrom(C(0x222c37), C(0x283442), C(0x324053)); break;   // heller
                default: ModernFrom(C(0x17202b), C(0x1c2734), C(0x253242)); break;
            }
            Text = C(0xe3, 0xec, 0xf5); TextDim = C(0x93, 0xa6, 0xba); TextFaint = C(0x5e, 0x70, 0x84);
            ScaleTick = C(0x55, 0x66, 0x7a); ScaleLabel = C(0x86, 0x9a, 0xb0);
            LcdText = C(0x8f, 0xdc, 0xff); ValueText = C(0x8f, 0xdc, 0xff);
            Primary = C(0x2f, 0x6c, 0xa8); PrimaryHover = C(0x3a, 0x80, 0xc4);
        }

        /// <summary>Gebuerstetes Alu, Holz drumherum, schwarze Beschriftung, bernsteinfarbene Anzeigen.</summary>
        private static void HifiColors(int scheme)
        {
            Color top, bottom, edge, wood;
            switch (scheme)
            {
                case 1: top = C(0xe6e9ed); bottom = C(0xc8cdd3); edge = C(0x8a9099); wood = C(0x2a1c12); break;   // kuehler
                case 2: top = C(0xdcdcdc); bottom = C(0xbdbdbd); edge = C(0x7c7c7c); wood = C(0x22160d); break;   // dunkler
                default: top = C(0xe4e5e6); bottom = C(0xc6c8ca); edge = C(0x868a90); wood = C(0x2c1d12); break;
            }
            Body = wood; WindowTop = Lighten(top, 0.15f); WindowBottom = top; BarBg = Darken(top, 0.04f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Color.White; PanelEdge = edge;
            Text = C(0x15, 0x17, 0x1a); TextDim = C(0x45, 0x4a, 0x52); TextFaint = C(0x78, 0x7e, 0x87); TextHover = Color.Black;
            Well = Lighten(bottom, 0.5f); WellHover = Lighten(bottom, 0.7f); WellEdge = Darken(bottom, 0.25f);
            PlateRim = Darken(bottom, 0.3f); Hole = C(0x0e, 0x0f, 0x11); Slot = Darken(bottom, 0.38f); SegOff = C(0x17, 0x18, 0x1b);
            KeyTop = Color.White; KeyBottom = Darken(bottom, 0.02f); KeyHoverTop = Color.White; KeyHoverBottom = Lighten(bottom, 0.4f); KeyEdge = Darken(bottom, 0.34f);
            ScaleTick = C(0x7a, 0x80, 0x88); ScaleLabel = C(0x3c, 0x41, 0x48); EngraveText = Darken(bottom, 0.32f);
            BadgeBg = Darken(top, 0.08f); BadgeEdge = Darken(top, 0.22f); LogoCap = C(0x2a, 0x2d, 0x32);
            // Holz-Grund: Bedienelemente darauf hell
            ScrollTrack = Lighten(wood, 0.08f); ScrollThumb = C(0x8a, 0x6a, 0x48); ScrollThumbHover = C(0xc4, 0x9a, 0x66);
            AddIdle = C(0x9c, 0x82, 0x64); AddHoverBg = Lighten(wood, 0.06f);
            DivDark = Darken(wood, 0.5f); DivLight = Lighten(wood, 0.14f);
            LcdBg = C(0x0a, 0x08, 0x05); LcdText = C(0xff, 0xb3, 0x47);
            ValueText = C(0xa8, 0x5a, 0x00);
        }

        /// <summary>Goldener Knopf (wie Chrom, nur in Gold) mit dunklem Zeiger.</summary>
        private static void GoldKnobBody(Graphics g, PointF c, float radius, float ca, float sa, bool hover)
        {
            FillEllipse(g, Color.FromArgb(90, 0, 0, 0), c.X, c.Y + F(1.6f), radius + F(0.6f));
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, Color.White, Color.Black, 60f))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { C(0xfbeab0), C(0xb8862e), C(0xf3d98b), C(0x6e4c14), C(0xd9b25a), C(0x4a3209) },
                    Positions = new[] { 0f, 0.22f, 0.42f, 0.6f, 0.8f, 1f },
                };
                g.FillEllipse(b, outer);
            }
            float ir = radius * 0.74f;
            var inner = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            using (var b = new LinearGradientBrush(inner, Color.White, Color.Black, 240f))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { hover ? C(0xfff4cc) : C(0xf6e2a0), C(0xc9993c), C(0xfbeab8), C(0x8e6a26) },
                    Positions = new[] { 0f, 0.4f, 0.55f, 1f },
                };
                g.FillEllipse(b, inner);
            }
            using (var pen = new Pen(Color.FromArgb(170, 0x2a, 0x1c, 0x06), F(1))) g.DrawEllipse(pen, outer);
            using (var pen = new Pen(Color.FromArgb(90, 0x4a, 0x32, 0x09), F(0.8f))) g.DrawEllipse(pen, inner);
            using (var pen = new Pen(C(0x1a, 0x12, 0x05), F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X + ca * ir * 0.2f, c.Y + sa * ir * 0.2f, c.X + ca * radius * 0.92f, c.Y + sa * radius * 0.92f);
        }
    }
}
