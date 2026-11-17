using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// Zeiger eines VU-Meters mit eigener Traegheit: wie im MASTER-Zug (steigt in ca. 0,3 s, faellt gemaechlich),
    /// dazu Schleppzeiger und PEAK-Leuchte.
    /// </summary>
    internal sealed class VuMotion
    {
        public float P, Pk = -0.02f;
        public bool Led;
        private float _v, _hold, _pkHold, _ledT;
        private float _shownP = -9f, _shownPk = -9f;
        private bool _shownLed;

        /// <summary>Neuer Pegel (Spitze je Bild, linear). true = sichtbar veraendert.</summary>
        public bool Update(float a, float dt)
        {
            if (dt <= 0f) return false;
            if (dt > 0.1f) dt = 0.1f;
            float x = StripBase.VuTarget(a);
            float tau = StripBase.VuTau();
            if (tau > 0f)
            {
                if (x >= _hold) _hold = x;
                else _hold += (x - _hold) * (1f - (float)Math.Exp(-dt / tau));
                x = _hold;
            }
            float w = StripBase.VuOmega; const float z = 0.8f;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = w * w * (x - P) - 2f * z * w * _v;
                _v += acc * h;
                P += _v * h;
                if (P < -0.015f) { P = -0.015f; if (_v < 0) _v = 0; }
                if (P > 1.06f) { P = 1.06f; if (_v > 0) _v = -_v * 0.2f; }
            }
            if (P >= Pk) { Pk = P; _pkHold = 1.6f; }
            else if (_pkHold > 0f) _pkHold -= dt;
            else Pk = Math.Max(P, Pk - dt * 0.3f);
            if (a >= StripBase.PeakLin) { Led = true; _ledT = 1f; }
            else { _ledT -= dt; Led = _ledT > 0f; }
            bool led = StripBase.VuPeakLed && Led;
            if (Math.Abs(P - _shownP) > 0.002f || (StripBase.VuPeakNeedle && Math.Abs(Pk - _shownPk) > 0.002f) || led != _shownLed)
            {
                _shownP = P; _shownPk = Pk; _shownLed = led;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Grosses VU-Paar (links/rechts) in einem eigenen Fenster – frei vergroesserbar, auf Wunsch immer im Vordergrund,
    /// z. B. auf dem zweiten Bildschirm. Darunter auf Wunsch das Spektrum – oder nur das Spektrum allein.
    /// </summary>
    internal sealed class VuWindow : Form
    {
        private readonly MixerForm _mixer;
        private readonly VuPanel _panel;
        public readonly VuMotion MeterL = new VuMotion(), MeterR = new VuMotion();
        public SpectrumModel Spectrum;
        public bool ShowSpectrum = true;
        /// <summary>false = nur das Spektrum (ohne VU-Meter).</summary>
        public bool ShowVu = true;
        private string _device;
        private string _caption = "SPEKTRUM";

        public VuWindow(MixerForm mixer, SpectrumModel spectrum)
        {
            _mixer = mixer;
            Spectrum = spectrum;
            Text = "VU-Meter";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Theme.Px(320), Theme.Px(190));
            Size = new Size(Theme.Px(560), Theme.Px(400));
            BackColor = MixerForm.BodyColor;
            KeyPreview = true;
            try { Icon = mixer.Icon; } catch { }
            _panel = new VuPanel(this) { Dock = DockStyle.Fill };
            Controls.Add(_panel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F11) FullScreen.Toggle(this);                       // 2.18: Vollbild
            else if (e.KeyCode == Keys.Escape) { if (FullScreen.IsOn(this)) FullScreen.Set(this, false); else Close(); }
        }

        /// <summary>Aus dem UI-Takt: Pegel des Ausgangs (L, R) und Geraetename.</summary>
        public void Feed(float l, float r, string device, float dt)
        {
            if (device != _device)
            {
                _device = device;
                UpdateTitle();
            }
            bool a = MeterL.Update(l, dt), b = MeterR.Update(r, dt);
            if (ShowVu)
            {
                if (a) _panel.InvalidateMeter(0);
                if (b) _panel.InvalidateMeter(1);
            }
            _panel.TickSpectrum();
        }

        /// <summary>Ueberschrift des Spektrums (welcher Ausgang).</summary>
        public string Caption
        {
            get => _caption;
            set { if (value != _caption) { _caption = value; _panel.Restyle(); } }
        }

        /// <summary>Titel: was zu sehen ist und von welchem Ausgang.</summary>
        public void UpdateTitle()
        {
            string what = !ShowVu ? "Spektrum" : ShowSpectrum ? "VU-Meter & Spektrum" : "VU-Meter";
            Text = what + (string.IsNullOrEmpty(_device) ? "" : " – " + _device);
        }

        /// <summary>Aussehen geaendert (Design, Skala, Farbe): neu zeichnen.</summary>
        public void Restyle()
        {
            UpdateTitle();
            BackColor = MixerForm.BodyColor;
            if (IsHandleCreated) DarkTitle.Apply(Handle);
            _panel.Restyle();
        }

        internal void ShowMenu(Point screen) => _mixer.ShowVuWindowMenu(this, screen);

        // ------------------------------------------------------------ Zeichenflaeche

        private sealed class VuPanel : Control
        {
            private readonly VuWindow _win;
            private readonly PaintCache _cache = new PaintCache();
            private readonly SpectrumView _spec = new SpectrumView();
            private bool _dirty = true;
            private RectangleF _vuL, _vuR, _specRect;
            private float _vuScale = 1f, _specScale = 1f;
            private int _specVersion = -1;
            private readonly ToolTip _tip = PultTip.Make(new ToolTip { InitialDelay = 700, AutoPopDelay = 6000 });

            public VuPanel(VuWindow win)
            {
                _win = win;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = MixerForm.BodyColor;
                _tip.SetToolTip(this, "Großes VU-Meter und Spektrum – Rechtsklick: was angezeigt wird, welcher Ausgang, immer im Vordergrund");
            }

            public void Restyle() { _dirty = true; _gUpload = true; BackColor = MixerForm.BodyColor; Invalidate(); }

            protected override void OnResize(EventArgs e) { base.OnResize(e); _dirty = true; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Right) _win.ShowMenu(PointToScreen(e.Location));
            }

            /// <summary>Zeichnungsgroesse eines VU-Meters bei Skalierung 1 (wie im MASTER-Zug).</summary>
            private const float BaseW = 99f, BaseH = 58f;

            private void LayoutAll()
            {
                float S = Theme.S, m = Theme.F(14);
                float w = ClientSize.Width, h = ClientSize.Height;
                _spec.Caption = _win.Caption;
                if (!_win.ShowVu)
                {
                    // nur das Spektrum: fuellt das ganze Fenster
                    _vuL = _vuR = RectangleF.Empty;
                    _specRect = new RectangleF(m, m, w - 2 * m, h - 2 * m);
                    _specScale = Math.Max(S, (float)Math.Floor(Math.Min(_specRect.Height / Theme.F(110) * S, w / Theme.F(360) * S) * 4f) / 4f);
                    _specScale = Math.Min(_specScale, S * 2.5f);
                    float sc0 = _specScale;
                    Theme.WithScale(sc0, () => _spec.Layout(_specRect));
                    return;
                }
                bool spec = _win.ShowSpectrum && _win.Spectrum != null && h > Theme.F(260);
                float specH = spec ? (float)Math.Round(Math.Max(Theme.F(90), h * 0.32f)) : 0f;
                float labelH = Theme.F(20);
                float areaH = h - 2 * m - (spec ? specH + m : 0f) - labelH;
                bool dual = Theme.VuDual;   // 3.1: ein Instrument mit zwei Zeigern
                float cellW = dual ? Math.Min(w - 2 * m, (w - 3 * m) / 2f * 1.35f) : (w - 3 * m) / 2f;
                // Skalierung in Viertel-Schritten (sonst entstuenden beim Ziehen am Fenster sehr viele Schriftgroessen)
                float k = Math.Max(1f, Math.Min(cellW / BaseW, areaH / BaseH));
                k = (float)Math.Floor(k * 4f) / 4f;
                _vuScale = k;
                float vw = (float)Math.Round(BaseW * k), vh = (float)Math.Round(BaseH * k);
                float top = (float)Math.Round(m + Math.Max(0f, (areaH - vh) / 2f));
                if (dual)
                {
                    _vuL = new RectangleF((float)Math.Round((w - vw) / 2f), top, vw, vh);
                    _vuR = RectangleF.Empty;
                }
                else
                {
                    _vuL = new RectangleF((float)Math.Round(m + (cellW - vw) / 2f), top, vw, vh);
                    _vuR = new RectangleF((float)Math.Round(2 * m + cellW + (cellW - vw) / 2f), top, vw, vh);
                }
                if (spec)
                {
                    _specScale = Math.Max(S, (float)Math.Floor(Math.Min(specH / Theme.F(90) * S, w / Theme.F(330) * S) * 4f) / 4f);
                    _specRect = new RectangleF(m, (float)Math.Round(h - m - specH), w - 2 * m, specH);
                    float sc = _specScale;
                    Theme.WithScale(sc, () => _spec.Layout(_specRect));
                }
                else _specRect = RectangleF.Empty;
            }

            private RectangleF MeterBounds(int i) => !_win.ShowVu ? RectangleF.Empty : RectangleF.Inflate(i == 0 || Theme.VuDual ? _vuL : _vuR, Theme.F(3) * _vuScale, Theme.F(3) * _vuScale);

            public void InvalidateMeter(int i)
            {
                if (!Visible || !_win.ShowVu) return;
                if (_gpuStyles) { Invalidate(); return; }   // 3.0: die Grafikkarte zeichnet immer das ganze Bild
                Invalidate(Rectangle.Ceiling(MeterBounds(i)));
            }

            public void TickSpectrum()
            {
                if (_specRect.IsEmpty || _win.Spectrum == null || !Visible) return;
                if (SpectrumView.Waterfall) { bool pushed = false; Theme.WithScale(_specScale, () => pushed = _spec.Push(_win.Spectrum)); if (!pushed) return; }
                else if (_win.Spectrum.Version == _specVersion) return;
                _specVersion = _win.Spectrum.Version;
                if (_gpuStyles) { Invalidate(); return; }
                Invalidate(_spec.LiveBounds);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            // ---------------------------------------------------------------- 3.0: Grafikkarte (Direct2D)
            private Native.D2DCanvas _gpu;
            private Native.D2DImage _gStatic, _gWf;
            private readonly Native.D2DImage[] _gCover = new Native.D2DImage[2];
            private readonly RectangleF[] _gCoverRect = new RectangleF[2];
            private bool _gUpload = true, _gpuStyles;
            private int _gGen = -1, _gWfVer = -1, _gWfEpoch = -1;
            private readonly List<RectangleF> _segR = new List<RectangleF>();
            private readonly List<Color> _segC = new List<Color>();

            /// <summary>Ist gerade die Grafikkarte am Zeichnen? (fuer die Anzeige)</summary>
            public bool GpuActive => _gpu != null && _gpuStyles;

            private void SetGpuStyles(bool on)
            {
                if (on == _gpuStyles) return;
                _gpuStyles = on;
                SetStyle(ControlStyles.OptimizedDoubleBuffer, !on);   // die Grafikkarte zeichnet direkt ins Fenster
            }

            private void DropGpu()
            {
                foreach (var im in new[] { _gStatic, _gWf, _gCover[0], _gCover[1] }) im?.Dispose();
                _gStatic = _gWf = _gCover[0] = _gCover[1] = null;
                _gpu?.Dispose();
                _gpu = null;
                SetGpuStyles(false);
                _dirty = true;   // feste Teile fuer das normale Zeichnen neu
            }

            /// <summary>Feste Teile (wie bisher mit GDI+ gezeichnet) und Abdeckung/Glas der VU-Meter als Bilder hochladen.</summary>
            private void UploadStatic()
            {
                foreach (var im in new[] { _gStatic, _gWf, _gCover[0], _gCover[1] }) im?.Dispose();
                _gStatic = _gWf = _gCover[0] = _gCover[1] = null;
                _gWfVer = -1; _gWfEpoch = -1;
                int w = ClientSize.Width, h = ClientSize.Height;
                using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) DrawStatic(g);
                    _gStatic = _gpu.Upload(bmp);
                }
                for (int i = 0; i < 2 && _win.ShowVu; i++)
                {
                    if (i == 1 && Theme.VuDual) break;
                    var mb = Rectangle.Ceiling(MeterBounds(i));
                    if (mb.Width <= 0 || mb.Height <= 0) continue;
                    _gCoverRect[i] = mb;
                    var r = i == 0 ? _vuL : _vuR;
                    using (var bmp = new Bitmap(mb.Width, mb.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            Theme.Setup(g);
                            g.TranslateTransform(-mb.X, -mb.Y);
                            Theme.WithScale(_vuScale, () => Theme.DrawVuCover(g, r));
                        }
                        _gCover[i] = _gpu.Upload(bmp);
                    }
                }
                _gGen = _gpu.Generation;
            }

            [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions, System.Security.SecurityCritical]
            private bool TryGpuPaint()
            {
                if (!GpuDraw.Enabled || !SpectrumView.GpuCapable || Theme.VuKind == 3) { if (_gpu != null) DropGpu(); return false; }   // 3.1: Kurve/rund zeichnet das Pult selbst
                try
                {
                    int w = ClientSize.Width, h = ClientSize.Height;
                    if (_gpu == null) { _gpu = new Native.D2DCanvas(this); _gUpload = true; }
                    SetGpuStyles(true);
                    GpuDraw.BeginFrame();
                    if (_dirty) { _dirty = false; LayoutAll(); _gUpload = true; }
                    if (!_gpu.Begin(w, h)) { GpuDraw.Fail(_gpu.Error ?? "Start"); DropGpu(); return false; }
                    if (_gUpload || _gGen != _gpu.Generation || _gStatic == null)
                    {
                        _gUpload = false;
                        UploadStatic();
                        if (_gStatic == null) { _gpu.End(); GpuDraw.Fail(_gpu.Error ?? "Bild"); DropGpu(); return false; }
                    }
                    _gpu.Image(_gStatic, new RectangleF(0, 0, w, h));
                    for (int i = 0; i < 2 && _win.ShowVu; i++)
                    {
                        var mo = i == 0 ? _win.MeterL : _win.MeterR;
                        var r = i == 0 ? _vuL : _vuR;
                        var gpu = _gpu;
                        if (Theme.VuDual)
                        {
                            if (i == 1) break;
                            bool ledOn = _win.MeterL.Led || _win.MeterR.Led;
                            Theme.WithScale(_vuScale, () => Theme.DrawVuPointer2Gpu(gpu, r, _win.MeterL.P, _win.MeterR.P, false, StripBase.VuPeakLed ? (ledOn ? 2 : 1) : 0));
                            if (_gCover[0] != null) _gpu.Image(_gCover[0], _gCoverRect[0]);
                            continue;
                        }
                        Theme.WithScale(_vuScale, () => Theme.DrawVuPointerGpu(gpu, r, mo.P, false, StripBase.VuPeakNeedle ? mo.Pk : -1f, StripBase.VuPeakLed ? (mo.Led ? 2 : 1) : 0));
                        if (_gCover[i] != null) _gpu.Image(_gCover[i], _gCoverRect[i]);
                    }
                    if (!_specRect.IsEmpty && _win.Spectrum != null)
                    {
                        if (SpectrumView.Waterfall) DrawWaterfallGpu();
                        else
                        {
                            Theme.WithScale(_specScale, () => _spec.LiveSegments(_win.Spectrum, _segR, _segC));
                            for (int k = 0; k < _segR.Count; k++) Theme.FillSegGpu(_gpu, _segR[k], _segC[k]);
                        }
                    }
                    if (!_gpu.End()) { GpuDraw.Fail(_gpu.Error ?? "Ende"); DropGpu(); Invalidate(); return false; }
                    GpuDraw.FrameOk();
                    return true;
                }
                catch (Exception ex)
                {
                    GpuDraw.Fail(ex.GetType().Name + ": " + ex.Message);
                    try { DropGpu(); } catch { }
                    Invalidate();
                    return false;
                }
            }

            /// <summary>Wasserfall: Bild auf der Grafikkarte, nur neue Zeilen werden hochgeladen; gezeichnet in zwei Teilen (Ringpuffer).</summary>
            private void DrawWaterfallGpu()
            {
                int[] px = null;
                Theme.WithScale(_specScale, () => px = _spec.WaterfallPixels);
                int ww = _spec.WaterfallWidth, wh = _spec.WaterfallHeight, top = _spec.WaterfallTop;
                if (px == null || ww < 1 || wh < 1) return;
                if (_gWf == null || _gWf.Width != ww || _gWf.Height != wh || _gWfEpoch != _spec.WaterfallEpoch)
                {
                    _gWf?.Dispose();
                    _gWf = _gpu.Create(ww, wh);
                    if (_gWf == null) return;
                    _gpu.UpdateRows(_gWf, px, 0, 0, wh);
                    _gWfEpoch = _spec.WaterfallEpoch;
                    _gWfVer = _spec.WaterfallVersion;
                }
                else if (_gWfVer != _spec.WaterfallVersion)
                {
                    int rows = Math.Min(wh, _spec.WaterfallVersion - _gWfVer);
                    for (int k = 0; k < rows; k++) { int row = (top + k) % wh; _gpu.UpdateRows(_gWf, px, row, row, 1); }
                    _gWfVer = _spec.WaterfallVersion;
                }
                var a = _spec.Area;
                float x = (float)Math.Round(a.X), y = (float)Math.Round(a.Y);
                int first = wh - top;   // Zeilen top..wh-1 oben, dann 0..top-1
                _gpu.Image(_gWf, new RectangleF(x, y, ww, first), new RectangleF(0, top, ww, first));
                if (top > 0) _gpu.Image(_gWf, new RectangleF(x, y + first, ww, top), new RectangleF(0, 0, ww, top));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (ClientSize.Width < 20 || ClientSize.Height < 20) return;
                if (TryGpuPaint()) return;   // 3.0: ueber die Grafikkarte – geht es nicht, wie bisher
                if (_dirty || !_cache.Fits(ClientSize.Width, ClientSize.Height))
                {
                    _dirty = false;
                    LayoutAll();
                    _cache.Ensure(ClientSize.Width, ClientSize.Height);
                    _cache.Render(DrawStatic);
                }
                var g = e.Graphics;
                _cache.Draw(g, e.ClipRectangle);
                for (int i = 0; i < 2 && _win.ShowVu; i++)
                {
                    if (i == 1 && Theme.VuDual) break;
                    if (!e.ClipRectangle.IntersectsWith(Rectangle.Ceiling(MeterBounds(i)))) continue;
                    var mo = i == 0 ? _win.MeterL : _win.MeterR;
                    var r = i == 0 ? _vuL : _vuR;
                    var st = g.Save();
                    if (Theme.VuDual)
                    {
                        bool ledOn = _win.MeterL.Led || _win.MeterR.Led;
                        Theme.WithScale(_vuScale, () => Theme.DrawVuNeedle2(g, r, _win.MeterL.P, _win.MeterR.P, false, StripBase.VuPeakLed ? (ledOn ? 2 : 1) : 0));
                    }
                    else Theme.WithScale(_vuScale, () => Theme.DrawVuNeedle(g, r, mo.P, false, StripBase.VuPeakNeedle ? mo.Pk : -1f, StripBase.VuPeakLed ? (mo.Led ? 2 : 1) : 0));
                    g.Restore(st);
                }
                if (!_specRect.IsEmpty && e.ClipRectangle.IntersectsWith(_spec.LiveBounds))
                {
                    var st = g.Save();
                    Theme.WithScale(_specScale, () => _spec.DrawLive(g, _win.Spectrum));
                    g.Restore(st);
                }
            }

            private void DrawStatic(Graphics g)
            {
                Theme.Setup(g);
                var all = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
                g.Clear(MixerForm.BodyColor);
                // Gehaeuse: dunkle Frontplatte (im Studio-Design gebuerstet)
                using (var b = new LinearGradientBrush(new RectangleF(0, -1, all.Width, all.Height + 2), Theme.Lighten(Theme.PanelTop, 0.02f), Theme.BarBg, LinearGradientMode.Vertical))
                    g.FillRectangle(b, all);
                if (Theme.Style == Theme.StyleStudio) Theme.FillBrushed(g, all);
                for (int i = 0; i < 2 && _win.ShowVu; i++)
                {
                    if (i == 1 && Theme.VuDual) break;
                    var r = i == 0 ? _vuL : _vuR;
                    Theme.WithScale(_vuScale, () =>
                    {
                        Theme.DrawVuFace(g, r, false);
                        var lr = new RectangleF(r.X, r.Bottom + Theme.F(4), r.Width, Theme.F(12));
                        Theme.Label(g, Theme.VuDual ? "L + R" : i == 0 ? "L" : "R", lr, Theme.Center, Theme.ScaleLabel, 9.5f);
                    });
                }
                if (!_specRect.IsEmpty) Theme.WithScale(_specScale, () => _spec.DrawStatic(g));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _cache.Dispose(); _tip.Dispose(); try { DropGpu(); } catch { } }
                base.Dispose(disposing);
            }
        }
    }
}
