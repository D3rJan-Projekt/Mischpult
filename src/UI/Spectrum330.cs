using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.3: Spektrum als 3D-Landschaft (die letzten Sekunden laufen nach hinten weg) und als Lichtorgel (Bass, Mitte, Hoehen).</summary>
    internal sealed partial class SpectrumView
    {
        private const int Rows3D = 16;
        private float[][] _hist = new float[0][];
        private int _histTop, _histCount, _histVer = -1;
        private double _histNext;

        private void Ensure3D()
        {
            if (_hist.Length == Rows3D && _hist[0].Length == _bars) return;
            _hist = new float[Rows3D][];
            for (int i = 0; i < Rows3D; i++) _hist[i] = new float[_bars];
            _histTop = 0; _histCount = 0;
        }

        /// <summary>Geometrie einer Reihe (0 = vorn): Grundlinie, Breite, Hoehe.</summary>
        private void Row3D(int k, out float baseY, out float x0, out float w, out float h)
        {
            float depth = _area.Height * 0.48f;           // so weit geht es nach hinten
            float t = k / (float)(Rows3D - 1);
            float persp = 1f - 0.42f * t;                   // hinten schmaler
            baseY = _area.Bottom - 1f - depth * (float)Math.Pow(t, 0.85);
            w = _area.Width * persp;
            x0 = _area.X + (_area.Width - w) / 2f + _area.Width * 0.08f * t;   // leicht schraeg nach rechts hinten
            h = (_area.Height - depth) * (0.95f - 0.35f * t);
        }

        private void Static3D(Graphics g, bool dimmed)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(dimmed ? 12 : 22, 255, 255, 255), 1f))
            {
                for (int k = 0; k < Rows3D; k += 3)
                {
                    Row3D(k, out float y, out float x0, out float w, out _);
                    g.DrawLine(pen, x0, y, x0 + w, y);
                }
                Row3D(0, out float y0, out float a0, out float w0, out _);
                Row3D(Rows3D - 1, out float y1, out float a1, out float w1, out _);
                for (int i = 0; i <= 4; i++)
                    g.DrawLine(pen, a0 + w0 * i / 4f, y0, a1 + w1 * i / 4f, y1);
            }
            g.SmoothingMode = sm;
        }

        private void Draw3D(Graphics g, SpectrumModel m)
        {
            if (_bars < 2) return;
            Ensure3D();
            double now = WfClock.Elapsed.TotalMilliseconds;
            if (m.Version != _histVer && now >= _histNext)
            {
                _histVer = m.Version;
                _histNext = now + 55;
                _histTop = (_histTop - 1 + Rows3D) % Rows3D;
                Array.Copy(_lvl, _hist[_histTop], _bars);
                if (_histCount < Rows3D) _histCount++;
            }
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var st = g.Save();
            g.SetClip(RectangleF.Inflate(_area, 1f, 1f));
            var pts = new PointF[_bars + 2];
            var line = new PointF[_bars];
            int rows = Math.Max(1, _histCount);
            for (int k = rows - 1; k >= 0; k--)   // von hinten nach vorn: vordere Reihen verdecken die hinteren
            {
                float[] row = k == 0 ? _lvl : _hist[(_histTop + k) % Rows3D];
                Row3D(k, out float baseY, out float x0, out float w, out float h);
                for (int i = 0; i < _bars; i++)
                {
                    float x = x0 + w * i / (_bars - 1);
                    float v = Math.Min(1f, row[i]);
                    line[i] = new PointF(x, baseY - v * h);
                    pts[i + 1] = line[i];
                }
                pts[0] = new PointF(x0, baseY);
                pts[_bars + 1] = new PointF(x0 + w, baseY);
                float fade = 1f - k / (float)Rows3D;
                using (var b = new SolidBrush(Color.FromArgb(235, 0x0b, 0x0c, 0x0f))) g.FillPolygon(b, pts);
                float peak = 0f; for (int i = 0; i < _bars; i++) peak = Math.Max(peak, row[i]);
                var col = SegColor(Math.Max(0.25f, peak), 0.5f);
                using (var b = new LinearGradientBrush(new RectangleF(x0, baseY - h - 1, w, h + 2), Color.FromArgb((int)(70 * fade), SegColor(1f, 0.5f)), Color.FromArgb(0, col), LinearGradientMode.Vertical))
                    g.FillPolygon(b, pts);
                if (Palette == 1)
                {
                    for (int i = 0; i < _bars - 1; i++)
                        using (var pen = new Pen(Color.FromArgb((int)(255 * fade), SegColor(0.8f, BarFrac(i))), Math.Max(1f, Theme.F(k == 0 ? 1.6f : 1.1f))))
                            g.DrawLine(pen, line[i], line[i + 1]);
                }
                else
                    using (var pen = new Pen(Color.FromArgb((int)(255 * fade), Theme.Lighten(col, k == 0 ? 0.2f : 0f)), Math.Max(1f, Theme.F(k == 0 ? 1.6f : 1.1f))) { LineJoin = LineJoin.Round })
                        g.DrawLines(pen, line);
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }

        // ---- Lichtorgel
        private static readonly Color[] OrganColors = { Color.FromArgb(0xff, 0x3a, 0x2a), Color.FromArgb(0xff, 0xc4, 0x2a), Color.FromArgb(0x3a, 0x8c, 0xff) };
        private static readonly string[] OrganNames = { "BASS", "MITTE", "HÖHEN" };

        private RectangleF OrganLamp(int i)
        {
            float gap = Theme.F(8);
            float d = Math.Min((_area.Width - gap * 2) / 3f, _area.Height - Theme.F(4));
            float total = d * 3 + gap * 2;
            float x = _area.X + (_area.Width - total) / 2f + i * (d + gap);
            return new RectangleF(x, _area.Y + (_area.Height - d) / 2f - Theme.F(2), d, d);
        }

        private void StaticOrgan(Graphics g, bool dimmed)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var font = Theme.Ui(7.5f, FontStyle.Bold);
            for (int i = 0; i < 3; i++)
            {
                var r = OrganLamp(i);
                // Reflektor: Chromring, dahinter die Lampe (aus)
                using (var b = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Color.FromArgb(0x9a, 0x9e, 0xa6), Color.FromArgb(0x2a, 0x2c, 0x30), LinearGradientMode.ForwardDiagonal))
                    g.FillEllipse(b, r);
                var inner = RectangleF.Inflate(r, -Theme.F(3), -Theme.F(3));
                using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0x10, 0x11, 0x14), OrganColors[i], dimmed ? 0.1f : 0.18f))) g.FillEllipse(b, inner);
                using (var pen = new Pen(Color.FromArgb(30, 0, 0, 0), 1f))
                    for (float k = inner.Width * 0.15f; k < inner.Width / 2f; k += Math.Max(2f, Theme.F(3)))
                        g.DrawEllipse(pen, inner.X + inner.Width / 2f - k, inner.Y + inner.Height / 2f - k, k * 2, k * 2);   // Riffelglas
                using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                    Theme.Str(g, OrganNames[i], font, b, new RectangleF(r.X - Theme.F(10), _area.Bottom + Theme.F(3), r.Width + Theme.F(20), Theme.F(11)), Theme.Center);
            }
            g.SmoothingMode = sm;
        }

        private float OrganLevel(int band)
        {
            int a = band == 0 ? 0 : band == 1 ? Math.Max(1, _bars / 4) : Math.Max(2, _bars * 2 / 3);
            int b = band == 0 ? Math.Max(1, _bars / 4) : band == 1 ? Math.Max(2, _bars * 2 / 3) : _bars;
            float max = 0f, sum = 0f;
            for (int i = a; i < b; i++) { max = Math.Max(max, _lvl[i]); sum += _lvl[i]; }
            float avg = sum / Math.Max(1, b - a);
            return Math.Min(1f, (max * 0.6f + avg * 0.4f) * 1.15f);
        }

        private void DrawOrgan(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 3; i++)
            {
                float v = OrganLevel(i);
                v = Math.Max(0f, (v - 0.12f) / 0.88f);   // leise bleibt aus
                if (v <= 0.01f) continue;
                var r = OrganLamp(i);
                var inner = RectangleF.Inflate(r, -Theme.F(3), -Theme.F(3));
                var c = Theme.LedLevel(OrganColors[i]);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(inner);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(255 * v), Theme.Lighten(c, 0.55f)), SurroundColors = new[] { Color.FromArgb((int)(200 * v), c) } })
                        g.FillPath(pb, path);
                }
                using (var path = new GraphicsPath())
                {
                    var glow = RectangleF.Inflate(r, Theme.F(5), Theme.F(5));
                    path.AddEllipse(glow);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(70 * v), c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                        g.FillPath(pb, path);
                }
            }
            g.SmoothingMode = sm;
        }
    }
}
