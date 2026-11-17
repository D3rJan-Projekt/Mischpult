using System;
using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>3.4: PC-Auslastung in der Meterbridge – zwei kleine Zeigerinstrumente (Prozessor, Grafikkarte).</summary>
    internal sealed class PcView
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _cpu, _gpu;
        private float _cpuShown, _gpuShown;
        private int _cpuQ = -1, _gpuQ = -1;
        public int Version { get; private set; }

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float pad = Theme.F(6), top = r.Y + Theme.F(18);
            float w = (r.Width - pad * 3) / 2f, h = r.Bottom - Theme.F(6) - top;
            _cpu = new RectangleF((float)Math.Round(r.X + pad), (float)Math.Round(top), (float)Math.Round(w), (float)Math.Round(h));
            _gpu = new RectangleF((float)Math.Round(r.X + pad * 2 + w), (float)Math.Round(top), (float)Math.Round(w), (float)Math.Round(h));
        }

        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(_cpu, _gpu)), 1, 1);

        /// <summary>Aus dem Takt: Zeiger weich nachfuehren. true = neu zeichnen.</summary>
        public bool Animate(float dt)
        {
            float c = float.IsNaN(PcLoad.Cpu) ? 0f : PcLoad.Cpu, g = float.IsNaN(PcLoad.Gpu) ? 0f : PcLoad.Gpu;
            float k = Math.Min(1f, Math.Max(0.01f, dt) * 4f);
            _cpuShown += (c - _cpuShown) * k;
            _gpuShown += (g - _gpuShown) * k;
            int cq = (int)Math.Round(_cpuShown * 4) + (float.IsNaN(PcLoad.Cpu) ? 100000 : 0), gq = (int)Math.Round(_gpuShown * 4) + (float.IsNaN(PcLoad.Gpu) ? 100000 : 0);
            if (cq == _cpuQ && gq == _gpuQ) return false;
            _cpuQ = cq; _gpuQ = gq;
            Version++;
            return true;
        }

        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 60 || r.Height < 40) return;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
                Theme.Str(g, "PC-AUSLASTUNG", Theme.Fit(g, "PC-AUSLASTUNG", r.Width - Theme.F(16), 9f, 7f), b, new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(3), r.Width - Theme.F(16), Theme.F(14)), Theme.Left);
            Theme.DrawLoadGauge(g, _cpu, "CPU", 0f, true);
            Theme.DrawLoadGauge(g, _gpu, "GPU", 0f, true);
        }

        public void DrawLive(Graphics g)
        {
            if (Bounds.Width < 60 || Bounds.Height < 40) return;
            Theme.DrawLoadGauge(g, _cpu, "CPU", _cpuShown, false, !float.IsNaN(PcLoad.Cpu));
            Theme.DrawLoadGauge(g, _gpu, "GPU", _gpuShown, false, !float.IsNaN(PcLoad.Gpu));
        }
    }
}
