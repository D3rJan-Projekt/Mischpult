using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// 4.0: Hoertest. Pro Ohr und Frequenz werden kurze Toene immer etwas lauter gespielt, bis "Ich hoere ihn" kommt;
    /// danach noch einmal leiser und in kleinen Schritten hoch (genauer). Daraus macht das Pult den Hoer-EQ.
    /// Kein medizinischer Test.
    /// </summary>
    internal sealed class HearTestWindow : Form
    {
        private readonly Action<SoundClip, float> _play;
        private readonly Action<HearProfile> _apply;
        private readonly Timer _timer = new Timer { Interval = 40 };
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private int _state;          // 0 Einleitung, 1 Test, 2 Ergebnis
        private bool _quick = true;
        private int _ear, _fi;       // aktuelles Ohr, Frequenz-Index in der Testfolge
        private int[] _order;        // Indizes in HearProfile.Freqs
        private float _level;        // dBFS
        private int _phase;          // 0 grob hoch, 1 fein hoch
        private double _toneAt = -10, _nextAt;
        private bool _heardNow;
        private readonly float[][] _thr = { new float[HearProfile.Freqs.Length], new float[HearProfile.Freqs.Length] };
        private int _strength;
        private string _hover;
        public string DeviceName;
        private const float StartDb = -70f, MaxDb = -12f;

        public HearTestWindow(Action<SoundClip, float> play, Action<HearProfile> apply, HearProfile previous)
        {
            _play = play; _apply = apply;
            Text = "Hörtest – Mischpult";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.Px(620), Theme.Px(460));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer.Tick += (s, e) => Step();
            if (previous != null && previous.Valid)
            {
                Array.Copy(previous.Left, _thr[0], _thr[0].Length);
                Array.Copy(previous.Right, _thr[1], _thr[1].Length);
                _strength = previous.Strength;
                _state = 2;
                _hasResult = true;
            }
        }

        private bool _hasResult;

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }
        protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }

        private double Now => _clock.Elapsed.TotalSeconds;

        // ---------------------------------------------------------------- Ablauf
        private void Start()
        {
            _order = _quick ? new[] { 3, 1, 2, 4, 6, 8 } : new[] { 3, 0, 1, 2, 4, 5, 6, 7, 8, 9 };   // 1 kHz zuerst
            for (int e = 0; e < 2; e++) for (int i = 0; i < _thr[e].Length; i++) _thr[e][i] = float.NaN;
            _ear = 0; _fi = 0;
            BeginFreq(StartDb);
            _state = 1;
            _timer.Start();
            Invalidate();
        }

        private void BeginFreq(float start)
        {
            _phase = 0;
            _level = Math.Max(-90f, Math.Min(-30f, start));
            _nextAt = Now + 0.8;
            _heardNow = false;
        }

        private void Step()
        {
            if (_state != 1) return;
            double now = Now;
            if (_heardNow)
            {
                _heardNow = false;
                if (_phase == 0) { _phase = 1; _level -= 12f; _nextAt = now + 0.9; Invalidate(); return; }
                Record(_level);
                return;
            }
            if (now < _nextAt) return;
            // naechster Ton (bzw. nach dem Ton: lauter)
            if (_toneAt > 0 && now - _toneAt < 1.6) return;
            if (_toneAt > 0 && now - _toneAt >= 1.6)
            {
                _toneAt = -10;
                _level += _phase == 0 ? 6f : 3f;
                if (_level > MaxDb) { Record(MaxDb); return; }
            }
            int fi = _order[_fi];
            _play(HearingEq.Pulses(HearProfile.Freqs[fi], _ear), (float)Math.Pow(10, _level / 20.0));
            _toneAt = now;
            Invalidate();
        }

        private void Heard()
        {
            if (_state != 1) return;
            if (_toneAt > 0 && Now - _toneAt <= 1.9) { _heardNow = true; _toneAt = -10; }
        }

        private void Record(float thr)
        {
            _thr[_ear][_order[_fi]] = thr;
            _fi++;
            if (_fi >= _order.Length)
            {
                _fi = 0;
                _ear++;
                if (_ear >= 2) { Finish(); return; }
            }
            BeginFreq(thr - 15f);
            _toneAt = -10;
            Invalidate();
        }

        private void Finish()
        {
            _timer.Stop();
            for (int e = 0; e < 2; e++) { var f = HearingEq.FillGaps(_thr[e]); Array.Copy(f, _thr[e], f.Length); }
            _state = 2;
            _hasResult = true;
            Invalidate();
        }

        private HearProfile Result => new HearProfile
        {
            Left = (float[])_thr[0].Clone(), Right = (float[])_thr[1].Clone(), Strength = _strength,
            Date = DateTime.Now.ToString("yyyy-MM-dd"), DeviceName = DeviceName,
        };

        // ---------------------------------------------------------------- Zeichnen
        private RectangleF BigBtn => new RectangleF(ClientSize.Width / 2f - Theme.F(150), Theme.F(250), Theme.F(300), Theme.F(64));
        private RectangleF Btn1 => new RectangleF(ClientSize.Width - Theme.F(150), ClientSize.Height - Theme.F(46), Theme.F(134), Theme.F(30));
        private RectangleF Btn2 => new RectangleF(ClientSize.Width - Theme.F(300), ClientSize.Height - Theme.F(46), Theme.F(142), Theme.F(30));
        private RectangleF Btn3 => new RectangleF(Theme.F(16), ClientSize.Height - Theme.F(46), Theme.F(150), Theme.F(30));
        private RectangleF ModeQuick => new RectangleF(Theme.F(32), Theme.F(300), Theme.F(270), Theme.F(34));
        private RectangleF ModeFull => new RectangleF(Theme.F(318), Theme.F(300), Theme.F(270), Theme.F(34));
        private RectangleF StrengthRect(int i) => new RectangleF(Theme.F(16) + i * Theme.F(150), Theme.F(330), Theme.F(144), Theme.F(28));
        private RectangleF Chart => new RectangleF(Theme.F(60), Theme.F(64), ClientSize.Width - Theme.F(80), Theme.F(230));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            float x = Theme.F(24), w = ClientSize.Width - Theme.F(48);
            using (var tb = new SolidBrush(Theme.Text))
            using (var db = new SolidBrush(Theme.TextDim))
            {
                if (_state == 0)
                {
                    Theme.Str(g, "Hörtest", Theme.Ui(19f, FontStyle.Bold), tb, new RectangleF(x, Theme.F(18), w, Theme.F(32)), Theme.Left);
                    string t = "So geht's:\n" +
                               "1. Setz deine Kopfhörer auf (am besten die, mit denen du am meisten hörst).\n" +
                               "2. Stell die Windows-Lautstärke auf etwa 30–50 % und lass sie während des Tests so.\n" +
                               "3. Du hörst gleich kurze, leise Töne – erst links, dann rechts. Sie werden langsam lauter.\n" +
                               "4. Klicke auf „ICH HÖRE IHN“ (oder drück die Leertaste), sobald du einen Ton hörst.\n\n" +
                               "Danach hebt der Hör-EQ Töne etwas an, die du schlechter hörst. Das ist kein Hörtest wie beim Arzt – " +
                               "hast du das Gefühl, schlecht zu hören, lass es dort prüfen.";
                    Theme.Str(g, t, Theme.Ui(11.5f), db, new RectangleF(x, Theme.F(60), w, Theme.F(220)), Theme.Wrap);
                    DrawKey(g, ModeQuick, "Schnell (6 Töne pro Ohr, ca. 2 Min.)", "quick", _quick);
                    DrawKey(g, ModeFull, "Genau (10 Töne pro Ohr, ca. 4 Min.)", "full", !_quick);
                    DrawKey(g, Btn1, "START", "start", true);
                    DrawKey(g, Btn2, "ABBRECHEN", "cancel", false);
                }
                else if (_state == 1)
                {
                    int total = _order.Length * 2, done = _ear * _order.Length + _fi;
                    Theme.Str(g, (_ear == 0 ? "◀  Linkes Ohr" : "Rechtes Ohr  ▶"), Theme.Ui(19f, FontStyle.Bold), tb, new RectangleF(x, Theme.F(26), w, Theme.F(34)), Theme.Center);
                    Theme.Str(g, "Ton " + (done + 1) + " von " + total + " · " + PeqWindow.HzText(HearProfile.Freqs[_order[_fi]]), Theme.Ui(12f), db, new RectangleF(x, Theme.F(66), w, Theme.F(22)), Theme.Center);
                    var bar = new RectangleF(x + Theme.F(60), Theme.F(100), w - Theme.F(120), Theme.F(8));
                    Theme.FillRound(g, Theme.Deep, bar, Theme.F(4));
                    Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, Math.Max(Theme.F(8), bar.Width * done / total), bar.Height), Theme.F(4));
                    bool playing = _toneAt > 0 && Now - _toneAt < 1.0;
                    var lamp = new RectangleF(ClientSize.Width / 2f - Theme.F(14), Theme.F(150), Theme.F(28), Theme.F(28));
                    using (var lb = new SolidBrush(playing ? Theme.Accent : Theme.Deep)) g.FillEllipse(lb, lamp);
                    Theme.Str(g, playing ? "Ton läuft …" : "Hör genau hin …", Theme.Ui(10.5f), db, new RectangleF(x, Theme.F(186), w, Theme.F(20)), Theme.Center);
                    var big = BigBtn;
                    Theme.DrawKey(g, big, "ICH HÖRE IHN", true, Theme.LedGreen, _hover == "heard");
                    Theme.Str(g, "Leertaste geht auch", Theme.Ui(9.5f), db, new RectangleF(x, big.Bottom + Theme.F(6), w, Theme.F(18)), Theme.Center);
                    DrawKey(g, Btn2, "ABBRECHEN", "cancel", false);
                }
                else
                {
                    Theme.Str(g, "Ergebnis", Theme.Ui(17f, FontStyle.Bold), tb, new RectangleF(x, Theme.F(14), w, Theme.F(30)), Theme.Left);
                    Theme.Str(g, "So hebt der Hör-EQ an (blau links, rot rechts):", Theme.Ui(10.5f), db, new RectangleF(x, Theme.F(42), w, Theme.F(18)), Theme.Left);
                    DrawChart(g);
                    for (int i = 0; i < 3; i++) DrawKey(g, StrengthRect(i), HearingEq.StrengthNames[i], "st" + i, _strength == i);
                    DrawKey(g, Btn1, "ÜBERNEHMEN", "apply", true);
                    DrawKey(g, Btn2, "SCHLIESSEN", "cancel", false);
                    DrawKey(g, Btn3, "NOCH EINMAL", "again", false);
                }
            }
        }

        private void DrawKey(Graphics g, RectangleF r, string text, string id, bool lit)
        {
            if (lit) Theme.DrawKey(g, r, text, true, Theme.Accent, _hover == id);
            else Theme.DrawKey(g, r, text, false, Theme.TextDim, _hover == id);
        }

        private void DrawChart(Graphics g)
        {
            var c = Chart;
            Theme.FillRound(g, Theme.Deep, RectangleF.Inflate(c, Theme.F(4), Theme.F(4)), Theme.F(6));
            var f = HearProfile.Freqs;
            Func<int, float> X = i => c.X + c.Width * i / (f.Length - 1);
            Func<float, float> Y = db => c.Bottom - db / 10f * c.Height;
            using (var pen = new Pen(Theme.Grid, Theme.F(1)))
            using (var lb = new SolidBrush(Theme.TextFaint))
            {
                for (int db = 0; db <= 10; db += 2)
                {
                    g.DrawLine(pen, c.X, Y(db), c.Right, Y(db));
                    Theme.Str(g, "+" + db, Theme.Ui(9f), lb, new RectangleF(c.X - Theme.F(46), Y(db) - Theme.F(7), Theme.F(40), Theme.F(14)), Theme.Right);
                }
                for (int i = 0; i < f.Length; i++)
                    Theme.Str(g, f[i] >= 1000 ? (f[i] / 1000) + "k" : f[i].ToString(), Theme.Ui(9f), lb, new RectangleF(X(i) - Theme.F(20), c.Bottom + Theme.F(6), Theme.F(40), Theme.F(14)), Theme.Center);
            }
            if (!_hasResult) return;
            var p = Result;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int ear = 0; ear < 2; ear++)
            {
                var gains = HearingEq.Gains(ear == 0 ? p.Left : p.Right, _strength);
                var pts = gains.Select((v, i) => new PointF(X(i), Y(v))).ToArray();
                Color col = ear == 0 ? Color.FromArgb(0x4c, 0xa8, 0xff) : Color.FromArgb(0xff, 0x6a, 0x5a);
                using (var pen = new Pen(col, Theme.F(2.2f))) g.DrawLines(pen, pts);
                using (var b = new SolidBrush(col)) foreach (var q in pts) g.FillEllipse(b, q.X - Theme.F(3.5f), q.Y - Theme.F(3.5f), Theme.F(7), Theme.F(7));
            }
            g.SmoothingMode = sm;
            float maxGain = Math.Max(HearingEq.Gains(p.Left, _strength).Max(), HearingEq.Gains(p.Right, _strength).Max());
            Theme.Label(g, maxGain < 1f ? "Alles ähnlich gut gehört – der Hör-EQ ändert fast nichts." : "Höchstens +" + maxGain.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " dB · alles andere wird dafür etwas leiser, damit nichts übersteuert.",
                new RectangleF(c.X + Theme.F(8), c.Y + Theme.F(4), c.Width - Theme.F(16), Theme.F(16)), Theme.Left, Theme.TextDim, 9.5f);
        }

        // ---------------------------------------------------------------- Maus/Tasten
        private string HitAt(Point p)
        {
            if (_state == 0) return ModeQuick.Contains(p) ? "quick" : ModeFull.Contains(p) ? "full" : Btn1.Contains(p) ? "start" : Btn2.Contains(p) ? "cancel" : null;
            if (_state == 1) return BigBtn.Contains(p) ? "heard" : Btn2.Contains(p) ? "cancel" : null;
            for (int i = 0; i < 3; i++) if (StrengthRect(i).Contains(p)) return "st" + i;
            return Btn1.Contains(p) ? "apply" : Btn2.Contains(p) ? "cancel" : Btn3.Contains(p) ? "again" : null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitAt(e.Location);
            if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string hit = HitAt(e.Location);
            switch (hit)
            {
                case "quick": _quick = true; Invalidate(); break;
                case "full": _quick = false; Invalidate(); break;
                case "start": Start(); break;
                case "cancel": Close(); break;
                case "heard": Heard(); break;
                case "again": _state = 0; Invalidate(); break;
                case "st0": case "st1": case "st2": _strength = hit[2] - '0'; Invalidate(); break;
                case "apply": _apply(Result); Close(); break;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { Heard(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) Close();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Space) { Heard(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        /// <summary>Nur Vorschau: Ergebnis zeigen.</summary>
        internal void DemoResult(HearProfile p)
        {
            Array.Copy(p.Left, _thr[0], _thr[0].Length);
            Array.Copy(p.Right, _thr[1], _thr[1].Length);
            _hasResult = true; _state = 2; Invalidate();
        }
    }
}
