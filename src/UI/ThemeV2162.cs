using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Mischpult.UI
{
    /// <summary>2.16.2: grosse Uhr im LCD-Look fuer die Meterbridge (Farben wie die Zahlen-Anzeigen).</summary>
    internal static partial class Theme
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>
        /// LCD-Uhr: HH:MM in grossen Segmenten (Doppelpunkt blinkt), Sekunden klein daneben, darunter Wochentag und Datum.
        /// staticPart = Untergrund (einmal), sonst das Leuchtende (einmal pro Sekunde).
        /// </summary>
        public static void DrawLcdClock(Graphics g, RectangleF r, bool staticPart)
        {
            if (staticPart)
            {
                DrawLcd(g, r, "", null);
                return;
            }
            LcdColors(out var bg, out var fg);
            if (EffLcd == 0) fg = LcdText;
            Color ghost = Mix(bg, fg, EffLcd == 1 ? 0.16f : 0.10f);
            var now = DateTime.Now;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float dateH = Math.Max(F(9), r.Height * 0.15f);
            float h = (float)Math.Floor(Math.Min(r.Height * 0.44f, (r.Width - F(28)) / 3.15f));
            float h2 = (float)Math.Round(h * 0.45f), gap = h * 0.12f;
            float w = Seg7Width("88:88", h) + gap + Seg7Width("88", h2);
            float x = r.X + (r.Width - w) / 2f;
            float blockH = h + F(6) + dateH;
            float y = r.Y + (r.Height - blockH) / 2f;

            string hh = now.Hour.ToString("00"), mm = now.Minute.ToString("00"), ss = now.Second.ToString("00");
            DrawSeg7(g, hh, x, y, h, fg, ghost);
            float cx = x + Seg7Width(hh, h);
            DrawSeg7(g, ":", cx, y, h, now.Second % 2 == 0 ? fg : ghost, Color.Empty, false);
            DrawSeg7(g, mm, cx + Seg7Width(":", h), y, h, fg, ghost);
            DrawSeg7(g, ss, x + Seg7Width("88:88", h) + gap, y + h - h2, h2, Alpha(fg, 210), ghost, false);

            string date = now.ToString("ddd d. MMM", De).Replace("..", ".").ToUpperInvariant();
            using (var b = new SolidBrush(Alpha(fg, 175)))
                Str(g, date, Ui(Math.Max(7f, dateH / S * 0.78f), FontStyle.Bold), b, new RectangleF(r.X + F(4), y + h + F(5), r.Width - F(8), dateH), Center);
            g.SmoothingMode = sm;
        }
    }
}
