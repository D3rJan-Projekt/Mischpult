using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.3: Oszilloskop wie ein Messgeraet – gruener Bildschirm mit Raster und Teilstrichen, die letzten Bilder leuchten nach.</summary>
    internal sealed partial class OscView
    {
        public static bool ScopeLook;
        private const int Traces = 5;
        private readonly PointF[][] _trace = new PointF[Traces][];
        private int _traceTop, _traceCount;

        private void PushTrace()
        {
            if (!_any) return;
            _traceTop = (_traceTop + 1) % Traces;
            var t = _trace[_traceTop];
            if (t == null || t.Length != _pts.Length) t = _trace[_traceTop] = new PointF[_pts.Length];
            Array.Copy(_pts, t, _pts.Length);
            if (_traceCount < Traces) _traceCount++;
        }

        private void ClearTraces() { _traceCount = 0; }

        private static readonly Color Screen = Color.FromArgb(0x06, 0x14, 0x0c);

        private void DrawScopeStatic(Graphics g)
        {
            var r = Bounds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Geraet: dunkles Blech, darin der Bildschirm mit gewoelbtem Glas
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0x2a, 0x2d, 0x31), Color.FromArgb(0x14, 0x15, 0x18), LinearGradientMode.Vertical))
                Theme.FillRound(g, b, r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x08, 0x08, 0x09), Theme.F(1), r, Theme.F(5));
            using (var b = new SolidBrush(Color.FromArgb(0xa8, 0xb0, 0xb8)))
                Theme.Str(g, "OSZILLOSKOP", Theme.Fit(g, "OSZILLOSKOP", r.Width - Theme.F(16), 8.5f, 7f), b, new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(16), Theme.F(14)), Theme.Left);
            var scr = RectangleF.Inflate(_plot, Theme.F(3), Theme.F(3));
            using (var path = new GraphicsPath())
            {
                path.AddPath(Theme.Round(scr, Theme.F(7)), false);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0x0c, 0x24, 0x16), SurroundColors = new[] { Screen } })
                    g.FillPath(pb, path);
            }
            Theme.StrokeRound(g, Color.FromArgb(0x05, 0x05, 0x06), Theme.F(1.5f), scr, Theme.F(7));
            // Raster 8 x 4 mit Teilstrichen auf den Mittellinien
            g.SmoothingMode = SmoothingMode.None;
            Color grid = Color.FromArgb(70, 0x6a, 0xd8, 0x98);
            using (var pen = new Pen(grid, 1f) { DashStyle = DashStyle.Dot })
            {
                for (int i = 1; i < 8; i++) { float x = (float)Math.Round(_plot.X + _plot.Width * i / 8f) + 0.5f; g.DrawLine(pen, x, _plot.Y, x, _plot.Bottom); }
                for (int i = 1; i < 4; i++) { float y = (float)Math.Round(_plot.Y + _plot.Height * i / 4f) + 0.5f; g.DrawLine(pen, _plot.X, y, _plot.Right, y); }
            }
            using (var pen = new Pen(Color.FromArgb(110, 0x6a, 0xd8, 0x98), 1f))
            {
                float cy = (float)Math.Round(_plot.Y + _plot.Height / 2f) + 0.5f, cx = (float)Math.Round(_plot.X + _plot.Width / 2f) + 0.5f;
                g.DrawLine(pen, _plot.X, cy, _plot.Right, cy);
                g.DrawLine(pen, cx, _plot.Y, cx, _plot.Bottom);
                float tick = Math.Max(2f, Theme.F(2.5f));
                for (int i = 1; i < 40; i++) { float x = (float)Math.Round(_plot.X + _plot.Width * i / 40f) + 0.5f; g.DrawLine(pen, x, cy - tick, x, cy + tick); }
                for (int i = 1; i < 20; i++) { float y = (float)Math.Round(_plot.Y + _plot.Height * i / 20f) + 0.5f; g.DrawLine(pen, cx - tick, y, cx + tick, y); }
            }
            g.SmoothingMode = sm;
        }

        private void DrawScopeLive(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            g.SetClip(RectangleF.Inflate(_plot, 1, 1));
            // Nachleuchten: aeltere Bilder immer blasser
            for (int k = _traceCount - 1; k >= 1; k--)
            {
                var t = _trace[(_traceTop - k + Traces) % Traces];
                if (t == null || t.Length < 2) continue;
                int a = (int)(70 * (1f - k / (float)Traces));
                using (var pen = new Pen(Color.FromArgb(a, Phosphor), Math.Max(1f, Theme.F(1.2f))) { LineJoin = LineJoin.Round }) g.DrawLines(pen, t);
            }
            if (_any && _pts.Length >= 2)
            {
                using (var glow = new Pen(Color.FromArgb(45, Phosphor), Math.Max(3f, Theme.F(5f))) { LineJoin = LineJoin.Round }) g.DrawLines(glow, _pts);
                using (var glow = new Pen(Color.FromArgb(90, Phosphor), Math.Max(2f, Theme.F(2.6f))) { LineJoin = LineJoin.Round }) g.DrawLines(glow, _pts);
                using (var pen = new Pen(Color.FromArgb(0xd8, 0xff, 0xe6), Math.Max(1f, Theme.F(0.9f))) { LineJoin = LineJoin.Round }) g.DrawLines(pen, _pts);
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }
    }
}
