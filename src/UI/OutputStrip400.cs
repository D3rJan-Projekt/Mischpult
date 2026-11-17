using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>4.0: Surround-Pegel – bei 5.1/7.1-Geraeten zeigt das Zeiger-Feld jeden Kanal einzeln.</summary>
    internal sealed partial class OutputStrip
    {
        public static bool SurroundOn;
        private float[] _sur = new float[0], _surShown = new float[0];
        private bool _surActive;

        protected override bool VuReplaced => _surActive;
        public bool SurroundShown => _surActive;

        internal static string[] SurroundNames(int n) =>
            n == 6 ? new[] { "L", "R", "C", "LFE", "LS", "RS" }
          : n == 8 ? new[] { "L", "R", "C", "LFE", "LB", "RB", "LS", "RS" }
          : n == 4 ? new[] { "L", "R", "LB", "RB" }
          : n == 3 ? new[] { "L", "R", "C" }
          : n == 5 ? new[] { "L", "R", "C", "LS", "RS" }
          : n == 7 ? new[] { "L", "R", "C", "LFE", "LB", "RB", "C2" }
          : null;

        /// <summary>Aus dem UI-Takt: Pegel weich nachfuehren, nur bei Aenderung neu zeichnen.</summary>
        public void UpdateSurround(float dt)
        {
            var cp = _dev?.ChanPeaks;
            bool active = SurroundOn && VuShown && cp != null && cp.Length > 2;
            if (active != _surActive) { _surActive = active; _sur = new float[0]; InvalidateChrome(); }
            if (!active) return;
            if (_sur.Length != cp.Length) { _sur = new float[cp.Length]; _surShown = new float[cp.Length]; }
            bool changed = false;
            for (int i = 0; i < cp.Length; i++)
            {
                float v = cp[i];
                float db = v > 1e-6f ? 20f * (float)Math.Log10(v) : -90f;
                float pos = Math.Max(0f, Math.Min(1f, (db + 54f) / 54f));
                _sur[i] = pos > _sur[i] ? pos : Math.Max(pos, _sur[i] - Math.Min(0.1f, dt) * 1.3f);
                if (Math.Abs(_sur[i] - _surShown[i]) > 0.006f) changed = true;
            }
            if (changed) { Array.Copy(_sur, _surShown, _sur.Length); Invalidate(VuBounds); }
        }

        private void PaintSurround400(Graphics g)
        {
            if (!_surActive || _sur.Length < 3 || !IsDirty(VuBounds)) return;
            Theme.Setup(g);
            var r = VuRect;
            Theme.FillRound(g, Theme.Deep, r, Theme.F(4));
            int n = _sur.Length;
            var names = SurroundNames(n);
            float pad = Theme.F(5), labelH = Theme.F(11);
            float bw = (r.Width - 2 * pad) / n;
            float top = r.Y + pad, bottom = r.Bottom - pad - labelH;
            using (var grid = new Pen(Theme.Alpha(Theme.Grid, 160), 1f))
                foreach (float db in new[] { -6f, -18f, -36f })
                {
                    float y = bottom - (db + 54f) / 54f * (bottom - top);
                    g.DrawLine(grid, r.X + pad, y, r.Right - pad, y);
                }
            for (int i = 0; i < n; i++)
            {
                float x = r.X + pad + i * bw + bw * 0.18f, w = bw * 0.64f;
                float h = _sur[i] * (bottom - top);
                Color c = _sur[i] > (54f - 6f) / 54f ? Theme.MeterRed : _sur[i] > (54f - 18f) / 54f ? Theme.SoloAmber : Theme.LedGreen;
                if (Dimmed) c = Theme.Darken(c, 0.5f);
                using (var b = new SolidBrush(Theme.Alpha(c, 60))) g.FillRectangle(b, x, top, w, bottom - top);
                if (h > 0.5f) using (var b = new SolidBrush(c)) g.FillRectangle(b, x, bottom - h, w, h);
                Theme.Label(g, names != null && i < names.Length ? names[i] : (i + 1).ToString(), new RectangleF(r.X + pad + i * bw - Theme.F(4), bottom + Theme.F(1), bw + Theme.F(8), labelH),
                    Theme.Center, Theme.TextDim, n > 6 ? 6.5f : 7f);
            }
        }

        internal string SurroundTip => _surActive ? "\nSurround: " + _sur.Length + " Kanäle (L/R vorne, C Mitte, LFE Subwoofer, LS/RS seitlich bzw. LB/RB hinten)" : "";
    }
}
