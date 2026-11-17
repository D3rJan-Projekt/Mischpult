using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mischpult.UI
{
    /// <summary>
    /// Goniometer (Stereo-Bild) wie im Tonstudio: jeder Stereo-Wert wird ein leuchtender Punkt – senkrecht = Mitte (mono),
    /// waagerecht = Seiten (Stereo-Breite). Die Punkte leuchten kurz nach (wie ein Leuchtschirm). Darunter die
    /// Phasen-Korrelation: rechts = mono/gut, Mitte = breit, links (rot) = gegenphasig.
    /// </summary>
    internal sealed class GonioView : IDisposable
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _plot, _corrRect;
        private Bitmap _phos;
        private float[] _lum = new float[0];
        private int[] _px = new int[0];
        private int _pw, _ph;
        private float _gain = 4f, _corr, _corrShown = -9f;
        private bool _any;
        public int Version { get; private set; }
        /// <summary>Ueberschrift (2.16: mit dem gewaehlten Ausgang, z. B. "STEREO · ALLE").</summary>
        public string Caption = "STEREO";

        public void Layout(RectangleF r)
        {
            Bounds = r;
            float top = r.Y + Theme.F(19), bottom = r.Bottom - Theme.F(16);
            float side = Math.Max(10f, Math.Min(r.Width - Theme.F(12), bottom - top));
            _plot = new RectangleF((float)Math.Round(r.X + (r.Width - side) / 2f), (float)Math.Round(top), (float)Math.Round(side), (float)Math.Round(side));
            _corrRect = new RectangleF((float)Math.Round(r.X + Theme.F(10)), (float)Math.Round(r.Bottom - Theme.F(11)), (float)Math.Round(r.Width - Theme.F(20)), (float)Math.Round(Theme.F(5)));
            int pw = Math.Max(4, (int)_plot.Width), ph = Math.Max(4, (int)_plot.Height);
            if (pw != _pw || ph != _ph || _phos == null)
            {
                _phos?.Dispose();
                _pw = pw; _ph = ph;
                _phos = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);   // vormultipliert: zeichnet schneller
                _lum = new float[pw * ph];
                _px = new int[pw * ph];
            }
        }

        /// <summary>Leuchtender Bereich (zum Neuzeichnen).</summary>
        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(_plot, RectangleF.Inflate(_corrRect, Theme.F(3), Theme.F(3)))), 1, 1);

        /// <summary>Rahmen, Achsen und Beschriftung (Bild-Cache).</summary>
        public void DrawStatic(Graphics g)
        {
            var r = Bounds;
            if (r.Width < 20 || r.Height < 20) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
            {
                var tr = new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(16), Theme.F(14));
                string cap = Caption ?? "STEREO";
                var tf = Theme.Fit(g, cap, tr.Width, 9f, 7f);
                if (Theme.TextWidth(g, cap, tf) > tr.Width) { cap = "STEREO"; tf = Theme.Ui(9f, FontStyle.Bold); }
                Theme.Str(g, cap, tf, b, tr, Theme.Left);
            }
            var p = _plot;
            float cx = p.X + p.Width / 2f, cy = p.Y + p.Height / 2f, rad = p.Width / 2f;
            using (var pen = new Pen(Color.FromArgb(0x26, 0x2a, 0x31), Math.Max(1f, Theme.F(0.8f))))
            {
                g.DrawEllipse(pen, p);
                g.DrawLine(pen, cx, p.Y, cx, p.Bottom);                     // M (Mitte)
                g.DrawLine(pen, p.X, cy, p.Right, cy);                      // S (Seiten)
                float d = rad * 0.7071f;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);            // L
                g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);            // R
            }
            var font = Theme.Ui(7.5f, FontStyle.Bold);
            using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
            {
                float d = rad * 0.7071f;
                Theme.Str(g, "L", font, b, new RectangleF(cx - d - Theme.F(10), cy - d - Theme.F(10), Theme.F(10), Theme.F(10)), Theme.Center);
                Theme.Str(g, "R", font, b, new RectangleF(cx + d, cy - d - Theme.F(10), Theme.F(10), Theme.F(10)), Theme.Center);
                Theme.Str(g, "M", font, b, new RectangleF(cx + Theme.F(2), p.Y - Theme.F(1), Theme.F(10), Theme.F(10)), Theme.Left);
            }
            // Korrelation: Spur von −1 bis +1
            var cr = _corrRect;
            Theme.FillRound(g, Color.FromArgb(0x16, 0x18, 0x1c), cr, cr.Height / 2f);
            using (var pen = new Pen(Color.FromArgb(0x3a, 0x3f, 0x47), 1f))
                g.DrawLine(pen, cr.X + cr.Width / 2f, cr.Y - Theme.F(1.5f), cr.X + cr.Width / 2f, cr.Bottom + Theme.F(1.5f));
            using (var b = new SolidBrush(Color.FromArgb(0x5a, 0x62, 0x6e)))
            {
                var f2 = Theme.Ui(7f, FontStyle.Bold);
                Theme.Str(g, "−", f2, b, new RectangleF(cr.X - Theme.F(9), cr.Y - Theme.F(4), Theme.F(8), Theme.F(12)), Theme.Center);
                Theme.Str(g, "+", f2, b, new RectangleF(cr.Right + Theme.F(1), cr.Y - Theme.F(4), Theme.F(8), Theme.F(12)), Theme.Center);
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Neue Stereo-Werte (L, R abwechselnd) – Leuchtschirm nachfuehren. true = sichtbar geaendert.</summary>
        public bool Update(float[] lr, float corr, float dt)
        {
            if (_phos == null) return false;
            if (dt > 0.1f) dt = 0.1f;
            float fade = (float)Math.Exp(-dt / 0.09);   // Nachleuchten ca. 0,1 s
            bool had = _any;
            _any = false;
            for (int i = 0; i < _lum.Length; i++)
            {
                float v = _lum[i] * fade;
                _lum[i] = v < 0.01f ? 0f : v;
                if (v >= 0.01f) _any = true;
            }
            if (lr != null && lr.Length >= 4)
            {
                int n = lr.Length / 2;
                float peak = 0f;
                for (int i = 0; i < n; i++)
                {
                    float l = lr[i * 2], r = lr[i * 2 + 1];
                    float s = Math.Abs(r - l), m = Math.Abs(r + l);
                    if (s > peak) peak = s; if (m > peak) peak = m;
                }
                peak *= 0.7071f;
                // automatische Verstaerkung: die Wolke fuellt den Kreis zu etwa drei Vierteln
                float want = peak > 1e-4f ? 0.78f / peak : _gain;
                want = Math.Max(1f, Math.Min(60f, want));
                _gain += (want - _gain) * Math.Min(1f, dt * (want < _gain ? 12f : 1.5f));
                float half = _pw / 2f, k = half * _gain * 0.7071f;
                for (int i = 0; i < n; i++)
                {
                    float l = lr[i * 2], r = lr[i * 2 + 1];
                    int x = (int)(half + (r - l) * k), y = (int)(_ph / 2f - (l + r) * k);
                    if (x < 0 || y < 0 || x >= _pw || y >= _ph) continue;
                    int idx = y * _pw + x;
                    _lum[idx] = Math.Min(1f, _lum[idx] + 0.22f);
                    _any = true;
                }
                _corr += (corr - _corr) * Math.Min(1f, dt * 6f);
            }
            else _corr += (0f - _corr) * Math.Min(1f, dt * 3f);
            if (!_any && !had && Math.Abs(_corr - _corrShown) < 0.01f) return false;
            Color c = Theme.Lighten(Theme.Accent, 0.25f);
            var px = _px;   // wiederverwendet (kein neuer Speicher pro Bild)
            for (int i = 0; i < px.Length; i++)
            {
                float v = _lum[i];
                if (v <= 0f) { px[i] = 0; continue; }
                int a = (int)(Math.Min(1f, v) * 255);
                // sehr helle Stellen werden weisslich (wie auf einem Leuchtschirm)
                float w = Math.Max(0f, v - 0.7f) * 2f;
                int r = (int)(c.R + (255 - c.R) * w), gg = (int)(c.G + (255 - c.G) * w), b = (int)(c.B + (255 - c.B) * w);
                px[i] = (a << 24) | (r * a / 255 << 16) | (gg * a / 255 << 8) | (b * a / 255);   // vormultipliert
            }
            var data = _phos.LockBits(new Rectangle(0, 0, _pw, _ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { for (int y = 0; y < _ph; y++) Marshal.Copy(px, y * _pw, data.Scan0 + y * data.Stride, _pw); }
            finally { _phos.UnlockBits(data); }
            _corrShown = _corr;
            Version++;
            return true;
        }

        /// <summary>Leuchtende Punkte und Korrelations-Marke.</summary>
        public void DrawLive(Graphics g) { DrawLiveCore(g); if (Theme.CrtOn) Theme.CrtOverlay(g, _plot); }   // 3.4.6: Roehrenbild

        private void DrawLiveCore(Graphics g)
        {
            if (_phos == null || Bounds.Width < 20) return;
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(_phos, (int)_plot.X, (int)_plot.Y);
            g.InterpolationMode = im;
            var cr = _corrRect;
            float x = cr.X + cr.Width * (Math.Max(-1f, Math.Min(1f, _corr)) + 1f) / 2f;
            Color c = _corr < 0f ? Theme.MeterRed : _corr < 0.3f ? Theme.MeterYellow : Theme.MeterGreen;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float mid = cr.X + cr.Width / 2f;
            Theme.FillRound(g, Theme.Alpha(c, 110), new RectangleF(Math.Min(mid, x), cr.Y + Theme.F(1), Math.Abs(x - mid), cr.Height - Theme.F(2)), Theme.F(1.5f));
            Theme.FillRound(g, c, new RectangleF(x - Theme.F(2), cr.Y - Theme.F(1.5f), Theme.F(4), cr.Height + Theme.F(3)), Theme.F(1.5f));
            g.SmoothingMode = sm;
        }

        public void Dispose() { _phos?.Dispose(); _phos = null; }
    }
}
