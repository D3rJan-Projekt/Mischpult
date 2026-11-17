using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.4: Lautheits-Verlauf als Papierschreiber – Papierstreifen mit Raster, ein Stift schreibt rechts mit.</summary>
    internal sealed partial class LoudHistView
    {
        public static bool PaperLook;
        private static readonly Color Paper = Color.FromArgb(0xf4, 0xee, 0xdc), GridRed = Color.FromArgb(0xe0, 0x9a, 0x8c), Ink = Color.FromArgb(0x1c, 0x3a, 0x9c);

        private void DrawPaperStatic(Graphics g, int minutes, string caption)
        {
            var r = Bounds;
            // Geraet: dunkles Gehaeuse, darin der Papierstreifen
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0x2c, 0x2e, 0x32), Color.FromArgb(0x15, 0x16, 0x19), LinearGradientMode.Vertical))
                Theme.FillRound(g, b, r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x08, 0x08, 0x09), Theme.F(1), r, Theme.F(5));
            string cap = string.IsNullOrEmpty(caption) ? "LAUTHEIT · " + minutes + " MIN" : caption;
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                Theme.Str(g, cap, Theme.Fit(g, cap, r.Width - Theme.F(66), 9f, 7f, FontStyle.Bold), b, new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(66), Theme.F(14)), sf);
            var paper = RectangleF.Inflate(_plot, Theme.F(2), Theme.F(2));
            using (var b = new SolidBrush(Paper)) g.FillRectangle(b, paper);
            using (var b = new LinearGradientBrush(new RectangleF(paper.X, paper.Y, paper.Width, Theme.F(6)), Color.FromArgb(60, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(b, paper.X, paper.Y, paper.Width, Theme.F(6));
            var font = Theme.Ui(7f, FontStyle.Bold);
            g.SmoothingMode = SmoothingMode.None;
            foreach (float db in Grid)
            {
                float y = (float)Math.Round(Y(db)) + 0.5f;
                using (var pen = new Pen(db == -14f ? Color.FromArgb(0xc8, 0x4a, 0x3a) : GridRed, 1f)) g.DrawLine(pen, _plot.X, y, _plot.Right, y);
                using (var b = new SolidBrush(Color.FromArgb(0xa8, 0xae, 0xb8)))
                    Theme.Str(g, db.ToString("0").Replace("-", "−"), font, b, new RectangleF(r.X + Theme.F(2), y - Theme.F(5.5f), Theme.F(20), Theme.F(11)), Theme.Right);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        private void DrawPaperLive(Graphics g, LoudnessHistory h)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float step = _plot.Width / (LoudnessHistory.Points - 1);
            int n = h.Count;
            // senkrechte Zeitlinien laufen mit dem Papier nach links
            using (var pen = new Pen(Color.FromArgb(110, GridRed), 1f))
            {
                int every = 30;   // alle 30 Punkte eine Linie
                int phase = (h.Version) % every;
                for (float x = _plot.Right - phase * step; x > _plot.X; x -= every * step)
                    g.DrawLine(pen, x, _plot.Y, x, _plot.Bottom);
            }
            // Tinte
            using (var ink = new Pen(Ink, Math.Max(1f, Theme.F(1.3f))) { LineJoin = LineJoin.Round })
            {
                int i = 0;
                while (i < n)
                {
                    while (i < n && float.IsNaN(h[i])) i++;
                    int s = i;
                    while (i < n && !float.IsNaN(h[i])) i++;
                    int len = i - s;
                    if (len <= 0) continue;
                    var pts = new PointF[Math.Max(2, len)];
                    for (int k = 0; k < len; k++) pts[k] = new PointF(_plot.Right - (n - 1 - (s + k)) * step, Y(h[s + k]));
                    if (len == 1) pts[1] = new PointF(pts[0].X + 1f, pts[0].Y);
                    g.DrawLines(ink, pts);
                }
            }
            // Stift: Arm von rechts, Spitze am letzten Wert
            float last = h.Last;
            float py = float.IsNaN(last) ? _plot.Bottom - Theme.F(2) : Y(last);
            var tip = new PointF(_plot.Right - Theme.F(1), py);
            using (var pen = new Pen(Color.FromArgb(0x9a, 0x9e, 0xa6), Math.Max(1.5f, Theme.F(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, tip.X + Theme.F(2), tip.Y, Bounds.Right - Theme.F(4), tip.Y - Theme.F(3));
            using (var b = new SolidBrush(Color.FromArgb(0xc8, 0x2a, 0x1e))) g.FillEllipse(b, tip.X - Theme.F(2.2f), tip.Y - Theme.F(2.2f), Theme.F(4.4f), Theme.F(4.4f));
            // Wert
            string t = float.IsNaN(last) ? "–" : last.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")).Replace("-", "−");
            using (var b = new SolidBrush(Color.FromArgb(0xe8, 0xe2, 0xcc)))
                Theme.Str(g, t, Theme.Mono(9.5f), b, _value, Theme.Right);
            g.SmoothingMode = sm;
        }
    }
}
