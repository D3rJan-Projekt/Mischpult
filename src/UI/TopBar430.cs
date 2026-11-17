using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>4.3: DUCKING-Taste zeigt, wer ausloest; Akku des Bluetooth-Kopfhoerers.</summary>
    internal sealed partial class TopBar
    {
        /// <summary>Text auf der DUCKING-Taste (null = normal), z. B. "MIKRO −15".</summary>
        public string DuckText, DuckTip;
        /// <summary>Akku-Anzeige (null = aus), z. B. "80 %".</summary>
        public string BatteryText, BatteryTip;
        public int BatteryPct = -1;
        private RectangleF _batRect;
        private bool _batFits;

        /// <summary>Lage der DUCKING-Taste auf dem Bildschirm (fuer das Menue).</summary>
        public Rectangle DuckButtonScreen
        {
            get { var r = Rectangle.Round(_duckBtn); return new Rectangle(PointToScreen(r.Location), r.Size); }
        }

        private void DrawDuckWho430(Graphics g)
        {
            Theme.DrawBarButton(g, _duckBtn, _hover == "duck");
            var r = new RectangleF(_duckBtn.X + Theme.F(24), _duckBtn.Y, _duckBtn.Width - Theme.F(27), _duckBtn.Height);
            var f = Theme.UiSemi(DuckText.Length > 12 ? 7.5f : 8.5f);
            using (var b = new SolidBrush(DuckActive ? Theme.SoloAmber : Theme.Text))
                Theme.Str(g, DuckText, f, b, r, Theme.Center);
        }

        private void DrawBattery430(Graphics g)
        {
            var r = _batRect;
            Theme.DrawLcd(g, r, "", null);
            Theme.LcdColors(out var bg, out var fg);
            Color c = BatteryPct >= 0 && BatteryPct <= 15 ? Theme.MuteRed : BatteryPct >= 0 && BatteryPct <= 30 ? Theme.SoloAmber : fg;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Akku-Zeichen: Rahmen, Pol, Fuellung nach Ladestand
            float w = Theme.F(15), h = Theme.F(8), x = r.X + Theme.F(6), y = r.Y + (r.Height - h) / 2f;
            using (var pen = new Pen(c, Math.Max(1f, Theme.F(1.1f)))) g.DrawRectangle(pen, x, y, w, h);
            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, x + w, y + h * 0.3f, Theme.F(1.6f), h * 0.4f);
                float fill = Math.Max(0f, Math.Min(1f, BatteryPct / 100f));
                if (BatteryPct >= 0) g.FillRectangle(b, x + Theme.F(1.5f), y + Theme.F(1.5f), (w - Theme.F(3)) * fill, h - Theme.F(3));
            }
            g.SmoothingMode = sm;
            using (var b = new SolidBrush(c))
                Theme.Str(g, BatteryText, Theme.Mono(11f), b, new RectangleF(x + w + Theme.F(4), r.Y, r.Right - (x + w + Theme.F(6)), r.Height), Theme.Center);
        }
    }
}
