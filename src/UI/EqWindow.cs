using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>Grosser EQ eines Kanals: 10-Band-Grafik-EQ, Gesamtkurve (inkl. LO/MID/HI) und Live-Spektrum.</summary>
    internal sealed class EqWindow : Form
    {
        private readonly MixerForm _mixer;
        private ChannelConfig _c;
        private readonly Timer _timer = new Timer { Interval = 50 };
        private readonly float[] _spec = new float[SpectrumAnalyzer.Bands];
        private bool _hasSpec;
        private int _drag = -1, _hover = -1;
        private float _dragStartDb;
        private int _dragStartY;
        private string _hoverButton;

        private const float RangeDb = 12f;

        public EqWindow(MixerForm mixer, ChannelConfig c)
        {
            _mixer = mixer;
            _c = c;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.Px(660), Theme.Px(470));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            for (int i = 0; i < _spec.Length; i++) _spec[i] = -120f;
            UpdateTitle();
            _timer.Tick += (s, e) => OnTick();
            _timer.Start();
        }

        public ChannelConfig Channel => _c;
        /// <summary>2.19: EQ eines Ausgangs (statt eines Kanals).</summary>
        private bool IsOutput => _c.Id != null && _c.Id.StartsWith(MixerForm.OutEqPrefix);

        public void SetChannel(ChannelConfig c)
        {
            _c = c;
            for (int i = 0; i < _spec.Length; i++) _spec[i] = -120f;
            UpdateTitle();
            Invalidate();
        }

        private void UpdateTitle() => Text = "EQ – " + _c.Name + " – Mischpult";

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _mixer.OnBigEqClosed();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- Bereiche
        private RectangleF Graph => new RectangleF(Theme.F(52), Theme.F(16), ClientSize.Width - Theme.F(70), Theme.F(220));
        private RectangleF Sliders => new RectangleF(Theme.F(52), Theme.F(262), ClientSize.Width - Theme.F(70), Theme.F(140));
        private RectangleF BandRect(int i)
        {
            var r = Sliders;
            float w = r.Width / 10f;
            return new RectangleF(r.X + w * i, r.Y, w, r.Height);
        }
        private RectangleF BtnEq => new RectangleF(Theme.F(16), ClientSize.Height - Theme.F(46), Theme.F(110), Theme.F(30));
        private RectangleF BtnFlat => new RectangleF(Theme.F(134), ClientSize.Height - Theme.F(46), Theme.F(150), Theme.F(30));
        private RectangleF BtnClose => new RectangleF(ClientSize.Width - Theme.F(126), ClientSize.Height - Theme.F(46), Theme.F(110), Theme.F(30));

        private static double FreqToX(RectangleF r, double f) => r.X + r.Width * Math.Log(f / 20.0) / Math.Log(1000.0);

        private float Band(int i) => _c.Geq != null && i >= 0 && i < _c.Geq.Length ? _c.Geq[i] : 0f;

        // ---------------------------------------------------------------- Ablauf
        private void OnTick()
        {
            var spec = _mixer.CurrentSpectrum;
            if (spec != null && spec.Length == _spec.Length)
            {
                for (int i = 0; i < _spec.Length; i++)
                    _spec[i] = spec[i] > _spec[i] ? spec[i] : Math.Max(spec[i], _spec[i] - 2.5f); // schnell hoch, langsam runter
                _hasSpec = true;
            }
            else
            {
                for (int i = 0; i < _spec.Length; i++) _spec[i] = Math.Max(-120f, _spec[i] - 3f);
                _hasSpec = false;
            }
            Invalidate();
        }

        // ---------------------------------------------------------------- Zeichnen
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            var gr = Graph;
            Theme.FillRound(g, Theme.Deep, RectangleF.Inflate(gr, Theme.F(4), Theme.F(4)), Theme.F(6));

            // Raster
            using (var pen = new Pen(Theme.Grid, Theme.F(1)))
            using (var lb = new SolidBrush(Theme.TextFaint))
            {
                foreach (var f in new[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
                {
                    float x = (float)FreqToX(gr, f);
                    g.DrawLine(pen, x, gr.Y, x, gr.Bottom);
                    Theme.Str(g, f >= 1000 ? (f / 1000) + "k" : f.ToString(), Theme.Ui(9f), lb, new RectangleF(x - Theme.F(20), gr.Bottom + Theme.F(4), Theme.F(40), Theme.F(14)), Theme.Center);
                }
                for (int db = -12; db <= 12; db += 6)
                {
                    float y = DbToY(gr, db);
                    using (var p2 = new Pen(db == 0 ? Theme.GridZero : Theme.Grid, Theme.F(1)))
                        g.DrawLine(p2, gr.X, y, gr.Right, y);
                    Theme.Str(g, (db > 0 ? "+" : "") + db, Theme.Ui(9f), lb, new RectangleF(gr.X - Theme.F(44), y - Theme.F(7), Theme.F(38), Theme.F(14)), Theme.Right);
                }
            }

            // Spektrum (dBFS -90..0 auf volle Hoehe)
            using (var path = new GraphicsPath())
            {
                var pts = new PointF[_spec.Length + 2];
                for (int i = 0; i < _spec.Length; i++)
                {
                    double f = SpectrumAnalyzer.BandFreq(i + 0.5);
                    float frac = Math.Max(0f, Math.Min(1f, (_spec[i] + 90f) / 90f));
                    pts[i + 1] = new PointF((float)FreqToX(gr, f), gr.Bottom - frac * gr.Height);
                }
                pts[0] = new PointF(gr.X, gr.Bottom);
                pts[pts.Length - 1] = new PointF(gr.Right, gr.Bottom);
                pts[1].X = gr.X;
                path.AddLines(pts);
                path.CloseFigure();
                var old = g.Clip;
                g.SetClip(gr);
                using (var b = new LinearGradientBrush(gr, Theme.Alpha(Theme.Accent, 110), Theme.Alpha(Theme.Accent, 20), LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                g.Clip = old;
            }
            if (!_hasSpec)
            {
                string hint = IsOutput ? "" : !_c.EqOn ? "Spektrum erscheint, sobald der EQ an ist" : "Spektrum erscheint, sobald der Kanal Ton ausgibt";
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, hint, Theme.Ui(11f), b, gr, Theme.Center);
            }

            // Gesamtkurve: LO/MID/HI + 10 Baender
            var low = new Biquad(); low.LowShelf(48000, DspMixer.FreqLow, _c.EqLow);
            var mid = new Biquad(); mid.Peaking(48000, DspMixer.FreqMid, _c.EqMid, 0.7);
            var high = new Biquad(); high.HighShelf(48000, DspMixer.FreqHigh, _c.EqHigh);
            var bands = new Biquad[10];
            for (int i = 0; i < 10; i++) { bands[i] = new Biquad(); bands[i].Peaking(48000, GraphicEq.Freqs[i], Band(i), GraphicEq.Q); }
            int steps = (int)gr.Width;
            var curve = new PointF[steps];
            for (int i = 0; i < steps; i++)
            {
                double f = 20.0 * Math.Pow(1000.0, i / (double)(steps - 1));
                double m = low.MagnitudeAt(48000, f) * mid.MagnitudeAt(48000, f) * high.MagnitudeAt(48000, f);
                foreach (var b in bands) m *= b.MagnitudeAt(48000, f);
                float db = (float)(20 * Math.Log10(Math.Max(m, 1e-6)));
                curve[i] = new PointF(gr.X + i, DbToY(gr, Math.Max(-RangeDb * 1.5f, Math.Min(RangeDb * 1.5f, db))));
            }
            {
                var old = g.Clip;
                g.SetClip(gr);
                using (var pen = new Pen(_c.EqOn ? Color.FromArgb(_c.Color) : Theme.TextDim, Theme.F(2.2f)) { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, curve);
                g.Clip = old;
            }
            Theme.Label(g, IsOutput ? (_c.EqOn ? "Kurve: wirkt auf alles, was auf diesem Ausgang über das Mischpult läuft" : "Ausgangs-EQ ist aus – Änderungen schalten ihn ein")
                    : _c.EqOn ? "Kurve: Gesamtwirkung (LO/MID/HI im Kanal + 10 Bänder)" : "EQ ist aus – Änderungen schalten ihn ein",
                new RectangleF(gr.X + Theme.F(8), gr.Y + Theme.F(4), gr.Width - Theme.F(16), Theme.F(14)), Theme.Left, _c.EqOn ? Theme.TextDim : Theme.WarnText, 9f);

            // 10 Baender
            for (int i = 0; i < 10; i++) DrawBand(g, i);

            DrawButton(g, BtnEq, _c.EqOn ? "EQ AN" : "EQ AUS", "eq", _c.EqOn ? Theme.LedGreen : (Color?)null);
            DrawButton(g, BtnFlat, "10 BÄNDER AUF 0", "flat", null);
            DrawButton(g, BtnClose, "SCHLIESSEN", "close", null);
        }

        private static float DbToY(RectangleF r, float db) => r.Y + r.Height / 2 - db / (RangeDb * 2.2f) * r.Height;

        private void DrawBand(Graphics g, int i)
        {
            var r = BandRect(i);
            float cx = r.X + r.Width / 2;
            var track = new RectangleF(cx - Theme.F(3), r.Y + Theme.F(18), Theme.F(6), r.Height - Theme.F(40));
            Theme.FillRound(g, Theme.Deep, track, Theme.F(3));
            float zeroY = track.Y + track.Height / 2;
            using (var p = new Pen(Theme.GridZero, Theme.F(1))) g.DrawLine(p, cx - Theme.F(10), zeroY, cx + Theme.F(10), zeroY);
            float v = Band(i);
            float y = zeroY - v / RangeDb * (track.Height / 2);
            Color col = _c.EqOn ? Color.FromArgb(_c.Color) : Theme.TextDim;
            if (Math.Abs(v) > 0.05f)
                using (var b = new SolidBrush(Theme.Alpha(col, 160)))
                    g.FillRectangle(b, cx - Theme.F(1.5f), Math.Min(y, zeroY), Theme.F(3), Math.Abs(y - zeroY));
            var cap = new RectangleF(cx - Theme.F(13), y - Theme.F(7), Theme.F(26), Theme.F(14));
            Theme.DrawFaderCap(g, cap, false, _hover == i || _drag == i, col);
            Theme.Label(g, (v > 0.05f ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture), new RectangleF(r.X, r.Y, r.Width, Theme.F(14)), Theme.Center,
                Math.Abs(v) > 0.05f ? Theme.ValueText : Theme.TextFaint, 9f);
            Theme.Label(g, GraphicEq.Labels[i], new RectangleF(r.X, r.Bottom - Theme.F(16), r.Width, Theme.F(14)), Theme.Center, Theme.TextDim, 9.5f);
        }

        private void DrawButton(Graphics g, RectangleF r, string text, string id, Color? lit)
        {
            bool hover = _hoverButton == id;
            if (lit.HasValue) Theme.DrawKey(g, r, text, true, lit.Value, hover);
            else Theme.DrawKey(g, r, text, false, Theme.TextDim, hover);
        }

        // ---------------------------------------------------------------- 3.4: EQ malen
        /// <summary>Mit gedrueckter Maus ueber die Baender bzw. die Kurve ziehen: jedes Band springt auf die Hoehe der Maus.</summary>
        public static bool PaintOn;
        private bool _painting;
        private int _lastPaint = -1;
        private float _lastPaintDb;

        private void PaintAt(Point p)
        {
            bool inGraph = Graph.Contains(p) && !Sliders.Contains(p);
            var area = inGraph ? Graph : Sliders;
            int band = Math.Max(0, Math.Min(9, (int)((p.X - area.X) / (area.Width / 10f))));
            float db;
            if (inGraph) db = (area.Y + area.Height / 2 - p.Y) / area.Height * (RangeDb * 2.2f);
            else
            {
                var r = BandRect(band);
                var track = new RectangleF(r.X, r.Y + Theme.F(18), r.Width, r.Height - Theme.F(40));
                db = (track.Y + track.Height / 2 - p.Y) / (track.Height / 2) * RangeDb;
            }
            db = Math.Max(-RangeDb, Math.Min(RangeDb, db));
            // uebersprungene Baender dazwischen gleichmaessig mitnehmen
            if (_lastPaint >= 0 && Math.Abs(band - _lastPaint) > 1)
            {
                int dir = band > _lastPaint ? 1 : -1;
                for (int i = _lastPaint + dir; i != band; i += dir)
                {
                    float t = (i - _lastPaint) / (float)(band - _lastPaint);
                    SetBand(i, _lastPaintDb + (db - _lastPaintDb) * t);
                }
            }
            SetBand(band, db);
            _lastPaint = band; _lastPaintDb = db;
        }

        // ---------------------------------------------------------------- Maus
        private int BandAt(Point p)
        {
            for (int i = 0; i < 10; i++) if (BandRect(i).Contains(p)) return i;
            return -1;
        }

        private void SetBand(int i, float db)
        {
            db = Math.Max(-RangeDb, Math.Min(RangeDb, (float)Math.Round(db * 2) / 2f));
            _mixer.SetGraphicBand(_c, i, db);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (BtnClose.Contains(e.Location)) { Close(); return; }
            if (BtnFlat.Contains(e.Location)) { for (int i = 0; i < 10; i++) _mixer.SetGraphicBand(_c, i, 0f, false); _mixer.OnChannelEdited(_c); Invalidate(); return; }
            if (BtnEq.Contains(e.Location)) { _mixer.ToggleChannelEq(_c); Invalidate(); return; }
            if (PaintOn && (Sliders.Contains(e.Location) || Graph.Contains(e.Location)))
            {
                // 3.4: EQ malen – mit gedrueckter Maus ueber die Baender ziehen
                _painting = true; _lastPaint = -1;
                PaintAt(e.Location);
                Capture = true;
                return;
            }
            int b = BandAt(e.Location);
            if (b < 0) return;
            _drag = b;
            _dragStartDb = Band(b);
            _dragStartY = e.Y;
            var r = BandRect(b);
            var track = new RectangleF(r.X, r.Y + Theme.F(18), r.Width, r.Height - Theme.F(40));
            float zeroY = track.Y + track.Height / 2;
            float capY = zeroY - _dragStartDb / RangeDb * (track.Height / 2);
            if (Math.Abs(e.Y - capY) > Theme.F(10)) // daneben geklickt: direkt dorthin springen
            {
                _dragStartDb = (zeroY - e.Y) / (track.Height / 2) * RangeDb;
                SetBand(b, _dragStartDb);
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_painting) { PaintAt(e.Location); return; }
            if (_drag >= 0)
            {
                var r = BandRect(_drag);
                float half = (r.Height - Theme.F(40)) / 2;
                float scale = (ModifierKeys & Keys.Shift) != 0 ? 0.25f : 1f;
                SetBand(_drag, _dragStartDb + (_dragStartY - e.Y) / half * RangeDb * scale);
                return;
            }
            int b = BandAt(e.Location);
            string hb = BtnEq.Contains(e.Location) ? "eq" : BtnFlat.Contains(e.Location) ? "flat" : BtnClose.Contains(e.Location) ? "close" : null;
            if (b != _hover || hb != _hoverButton) { _hover = b; _hoverButton = hb; Cursor = b >= 0 || hb != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_painting) { _painting = false; Capture = false; _mixer.OnChannelEdited(_c); return; }
            if (_drag >= 0) { _drag = -1; Capture = false; _mixer.OnChannelEdited(_c); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int b = BandAt(e.Location);
            if (b >= 0) { SetBand(b, 0f); _mixer.OnChannelEdited(_c); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int b = BandAt(e.Location);
            if (b >= 0) { SetBand(b, Band(b) + e.Delta / 120f * 0.5f); _mixer.OnChannelEdited(_c); }
        }
    }
}
