using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// 3.4.6 Pegel-Lupe: ein Pegel gross in einem kleinen Fenster – feine Skala von −30 bis 0 dB (jedes dB ein Strich),
    /// Spitze der letzten Sekunden als Zahl. Doppelklick auf einen Pegel oeffnet sie, das Fenster bleibt oben.
    /// </summary>
    internal sealed class MeterLupe : Form
    {
        private StripBase _strip;
        private float _l, _r, _holdL, _holdR, _holdT, _max;
        private double _maxT;
        public const float MinDb = -30f;

        public MeterLupe()
        {
            Text = "Pegel-Lupe";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(0x14, 0x15, 0x18);
            MinimumSize = new Size(Theme.Px(150), Theme.Px(260));
            Size = new Size(Theme.Px(190), Theme.Px(460));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public StripBase Strip => _strip;

        public void ShowFor(StripBase s, Point near)
        {
            _strip = s;
            _holdL = _holdR = _max = 0f;
            Text = "Pegel-Lupe – " + s.DisplayName;
            var wa = Screen.FromPoint(near).WorkingArea;
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, near.X + Theme.Px(20))), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, near.Y - Height / 2)));
            if (!Visible) Show();
            Invalidate();
        }

        /// <summary>Aus dem UI-Takt.</summary>
        public void Tick(float dt, double now)
        {
            if (_strip == null || _strip.IsDisposed) { Hide(); return; }
            float pl = _strip.LastPeakL, pr = _strip.LastPeakR;
            float fall = (float)Math.Pow(10, -20f * dt / 20f);   // 20 dB/s
            _l = pl > _l ? pl : _l * fall;
            _r = pr > _r ? pr : _r * fall;
            float m = Math.Max(pl, pr);
            if (m >= Math.Max(_holdL, _holdR)) _holdT = 0f; else _holdT += dt;
            _holdL = pl > _holdL || _holdT > 2f ? pl : _holdL;
            _holdR = pr > _holdR || _holdT > 2f ? pr : _holdR;
            if (m > _max || now - _maxT > 3.0) { _max = m; _maxT = now; }
            Invalidate();
        }

        private static float Db(float v) => v > 1e-6f ? 20f * (float)Math.Log10(v) : float.NegativeInfinity;
        private static float Frac(float db) => float.IsNegativeInfinity(db) ? 0f : Math.Max(0f, Math.Min(1f, (db - MinDb) / -MinDb));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width, h = ClientSize.Height;
            var col = _strip?.LupeColor ?? Theme.Accent;
            // Kopf: Name und Spitze der letzten 3 s
            var head = new RectangleF(Theme.F(8), Theme.F(6), w - Theme.F(16), Theme.F(22));
            using (var b = new SolidBrush(col)) Theme.Str(g, _strip?.DisplayName ?? "", Theme.Ui(10f, FontStyle.Bold), b, head, Theme.Center);
            float md = Db(_max);
            var lcd = new RectangleF(Theme.F(12), Theme.F(30), w - Theme.F(24), Theme.F(24));
            Theme.DrawLcd(g, lcd, "", null);
            Theme.LcdColors(out var bg, out var fg);
            using (var b = new SolidBrush(md > -0.1f ? Theme.MeterRed : fg))
                Theme.Str(g, float.IsNegativeInfinity(md) ? "– dB" : md.ToString("0.0", CultureInfo.InvariantCulture) + " dB", Theme.Mono(11f), b, lcd, Theme.Center);
            // Skala und zwei Balken
            float top = Theme.F(64), bottom = h - Theme.F(14);
            float barW = Math.Max(Theme.F(14), (w - Theme.F(90)) / 2f);
            float x0 = w / 2f - barW - Theme.F(4), x1 = w / 2f + Theme.F(4);
            using (var tick = new Pen(Theme.ScaleTick, 1f))
            using (var lb = new SolidBrush(Theme.ScaleLabel))
                for (int db = 0; db >= (int)MinDb; db--)
                {
                    float y = bottom - Frac(db) * (bottom - top);
                    bool major = db % 3 == 0;
                    g.DrawLine(tick, x0 - Theme.F(major ? 9 : 5), y, x0 - Theme.F(2), y);
                    g.DrawLine(tick, x1 + barW + Theme.F(2), y, x1 + barW + Theme.F(major ? 9 : 5), y);
                    if (major) Theme.Str(g, db.ToString(CultureInfo.InvariantCulture), Theme.Ui(7.5f, FontStyle.Bold), lb, new RectangleF(x0 - Theme.F(36), y - Theme.F(6), Theme.F(25), Theme.F(12)), Theme.Right);
                }
            DrawBar(g, new RectangleF(x0, top, barW, bottom - top), _l, _holdL);
            DrawBar(g, new RectangleF(x1, top, barW, bottom - top), _r, _holdR);
        }

        private static void DrawBar(Graphics g, RectangleF r, float v, float hold)
        {
            using (var b = new SolidBrush(Color.FromArgb(0x0a, 0x0b, 0x0d))) g.FillRectangle(b, r);
            float f = Frac(Db(v));
            if (f > 0f)
            {
                var fill = new RectangleF(r.X, r.Bottom - f * r.Height, r.Width, f * r.Height);
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.MeterRed, Theme.MeterGreen, LinearGradientMode.Vertical))
                {
                    var blend = new ColorBlend { Colors = new[] { Theme.MeterRed, Theme.MeterRed, Theme.SoloAmber, Theme.MeterGreen, Theme.MeterGreen }, Positions = new[] { 0f, 0.1f, 0.2f, 0.4f, 1f } };
                    b.InterpolationColors = blend;
                    g.FillRectangle(b, fill);
                }
            }
            float hf = Frac(Db(hold));
            if (hf > 0f)
                using (var p = new Pen(Color.White, Theme.F(2))) g.DrawLine(p, r.X, r.Bottom - hf * r.Height, r.Right, r.Bottom - hf * r.Height);
        }
    }
}
