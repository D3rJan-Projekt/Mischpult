using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// Design-Stile: Studio (Metall), Klassisch (flach, wie frueher), Modern (Windows 11), Neon (Gaming), Hell.
    /// Unabhaengig von Farbschema und Akzentfarbe. Alle Farben, die sich je Stil aendern, stehen hier als Tokens.
    /// </summary>
    internal static partial class Theme
    {
        public const int StyleStudio = 0, StyleClassic = 1, StyleModern = 2, StyleNeon = 3, StyleLight = 4;
        /// <summary>Nur als Auswahl (UiStyle): "Vintage" zeichnet wie "Hell", aber in warmen Farben mit Holz und Bakelit.</summary>
        public const int StyleVintage = 5;
        public static readonly string[] StyleNames = { "Studio (Metall)", "Klassisch (flach)", "Modern", "Neon", "Hell", "Vintage", "Schwarz-Gold", "Broadcast-Blau", "Retro-Hi-Fi", "Carbon", "Holz & Messing", "Großkonsole", "Glas & Licht", "Industrial", "Weltraum", "Synthwave", "Retro-Game", "Cockpit" };
        public static readonly string[] StyleHints =
        {
            "gebürstetes Metall, Schrauben, Glas",
            "schlicht wie früher, ohne Metall",
            "flach und ruhig, wie Windows 11",
            "dunkel, Leuchtkanten in Kanalfarbe",
            "helle Oberfläche, dunkle Schrift",
            "70er-Jahre-Pult: Holz, Creme, Bakelit-Knöpfe",
            "schwarz eloxiertes Metall, goldene Knöpfe und Anzeigen",
            "ruhiges Blaugrau wie im Sendestudio, Anzeigen in Cyan",
            "Hi-Fi-Verstärker der 80er: gebürstetes Alu, Holz, Chrom, Bernstein-Anzeigen",
            "Kohlefaser, schwarze Kappen, gerändelte Knöpfe, rote Anzeigen",
            "dunkles Nussbaum, Messing-Knöpfe und -Kappen, Nixie-Anzeigen",
            "graublau wie die große Studiokonsole: farbige Knopfkappen je Funktion, weiße Beschriftungsstreifen",
            "mattes Acryl, die Kanten leuchten in der Kanalfarbe, Glas-Kappen",
            "Stahl, Nieten, gelb-schwarze Warnstreifen, MUTE als Kippschalter mit Schutzkappe",
            "Nebel und Sterne im Hintergrund, blau glühende Kanten",
            "80er-Neon: violetter Sonnenuntergang, Gitter am Boden, Kanten in Pink und Cyan",
            "Pixel-Look wie alte Spielkonsolen: Nachtblau, Stufen-Ecken, Punktmatrix-Anzeigen",
            "wie im Flugzeug: matt lackiert, weiße Umrandung, Schnellverschlüsse, grüne Anzeigen",
        };

        /// <summary>Aktueller Design-Stil (0..4).</summary>
        public static int Style = StyleStudio;
        public static bool IsLight => Style == StyleLight;
        /// <summary>Ecken der Kanalzuege.</summary>
        public static float PanelRad => F(Style == StyleModern || Style == StyleLight ? 10f : Style == StyleNeon ? 8f : 7f);

        // ---------------------------------------------------------------- Tokens (Standard = Studio, dunkel)
        /// <summary>Werte-Text auf normalem Hintergrund (nicht im LCD), z. B. Balance "L20".</summary>
        public static Color ValueText = Color.FromArgb(0xff, 0xb5, 0x47);
        /// <summary>Text unter dem Mauszeiger (dunkel: weiss).</summary>
        public static Color TextHover = Color.White;
        /// <summary>Gruener Text "alles gut" (auf hellem Grund dunkler).</summary>
        public static Color OkText => IsLight ? Color.FromArgb(0x1e, 0x8a, 0x48) : LedGreen;
        /// <summary>Akzentfarbe als Schrift (auf hellem Grund kraeftiger).</summary>
        public static Color AccentText => IsLight ? ValueText : Accent;
        /// <summary>Gelber Hinweis-Text (auf hellem Grund dunkler).</summary>
        public static Color WarnText => IsLight ? Color.FromArgb(0xa8, 0x6a, 0x00) : SoloAmber;
        /// <summary>Vertiefte Felder im Zug (Apps, Geraet, Gruppe).</summary>
        public static Color Well = Color.FromArgb(0x11, 0x13, 0x16), WellHover = Color.FromArgb(0x16, 0x19, 0x1e), WellEdge = Color.FromArgb(0x06, 0x07, 0x08);
        public static Color PlateRim = Color.FromArgb(0x05, 0x06, 0x07);
        /// <summary>Hintergrund der Pegel (bleibt auch bei Hell dunkel).</summary>
        public static Color Hole = Color.FromArgb(0x05, 0x06, 0x07);
        public static Color Slot = Color.FromArgb(0x05, 0x06, 0x07);
        public static Color SegOff = Color.FromArgb(0x10, 0x12, 0x14);
        public static Color Chip = Color.FromArgb(0x2a, 0x2e, 0x35);
        public static Color Disabled = Color.FromArgb(0x6a, 0x72, 0x7d), RingOff = Color.FromArgb(0x3a, 0x3f, 0x47);
        public static Color ScaleTick = Color.FromArgb(0x55, 0x5c, 0x66), ScaleLabel = Color.FromArgb(0x7c, 0x85, 0x91);
        public static Color EngraveText = Color.FromArgb(0x4a, 0x50, 0x59);
        public static Color KeyTop = Color.FromArgb(0x3e, 0x42, 0x4a), KeyBottom = Color.FromArgb(0x21, 0x24, 0x29);
        public static Color KeyHoverTop = Color.FromArgb(0x4b, 0x50, 0x59), KeyHoverBottom = Color.FromArgb(0x2b, 0x2e, 0x34), KeyEdge = Color.FromArgb(0x09, 0x0a, 0x0c);
        public static Color DispBg = Color.FromArgb(0x12, 0x14, 0x18), DispHover = Color.FromArgb(0x1c, 0x1f, 0x25);
        public static Color DispEdge = Color.FromArgb(0x05, 0x06, 0x07), DispEdgeHover = Color.FromArgb(0x4a, 0x50, 0x5a);
        public static Color BtnTop = Color.FromArgb(0x30, 0x34, 0x3b), BtnBottom = Color.FromArgb(0x22, 0x25, 0x2a);
        public static Color BtnHoverTop = Color.FromArgb(0x3c, 0x41, 0x49), BtnHoverBottom = Color.FromArgb(0x2a, 0x2e, 0x34), BtnEdge = Color.FromArgb(0x06, 0x07, 0x08);
        public static Color BadgeBg = Color.FromArgb(0x24, 0x27, 0x2d), BadgeEdge = Color.FromArgb(0x38, 0x3d, 0x45), LogoCap = Color.FromArgb(0xc9, 0xcd, 0xd3);
        public static Color ScrollTrack = Color.FromArgb(0x1c, 0x1f, 0x24), ScrollThumb = Color.FromArgb(0x44, 0x4a, 0x53), ScrollThumbHover = Color.FromArgb(0x6a, 0x72, 0x7d);
        public static Color AddIdle = Color.FromArgb(0x3c, 0x42, 0x4b), AddHoverBg = Color.FromArgb(0x1a, 0x1c, 0x21);
        public static Color DivDark = Color.FromArgb(0x06, 0x07, 0x08), DivLight = Color.FromArgb(0x26, 0x29, 0x2f);
        public static Color PopupBg = Color.FromArgb(0x1a, 0x1d, 0x22), PopupHeadTop = Color.FromArgb(0x26, 0x2a, 0x30), PopupHeadBottom = Color.FromArgb(0x1d, 0x20, 0x25);
        public static Color PopupHover = Color.FromArgb(0x22, 0x26, 0x2c), PopupLine = Color.FromArgb(0x26, 0x29, 0x2f);
        /// <summary>Tiefe Flaechen in Fenstern (EQ-Grafik, Regler-Bahnen).</summary>
        public static Color Deep = Color.FromArgb(0x0b, 0x0c, 0x0e), Grid = Color.FromArgb(0x22, 0x26, 0x2d), GridZero = Color.FromArgb(0x3a, 0x40, 0x49);
        public static Color Input = Color.FromArgb(0x12, 0x14, 0x18), InputFocus = Color.FromArgb(0x2a, 0x2e, 0x35), Inset = Color.FromArgb(0x1b, 0x1e, 0x23);
        public static Color Button = Color.FromArgb(0x33, 0x37, 0x3e), ButtonHover = Color.FromArgb(0x42, 0x47, 0x50), ButtonEdge = Color.FromArgb(0x10, 0x11, 0x13), ButtonText = Color.White;
        public static Color Primary = Color.FromArgb(0x3a, 0x6e, 0x4c), PrimaryHover = Color.FromArgb(0x47, 0x86, 0x5c);
        public static Color Track = Color.FromArgb(0x33, 0x37, 0x3e);
        public static Color MenuSep = Color.FromArgb(0x3a, 0x3f, 0x47), MenuSepLight = Color.FromArgb(0x22, 0x25, 0x2b);
        public static Color CheckBg = Color.FromArgb(0x3a, 0x3f, 0x47), CheckSelBg = Color.FromArgb(0x4a, 0x50, 0x5a);
        public static Color HoverFill = Color.FromArgb(0x3a, 0x3f, 0x47);
        public static Color PadTop = Color.FromArgb(0x33, 0x37, 0x3e), PadBottom = Color.FromArgb(0x25, 0x28, 0x2e);
        public static Color PadHoverTop = Color.FromArgb(0x3e, 0x43, 0x4c), PadHoverBottom = Color.FromArgb(0x2c, 0x30, 0x36);
        public static Color FootBtn = Color.FromArgb(0x26, 0x2a, 0x30), FootBtnHover = Color.FromArgb(0x35, 0x3a, 0x43), FootBtnEdge = Color.FromArgb(0x0c, 0x0d, 0x10);

        private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);
        private static Color C(int rgb) => Color.FromArgb(0xff, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);

        /// <summary>Alle Tokens auf die dunklen Studio-Werte.</summary>
        private static void DarkTokens()
        {
            Text = C(0xe8, 0xeb, 0xef); TextDim = C(0x8f, 0x98, 0xa4); TextFaint = C(0x5a, 0x62, 0x6d); TextHover = Color.White;
            PanelEdge = C(0x07, 0x08, 0x0a); LcdBg = C(0x0a, 0x0c, 0x0f);
            Well = C(0x11, 0x13, 0x16); WellHover = C(0x16, 0x19, 0x1e); WellEdge = C(0x06, 0x07, 0x08);
            PlateRim = C(0x05, 0x06, 0x07); Hole = C(0x05, 0x06, 0x07); Slot = C(0x05, 0x06, 0x07); SegOff = C(0x10, 0x12, 0x14);
            Chip = C(0x2a, 0x2e, 0x35); Disabled = C(0x6a, 0x72, 0x7d); RingOff = C(0x3a, 0x3f, 0x47);
            ScaleTick = C(0x55, 0x5c, 0x66); ScaleLabel = C(0x7c, 0x85, 0x91); EngraveText = C(0x4a, 0x50, 0x59);
            KeyTop = C(0x3e, 0x42, 0x4a); KeyBottom = C(0x21, 0x24, 0x29); KeyHoverTop = C(0x4b, 0x50, 0x59); KeyHoverBottom = C(0x2b, 0x2e, 0x34); KeyEdge = C(0x09, 0x0a, 0x0c);
            DispBg = C(0x12, 0x14, 0x18); DispHover = C(0x1c, 0x1f, 0x25); DispEdge = C(0x05, 0x06, 0x07); DispEdgeHover = C(0x4a, 0x50, 0x5a);
            BtnTop = C(0x30, 0x34, 0x3b); BtnBottom = C(0x22, 0x25, 0x2a); BtnHoverTop = C(0x3c, 0x41, 0x49); BtnHoverBottom = C(0x2a, 0x2e, 0x34); BtnEdge = C(0x06, 0x07, 0x08);
            BadgeBg = C(0x24, 0x27, 0x2d); BadgeEdge = C(0x38, 0x3d, 0x45); LogoCap = C(0xc9, 0xcd, 0xd3);
            ScrollTrack = C(0x1c, 0x1f, 0x24); ScrollThumb = C(0x44, 0x4a, 0x53); ScrollThumbHover = C(0x6a, 0x72, 0x7d);
            AddIdle = C(0x3c, 0x42, 0x4b); AddHoverBg = C(0x1a, 0x1c, 0x21);
            DivDark = C(0x06, 0x07, 0x08); DivLight = C(0x26, 0x29, 0x2f);
            PopupBg = C(0x1a, 0x1d, 0x22); PopupHeadTop = C(0x26, 0x2a, 0x30); PopupHeadBottom = C(0x1d, 0x20, 0x25); PopupHover = C(0x22, 0x26, 0x2c); PopupLine = C(0x26, 0x29, 0x2f);
            Deep = C(0x0b, 0x0c, 0x0e); Grid = C(0x22, 0x26, 0x2d); GridZero = C(0x3a, 0x40, 0x49);
            Input = C(0x12, 0x14, 0x18); InputFocus = C(0x2a, 0x2e, 0x35); Inset = C(0x1b, 0x1e, 0x23);
            Button = C(0x33, 0x37, 0x3e); ButtonHover = C(0x42, 0x47, 0x50); ButtonEdge = C(0x10, 0x11, 0x13); ButtonText = Color.White;
            Primary = C(0x3a, 0x6e, 0x4c); PrimaryHover = C(0x47, 0x86, 0x5c); Track = C(0x33, 0x37, 0x3e);
            MenuSep = C(0x3a, 0x3f, 0x47); MenuSepLight = C(0x22, 0x25, 0x2b); CheckBg = C(0x3a, 0x3f, 0x47); CheckSelBg = C(0x4a, 0x50, 0x5a);
            HoverFill = C(0x3a, 0x3f, 0x47);
            PadTop = C(0x33, 0x37, 0x3e); PadBottom = C(0x25, 0x28, 0x2e); PadHoverTop = C(0x3e, 0x43, 0x4c); PadHoverBottom = C(0x2c, 0x30, 0x36);
            FootBtn = C(0x26, 0x2a, 0x30); FootBtnHover = C(0x35, 0x3a, 0x43); FootBtnEdge = C(0x0c, 0x0d, 0x10);
        }

        /// <summary>Farben je Stil (nach dem Farbschema). Studio und Klassisch nutzen das Schema unveraendert.</summary>
        private static void ApplyStyleColors(int scheme, int accent)
        {
            switch (Style)
            {
                case StyleModern: ModernColors(scheme); break;
                case StyleNeon: NeonColors(scheme); break;
                case StyleLight: LightColors(scheme, accent); break;
            }
        }

        /// <summary>Flache Flaechen, feine helle Raender (wie Windows 11).</summary>
        private static void ModernColors(int scheme)
        {
            switch (scheme)
            {
                case 1: ModernFrom(C(0x151a25), C(0x1a2030), C(0x232a3a)); break;
                case 2: ModernFrom(C(0x262729), C(0x2c2d30), C(0x37393d)); break;
                default: ModernFrom(C(0x1b1c1f), C(0x212226), C(0x2b2c30)); break;
            }
        }

        /// <summary>Alle Farben des flachen Stils aus Grund, Leiste und Zug (auch fuer Broadcast-Blau).</summary>
        private static void ModernFrom(Color body, Color bar, Color panel)
        {
            Body = body; WindowTop = bar; WindowBottom = body; BarBg = bar;
            PanelTop = Lighten(panel, 0.015f); PanelBottom = panel; PanelHighlight = Lighten(panel, 0.11f); PanelEdge = Darken(body, 0.3f);
            MenuBg = Lighten(panel, 0.02f); MenuHover = Lighten(panel, 0.09f); MenuBorder = Lighten(panel, 0.15f);
            MenuSep = Lighten(panel, 0.12f); MenuSepLight = MenuBg; CheckBg = Lighten(panel, 0.12f); CheckSelBg = Lighten(panel, 0.2f);
            Well = Darken(panel, 0.28f); WellHover = Darken(panel, 0.16f); WellEdge = Lighten(panel, 0.05f);
            DispBg = Lighten(panel, 0.05f); DispHover = Lighten(panel, 0.1f); DispEdge = Lighten(panel, 0.12f); DispEdgeHover = Lighten(panel, 0.26f);
            KeyTop = KeyBottom = Lighten(panel, 0.07f); KeyHoverTop = KeyHoverBottom = Lighten(panel, 0.13f); KeyEdge = Lighten(panel, 0.14f);
            BtnTop = BtnBottom = Lighten(bar, 0.07f); BtnHoverTop = BtnHoverBottom = Lighten(bar, 0.13f); BtnEdge = Lighten(bar, 0.14f);
            BadgeBg = Lighten(bar, 0.05f); BadgeEdge = Lighten(bar, 0.12f);
            Chip = Lighten(panel, 0.1f); Hole = Darken(body, 0.45f); Slot = Darken(panel, 0.42f); SegOff = Darken(panel, 0.55f);
            RingOff = Lighten(panel, 0.12f); EngraveText = Lighten(panel, 0.16f);
            ScrollTrack = Lighten(body, 0.04f); ScrollThumb = Lighten(body, 0.2f); ScrollThumbHover = Lighten(body, 0.36f);
            AddIdle = Lighten(body, 0.18f); AddHoverBg = Lighten(body, 0.04f);
            DivDark = Lighten(body, 0.07f); DivLight = body;
            PopupBg = panel; PopupHeadTop = PopupHeadBottom = Lighten(panel, 0.04f); PopupHover = Lighten(panel, 0.06f); PopupLine = Lighten(panel, 0.08f);
            Deep = Darken(panel, 0.35f); Grid = Lighten(Deep, 0.1f); GridZero = Lighten(Deep, 0.22f);
            Input = Darken(panel, 0.22f); InputFocus = Lighten(panel, 0.08f); Inset = Darken(panel, 0.1f);
            Button = Lighten(panel, 0.08f); ButtonHover = Lighten(panel, 0.15f); ButtonEdge = Lighten(panel, 0.16f);
            Track = Lighten(panel, 0.12f); HoverFill = Lighten(panel, 0.1f);
            PadTop = PadBottom = Lighten(panel, 0.06f); PadHoverTop = PadHoverBottom = Lighten(panel, 0.11f);
            FootBtn = Lighten(panel, 0.06f); FootBtnHover = Lighten(panel, 0.12f); FootBtnEdge = Lighten(panel, 0.14f);
            LcdBg = Darken(panel, 0.45f);
        }

        /// <summary>Fast schwarz, Leuchtkanten in Kanalfarbe.</summary>
        private static void NeonColors(int scheme)
        {
            Color body, bar, panel;
            switch (scheme)
            {
                case 1: body = C(0x05070f); bar = C(0x04050b); panel = C(0x0b0f1c); break;
                case 2: body = C(0x0c0c0e); bar = C(0x09090b); panel = C(0x151619); break;
                default: body = C(0x060709); bar = C(0x050607); panel = C(0x0d0f12); break;
            }
            Body = body; WindowTop = bar; WindowBottom = body; BarBg = bar;
            PanelTop = Lighten(panel, 0.02f); PanelBottom = Darken(panel, 0.2f); PanelHighlight = Lighten(panel, 0.1f); PanelEdge = Color.Black;
            MenuBg = Lighten(panel, 0.03f); MenuHover = Lighten(panel, 0.1f); MenuBorder = Alpha(Accent, 255);
            MenuSep = Lighten(panel, 0.12f); MenuSepLight = MenuBg; CheckBg = Lighten(panel, 0.12f); CheckSelBg = Lighten(panel, 0.2f);
            Well = Darken(panel, 0.5f); WellHover = Lighten(panel, 0.03f); WellEdge = Lighten(panel, 0.1f);
            DispBg = Darken(panel, 0.45f); DispHover = Lighten(panel, 0.04f); DispEdge = Lighten(panel, 0.13f); DispEdgeHover = Accent;
            KeyTop = KeyBottom = Darken(panel, 0.35f); KeyHoverTop = KeyHoverBottom = Lighten(panel, 0.05f); KeyEdge = Lighten(panel, 0.12f);
            BtnTop = BtnBottom = Lighten(bar, 0.04f); BtnHoverTop = BtnHoverBottom = Lighten(bar, 0.1f); BtnEdge = Lighten(bar, 0.14f);
            BadgeBg = Lighten(bar, 0.04f); BadgeEdge = Alpha(Accent, 255);
            Chip = Lighten(panel, 0.1f); Hole = Color.Black; Slot = Color.Black; SegOff = Lighten(panel, 0.02f);
            RingOff = Lighten(panel, 0.1f); EngraveText = Lighten(panel, 0.14f);
            ScrollTrack = Lighten(body, 0.04f); ScrollThumb = Darken(Accent, 0.45f); ScrollThumbHover = Accent;
            AddIdle = Lighten(body, 0.2f); AddHoverBg = Lighten(body, 0.03f);
            DivDark = Darken(Accent, 0.6f); DivLight = body;
            PopupBg = panel; PopupHeadTop = PopupHeadBottom = Lighten(panel, 0.04f); PopupHover = Lighten(panel, 0.06f); PopupLine = Lighten(panel, 0.08f);
            Deep = Color.Black; Grid = Lighten(panel, 0.08f); GridZero = Lighten(panel, 0.2f);
            Input = Darken(panel, 0.4f); InputFocus = Lighten(panel, 0.08f); Inset = Darken(panel, 0.2f);
            Button = Lighten(panel, 0.07f); ButtonHover = Lighten(panel, 0.14f); ButtonEdge = Lighten(panel, 0.18f);
            Track = Lighten(panel, 0.12f); HoverFill = Lighten(panel, 0.1f);
            PadTop = PadBottom = Lighten(panel, 0.04f); PadHoverTop = PadHoverBottom = Lighten(panel, 0.09f);
            FootBtn = Lighten(panel, 0.05f); FootBtnHover = Lighten(panel, 0.11f); FootBtnEdge = Lighten(panel, 0.14f);
            LcdBg = Color.Black;
        }

        /// <summary>Helle Oberflaeche, dunkle Schrift. Pegel und LCDs bleiben dunkel (wie bei echter Hardware).</summary>
        private static void LightColors(int scheme, int accent)
        {
            Color body, top, bottom, edge;
            switch (scheme)
            {
                case 1: body = C(0xcbd3e0); top = C(0xf6f9fd); bottom = C(0xe2e8f2); edge = C(0xa7b2c5); break;
                case 2: body = C(0xd7d4ce); top = C(0xf9f8f5); bottom = C(0xe9e6e0); edge = C(0xb8b3ab); break;
                default: body = C(0xd3d6db); top = C(0xf8f9fa); bottom = C(0xe7e9ed); edge = C(0xb3b8c0); break;
            }
            Body = body; WindowTop = top; WindowBottom = body; BarBg = Darken(top, 0.06f);
            PanelTop = top; PanelBottom = bottom; PanelHighlight = Color.White; PanelEdge = edge;
            Text = C(0x1b1e23); TextDim = C(0x4f5761); TextFaint = C(0x7f8792); TextHover = Color.Black;
            MenuBg = Lighten(top, 0.4f); MenuHover = Darken(MenuBg, 0.08f); MenuBorder = Darken(MenuBg, 0.24f);
            MenuSep = Darken(MenuBg, 0.13f); MenuSepLight = MenuBg; CheckBg = Darken(MenuBg, 0.1f); CheckSelBg = Darken(MenuBg, 0.18f);
            Well = C(0xfdfdfe); WellHover = Lighten(bottom, 0.55f); WellEdge = Darken(bottom, 0.2f);
            PlateRim = Darken(bottom, 0.24f); Hole = C(0x16181c); Slot = Darken(bottom, 0.28f); SegOff = C(0x1d2025);
            Chip = Darken(bottom, 0.1f); Disabled = C(0x9aa1ab); RingOff = Darken(bottom, 0.17f);
            ScaleTick = C(0x9aa1ab); ScaleLabel = C(0x69717c); EngraveText = Darken(bottom, 0.22f);
            KeyTop = Color.White; KeyBottom = Darken(bottom, 0.03f); KeyHoverTop = Color.White; KeyHoverBottom = Lighten(bottom, 0.45f); KeyEdge = Darken(bottom, 0.28f);
            DispBg = Color.White; DispHover = Lighten(bottom, 0.6f); DispEdge = Darken(bottom, 0.2f); DispEdgeHover = Darken(bottom, 0.42f);
            BtnTop = Color.White; BtnBottom = Darken(top, 0.05f); BtnHoverTop = Color.White; BtnHoverBottom = Lighten(top, 0.3f); BtnEdge = Darken(top, 0.26f);
            BadgeBg = Darken(top, 0.1f); BadgeEdge = Darken(top, 0.2f); LogoCap = C(0x5a616b);
            ScrollTrack = Darken(body, 0.06f); ScrollThumb = Darken(body, 0.25f); ScrollThumbHover = Darken(body, 0.42f);
            AddIdle = Darken(body, 0.3f); AddHoverBg = Lighten(body, 0.35f);
            DivDark = Darken(body, 0.18f); DivLight = Lighten(body, 0.55f);
            PopupBg = Lighten(bottom, 0.35f); PopupHeadTop = Color.White; PopupHeadBottom = bottom; PopupHover = Darken(PopupBg, 0.05f); PopupLine = Darken(PopupBg, 0.1f);
            Deep = Darken(bottom, 0.1f); Grid = Darken(bottom, 0.2f); GridZero = Darken(bottom, 0.36f);
            Input = Color.White; InputFocus = C(0xe2eaf6); Inset = Darken(MenuBg, 0.03f);
            Button = Darken(MenuBg, 0.07f); ButtonHover = Darken(MenuBg, 0.14f); ButtonEdge = Darken(MenuBg, 0.26f); ButtonText = Text;
            Primary = C(0x2f8a52); PrimaryHover = C(0x37a160);
            Track = Darken(MenuBg, 0.15f); HoverFill = Darken(PopupBg, 0.1f);
            PadTop = Color.White; PadBottom = Darken(MenuBg, 0.06f); PadHoverTop = Color.White; PadHoverBottom = Darken(MenuBg, 0.02f);
            FootBtn = Darken(PopupBg, 0.05f); FootBtnHover = Darken(PopupBg, 0.12f); FootBtnEdge = Darken(PopupBg, 0.25f);
            LcdBg = C(0x1a1d22);
            // Akzent auf hellem Grund etwas kraeftiger, Weiss wird Schiefer (im LCD bleibt die echte Farbe)
            Accent = accent == 4 ? C(0x3a414c) : Darken(Accent, 0.14f);
            ValueText = accent == 4 ? C(0x2b3038) : Darken(LcdText, 0.36f);
        }

        // ---------------------------------------------------------------- Zug-Bausteine (alle Stile)

        /// <summary>
        /// Oberer Streifen (Hoehe h) eines abgerundeten Rechtecks als Pfad. Wird als Flaeche gefuellt statt per Clipping
        /// ausgeschnitten – Clipping ist in Windows nie geglaettet, die runden Ecken bekaemen sonst Treppenstufen.
        /// </summary>
        public static GraphicsPath RoundTopBand(RectangleF r, float rad, float h)
        {
            var p = new GraphicsPath();
            h = Math.Max(0.5f, Math.Min(h, r.Height));
            rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
            float d = rad * 2f, k = Math.Min(h, rad);
            float a = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, (rad - k) / rad))) * 180.0 / Math.PI);
            float s = 180f + a;
            if (h > rad) p.AddLine(r.X, r.Y + h, r.X, r.Y + rad);
            p.AddArc(r.X, r.Y, d, d, s, 270f - s);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f - a);
            if (h > rad) p.AddLine(r.Right, r.Y + rad, r.Right, r.Y + h);
            p.CloseFigure();
            return p;
        }

        /// <summary>Farbige Kante oben am Zug. striped = Gruppe (schraeg gestreift).</summary>
        public static void DrawColorEdge(Graphics g, RectangleF panel, Color col, bool striped = false)
        {
            float rad = PanelRad;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Style == StyleNeon)
            {
                using (var path = RoundTopBand(panel, rad, F(16)))
                using (var b = new LinearGradientBrush(new RectangleF(panel.X, panel.Y - 1, panel.Width, F(16) + 2), Alpha(col, 80), Alpha(col, 0), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (var path = RoundTopBand(panel, rad, F(2)))
                using (var b = new SolidBrush(col)) g.FillPath(b, path);
            }
            else
            {
                using (var path = RoundTopBand(panel, rad, F(Style == StyleModern ? 3 : 4)))
                using (var b = new SolidBrush(col)) g.FillPath(b, path);
            }
            if (striped)
            {
                // Streifen nur im geraden Teil (ohne die runden Ecken) – dort reicht ein rechteckiger Ausschnitt
                var st = g.Save();
                g.SetClip(new RectangleF(panel.X + rad, panel.Y, panel.Width - rad * 2, F(12)), CombineMode.Intersect);
                using (var pen = new Pen(Alpha(col, 70), F(2)))
                    for (float sx = panel.X - F(10); sx < panel.Right; sx += F(9))
                        g.DrawLine(pen, sx, panel.Y + F(10), sx + F(6), panel.Y + F(4));
                g.Restore(st);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Fassung um das Namensschild.</summary>
        public static void DrawPlateRim(Graphics g, RectangleF nr)
        {
            if (Style == StyleStudio || Style == StyleClassic)
                FillRound(g, PlateRim, RectangleF.Inflate(nr, F(1.5f), F(1.5f)), F(4.5f));
            else if (Style == StyleLight)
                FillRound(g, Alpha(PlateRim, 150), RectangleF.Inflate(nr, F(1), F(1)), F(6));
        }

        /// <summary>Zusaetzlicher Glanz auf dem Namensschild der Kanaele (nur Metall/Klassisch).</summary>
        public static void DrawNameGloss(Graphics g, RectangleF nr)
        {
            if (TapePlates || DigitalPlates || DymoPlates || (Style != StyleStudio && Style != StyleClassic)) return;
            using (var b = new LinearGradientBrush(new RectangleF(nr.X, nr.Y, nr.Width, nr.Height / 2 + 1), Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                FillRound(g, b, new RectangleF(nr.X + F(1), nr.Y + F(1), nr.Width - F(2), nr.Height / 2), F(3));
        }

        /// <summary>Schriftfarbe auf dem Namensschild.</summary>
        public static Color PlateText(Color bg) => DymoPlates ? Color.FromArgb(0xf6, 0xf6, 0xf2) : TapePlates ? TapeInk : DigitalPlates ? DigitalInk(bg) : BigConsole ? ConsoleInk : Style == StyleNeon ? Lighten(bg, 0.5f) : TextOn(bg);

        /// <summary>Vertieftes Feld (Apps, Geraete-Info, Gruppe ...).</summary>
        public static void DrawWell(Graphics g, RectangleF r, bool hover)
        {
            float rad = Style == StyleModern || Style == StyleLight ? F(6) : F(4);
            FillRound(g, hover ? WellHover : Well, r, rad);
            StrokeRound(g, Style == StyleNeon && hover ? Alpha(Accent, 160) : WellEdge, F(1), r, rad);
        }

        /// <summary>Faderschlitz.</summary>
        public static void DrawSlot(Graphics g, RectangleF r)
        {
            switch (Style)
            {
                case StyleModern:
                    FillRound(g, Slot, RectangleF.Inflate(r, -F(1), 0), F(2));
                    break;
                case StyleNeon:
                    FillRound(g, Slot, r, F(3));
                    StrokeRound(g, Alpha(Text, 26), F(1), r, F(3));
                    break;
                case StyleLight:
                    FillRound(g, Slot, RectangleF.Inflate(r, -F(0.5f), 0), F(3));
                    using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), F(1)))
                        g.DrawLine(pen, r.X + F(1), r.Bottom + F(0.5f), r.Right - F(1), r.Bottom + F(0.5f));
                    break;
                default:
                    FillRound(g, Slot, r, F(3));
                    using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), F(1)))
                        g.DrawLine(pen, r.X + F(1), r.Bottom + F(0.5f), r.Right - F(1), r.Bottom + F(0.5f));
                    break;
            }
        }

        // ---------------------------------------------------------------- Stil-Varianten

        private static void StyledPanel(Graphics g, RectangleF r, float rad, Color? tint)
        {
            switch (Style)
            {
                case StyleClassic:
                {
                    for (int i = 3; i >= 1; i--)
                        FillRound(g, Color.FromArgb(30, 0, 0, 0), new RectangleF(r.X - i * 0.5f, r.Y + i, r.Width + i, r.Height + i * 0.5f), rad + i);
                    FillVertical(g, r, PanelTop, PanelBottom, rad);
                    StrokeRound(g, PanelEdge, F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad - F(0.5f));
                    DrawTopHighlight(g, RectangleF.Inflate(r, -F(1), -F(1)), rad - 1, 210, F(40));
                    break;
                }
                case StyleModern:
                    FillRound(g, Color.FromArgb(46, 0, 0, 0), new RectangleF(r.X, r.Y + F(1.5f), r.Width, r.Height), rad);
                    FillVertical(g, r, PanelTop, PanelBottom, rad);
                    StrokeRound(g, Alpha(PanelHighlight, 170), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                case StyleNeon:
                {
                    Color t = tint ?? Accent;
                    FillVertical(g, r, PanelTop, PanelBottom, rad);
                    // Leuchten innen an der Kante entlang
                    for (int i = 0; i < 6; i++)
                        StrokeRound(g, Alpha(t, 44 - i * 7), F(1.5f), RectangleF.Inflate(r, -F(1.2f + i * 1.1f), -F(1.2f + i * 1.1f)), Math.Max(F(1), rad - F(i)));
                    StrokeRound(g, Alpha(t, 235), F(1.3f), RectangleF.Inflate(r, -F(0.7f), -F(0.7f)), rad);
                    break;
                }
                case StyleLight:
                    for (int i = 4; i >= 1; i--)
                        FillRound(g, Color.FromArgb(11, 0, 0, 0), new RectangleF(r.X - i * 0.5f, r.Y + i, r.Width + i, r.Height + i * 0.5f), rad + i);
                    FillVertical(g, r, PanelTop, PanelBottom, rad);
                    StrokeRound(g, PanelEdge, F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad - F(0.5f));
                    using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), F(1)))
                        g.DrawLine(pen, r.X + rad, r.Y + F(1.5f), r.Right - rad, r.Y + F(1.5f));
                    break;
            }
        }

        private static void StyledEngrave(Graphics g, float x1, float x2, float y)
        {
            if (Style == StyleLight)
            {
                using (var dark = new Pen(Color.FromArgb(34, 0, 0, 0), F(1)))
                using (var light = new Pen(Color.FromArgb(220, 255, 255, 255), F(1)))
                {
                    g.DrawLine(dark, x1, y, x2, y);
                    g.DrawLine(light, x1, y + F(1), x2, y + F(1));
                }
                return;
            }
            using (var pen = new Pen(Alpha(Text, Style == StyleNeon ? 18 : 22), F(1)))
                g.DrawLine(pen, x1, y, x2, y);
        }

        private static void StyledKey(Graphics g, RectangleF r, string text, bool on, Color lit, bool hover, bool half)
        {
            float rad = KeyRad(Style == StyleClassic ? F(3) : F(4.5f), r);   // 3.1: Tasten-Form
            switch (Style)
            {
                case StyleClassic:
                    if (on)
                    {
                        FillVertical(g, r, Lighten(lit, 0.18f), Darken(lit, 0.12f), rad);
                        StrokeRound(g, Darken(lit, 0.45f), F(1), r, rad);
                        using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), F(1)))
                            g.DrawLine(pen, r.X + rad, r.Y + F(1.5f), r.Right - rad, r.Y + F(1.5f));
                        Label(g, text, r, Center, TextOn(lit), KeyPx(g, text, r.Width));
                    }
                    else
                    {
                        FillVertical(g, r, hover ? KeyHoverTop : KeyTop, hover ? KeyHoverBottom : KeyBottom, rad);
                        StrokeRound(g, KeyEdge, F(1), r, rad);
                        using (var pen = new Pen(Color.FromArgb(30, 255, 255, 255), F(1)))
                            g.DrawLine(pen, r.X + rad, r.Y + F(1.5f), r.Right - rad, r.Y + F(1.5f));
                        if (half) StrokeRound(g, Alpha(lit, 170), F(1.2f), RectangleF.Inflate(r, -F(1.5f), -F(1.5f)), rad - 1);
                        Label(g, text, r, Center, half ? Mix(TextDim, lit, 0.75f) : hover ? Text : TextDim, KeyPx(g, text, r.Width));
                    }
                    break;

                case StyleNeon:
                    if (on)
                    {
                        for (int i = 4; i >= 1; i--)
                            FillRound(g, Alpha(lit, 15), RectangleF.Inflate(r, F(i), F(i)), rad + F(i));
                        FillRound(g, Alpha(lit, 72), r, rad);
                        StrokeRound(g, Lighten(lit, 0.2f), F(1.4f), RectangleF.Inflate(r, -F(0.7f), -F(0.7f)), rad);
                        Label(g, text, r, Center, Lighten(lit, 0.65f), KeyPx(g, text, r.Width));
                    }
                    else
                    {
                        FillRound(g, hover ? KeyHoverTop : KeyTop, r, rad);
                        StrokeRound(g, Alpha(lit, half ? 220 : hover ? 150 : 75), F(1.1f), RectangleF.Inflate(r, -F(0.6f), -F(0.6f)), rad);
                        Label(g, text, r, Center, half ? lit : hover ? Mix(Text, lit, 0.35f) : Mix(TextDim, lit, 0.3f), KeyPx(g, text, r.Width));
                    }
                    break;

                default: // Modern, Hell
                    if (on)
                    {
                        if (Style == StyleLight) FillRound(g, Color.FromArgb(40, 0, 0, 0), new RectangleF(r.X, r.Y + F(1), r.Width, r.Height), rad);
                        FillRound(g, hover ? Lighten(lit, 0.08f) : lit, r, rad);
                        Label(g, text, r, Center, TextOn(lit), KeyPx(g, text, r.Width));
                    }
                    else
                    {
                        if (Style == StyleLight) FillRound(g, Color.FromArgb(24, 0, 0, 0), new RectangleF(r.X, r.Y + F(1), r.Width, r.Height), rad);
                        FillVertical(g, r, hover ? KeyHoverTop : KeyTop, hover ? KeyHoverBottom : KeyBottom, rad);
                        StrokeRound(g, half ? Alpha(lit, 220) : KeyEdge, F(half ? 1.4f : 1f), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                        Color tc = half ? (IsLight ? Darken(lit, 0.25f) : Mix(TextDim, lit, 0.75f)) : hover ? Text : TextDim;
                        Label(g, text, r, Center, tc, KeyPx(g, text, r.Width));
                    }
                    break;
            }
        }

        private static void StyledLcd(Graphics g, RectangleF r, string text, Color c)
        {
            var font = Mono(11.5f);
            switch (Style)
            {
                case StyleModern:
                    FillRound(g, LcdBg, r, F(5));
                    using (var b = new SolidBrush(c)) Theme.Str(g, text, font, b, r, Center);
                    break;
                case StyleNeon:
                {
                    FillRound(g, LcdBg, r, F(4));
                    StrokeRound(g, Alpha(c, 110), F(1), r, F(4));
                    using (var glow = new SolidBrush(Alpha(c, 36)))
                    {
                        float d = F(1f);
                        Theme.Str(g, text, font, glow, new RectangleF(r.X - d, r.Y, r.Width, r.Height), Center);
                        Theme.Str(g, text, font, glow, new RectangleF(r.X + d, r.Y, r.Width, r.Height), Center);
                    }
                    using (var b = new SolidBrush(Lighten(c, 0.2f))) Theme.Str(g, text, font, b, r, Center);
                    break;
                }
                default: // Klassisch, Hell
                {
                    float rad = Style == StyleLight ? F(4) : F(3);
                    FillRound(g, LcdBg, r, rad);
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, F(5) + 2), Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                        FillRound(g, b, new RectangleF(r.X, r.Y, r.Width, F(5)), rad);
                    using (var b = new SolidBrush(c)) Theme.Str(g, text, font, b, r, Center);
                    StrokeRound(g, Style == StyleLight ? Darken(PanelBottom, 0.35f) : Color.FromArgb(0x02, 0x02, 0x03), F(1), r, rad);
                    using (var pen = new Pen(Style == StyleLight ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255), F(1)))
                        g.DrawLine(pen, r.X + rad, r.Bottom + F(0.5f), r.Right - rad, r.Bottom + F(0.5f));
                    break;
                }
            }
        }

        /// <summary>Drehregler als Bogen (Modern, Neon, Hell).</summary>
        private static void StyledKnob(Graphics g, PointF c, float radius, float value, Color led, bool bipolar, bool hover, float ringOffset)
        {
            float pos = Math.Max(0f, Math.Min(1f, bipolar ? (value + 1f) / 2f : value));
            float ringR = radius + F(ringOffset) - F(0.6f);
            float w = Math.Max(1.5f, F(ringOffset >= 5 ? 2.4f : 2f));
            var rect = new RectangleF(c.X - ringR, c.Y - ringR, ringR * 2, ringR * 2);
            if (!KnobRing)   // 3.3: mit LED-Kranz kein Bogen
                using (var pen = new Pen(RingOff, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, 135f, 270f);
            float a0 = bipolar ? 270f : 135f, a1 = 135f + 270f * pos;
            float sweep = a1 - a0;
            if (Math.Abs(sweep) < 4f && bipolar) sweep = 0f;
            if (KnobRing) { }
            else if (Math.Abs(sweep) > 0.5f)
            {
                if (Style == StyleNeon)
                    using (var pen = new Pen(Alpha(led, 70), w * 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, rect, a0, sweep);
                using (var pen = new Pen(Style == StyleNeon ? Lighten(led, 0.15f) : led, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, a0, sweep);
            }
            else if (bipolar)
                FillEllipse(g, led, c.X, c.Y - ringR, w * 0.75f);

            double ang = (-135 + 270.0 * pos - 90) * Math.PI / 180.0;
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            switch (Style)
            {
                case StyleNeon:
                    FillEllipse(g, C(0x0a, 0x0b, 0x0d), c.X, c.Y, radius);
                    using (var pen = new Pen(Alpha(led, hover ? 230 : 150), F(1.2f))) g.DrawEllipse(pen, outer);
                    using (var pen = new Pen(Alpha(led, 80), F(4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, c.X + ca * radius * 0.25f, c.Y + sa * radius * 0.25f, c.X + ca * radius * 0.78f, c.Y + sa * radius * 0.78f);
                    using (var pen = new Pen(Lighten(led, 0.4f), F(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, c.X + ca * radius * 0.25f, c.Y + sa * radius * 0.25f, c.X + ca * radius * 0.78f, c.Y + sa * radius * 0.78f);
                    break;
                case StyleLight when Vintage:
                    VintageKnobBody(g, c, radius, ca, sa, hover);
                    break;
                case StyleLight when Hifi:
                    ChromeKnobBody(g, c, radius, ca, sa, hover);
                    break;
                case StyleLight:
                    FillEllipse(g, Color.FromArgb(50, 0, 0, 0), c.X, c.Y + F(1.4f), radius + F(0.4f));
                    using (var b = new LinearGradientBrush(outer, Color.White, hover ? C(0xeef1f4) : Darken(PanelBottom, 0.06f), 70f))
                        g.FillEllipse(b, outer);
                    using (var pen = new Pen(Darken(PanelBottom, 0.3f), F(1))) g.DrawEllipse(pen, outer);
                    using (var pen = new Pen(Text, F(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, c.X + ca * radius * 0.3f, c.Y + sa * radius * 0.3f, c.X + ca * radius * 0.8f, c.Y + sa * radius * 0.8f);
                    break;
                default: // Modern
                    FillEllipse(g, Color.FromArgb(60, 0, 0, 0), c.X, c.Y + F(1f), radius + F(0.3f));
                    FillEllipse(g, hover ? Lighten(PanelBottom, 0.2f) : Lighten(PanelBottom, 0.13f), c.X, c.Y, radius);
                    using (var pen = new Pen(Lighten(PanelBottom, 0.24f), F(1))) g.DrawEllipse(pen, outer);
                    using (var pen = new Pen(Text, F(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, c.X + ca * radius * 0.3f, c.Y + sa * radius * 0.3f, c.X + ca * radius * 0.8f, c.Y + sa * radius * 0.8f);
                    break;
            }
        }

        /// <summary>Drehknopf wie frueher: Metallrand, dunkle Kappe, weisser Strich.</summary>
        private static void ClassicKnobBody(Graphics g, PointF c, float radius, float pos, bool hover)
        {
            FillEllipse(g, Color.FromArgb(90, 0, 0, 0), c.X, c.Y + F(1.5f), radius + F(0.5f));
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var b = new LinearGradientBrush(outer, hover ? C(0x9a, 0x9f, 0xa7) : C(0x84, 0x8a, 0x93), C(0x2a, 0x2d, 0x32), 60f))
                g.FillEllipse(b, outer);
            float ir = radius * 0.8f;
            var inner = new RectangleF(c.X - ir, c.Y - ir, ir * 2, ir * 2);
            using (var b = new LinearGradientBrush(inner, hover ? C(0x3a, 0x3e, 0x45) : C(0x2e, 0x32, 0x38), C(0x15, 0x17, 0x1a), 90f))
                g.FillEllipse(b, inner);
            using (var pen = new Pen(Color.FromArgb(0x05, 0x05, 0x06), F(1))) g.DrawEllipse(pen, outer);
            double a = (-135 + 270.0 * Math.Max(0f, Math.Min(1f, pos)) - 90) * Math.PI / 180.0;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            using (var pen = new Pen(C(0xf2, 0xf4, 0xf6), F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, c.X + ca * ir * 0.25f, c.Y + sa * ir * 0.25f, c.X + ca * ir * 0.95f, c.Y + sa * ir * 0.95f);
        }

        private static void StyledFaderCap(Graphics g, RectangleF r, bool master, bool hover, Color accent)
        {
            float rad = CapRad(F(4), r);
            float cy = r.Y + r.Height / 2;
            if (Style == StyleNeon)
            {
                Color col = master ? MuteRed : accent;
                FillRound(g, Alpha(col, 34), RectangleF.Inflate(r, F(2.5f), F(2.5f)), rad + F(2));
                FillRound(g, hover ? C(0x1a, 0x1c, 0x21) : C(0x10, 0x12, 0x15), r, rad);
                StrokeRound(g, col, F(1.4f), RectangleF.Inflate(r, -F(0.7f), -F(0.7f)), rad);
                using (var pen = new Pen(Alpha(col, 90), F(5))) g.DrawLine(pen, r.X + F(4), cy, r.Right - F(4), cy);
                using (var pen = new Pen(Lighten(col, 0.4f), F(1.8f))) g.DrawLine(pen, r.X + F(4), cy, r.Right - F(4), cy);
                return;
            }
            bool light = Style == StyleLight;
            FillRound(g, Color.FromArgb(light ? 55 : 90, 0, 0, 0), new RectangleF(r.X, r.Y + F(light ? 2 : 1.5f), r.Width, r.Height), rad);
            Color face = master ? C(0xe2, 0x4d, 0x41) : light ? Color.White : C(0xdc, 0xdf, 0xe3);
            if (hover) face = Lighten(face, master ? 0.1f : 0.35f);
            if (light && !master)
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.White, hover ? C(0xf4f6f8) : C(0xe4e7eb), LinearGradientMode.Vertical))
                    FillRound(g, b, r, rad);
            else FillRound(g, face, r, rad);
            StrokeRound(g, master ? Darken(face, 0.4f) : light ? C(0x9a, 0xa1, 0xab) : Darken(face, 0.45f), F(1), r, rad);
            Color line = master ? Color.White : (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0 > 0.75 ? Darken(accent, 0.5f) : accent;
            using (var pen = new Pen(line, F(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, r.X + F(4), cy, r.Right - F(4), cy);
        }

        private static void StyledNamePlate(Graphics g, RectangleF r, Color bg)
        {
            switch (Style)
            {
                case StyleClassic:
                {
                    float rad = F(3.5f);
                    FillVertical(g, r, Lighten(bg, 0.1f), Darken(bg, 0.14f), rad);
                    StrokeRound(g, Alpha(Lighten(bg, 0.35f), 70), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                }
                case StyleModern:
                    FillRound(g, bg, r, F(5));
                    break;
                case StyleNeon:
                {
                    float rad = F(5);
                    FillRound(g, Alpha(bg, 30), RectangleF.Inflate(r, F(2), F(2)), rad + F(2));
                    FillRound(g, Darken(bg, 0.82f), r, rad);
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Alpha(bg, 70), Alpha(bg, 10), LinearGradientMode.Vertical))
                        FillRound(g, b, r, rad);
                    StrokeRound(g, bg, F(1.3f), RectangleF.Inflate(r, -F(0.6f), -F(0.6f)), rad);
                    break;
                }
                case StyleLight:
                {
                    float rad = F(5);
                    FillVertical(g, r, Lighten(bg, 0.08f), Darken(bg, 0.08f), rad);
                    StrokeRound(g, Alpha(Darken(bg, 0.3f), 120), F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                }
            }
        }

        private static void StyledMeterBezel(Graphics g, RectangleF r)
        {
            float rad = Style == StyleClassic ? F(2.5f) : F(3);
            FillRound(g, Hole, r, rad);
            if (Style == StyleNeon) StrokeRound(g, Alpha(Text, 24), F(1), r, rad);
            else if (Style == StyleLight)
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), F(1)))
                    g.DrawLine(pen, r.X + F(2), r.Bottom + F(0.5f), r.Right - F(2), r.Bottom + F(0.5f));
        }

        // ---------------------------------------------------------------- Kopfleiste

        /// <summary>Hintergrund der Kopfleiste (Studio zeichnet die Leiste selbst mit Metall und Schrauben).</summary>
        public static void DrawBarBackground(Graphics g, Rectangle r)
        {
            switch (Style)
            {
                case StyleModern:
                    using (var b = new SolidBrush(BarBg)) g.FillRectangle(b, r);
                    using (var pen = new Pen(Alpha(Text, 24), F(1))) g.DrawLine(pen, r.X, r.Bottom - F(1), r.Right, r.Bottom - F(1));
                    break;
                case StyleNeon:
                    using (var b = new SolidBrush(BarBg)) g.FillRectangle(b, r);
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Bottom - F(12) - 1, r.Width, F(12) + 2), Alpha(Accent, 0), Alpha(Accent, 60), LinearGradientMode.Vertical))
                        g.FillRectangle(b, r.X, r.Bottom - F(12), r.Width, F(12));
                    using (var b = new SolidBrush(Accent)) g.FillRectangle(b, r.X, r.Bottom - F(1.5f), r.Width, F(1.5f));
                    break;
                case StyleLight:
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), PanelTop, BarBg, LinearGradientMode.Vertical)) g.FillRectangle(b, r);
                    using (var pen = new Pen(Color.White, F(1))) g.DrawLine(pen, r.X, F(0.5f), r.Right, F(0.5f));
                    using (var pen = new Pen(PanelEdge, F(1))) g.DrawLine(pen, r.X, r.Bottom - F(1), r.Right, r.Bottom - F(1));
                    break;
                default: // Klassisch
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Lighten(PanelTop, 0.02f), BarBg, LinearGradientMode.Vertical)) g.FillRectangle(b, r);
                    using (var pen = new Pen(Alpha(PanelHighlight, 130), F(1))) g.DrawLine(pen, r.X, F(0.5f), r.Right, F(0.5f));
                    using (var pen = new Pen(Color.FromArgb(0x03, 0x03, 0x04), F(1))) g.DrawLine(pen, r.X, r.Bottom - F(1), r.Right, r.Bottom - F(1));
                    break;
            }
        }

        /// <summary>Knopf in der Kopfleiste (ohne Text).</summary>
        public static void DrawBarButton(Graphics g, RectangleF r, bool hover)
        {
            float rad = Style == StyleModern || Style == StyleLight ? F(6) : F(5);
            switch (Style)
            {
                case StyleNeon:
                    FillRound(g, hover ? BtnHoverTop : BtnTop, r, rad);
                    StrokeRound(g, Alpha(Accent, hover ? 230 : 110), F(1.1f), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                case StyleModern:
                    FillRound(g, hover ? BtnHoverTop : BtnTop, r, rad);
                    StrokeRound(g, BtnEdge, F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                case StyleLight:
                    FillRound(g, Color.FromArgb(26, 0, 0, 0), new RectangleF(r.X, r.Y + F(1), r.Width, r.Height), rad);
                    FillVertical(g, r, hover ? BtnHoverTop : BtnTop, hover ? BtnHoverBottom : BtnBottom, rad);
                    StrokeRound(g, BtnEdge, F(1), RectangleF.Inflate(r, -F(0.5f), -F(0.5f)), rad);
                    break;
                default:
                    FillVertical(g, r, hover ? BtnHoverTop : BtnTop, hover ? BtnHoverBottom : BtnBottom, rad);
                    StrokeRound(g, BtnEdge, F(1), r, rad);
                    using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), F(1)))
                        g.DrawLine(pen, r.X + rad, r.Y + F(1.5f), r.Right - rad, r.Y + F(1.5f));
                    break;
            }
        }
    }
}
