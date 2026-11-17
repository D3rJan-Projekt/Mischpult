using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>
    /// Pult-Helligkeit: abends dunkler. Flaechen, Schilder und Beschriftungen werden abgedunkelt; was leuchtet
    /// (Pegel, LEDs, gedrueckte Tasten, VU-Zeiger) bleibt hell – wie ein Pult im dunklen Studio.
    /// </summary>
    internal static partial class Theme
    {
        /// <summary>0 = normal, 0,3 = 30 % dunkler … (hoechstens 0,7).</summary>
        public static float NightDim;

        public static bool NightOn => NightDim > 0.001f;

        /// <summary>Farbe mit der aktuellen Abdunklung.</summary>
        public static Color DimColor(Color c) => NightOn ? Darken(c, NightDim) : c;

        /// <summary>Flaeche abdunkeln (gerundet wie das Bauteil darunter).</summary>
        public static void DimOverlay(Graphics g, RectangleF r, float rad)
        {
            if (!NightOn || r.Width <= 0 || r.Height <= 0) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = rad > 0 ? SmoothingMode.AntiAlias : SmoothingMode.None;
            var c = Color.FromArgb((int)Math.Round(Math.Min(0.7f, NightDim) * 255), 0, 0, 0);
            if (rad > 0) FillRound(g, c, r, rad);
            else using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
            g.SmoothingMode = sm;
        }

        /// <summary>
        /// Waehrend ein Zug mit Abdunklung gezeichnet wird: leuchtende Tasten merken – sie werden nach dem Abdunkeln
        /// noch einmal hell darueber gezeichnet (null = nichts merken).
        /// </summary>
        public static List<Action> LitRedraw;

        /// <summary>
        /// Wie WithNightDim, dazu das Licht der Pultlampe (auf die ganze Flaeche 'all', Lage im Fenster 'origin') –
        /// leuchtende Tasten und Anzeigen kommen danach unveraendert darueber.
        /// </summary>
        public static void WithNightDimAndLamp(Graphics g, RectangleF area, float rad, RectangleF all, Point origin, Action draw)
        {
            if (!LampOn && !TiltOn && !SunOn) { WithNightDim(g, area, rad, draw); return; }   // 3.4: auch Licht nach Uhrzeit
            var list = new List<Action>();
            LitRedraw = list;
            try { draw(); }
            finally { LitRedraw = null; }
            DimOverlay(g, area, rad);
            ExtraDimOverlay(g, area, rad);   // 3.1: stummer/ausgeblendeter Zug insgesamt dunkler
            TiltShade(g, all, origin);   // 2.20: Schraeglage
            LampLight(g, all, origin);
            foreach (var a in list) { try { a(); } catch { } }
        }

        /// <summary>Zeichnet mit Abdunklung: erst alles, dann dunkler, dann die leuchtenden Tasten wieder hell.</summary>
        public static void WithNightDim(Graphics g, RectangleF area, float rad, Action draw)
        {
            if (!NightOn && ExtraDim <= 0) { draw(); return; }
            var list = new List<Action>();
            LitRedraw = list;
            try { draw(); }
            finally { LitRedraw = null; }
            DimOverlay(g, area, rad);
            ExtraDimOverlay(g, area, rad);   // 3.1
            foreach (var a in list) { try { a(); } catch { } }
        }
    
        /// <summary>3.1: zusaetzliche Abdunklung eines ganzen Zugs (0 = keine, sonst Deckkraft 0..255) – leuchtende Tasten bleiben hell.</summary>
        [ThreadStatic] public static int ExtraDim;

        private static void ExtraDimOverlay(Graphics g, RectangleF area, float rad)
        {
            if (ExtraDim <= 0) return;
            FillRound(g, Color.FromArgb(Math.Min(220, ExtraDim), 0, 0, 0), area, rad);
        }
}
}
