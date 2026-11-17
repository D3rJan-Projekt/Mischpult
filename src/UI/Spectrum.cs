using System.Collections.Generic;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// Spektrum des Ausgangs fuer die Anzeige: geglaettet (steigt schnell, faellt langsam) mit kurz stehenbleibender Spitze.
    /// Leicht "gekippt" (+3 dB pro Oktave ueber 1 kHz), damit Musik nicht nur im Bass ausschlaegt – wie bei Hi-Fi-Analysern.
    /// </summary>
    internal sealed class SpectrumModel
    {
        public const int N = SpectrumAnalyzer.Bands;
        public const float Floor = -72f, Top = -8f;
        public const float LowHz = 31.5f, HighHz = 16000f;
        /// <summary>2.17: Spitzen im Spektrum – 0 aus, 1 kurz (0,7 s), 2 lang (2,5 s).</summary>
        public static int PeakMode = 1;
        private static float HoldSec => PeakMode == 2 ? 2.5f : 0.7f;
        private readonly float[] _lvl = new float[N], _pk = new float[N], _hold = new float[N];
        private static readonly float[] Tilt = new float[N];

        /// <summary>Zaehlt hoch, wenn sich etwas Sichtbares geaendert hat.</summary>
        public int Version { get; private set; }
        /// <summary>Kommt gerade (oder kam eben noch) Ton an?</summary>
        public bool Active { get; private set; }

        static SpectrumModel()
        {
            for (int b = 0; b < N; b++) Tilt[b] = 3f * (float)Math.Log(SpectrumAnalyzer.BandFreq(b + 0.5) / 1000.0, 2);
        }

        public SpectrumModel() { Reset(); }

        public void Reset()
        {
            for (int b = 0; b < N; b++) { _lvl[b] = Floor; _pk[b] = Floor; _hold[b] = 0f; }
            Active = false;
            Version++;
        }

        public void Update(float[] db, float dt)
        {
            if (dt <= 0f) return;
            if (dt > 0.1f) dt = 0.1f;
            bool data = db != null && db.Length == N;
            float att = Math.Min(1f, dt * 28f), fall = 32f * dt, pkFall = 14f * dt;
            bool any = false, changed = false;
            for (int b = 0; b < N; b++)
            {
                float target = data && !float.IsNaN(db[b]) ? Math.Max(Floor, Math.Min(6f, db[b] + Tilt[b])) : Floor;
                float l = _lvl[b];
                if (target > l) l += (target - l) * att;
                else l = Math.Max(target, l - fall);
                if (Math.Abs(l - _lvl[b]) > 0.05f) changed = true;
                _lvl[b] = l;
                if (PeakMode == 0) { if (Math.Abs(_pk[b] - l) > 0.05f) changed = true; _pk[b] = l; _hold[b] = 0f; }
                else if (l >= _pk[b]) { _pk[b] = l; _hold[b] = HoldSec; }
                else if (_hold[b] > 0f) _hold[b] -= dt;
                else { float p = Math.Max(l, _pk[b] - pkFall); if (Math.Abs(p - _pk[b]) > 0.05f) changed = true; _pk[b] = p; }
                if (_pk[b] > Floor + 1f) any = true;
            }
            Active = any;
            if (changed) Version++;
        }

        private static int BandIndex(double f) => Math.Max(0, Math.Min(N - 1, (int)(N * Math.Log(f / 20.0) / Math.Log(1000.0))));

        /// <summary>Unterste Frequenz von Balken i (von count), logarithmisch von LowHz bis HighHz.</summary>
        public static double BarFreq(int i, int count) => LowHz * Math.Pow(HighHz / LowHz, i / (double)count);

        /// <summary>Hoehe (0..1) und Spitze (0..1) fuer count Balken.</summary>
        public void Bars(int count, float[] level, float[] peak)
        {
            for (int i = 0; i < count; i++)
            {
                int b0 = BandIndex(BarFreq(i, count)), b1 = Math.Max(b0, BandIndex(BarFreq(i + 1, count)) - 1);
                float l = Floor, p = Floor;
                for (int b = b0; b <= b1; b++) { if (_lvl[b] > l) l = _lvl[b]; if (_pk[b] > p) p = _pk[b]; }
                level[i] = Norm(l);
                peak[i] = Norm(p);
            }
        }

        public static float Norm(float db) => Math.Max(0f, Math.Min(1f, (db - Floor) / (Top - Floor)));
    }

    /// <summary>
    /// Zeichnet das Spektrum als LED-Balken (wie die Pegel): Fenster, Beschriftung und unbeleuchtete Segmente fest (Bild-Cache),
    /// leuchtende Segmente und Spitzen live.
    /// </summary>
    internal sealed partial class SpectrumView
    {
        public RectangleF Bounds { get; private set; }
        private RectangleF _area;
        private int _bars, _segs;
        private float _barW, _gap, _segH, _segGap;
        private float[] _lvl = new float[0], _pk = new float[0];
        private readonly SolidBrush _brush = new SolidBrush(Color.Black);
        public bool Title = true;

        // ---- 2.16 Wasserfall: die Frequenzen laufen als Farbbild nach unten durch (oben = jetzt)
        /// <summary>Spektrum als Wasserfall statt LED-Balken (OPTIONEN → Aussehen).</summary>
        public static bool Waterfall;
        /// <summary>So oft (ms) kommt eine neue Zeile dazu.</summary>
        public const int WaterfallRowMs = 40;
        private System.Drawing.Bitmap _wfBmp;
        private int[] _wfPx = new int[0];
        private int _wfW, _wfH, _wfTop, _wfLitRows;
        private float[] _wfLvl = new float[0], _wfPk = new float[0];
        private double _wfNext;
        private static readonly System.Diagnostics.Stopwatch WfClock = System.Diagnostics.Stopwatch.StartNew();

        // ---- 3.1: weitere Arten und Farben
        /// <summary>Art: 0 LED-Balken, 1 Wasserfall, 2 Kurve, 3 gespiegelt, 4 rund.</summary>
        public static int Shape;
        /// <summary>Farben: 0 klassisch, 1 Regenbogen, 2 Akzentfarbe, 3 Feuer, 4 Eis.</summary>
        public static int Palette;
        /// <summary>3.1: grosse Einschalt-Show laeuft – Spektrum als Welle von links nach rechts.</summary>
        public static bool ShowRunning => StripBase.BigShow && StripBase.SelfTestRunning && StripBase.SelfTestTime >= 0f;

        /// <summary>Statt der Messwerte die Welle der Einschalt-Show in die Balken legen.</summary>
        private void ShowWave()
        {
            float t = StripBase.SelfTestTime;
            for (int i = 0; i < _bars; i++)
            {
                _lvl[i] = StripBase.TestLevelAt(t - 0.35f * i / Math.Max(1, _bars));
                _pk[i] = 0f;
            }
        }

        public static readonly string[] ShapeNames = { "LED-Balken", "Wasserfall", "Kurve", "Gespiegelt", "Rund", "3D-Landschaft", "Lichtorgel", "Skyline" };
        public static readonly string[] PaletteNames = { "Klassisch", "Regenbogen", "Akzentfarbe", "Feuer", "Eis" };
        /// <summary>Aendert sich mit Art oder Farben (fuer die Bildspeicher der Meterbridge).</summary>
        public static int ConfigKey => (Waterfall ? 1 : Shape) * 16 + Palette;
        /// <summary>Kann die Grafikkarte diese Art zeichnen? (Kurve und rund zeichnet das Pult selbst.)</summary>
        public static bool GpuCapable => Waterfall || Shape == 0 || Shape == 3;
        private static int Eff => Waterfall ? 1 : Shape;
        /// <summary>LED-Balken von unten (die einzige Art, die den Bildspeicher mit allen leuchtenden LEDs nutzt).</summary>
        private static bool LedBars => Eff == 0;
        private static int[][] _wfPals = new int[5][];

        /// <summary>Farbe eines Segments: frac = Hoehe 0..1, bar = Position 0..1 (Bass links) – je nach Farben.</summary>
        internal static Color SegColor(float frac, float bar)
        {
            switch (Palette)
            {
                case 1: return Theme.LedLevel(Theme.Lighten(Hue(bar * 285f), frac * 0.18f));
                case 2: return Theme.LedLevel(Theme.Mix(Theme.Darken(Theme.Accent, 0.35f), Theme.Lighten(Theme.Accent, 0.45f), frac));
                case 3: return Theme.LedLevel(Grad(frac, new[] { 0x7a0d06, 0xd62510, 0xff6a10, 0xffb21f, 0xfff07a }));
                case 4: return Theme.LedLevel(Grad(frac, new[] { 0x0b2a7a, 0x1667d6, 0x1fb6ff, 0x8fe8ff, 0xf2fdff }));
                default: return Theme.SegColor(frac, Theme.Accent, 0.6f, 0.86f);   // enthaelt die LED-Helligkeit schon
            }
        }

        private static Color Hue(float deg)
        {
            float h = (deg % 360f + 360f) % 360f / 60f, x = 1f - Math.Abs(h % 2f - 1f);
            float r, g, b;
            if (h < 1) { r = 1; g = x; b = 0; } else if (h < 2) { r = x; g = 1; b = 0; } else if (h < 3) { r = 0; g = 1; b = x; }
            else if (h < 4) { r = 0; g = x; b = 1; } else if (h < 5) { r = x; g = 0; b = 1; } else { r = 1; g = 0; b = x; }
            return Color.FromArgb((int)(40 + r * 215), (int)(40 + g * 215), (int)(40 + b * 215));
        }

        private static Color Grad(float t, int[] stops)
        {
            t = Math.Max(0f, Math.Min(1f, t)) * (stops.Length - 1);
            int k = Math.Min(stops.Length - 2, (int)t);
            float f = t - k;
            Color a = Color.FromArgb(unchecked((int)0xff000000) | stops[k]), b = Color.FromArgb(unchecked((int)0xff000000) | stops[k + 1]);
            return Theme.Mix(a, b, f);
        }

        /// <summary>Farbverlauf des Wasserfalls (dunkel → violett → rot → orange → hell), 256 Stufen, vormultipliert deckend.</summary>
        private static int[] WfPalette()
        {
            int pi = Palette == 1 ? 0 : Math.Max(0, Math.Min(4, Palette));   // Regenbogen: wie klassisch (Farbe = Lautstaerke)
            if (_wfPals[pi] != null) return _wfPals[pi];
            int acc = Theme.Accent.ToArgb() & 0xffffff, accHi = Theme.Lighten(Theme.Accent, 0.6f).ToArgb() & 0xffffff;
            var stops = pi == 2 ? new[] { 0x0b0c0f, Theme.Darken(Theme.Accent, 0.7f).ToArgb() & 0xffffff, acc, accHi, 0xffffff }
                      : pi == 3 ? new[] { 0x0b0c0f, 0x4a0804, 0xb81a0a, 0xff6a10, 0xffc02a, 0xfff6c0 }
                      : pi == 4 ? new[] { 0x0b0c0f, 0x0a1a52, 0x1a4fc0, 0x1fb6ff, 0xa6efff, 0xffffff }
                      : new[] { 0x0b0c0f, 0x2c105c, 0x721f81, 0xb73779, 0xf1605d, 0xfeb078, 0xfcfdbf };
            var pos = new float[stops.Length];
            if (pi == 0) pos = new[] { 0f, 0.22f, 0.42f, 0.6f, 0.76f, 0.9f, 1f };
            else for (int i = 0; i < pos.Length; i++) pos[i] = i / (float)(pos.Length - 1);
            var pal = new int[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                int k = 0;
                while (k < pos.Length - 2 && t > pos[k + 1]) k++;
                float f = (t - pos[k]) / Math.Max(1e-4f, pos[k + 1] - pos[k]);
                int a = stops[k], b = stops[k + 1];
                int r = (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * f);
                int gg = (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * f);
                int bb = (int)((a & 255) + ((b & 255) - (a & 255)) * f);
                pal[i] = unchecked((int)0xff000000) | (r << 16) | (gg << 8) | bb;
            }
            if (pi == 2) return pal;   // Akzentfarbe kann sich aendern: nicht merken
            return _wfPals[pi] = pal;
        }

        private void EnsureWaterfall()
        {
            int w = Math.Max(1, (int)Math.Round(_area.Width)), h = Math.Max(1, (int)Math.Round(_area.Height));
            if (_wfBmp != null && w == _wfW && h == _wfH) return;
            _wfBmp?.Dispose();
            _wfW = w; _wfH = h;
            _wfBmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _wfPx = new int[w * h];
            int bg = WfPalette()[0];
            for (int i = 0; i < _wfPx.Length; i++) _wfPx[i] = bg;
            _wfTop = 0; _wfLitRows = 0;
            _wfLvl = new float[w]; _wfPk = new float[w];
            WaterfallEpoch++;
            CopyWaterfall();
        }

        private void CopyWaterfall()
        {
            var data = _wfBmp.LockBits(new Rectangle(0, 0, _wfW, _wfH), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < _wfH; y++)
                    System.Runtime.InteropServices.Marshal.Copy(_wfPx, ((_wfTop + y) % _wfH) * _wfW, data.Scan0 + y * data.Stride, _wfW);
            }
            finally { _wfBmp.UnlockBits(data); }
        }

        /// <summary>
        /// Aus dem Takt: im Wasserfall-Modus alle 40 ms eine neue Zeile. true = das Bild hat sich geaendert (neu zeichnen).
        /// Ist alles dunkel und es kommt nichts, ruht er.
        /// </summary>
        public bool Push(SpectrumModel m)
        {
            if (!Waterfall || m == null || _area.Width < 4 || _area.Height < 4) return false;
            double now = WfClock.Elapsed.TotalMilliseconds;
            if (now < _wfNext) return false;
            _wfNext += WaterfallRowMs;                          // gleichmaessiger Takt …
            if (_wfNext <= now) _wfNext = now + WaterfallRowMs;  // … ausser weit hinterher (z. B. nach einer Pause)
            EnsureWaterfall();
            if (!m.Active && _wfLitRows == 0) return false;
            m.Bars(_wfW, _wfLvl, _wfPk);
            var pal = WfPalette();
            _wfTop = (_wfTop - 1 + _wfH) % _wfH;   // neue Zeile oben
            int row = _wfTop * _wfW;
            bool lit = false;
            for (int x = 0; x < _wfW; x++)
            {
                float v = _wfLvl[x];
                v = (float)Math.Pow(v, 1.6);   // mehr Kontrast: Leises bleibt dunkel
                int i = Math.Max(0, Math.Min(255, (int)(v * 255f)));
                if (i > 6) lit = true;
                _wfPx[row + x] = pal[i];
            }
            if (lit) _wfLitRows = _wfH; else if (_wfLitRows > 0) _wfLitRows--;
            CopyWaterfall();
            WaterfallVersion++;
            return true;
        }

        private void DrawWaterfall(Graphics g)
        {
            EnsureWaterfall();
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(_wfBmp, (int)Math.Round(_area.X), (int)Math.Round(_area.Y));
            g.InterpolationMode = im;
        }
        /// <summary>Ueberschrift, z. B. "SPEKTRUM · ALLE AUSGABEN".</summary>
        public string Caption = "SPEKTRUM";

        /// <summary>Aufteilung fuer dieses Rechteck berechnen.</summary>
        public void Layout(RectangleF r)
        {
            Bounds = r;
            float top = r.Y + (Title ? Theme.F(21) : Theme.F(8)), bottom = r.Bottom - Theme.F(17);
            float left = r.X + Theme.F(8), right = r.Right - Theme.F(8);
            float w = Math.Max(10f, right - left), h = Math.Max(10f, bottom - top);
            _gap = Math.Max(1f, (float)Math.Round(Theme.F(2)));
            float want = Math.Max(Theme.F(5), Math.Min(Theme.F(14), w / 30f));
            _bars = Math.Max(6, Math.Min(32, (int)((w + _gap) / (want + _gap))));
            _barW = (float)Math.Floor((w - _gap * (_bars - 1)) / _bars);
            float used = _barW * _bars + _gap * (_bars - 1);
            _segH = Math.Max(2f, (float)Math.Round(Theme.F(2) * Math.Max(1f, h / Theme.F(60))));
            _segGap = Math.Max(1f, (float)Math.Round(_segH / 2f));
            _segs = Math.Max(6, (int)((h + _segGap) / (_segH + _segGap)));
            float usedH = _segs * (_segH + _segGap) - _segGap;
            _area = new RectangleF((float)Math.Round(left + (w - used) / 2f), (float)Math.Round(bottom - usedH), used, usedH);
            if (_lvl.Length != _bars) { _lvl = new float[_bars]; _pk = new float[_bars]; }
        }

        private float BarFrac(int i) => _bars <= 1 ? 0f : i / (float)(_bars - 1);

        /// <summary>Fenster, Titel, Frequenzen und unbeleuchtete Segmente.</summary>
        public void DrawStatic(Graphics g, bool dimmed = false)
        {
            var r = Bounds;
            if (r.Width < 20 || r.Height < 20) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), r, Theme.F(5));
            Theme.StrokeRound(g, Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(1), r, Theme.F(5));
            if (Title)
                using (var b = new SolidBrush(Color.FromArgb(0xb8, 0xbe, 0xc7)))
                {
                    var tr = new RectangleF(r.X + Theme.F(8), r.Y + Theme.F(4), r.Width - Theme.F(16), Theme.F(14));
                    string cap = Caption ?? "SPEKTRUM";
                    var tf = Theme.Fit(g, cap, tr.Width, 9f, 7f);
                    // passt es auch klein nicht, nur noch "SPEKTRUM" (bzw. abgeschnitten)
                    if (Theme.TextWidth(g, cap, tf) > tr.Width) { cap = cap.Contains("·") ? "SPEKTRUM" : cap; tf = Theme.Fit(g, cap, tr.Width, 9f, 7f); }
                    Theme.Str(g, cap, tf, b, tr, Theme.Left);
                }
            g.SmoothingMode = SmoothingMode.None;
            float step = _segH + _segGap;
            int eff = Eff;
            if (eff == 1)
            {
                // Wasserfall: dunkle Flaeche, darin laeuft das Bild (live); Rahmen drumherum
                using (var b = new SolidBrush(Color.FromArgb(0x0b, 0x0c, 0x0f))) g.FillRectangle(b, _area);
                using (var pen = new Pen(Color.FromArgb(0x1e, 0x21, 0x27), 1f)) g.DrawRectangle(pen, _area.X - 1, _area.Y - 1, _area.Width + 1, _area.Height + 1);
            }
            else if (eff == 2)
            {
                // Kurve: feine Rasterlinien
                using (var pen = new Pen(Color.FromArgb(dimmed ? 14 : 26, 255, 255, 255), 1f))
                    for (int k = 1; k <= 3; k++) { float y = (float)Math.Round(_area.Bottom - _area.Height * k / 4f) + 0.5f; g.DrawLine(pen, _area.X, y, _area.Right, y); }
                using (var pen = new Pen(Color.FromArgb(dimmed ? 30 : 50, 255, 255, 255), 1f)) g.DrawLine(pen, _area.X, _area.Bottom - 0.5f, _area.Right, _area.Bottom - 0.5f);
            }
            else if (eff == 3)
            {
                int half = HalfSegs;
                float cy = MirrorCenter;
                for (int i = 0; i < _bars; i++)
                {
                    float x = _area.X + i * (_barW + _gap);
                    for (int s = 0; s < half; s++)
                    {
                        _brush.Color = Theme.Mix(Color.FromArgb(0x13, 0x15, 0x18), SegColor((s + 1f) / half, BarFrac(i)), dimmed ? 0.06f : 0.13f);
                        Theme.FillSeg(g, _brush, x, cy - (s + 1) * step + _segGap / 2f, _barW, _segH);
                        Theme.FillSeg(g, _brush, x, cy + s * step + _segGap / 2f, _barW, _segH);
                    }
                }
            }
            else if (eff == 4)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RadialGeometry(out var c, out float r0, out float r1, out _);
                using (var b = new SolidBrush(Color.FromArgb(0x10, 0x12, 0x16))) g.FillEllipse(b, c.X - r0, c.Y - r0, r0 * 2, r0 * 2);
                using (var pen = new Pen(Color.FromArgb(dimmed ? 20 : 34, 255, 255, 255), 1f))
                {
                    g.DrawEllipse(pen, c.X - r0, c.Y - r0, r0 * 2, r0 * 2);
                    g.DrawEllipse(pen, c.X - r1, c.Y - r1, r1 * 2, r1 * 2);
                }
            }
            else if (eff == 5) Static3D(g, dimmed);     // 3.3
            else if (eff == 6) StaticOrgan(g, dimmed);
            else if (eff == SkylineShape) StaticSkyline(g, dimmed);   // 4.0 finale
            for (int i = 0; i < (eff == 0 ? _bars : 0); i++)
            {
                float x = _area.X + i * (_barW + _gap);
                for (int s = 0; s < _segs; s++)
                {
                    _brush.Color = Theme.Mix(Color.FromArgb(0x13, 0x15, 0x18), SegColor((s + 1f) / _segs, BarFrac(i)), dimmed ? 0.06f : 0.13f);
                    Theme.FillSeg(g, _brush, x, _area.Bottom - (s + 1) * step + _segGap, _barW, _segH);
                }
            }
            // Frequenzen unter den Balken (rund: keine – die Frequenz laeuft im Kreis)
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var font = Theme.Ui(7.5f, FontStyle.Bold);
            if (eff != 4 && eff != 6 && eff != SkylineShape)
                using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                {
                    float lastRight = float.MinValue;
                    foreach (var kv in new[] { new Tuple<double, string>(50, "50"), new Tuple<double, string>(100, "100"), new Tuple<double, string>(250, "250"),
                                               new Tuple<double, string>(1000, "1k"), new Tuple<double, string>(4000, "4k"), new Tuple<double, string>(10000, "10k") })
                    {
                        double pos = Math.Log(kv.Item1 / SpectrumModel.LowHz) / Math.Log(SpectrumModel.HighHz / SpectrumModel.LowHz);
                        float x = _area.X + (float)pos * _area.Width;
                        float tw = Theme.TextWidth(g, kv.Item2, font);
                        if (x - tw / 2f < lastRight + Theme.F(4) || x + tw / 2f > _area.Right + Theme.F(4)) continue;
                        Theme.Str(g, kv.Item2, font, b, new RectangleF(x - Theme.F(14), _area.Bottom + Theme.F(3), Theme.F(28), Theme.F(11)), Theme.Center);
                        lastRight = x + tw / 2f;
                    }
                }
            else if (eff == 4)
                using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                    Theme.Str(g, "BASS OBEN · HÖHEN UNTEN", font, b, new RectangleF(_area.X, _area.Bottom + Theme.F(3), _area.Width, Theme.F(11)), Theme.Center);
            g.SmoothingMode = sm;
        }

        // ---- 3.1: Geometrie der neuen Arten
        private int HalfSegs => Math.Max(3, _segs / 2);
        private float MirrorCenter => (float)Math.Round(_area.Y + _area.Height / 2f);

        private void RadialGeometry(out PointF c, out float r0, out float r1, out float barW)
        {
            c = new PointF(_area.X + _area.Width / 2f, _area.Y + _area.Height / 2f);
            float R = Math.Min(_area.Width, _area.Height) / 2f;
            r0 = Math.Max(Theme.F(6), R * 0.26f);
            r1 = Math.Max(r0 + Theme.F(6), R - Theme.F(1));
            barW = Math.Max(1.5f, Math.Min(Theme.F(7), (float)(Math.PI * r0 / Math.Max(1, _bars)) * 0.8f));
        }

        /// <summary>Alle Segmente leuchtend (fuer den Bildspeicher des Wirts) – nur bei LED-Balken.</summary>
        public void DrawLit(Graphics g)
        {
            if (Bounds.Width < 20 || Bounds.Height < 20 || !LedBars) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            float step = _segH + _segGap;
            for (int i = 0; i < _bars; i++)
            {
                float x = _area.X + i * (_barW + _gap);
                for (int s = 0; s < _segs; s++)
                {
                    _brush.Color = SegColor((s + 1f) / _segs, BarFrac(i));
                    Theme.FillSeg(g, _brush, x, _area.Bottom - (s + 1) * step + _segGap, _barW, _segH);
                }
            }
            g.SmoothingMode = sm;
        }

        private Rectangle[] _rects = new Rectangle[0];

        /// <summary>Leuchtende Segmente aus dem Bildspeicher <paramref name="lit"/> (null = einzeln zeichnen), Spitzen live.</summary>
        public void DrawLive(Graphics g, SpectrumModel m, PaintCache lit, Rectangle clip) { DrawLiveCoreA(g, m, lit, clip); if (Theme.CrtOn) Theme.CrtOverlay(g, _area); }   // 3.4.6: Roehrenbild
        public void DrawLive(Graphics g, SpectrumModel m) { DrawLiveCoreB(g, m); if (Theme.CrtOn) Theme.CrtOverlay(g, _area); }

        private void DrawLiveCoreA(Graphics g, SpectrumModel m, PaintCache lit, Rectangle clip)
        {
            if (lit == null || !LedBars) { DrawLiveCoreB(g, m); return; }
            if (Bounds.Width < 20 || Bounds.Height < 20 || m == null) return;
            m.Bars(_bars, _lvl, _pk);
            if (ShowRunning) ShowWave();
            if (_rects.Length < _bars) _rects = new Rectangle[_bars];
            float step = _segH + _segGap;
            int k = 0;
            for (int i = 0; i < _bars; i++)
            {
                int on = Math.Min(_segs, (int)(_lvl[i] * _segs + 0.5f));
                if (on <= 0) continue;
                float x = _area.X + i * (_barW + _gap);
                float top = _area.Bottom - on * step + _segGap;
                _rects[k++] = Rectangle.FromLTRB((int)Math.Floor(x), (int)Math.Floor(top), (int)Math.Ceiling(x + _barW), (int)Math.Ceiling(_area.Bottom));
            }
            lit.DrawRects(g, _rects, k, clip);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 0; i < _bars; i++)
            {
                int on = (int)(_lvl[i] * _segs + 0.5f);
                int pk = (int)(_pk[i] * _segs + 0.5f) - 1;
                if (pk >= on && pk > 0 && pk < _segs)
                {
                    float x = _area.X + i * (_barW + _gap);
                    _brush.Color = Theme.Lighten(SegColor((pk + 1f) / _segs, BarFrac(i)), 0.25f);
                    Theme.FillSeg(g, _brush, x, _area.Bottom - (pk + 1) * step + _segGap, _barW, _segH);
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Leuchtende Segmente und Spitzen.</summary>
        private void DrawLiveCoreB(Graphics g, SpectrumModel m)
        {
            if (Bounds.Width < 20 || Bounds.Height < 20 || m == null) return;
            int eff = Eff;
            if (eff == 1) { DrawWaterfall(g); return; }
            m.Bars(_bars, _lvl, _pk);
            if (ShowRunning) ShowWave();
            if (eff == 2) { DrawCurve(g); return; }
            if (eff == 4) { DrawRadial(g); return; }
            if (eff == 5) { Draw3D(g, m); return; }     // 3.3
            if (eff == 6) { DrawOrgan(g); return; }
            if (eff == SkylineShape) { DrawSkyline(g); return; }   // 4.0 finale
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            float step = _segH + _segGap;
            if (eff == 3)
            {
                int half = HalfSegs;
                float cy = MirrorCenter;
                for (int i = 0; i < _bars; i++)
                {
                    float x = _area.X + i * (_barW + _gap);
                    int lit = Math.Min(half, (int)(_lvl[i] * half + 0.5f));
                    int pk = Math.Min(half, (int)(_pk[i] * half + 0.5f)) - 1;
                    for (int s = 0; s < lit; s++)
                    {
                        _brush.Color = SegColor((s + 1f) / half, BarFrac(i));
                        Theme.FillSeg(g, _brush, x, cy - (s + 1) * step + _segGap / 2f, _barW, _segH);
                        Theme.FillSeg(g, _brush, x, cy + s * step + _segGap / 2f, _barW, _segH);
                    }
                    if (pk >= lit && pk > 0)
                    {
                        _brush.Color = Theme.Lighten(SegColor((pk + 1f) / half, BarFrac(i)), 0.25f);
                        Theme.FillSeg(g, _brush, x, cy - (pk + 1) * step + _segGap / 2f, _barW, _segH);
                        Theme.FillSeg(g, _brush, x, cy + pk * step + _segGap / 2f, _barW, _segH);
                    }
                }
                g.SmoothingMode = sm;
                return;
            }
            for (int i = 0; i < _bars; i++)
            {
                float x = _area.X + i * (_barW + _gap);
                int lit = (int)(_lvl[i] * _segs + 0.5f);
                int pk = (int)(_pk[i] * _segs + 0.5f) - 1;
                for (int s = 0; s < lit; s++)
                {
                    _brush.Color = SegColor((s + 1f) / _segs, BarFrac(i));
                    Theme.FillSeg(g, _brush, x, _area.Bottom - (s + 1) * step + _segGap, _barW, _segH);
                }
                if (pk >= lit && pk > 0)
                {
                    _brush.Color = Theme.Lighten(SegColor((pk + 1f) / _segs, BarFrac(i)), 0.25f);
                    Theme.FillSeg(g, _brush, x, _area.Bottom - (pk + 1) * step + _segGap, _barW, _segH);
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>3.1 Kurve: weich durch alle Baender, darunter leuchtend gefuellt, Spitzen als feine Linie.</summary>
        private void DrawCurve(Graphics g)
        {
            if (_bars < 2) return;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pts = new PointF[_bars];
            var pks = new PointF[_bars];
            float bottom = _area.Bottom - 1f;
            for (int i = 0; i < _bars; i++)
            {
                float x = _area.X + i * (_barW + _gap) + _barW / 2f;
                pts[i] = new PointF(x, bottom - Math.Min(1f, _lvl[i]) * (_area.Height - 2f));
                pks[i] = new PointF(x, bottom - Math.Min(1f, Math.Max(_lvl[i], _pk[i])) * (_area.Height - 2f));
            }
            Color top = SegColor(1f, 0.5f), mid = SegColor(0.55f, 0.5f), low = SegColor(0.15f, 0.5f);
            var st = g.Save();
            g.SetClip(RectangleF.Inflate(_area, 0, 1f));
            using (var path = new GraphicsPath())
            {
                path.AddLine(_area.X, bottom, pts[0].X, pts[0].Y);
                path.AddCurve(pts, 0.5f);
                path.AddLine(pts[_bars - 1].X, pts[_bars - 1].Y, _area.Right, bottom);
                path.CloseFigure();
                using (var b = FillBrush(top, mid, low)) g.FillPath(b, path);
            }
            using (var line = new GraphicsPath())
            {
                line.AddCurve(pts, 0.5f);
                using (var glow = LineBrush(110)) using (var p = new Pen(glow, Theme.F(4.5f)) { LineJoin = LineJoin.Round }) g.DrawPath(p, line);
                using (var core = LineBrush(255)) using (var p = new Pen(core, Math.Max(1.2f, Theme.F(1.6f))) { LineJoin = LineJoin.Round }) g.DrawPath(p, line);
            }
            using (var pk = new GraphicsPath())
            {
                pk.AddCurve(pks, 0.5f);
                using (var p = new Pen(Color.FromArgb(120, Theme.Lighten(top, 0.3f)), 1f) { DashStyle = DashStyle.Dot }) g.DrawPath(p, pk);
            }
            g.Restore(st);
            g.SmoothingMode = sm;
        }

        private Brush FillBrush(Color top, Color mid, Color low)
        {
            if (Palette == 1)
            {
                var lb = new LinearGradientBrush(new RectangleF(_area.X - 1, _area.Y, _area.Width + 2, _area.Height), Color.Red, Color.Blue, LinearGradientMode.Horizontal);
                lb.InterpolationColors = RainbowBlend(120);
                return lb;
            }
            var b = new LinearGradientBrush(new RectangleF(_area.X, _area.Y - 1, _area.Width, _area.Height + 2), Color.FromArgb(150, top), Color.FromArgb(18, low), LinearGradientMode.Vertical);
            b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(160, top), Color.FromArgb(90, mid), Color.FromArgb(16, low) }, Positions = new[] { 0f, 0.5f, 1f } };
            return b;
        }

        private Brush LineBrush(int alpha)
        {
            if (Palette == 1)
            {
                var lb = new LinearGradientBrush(new RectangleF(_area.X - 1, _area.Y, _area.Width + 2, _area.Height), Color.Red, Color.Blue, LinearGradientMode.Horizontal);
                lb.InterpolationColors = RainbowBlend(alpha);
                return lb;
            }
            // senkrechter Verlauf wie die LEDs: unten die leise Farbe, oben die laute
            var b = new LinearGradientBrush(new RectangleF(_area.X, _area.Y - 1, _area.Width, _area.Height + 2), Color.White, Color.White, LinearGradientMode.Vertical);
            b.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(alpha, Theme.Lighten(SegColor(1f, 0.5f), 0.1f)), Color.FromArgb(alpha, Theme.Lighten(SegColor(0.75f, 0.5f), 0.1f)), Color.FromArgb(alpha, Theme.Lighten(SegColor(0.3f, 0.5f), 0.1f)) },
                Positions = new[] { 0f, 0.3f, 1f }
            };
            return b;
        }

        private static ColorBlend RainbowBlend(int alpha)
        {
            var cols = new Color[7]; var pos = new float[7];
            for (int i = 0; i < 7; i++) { pos[i] = i / 6f; cols[i] = Color.FromArgb(alpha, Hue(pos[i] * 285f)); }
            return new ColorBlend { Colors = cols, Positions = pos };
        }

        /// <summary>3.1 Rund: Baender als LED-Strahlen um einen Kreis – Bass oben, Hoehen unten, links und rechts gespiegelt.</summary>
        private void DrawRadial(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RadialGeometry(out var c, out float r0, out float r1, out float bw);
            float step = Math.Max(2f, _segH + _segGap), seg = Math.Max(1.2f, _segH);
            int segs = Math.Max(3, (int)((r1 - r0) / step));
            using (var pen = new Pen(Color.White, bw) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                for (int i = 0; i < _bars; i++)
                {
                    int lit = Math.Min(segs, (int)(_lvl[i] * segs + 0.5f));
                    int pk = Math.Min(segs, (int)(_pk[i] * segs + 0.5f)) - 1;
                    double t = (i + 0.5) / _bars;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double a = (-90 + side * t * 180) * Math.PI / 180.0;
                        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        for (int s = 0; s < lit; s++)
                        {
                            float d0 = r0 + s * step + (step - seg) / 2f, d1 = d0 + seg;
                            pen.Color = SegColor((s + 1f) / segs, BarFrac(i));
                            g.DrawLine(pen, c.X + ca * d0, c.Y + sa * d0, c.X + ca * d1, c.Y + sa * d1);
                        }
                        if (pk >= lit && pk > 0)
                        {
                            float d0 = r0 + pk * step + (step - seg) / 2f, d1 = d0 + seg;
                            pen.Color = Theme.Lighten(SegColor((pk + 1f) / segs, BarFrac(i)), 0.25f);
                            g.DrawLine(pen, c.X + ca * d0, c.Y + sa * d0, c.X + ca * d1, c.Y + sa * d1);
                        }
                    }
                }
            // Mitte: glimmt mit dem Bass
            float bass = 0f; for (int i = 0; i < Math.Min(3, _bars); i++) bass = Math.Max(bass, _lvl[i]);
            if (bass > 0.02f)
            {
                float rr = r0 * 0.8f;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(150 * bass), SegColor(bass, 0f)), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } })
                        g.FillPath(pb, path);
                }
            }
            g.SmoothingMode = sm;
        }

        /// <summary>3.0: leuchtende Segmente und Spitzen als Liste (zum Zeichnen ueber die Grafikkarte) – LED-Balken und gespiegelt.</summary>
        public int LiveSegments(SpectrumModel m, List<RectangleF> rects, List<Color> colors)
        {
            rects.Clear(); colors.Clear();
            int eff = Eff;
            if (Bounds.Width < 20 || Bounds.Height < 20 || m == null || (eff != 0 && eff != 3)) return 0;
            m.Bars(_bars, _lvl, _pk);
            if (ShowRunning) ShowWave();
            float step = _segH + _segGap;
            if (eff == 3)
            {
                int half = HalfSegs;
                float cy = MirrorCenter;
                for (int i = 0; i < _bars; i++)
                {
                    float x = _area.X + i * (_barW + _gap);
                    int lit = Math.Min(half, (int)(_lvl[i] * half + 0.5f));
                    int pk = Math.Min(half, (int)(_pk[i] * half + 0.5f)) - 1;
                    for (int s = 0; s < lit; s++)
                    {
                        var col = SegColor((s + 1f) / half, BarFrac(i));
                        rects.Add(new RectangleF(x, cy - (s + 1) * step + _segGap / 2f, _barW, _segH)); colors.Add(col);
                        rects.Add(new RectangleF(x, cy + s * step + _segGap / 2f, _barW, _segH)); colors.Add(col);
                    }
                    if (pk >= lit && pk > 0)
                    {
                        var col = Theme.Lighten(SegColor((pk + 1f) / half, BarFrac(i)), 0.25f);
                        rects.Add(new RectangleF(x, cy - (pk + 1) * step + _segGap / 2f, _barW, _segH)); colors.Add(col);
                        rects.Add(new RectangleF(x, cy + pk * step + _segGap / 2f, _barW, _segH)); colors.Add(col);
                    }
                }
                return rects.Count;
            }
            for (int i = 0; i < _bars; i++)
            {
                float x = _area.X + i * (_barW + _gap);
                int lit = Math.Min(_segs, (int)(_lvl[i] * _segs + 0.5f));
                int pk = (int)(_pk[i] * _segs + 0.5f) - 1;
                for (int s = 0; s < lit; s++)
                {
                    rects.Add(new RectangleF(x, _area.Bottom - (s + 1) * step + _segGap, _barW, _segH));
                    colors.Add(SegColor((s + 1f) / _segs, BarFrac(i)));
                }
                if (pk >= lit && pk > 0 && pk < _segs)
                {
                    rects.Add(new RectangleF(x, _area.Bottom - (pk + 1) * step + _segGap, _barW, _segH));
                    colors.Add(Theme.Lighten(SegColor((pk + 1f) / _segs, BarFrac(i)), 0.25f));
                }
            }
            return rects.Count;
        }

        // 3.0: Wasserfall fuer die Grafikkarte (Ringpuffer: oberste Zeile = _wfTop)
        internal int[] WaterfallPixels { get { if (Waterfall) EnsureWaterfall(); return _wfPx; } }
        internal int WaterfallWidth => _wfW;
        internal int WaterfallHeight => _wfH;
        internal int WaterfallTop => _wfTop;
        /// <summary>Zaehlt hoch bei jeder neuen Zeile; WaterfallEpoch bei neuer Groesse (dann alles neu hochladen).</summary>
        internal int WaterfallVersion, WaterfallEpoch;

        /// <summary>Bereich, der sich live aendert (zum Neuzeichnen).</summary>
        public Rectangle LiveBounds => Rectangle.Inflate(Rectangle.Ceiling(_area), 1, 1);

        /// <summary>Flaeche der Balken (fuer die Anzeige unter der Maus).</summary>
        public RectangleF Area => _area;

        /// <summary>
        /// 2.17: Frequenz und Pegel an der Stelle x (Maus): "1,2 kHz · −24 dB". false = ausserhalb.
        /// Der Pegel ist der des Balkens (mit der Hi-Fi-Neigung des Spektrums).
        /// </summary>
        public bool HoverInfo(SpectrumModel m, float x, out string text)
        {
            text = null;
            if (m == null || _bars <= 0 || x < _area.X || x > _area.Right || Eff == 4 || Eff == 6) return false;
            double t = (x - _area.X) / Math.Max(1f, _area.Width);
            double f = SpectrumModel.LowHz * Math.Pow(SpectrumModel.HighHz / SpectrumModel.LowHz, t);
            int bar = Math.Max(0, Math.Min(_bars - 1, (int)((x - _area.X) / (_barW + _gap))));
            var lvl = new float[_bars]; var pk = new float[_bars];
            m.Bars(_bars, lvl, pk);
            float db = SpectrumModel.Floor + lvl[bar] * (SpectrumModel.Top - SpectrumModel.Floor);
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            string fs = f >= 1000 ? (f / 1000).ToString(f >= 10000 ? "0" : "0.0", de) + " kHz" : f.ToString("0", de) + " Hz";
            text = fs + "  ·  " + (lvl[bar] <= 0.001f ? "still" : db.ToString("0", de).Replace("-", "\u2212") + " dB");
            return true;
        }
    }
}
