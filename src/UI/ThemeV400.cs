using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0 Designs: "Carbon" (Kohlefaser-Zuege, schwarze Kappen, gerändelte Knoepfe, rote Anzeigen) und
    /// "Holz &amp; Messing" (dunkles Nussbaum, Messing-Knoepfe und -Kappen, Nixie-Anzeigen). Beide bauen auf Studio auf.
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleCarbon = 9, StyleBrass = 10;
        public static bool Carbon => Design == StyleCarbon;
        public static bool Brass => Design == StyleBrass;
        /// <summary>Hoechste Design-Nummer.</summary>
        public const int LastDesign = StyleJapan;   // 4.3: 18 Steampunk, 19 Arktis, 20 Japan
        // 4.0 finale: 11 Grosskonsole … 14 Weltraum; 4.2: 15 Synthwave, 16 Retro-Game, 17 Cockpit

        private static void Designs400(int scheme)
        {
            if (Carbon) CarbonColors(scheme);
            else if (Brass) BrassColors(scheme);
        }

        /// <summary>Kohlefaser: tiefes Graphit, kuehles Weiss, rote Anzeigen wie im Rennwagen.</summary>
        private static void CarbonColors(int scheme)
        {
            Color body, top, bottom, red;
            switch (scheme)
            {
                case 1: body = C(0x0b0d12); top = C(0x252a33); bottom = C(0x15181e); red = C(0xff, 0x55, 0x48); break;
                case 2: body = C(0x17181a); top = C(0x33353a); bottom = C(0x202226); red = C(0xff, 0x5a, 0x4a); break;
                default: body = C(0x0d0e10); top = C(0x2a2c30); bottom = C(0x17181b); red = C(0xff, 0x4d, 0x3d); break;
            }
            Body = body; WindowTop = Lighten(body, 0.07f); WindowBottom = body; BarBg = Darken(body, 0.2f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Lighten(top, 0.22f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xee, 0xf0, 0xf3); TextDim = C(0xa2, 0xa8, 0xb1); TextFaint = C(0x6c, 0x72, 0x7b);
            MenuBg = Lighten(bottom, 0.05f); MenuHover = Mix(MenuBg, red, 0.18f); MenuBorder = Lighten(bottom, 0.2f);
            MenuSep = Lighten(bottom, 0.14f); MenuSepLight = MenuBg;
            KeyTop = Lighten(top, 0.06f); KeyBottom = Darken(bottom, 0.2f); KeyHoverTop = Lighten(top, 0.14f); KeyHoverBottom = bottom; KeyEdge = Darken(body, 0.5f);
            BadgeBg = Lighten(bottom, 0.06f); BadgeEdge = Mix(Lighten(bottom, 0.2f), red, 0.4f); LogoCap = red;
            ScaleTick = C(0x5c, 0x62, 0x6c); ScaleLabel = C(0x96, 0x9d, 0xa8); EngraveText = C(0x5a, 0x60, 0x6a);
            DispEdgeHover = red; ScrollThumbHover = red;
            Primary = C(0xa0, 0x2a, 0x20); PrimaryHover = C(0xbc, 0x34, 0x28);
            LcdBg = C(0x0a, 0x05, 0x05); LcdText = red; ValueText = C(0xff, 0x7a, 0x6a);
        }

        /// <summary>Holz &amp; Messing: dunkles Nussbaum, cremige Schrift, warmes Messing und Nixie-Glimmen.</summary>
        private static void BrassColors(int scheme)
        {
            Color body, top, bottom;
            switch (scheme)
            {
                case 1: body = C(0x16100c); top = C(0x3e2c1f); bottom = C(0x261a12); break;   // dunkler
                case 2: body = C(0x231810); top = C(0x5a4030); bottom = C(0x3a281c); break;   // heller (Kirsche)
                default: body = C(0x1a120c); top = C(0x4a3424); bottom = C(0x2d1f15); break;
            }
            Color brass = C(0xd8, 0xb0, 0x5a), brassDark = C(0x8a, 0x66, 0x26);
            Body = body; WindowTop = Lighten(body, 0.08f); WindowBottom = body; BarBg = Darken(body, 0.15f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Mix(Lighten(top, 0.25f), brass, 0.3f); PanelEdge = Darken(body, 0.5f);
            Text = C(0xf4, 0xe9, 0xd2); TextDim = C(0xc9, 0xb3, 0x8e); TextFaint = C(0x8e, 0x78, 0x58);
            Well = Darken(bottom, 0.45f); WellHover = Darken(bottom, 0.3f); WellEdge = Darken(body, 0.4f);
            MenuBg = Lighten(bottom, 0.04f); MenuHover = Mix(MenuBg, brassDark, 0.4f); MenuBorder = Mix(Lighten(bottom, 0.2f), brassDark, 0.5f);
            MenuSep = Lighten(bottom, 0.14f); MenuSepLight = MenuBg;
            KeyTop = Lighten(top, 0.1f); KeyBottom = Darken(bottom, 0.25f); KeyHoverTop = Lighten(top, 0.18f); KeyHoverBottom = bottom; KeyEdge = Darken(body, 0.45f);
            BtnTop = Lighten(top, 0.04f); BtnBottom = bottom; BtnHoverTop = Lighten(top, 0.12f); BtnHoverBottom = Lighten(bottom, 0.06f);
            DispBg = Darken(bottom, 0.4f); DispHover = Darken(bottom, 0.25f);
            BadgeBg = Darken(bottom, 0.15f); BadgeEdge = brassDark; LogoCap = brass;
            ScaleTick = Mix(C(0x6a, 0x58, 0x44), brassDark, 0.5f); ScaleLabel = C(0xc2, 0xa2, 0x70); EngraveText = C(0x8a, 0x70, 0x48);
            DispEdgeHover = brass; ScrollThumbHover = brass;
            Primary = C(0x8a, 0x66, 0x26); PrimaryHover = C(0xa0, 0x78, 0x2e);
            Input = Darken(bottom, 0.35f); InputFocus = Darken(bottom, 0.15f);
            Deep = Darken(body, 0.3f); Grid = Lighten(body, 0.1f); GridZero = Lighten(body, 0.2f);
            LcdBg = C(0x0c, 0x07, 0x04); LcdText = C(0xff, 0x9a, 0x3c); ValueText = brass;
        }
    }
}
