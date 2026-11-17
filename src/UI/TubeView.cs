using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Mischpult.UI
{
    /// <summary>3.5: Gluehende Roehren in der Meterbridge – drei Verstaerkerroehren, die mit dem Pegel des MASTERs aufgluehen.</summary>
    internal sealed class TubeView
    {
        public RectangleF Bounds { get; private set; }
        private readonly RectangleF[] _tubes = new RectangleF[3];
        private readonly float[] _glow = new float[3];
        private readonly int[] _q = { -1, -1, -1 };
        public int Version { get; private set; }
        /// <summary>Pegel (linear) von aussen, links/rechts.</summary>
        public static float LevelL, LevelR;
        private double _flicker;

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float pad = Theme.F(8), top = r.Y + Theme.F(16);
            float w = (r.Width - pad * 4) / 3f, h = r.Bottom - Theme.F(4) - top;
            for (int i = 0; i < 3; i++)
                _tubes[i] = new RectangleF((float)Math.Round(r.X + pad + i * (w + pad)), (float)Math.Round(top), (float)Math.Round(w), (float)Math.Round(h));
        }

        public Rectangle LiveBounds
        {
            get
            {
                var u = RectangleF.Union(RectangleF.Union(_tubes[0], _tubes[1]), _tubes[2]);
                return Rectangle.Inflate(Rectangle.Ceiling(u), 2, 2);
            }
        }

        /// <summary>Aus dem Takt: Gluehen nachfuehren (traege wie ein Heizfaden). true = neu zeichnen.</summary>
        public bool Animate(float dt)
        {
            _flicker += dt;
            float l = Math.Min(1f, LevelL), r = Math.Min(1f, LevelR), m = Math.Max(l, r);
            float[] want = { l, m, r };
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                float w = 0.25f + 0.75f * (float)Math.Sqrt(Math.Max(0f, want[i]));   // Grundgluehen, auch in der Stille
                w *= 1f + 0.03f * (float)Math.Sin(_flicker * (7.3 + i * 2.1));      // leichtes Flackern
                float k = Math.Min(1f, Math.Max(0.01f, dt) * (w > _glow[i] ? 9f : 3f));
                _glow[i] += (w - _glow[i]) * k;
                int q = (int)Math.Round(_glow[i] * 40);
                if (q != _q[i]) { _q[i] = q; changed = true; }
            }
            if (changed) Version++;
            return changed;
        }

        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 50 || r.Height < 36) return;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
                Theme.Str(g, "RÖHREN", Theme.Fit(g, "RÖHREN", r.Width - Theme.F(16), 9f, 7f), b, new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(2), r.Width - Theme.F(16), Theme.F(14)), Theme.Left);
        }

        public void DrawLive(Graphics g)
        {
            if (Bounds.Width < 50 || Bounds.Height < 36) return;
            using (var b = new SolidBrush(Color.FromArgb(0x0b, 0x0c, 0x0f))) g.FillRectangle(b, LiveBounds);
            for (int i = 0; i < 3; i++) Theme.DrawTube(g, _tubes[i], _glow[i]);
        }
    }
}
