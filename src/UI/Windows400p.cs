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
    /// <summary>4.0 (finale): Fenster mit Schiebereglern und Tasten (Kompressor, Crossfader, Signalgenerator).</summary>
    internal abstract class ProWin : PultWin
    {
        protected static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        protected sealed class Sl
        {
            public string Id, Label;
            public float Min, Max, Def, Step;
            public bool Log;
            public Func<float> Get;
            public Action<float, bool> Set;
            public Func<float, string> Fmt;
            public Func<bool> Enabled;
            public RectangleF Track;
        }

        protected readonly List<Sl> Sliders = new List<Sl>();
        protected readonly Dictionary<string, RectangleF> Btn = new Dictionary<string, RectangleF>();
        private Sl _drag;
        protected Color SliderColor = Theme.Accent;

        protected ProWin(string title, int w, int h) : base(title, w, h) { }

        protected static float Frac(Sl s, float v)
        {
            float f = s.Log ? (float)(Math.Log(Math.Max(s.Min, v) / s.Min) / Math.Log(s.Max / s.Min)) : (v - s.Min) / (s.Max - s.Min);
            return Math.Max(0f, Math.Min(1f, f));
        }

        protected static float ValAt(Sl s, float f)
        {
            f = Math.Max(0f, Math.Min(1f, f));
            float v = s.Log ? (float)(s.Min * Math.Pow(s.Max / s.Min, f)) : s.Min + (s.Max - s.Min) * f;
            if (s.Step > 0f) v = (float)Math.Round(v / s.Step) * s.Step;
            return Math.Max(s.Min, Math.Min(s.Max, v));
        }

        protected void DrawSlider(Graphics g, Sl s, RectangleF row)
        {
            bool en = s.Enabled == null || s.Enabled();
            float lw = Math.Min(Theme.F(110), row.Width * 0.3f), vw = Theme.F(78);
            Label(g, s.Label, new RectangleF(row.X, row.Y, lw, row.Height), en ? Theme.Text : Theme.TextFaint, 10f, false, VCenter);
            var tr = new RectangleF(row.X + lw + Theme.F(6), row.Y + row.Height / 2 - Theme.F(3), row.Width - lw - vw - Theme.F(14), Theme.F(6));
            s.Track = new RectangleF(tr.X - Theme.F(6), row.Y, tr.Width + Theme.F(12), row.Height);
            float v = s.Get(), f = Frac(s, v);
            Theme.FillRound(g, Theme.Hole, tr, Theme.F(3));
            var c = en ? SliderColor : Theme.Disabled;
            if (f > 0f) Theme.FillRound(g, Theme.Alpha(c, 200), new RectangleF(tr.X, tr.Y, tr.Width * f, tr.Height), Theme.F(3));
            float cx = tr.X + tr.Width * f, cr = Theme.F(7);
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Hover == "sl:" + s.Id ? Theme.Lighten(Theme.Text, 0.1f) : Theme.Lighten(Theme.PanelTop, 0.35f))) g.FillEllipse(b, cx - cr, tr.Y + tr.Height / 2 - cr, cr * 2, cr * 2);
            using (var p = new Pen(c, Math.Max(1f, Theme.F(1.6f)))) g.DrawEllipse(p, cx - cr, tr.Y + tr.Height / 2 - cr, cr * 2, cr * 2);
            g.SmoothingMode = sm;
            Label(g, s.Fmt(v), new RectangleF(row.Right - vw, row.Y, vw, row.Height), en ? Theme.ValueText : Theme.TextFaint, 10f, true, VRight);
        }

        protected static readonly StringFormat VCenter = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        protected static readonly StringFormat VRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

        protected void DrawBtn(Graphics g, string id, RectangleF r, string text, bool lit, Color c)
        {
            Btn[id] = r;
            Theme.DrawKey(g, r, text, lit, c, Hover == id);
        }

        private Sl SliderAt(Point p) => Sliders.FirstOrDefault(s => s.Track.Contains(p) && (s.Enabled == null || s.Enabled()));

        protected override string ContentZone(Point p)
        {
            foreach (var kv in Btn) if (kv.Value.Contains(p)) return kv.Key;
            var s = SliderAt(p);
            return s != null ? "sl:" + s.Id : ExtraZone(p);
        }

        protected virtual string ExtraZone(Point p) => null;
        protected abstract void OnButton(string id, MouseEventArgs e);
        protected virtual bool ExtraDown(MouseEventArgs e) => false;

        private void SetFrom(Sl s, int x, bool final)
        {
            float tx = s.Track.X + Theme.F(6), tw = s.Track.Width - Theme.F(12);
            s.Set(ValAt(s, (x - tx) / Math.Max(1f, tw)), final);
            Invalidate();
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            foreach (var kv in Btn) if (kv.Value.Contains(e.Location)) { OnButton(kv.Key, e); Invalidate(); return true; }
            if (e.Button == MouseButtons.Left)
            {
                var s = SliderAt(e.Location);
                if (s != null) { _drag = s; Capture = true; SetFrom(s, e.X, false); return true; }
            }
            return ExtraDown(e);
        }

        protected override void ContentMove(MouseEventArgs e) { if (_drag != null) SetFrom(_drag, e.X, false); }

        protected override void ContentUp(MouseEventArgs e)
        {
            if (_drag == null) return;
            SetFrom(_drag, e.X, true);
            _drag = null; Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var s = SliderAt(e.Location);
            if (s == null) return;
            float f = Frac(s, s.Get()) + Math.Sign(e.Delta) * ((ModifierKeys & Keys.Control) != 0 ? 0.005f : 0.02f);
            float v = ValAt(s, f);
            if (v == s.Get() && s.Step > 0f) v = Math.Max(s.Min, Math.Min(s.Max, s.Get() + Math.Sign(e.Delta) * s.Step));
            s.Set(v, true);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var s = SliderAt(e.Location);
            if (s != null) { s.Set(s.Def, true); Invalidate(); }
        }

        protected static string Db(float v, int dec = 1) => (v > 0.04f ? "+" : v < -0.04f ? "−" : "") + Math.Abs(v).ToString(dec == 0 ? "0" : "0.0", De) + " dB";
        protected static string Ms(float v) => v < 10f ? v.ToString("0.0", De) + " ms" : v < 1000f ? v.ToString("0", De) + " ms" : (v / 1000f).ToString("0.00", De) + " s";
    }

    /// <summary>4.0 (finale): Kompressor-Fenster eines Kanals – Werte, Kennlinie und Verlauf der Absenkung.</summary>
    internal sealed class CompWindow : ProWin
    {
        public string Key;
        public Func<CompConfig> Get;            // null = feste Werte
        public Action<CompConfig, bool> Set;
        public Func<bool> On;
        public Action<bool> SetOn;
        private readonly float[] _gr = new float[180];
        private int _grPos;
        private float _grNow, _inDb = -100f;

        public CompWindow(string name, Color color) : base("Kompressor – " + name, 600, 420)
        {
            SliderColor = color.GetBrightness() < 0.25f ? Theme.Accent : color;
            Add("thr", "Schwelle", -60f, 0f, -24f, 0.5f, false, v => Db(v), c => c.Thr, (c, v) => c.Thr = v);
            Add("ratio", "Verhältnis", 1f, 20f, 4f, 0.1f, true, v => v.ToString(v < 10 ? "0.0" : "0", De) + " : 1", c => c.Ratio, (c, v) => c.Ratio = v);
            Add("att", "Attack", 0.1f, 200f, 5f, 0f, true, Ms, c => c.Att, (c, v) => c.Att = (float)Math.Round(v, v < 10 ? 1 : 0));
            Add("rel", "Release", 10f, 3000f, 200f, 0f, true, Ms, c => c.Rel, (c, v) => c.Rel = (float)Math.Round(v / 5f) * 5f);
            Add("mk", "Make-up", 0f, 24f, 8f, 0.5f, false, v => Db(v), c => c.Makeup, (c, v) => c.Makeup = v);
            Add("knee", "Knie", 0f, 18f, 6f, 0.5f, false, v => Db(v), c => c.Knee, (c, v) => c.Knee = v);
        }

        private void Add(string id, string label, float min, float max, float def, float step, bool log, Func<float, string> fmt, Func<CompConfig, float> get, Action<CompConfig, float> set)
        {
            Sliders.Add(new Sl
            {
                Id = id, Label = label, Min = min, Max = max, Def = def, Step = step, Log = log, Fmt = fmt,
                Get = () => get(Cur),
                Set = (v, final) => { var c = Cur.Clone(); set(c, v); c.Clamp(); Set?.Invoke(c, final); },
            });
        }

        private CompConfig Cur => Get?.Invoke() ?? CompConfig.Defaults();

        /// <summary>Neue Messwerte (Absenkung dB, Eingangspegel dBFS) – ca. 30 mal pro Sekunde.</summary>
        public void Feed(float grDb, float inDb)
        {
            _grNow = grDb; _inDb = inDb;
            _gr[_grPos] = grDb; _grPos = (_grPos + 1) % _gr.Length;
            Invalidate();
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            Btn.Clear();
            var c = Cur;
            bool custom = Get?.Invoke() != null;
            // Kennlinie
            float gs = Theme.F(180);
            var gr = new RectangleF(r.X + Theme.F(4), r.Y + Theme.F(4), gs, gs);
            Theme.FillRound(g, Theme.Well, RectangleF.Inflate(gr, Theme.F(3), Theme.F(3)), Theme.F(4));
            using (var p = new Pen(Theme.Alpha(Theme.TextFaint, 60), 1f))
                for (int d = -60; d <= 0; d += 12)
                {
                    float x = gr.X + gr.Width * (d + 60) / 60f, y = gr.Bottom - gr.Height * (d + 60) / 60f;
                    g.DrawLine(p, x, gr.Y, x, gr.Bottom); g.DrawLine(p, gr.X, y, gr.Right, y);
                }
            using (var p = new Pen(Theme.Alpha(Theme.TextFaint, 120), 1f) { DashStyle = DashStyle.Dash }) g.DrawLine(p, gr.X, gr.Bottom, gr.Right, gr.Y);
            var pts = new List<PointF>();
            for (int i = 0; i <= 60; i++)
            {
                float x = -60f + i;
                float y = x + CompSpec.Curve(x, c.Thr, c.Ratio, c.Knee) + c.Makeup;
                pts.Add(new PointF(gr.X + gr.Width * i / 60f, Math.Max(gr.Y, Math.Min(gr.Bottom, gr.Bottom - gr.Height * (y + 60) / 60f))));
            }
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(SliderColor, Math.Max(1.5f, Theme.F(2)))) g.DrawLines(p, pts.ToArray());
            float tx = gr.X + gr.Width * (c.Thr + 60) / 60f;
            using (var p = new Pen(Theme.Alpha(Theme.MuteRed, 140), 1f) { DashStyle = DashStyle.Dot }) g.DrawLine(p, tx, gr.Y, tx, gr.Bottom);
            if (_inDb > -60f && On != null && On())
            {
                float x = Math.Min(0f, _inDb), y = x + CompSpec.Curve(x, c.Thr, c.Ratio, c.Knee) + c.Makeup;
                float px = gr.X + gr.Width * (x + 60) / 60f, py = Math.Max(gr.Y, Math.Min(gr.Bottom, gr.Bottom - gr.Height * (y + 60) / 60f));
                using (var b = new SolidBrush(Theme.LedGreen)) g.FillEllipse(b, px - Theme.F(4), py - Theme.F(4), Theme.F(8), Theme.F(8));
            }
            g.SmoothingMode = sm;
            Label(g, "Eingang →", new RectangleF(gr.X, gr.Bottom + Theme.F(4), gr.Width, Theme.F(14)), Theme.TextFaint, 8f, false, Theme.Center);

            // Verlauf der Absenkung
            var hr = new RectangleF(gr.Right + Theme.F(18), gr.Y, r.Right - gr.Right - Theme.F(22), gs);
            Theme.FillRound(g, Theme.Well, RectangleF.Inflate(hr, Theme.F(3), Theme.F(3)), Theme.F(4));
            Label(g, "ABSENKUNG", new RectangleF(hr.X + Theme.F(6), hr.Y + Theme.F(4), hr.Width, Theme.F(14)), Theme.TextDim, 8f, true);
            Label(g, _grNow > 0.05f ? "−" + _grNow.ToString("0.0", De) + " dB" : "0,0 dB", new RectangleF(hr.X, hr.Y + Theme.F(2), hr.Width - Theme.F(6), Theme.F(18)), _grNow > 6f ? Theme.MuteRed : Theme.ValueText, 11f, true, VRight);
            const float range = 24f;
            float bw = hr.Width / _gr.Length;
            using (var b = new SolidBrush(Theme.Alpha(SliderColor, 190)))
                for (int i = 0; i < _gr.Length; i++)
                {
                    float v = _gr[(_grPos + i) % _gr.Length];
                    if (v <= 0.05f) continue;
                    float h = (hr.Height - Theme.F(24)) * Math.Min(1f, v / range);
                    g.FillRectangle(b, hr.X + i * bw, hr.Y + Theme.F(22), Math.Max(1f, bw), h);
                }
            using (var f = new Pen(Theme.Alpha(Theme.TextFaint, 70), 1f))
                foreach (int d in new[] { 6, 12, 18 })
                {
                    float y = hr.Y + Theme.F(22) + (hr.Height - Theme.F(24)) * d / range;
                    g.DrawLine(f, hr.X, y, hr.Right, y);
                    Label(g, "−" + d, new RectangleF(hr.X + Theme.F(2), y - Theme.F(12), Theme.F(30), Theme.F(12)), Theme.TextFaint, 7.5f);
                }

            // Regler
            float sy = gr.Bottom + Theme.F(24), rh = Theme.F(24);
            for (int i = 0; i < Sliders.Count; i++)
            {
                int col = i % 2, row = i / 2;
                float cw = (r.Width - Theme.F(16)) / 2f;
                DrawSlider(g, Sliders[i], new RectangleF(r.X + col * (cw + Theme.F(16)), sy + row * (rh + Theme.F(6)), cw, rh));
            }
            float by = r.Bottom - Theme.F(30);
            bool on = On != null && On();
            DrawBtn(g, "on", new RectangleF(r.X, by, Theme.F(130), Theme.F(28)), on ? "KOMP AN" : "KOMP AUS", on, Theme.LedGreen);
            DrawBtn(g, "def", new RectangleF(r.X + Theme.F(138), by, Theme.F(150), Theme.F(28)), custom ? "STANDARD-WERTE" : "FESTE WERTE", !custom, Theme.Accent);
            Label(g, custom ? "Eigene Werte" : "Feste Werte (wie bisher) – Regler ändern = eigene Werte", new RectangleF(r.X + Theme.F(296), by, r.Width - Theme.F(296), Theme.F(28)), Theme.TextFaint, 9f, false, VCenter);
        }

        protected override void OnButton(string id, MouseEventArgs e)
        {
            if (id == "on") SetOn?.Invoke(!(On?.Invoke() ?? false));
            else if (id == "def") Set?.Invoke(null, true);
        }
    }

    /// <summary>4.0 (finale): Crossfader zwischen zwei Kanaelen.</summary>
    internal sealed class CrossfaderWindow : ProWin
    {
        public Func<string> NameA, NameB;
        public Func<Color> ColorA, ColorB;
        public Func<float> Pos;
        public Action<float, bool> SetPos;
        public event Action<bool, Point> Pick;   // (B?, Bildschirmpunkt)
        private RectangleF _track, _ra, _rb;
        private bool _drag;

        public CrossfaderWindow() : base("Crossfader", 460, 128) { }

        protected override string ExtraZone(Point p) => _ra.Contains(p) ? "a" : _rb.Contains(p) ? "b" : _track.Contains(p) ? "track" : null;

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            Btn.Clear();
            float nw = Theme.F(92);
            _ra = new RectangleF(r.X, r.Y + Theme.F(6), nw, Theme.F(46));
            _rb = new RectangleF(r.Right - nw, r.Y + Theme.F(6), nw, Theme.F(46));
            DrawName(g, _ra, "A", NameA?.Invoke(), ColorA?.Invoke() ?? Theme.Accent, Hover == "a");
            DrawName(g, _rb, "B", NameB?.Invoke(), ColorB?.Invoke() ?? Theme.Accent, Hover == "b");
            _track = new RectangleF(_ra.Right + Theme.F(18), r.Y + Theme.F(10), _rb.X - _ra.Right - Theme.F(36), Theme.F(38));
            var slot = new RectangleF(_track.X, _track.Y + _track.Height / 2 - Theme.F(3), _track.Width, Theme.F(6));
            Theme.FillRound(g, Theme.Hole, slot, Theme.F(3));
            using (var p = new Pen(Theme.Alpha(Theme.TextFaint, 120), 1f))
                for (int i = 0; i <= 10; i++)
                {
                    float x = slot.X + slot.Width * i / 10f, h = i == 5 ? Theme.F(12) : Theme.F(6);
                    g.DrawLine(p, x, _track.Y, x, _track.Y + h);
                    g.DrawLine(p, x, _track.Bottom - h, x, _track.Bottom);
                }
            float pos = Pos?.Invoke() ?? 0.5f;
            float cx = slot.X + slot.Width * pos, cw = Theme.F(22), ch = _track.Height;
            var cap = new RectangleF(cx - cw / 2, _track.Y, cw, ch);
            using (var b = new LinearGradientBrush(cap, Theme.Lighten(Theme.PanelTop, 0.45f), Theme.Darken(Theme.PanelBottom, 0.1f), LinearGradientMode.Horizontal)) Theme.FillRound(g, b, cap, Theme.F(3));
            using (var p = new Pen(Theme.Text, Math.Max(1.5f, Theme.F(2)))) g.DrawLine(p, cx, cap.Y + Theme.F(4), cx, cap.Bottom - Theme.F(4));
            Theme.StrokeRound(g, Theme.PanelEdge, 1f, cap, Theme.F(3));
            string t = Math.Abs(pos - 0.5f) < 0.01f ? "Mitte" : pos < 0.5f ? "A " + Math.Round((0.5f - pos) * 200) + " %" : "B " + Math.Round((pos - 0.5f) * 200) + " %";
            Label(g, t + "   · Doppelklick: Mitte · Namen anklicken: Kanal wählen", new RectangleF(r.X, r.Bottom - Theme.F(20), r.Width, Theme.F(18)), Theme.TextFaint, 8.5f, false, Theme.Center);
        }

        private static void DrawName(Graphics g, RectangleF r, string tag, string name, Color c, bool hover)
        {
            Theme.FillRound(g, hover ? Theme.Lighten(Theme.Well, 0.06f) : Theme.Well, r, Theme.F(4));
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r.X, r.Y, Theme.F(4), r.Height);
            Label(g, tag, new RectangleF(r.X + Theme.F(10), r.Y + Theme.F(3), r.Width, Theme.F(16)), Theme.TextDim, 9f, true);
            Label(g, (name ?? "– wählen –") + " ▾", new RectangleF(r.X + Theme.F(10), r.Y + Theme.F(20), r.Width - Theme.F(12), Theme.F(20)), name == null ? Theme.TextFaint : Theme.Text, 10f, true, VCenter);
        }

        private void SetFromX(int x, bool final)
        {
            float f = (x - _track.X) / Math.Max(1f, _track.Width);
            f = Math.Max(0f, Math.Min(1f, f));
            if (Math.Abs(f - 0.5f) < 0.015f) f = 0.5f;   // Mitte rastet ein
            SetPos?.Invoke(f, final);
            Invalidate();
        }

        protected override bool ExtraDown(MouseEventArgs e)
        {
            if (_ra.Contains(e.Location) || _rb.Contains(e.Location)) { Pick?.Invoke(_rb.Contains(e.Location), PointToScreen(new Point((int)(_rb.Contains(e.Location) ? _rb.X : _ra.X), (int)_ra.Bottom))); return true; }
            if (_track.Contains(e.Location) && e.Button == MouseButtons.Left) { _drag = true; Capture = true; SetFromX(e.X, false); return true; }
            return false;
        }

        protected override void ContentMove(MouseEventArgs e) { if (_drag) SetFromX(e.X, false); else base.ContentMove(e); }
        protected override void ContentUp(MouseEventArgs e) { if (_drag) { _drag = false; Capture = false; SetFromX(e.X, true); } else base.ContentUp(e); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (_track.Contains(e.Location)) { SetPos?.Invoke(0.5f, true); Invalidate(); return; }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float p = (Pos?.Invoke() ?? 0.5f) + Math.Sign(e.Delta) * 0.05f;
            SetPos?.Invoke(Math.Max(0f, Math.Min(1f, (float)Math.Round(p * 20) / 20f)), true);
            Invalidate();
        }

        protected override void OnButton(string id, MouseEventArgs e) { }
    }

    /// <summary>4.0 (finale): Signalgenerator – Sinus, Gleitton, rosa und weisses Rauschen auf einem Geraet.</summary>
    internal sealed class GeneratorWindow : ProWin
    {
        public Func<int> Kind; public Action<int> SetKind;
        public Func<float> Hz; public Action<float> SetHz;
        public Func<int> Level; public Action<int> SetLevel;
        public Func<int> Side; public Action<int> SetSide;
        public Func<string> DeviceName;
        public Func<bool> Running; public Action<bool> SetRunning;
        public event Action<Point> PickDevice;

        public GeneratorWindow() : base("Signalgenerator", 470, 300)
        {
            SliderColor = Color.FromArgb(0x3c, 0xd6, 0xc8);
            Sliders.Add(new Sl { Id = "hz", Label = "Frequenz", Min = 20f, Max = 20000f, Def = 1000f, Log = true, Get = () => Hz(), Set = (v, f) => SetHz(SnapHz(v)), Fmt = v => PeqWindow.HzText(v), Enabled = () => Kind() == 0 });
            Sliders.Add(new Sl { Id = "lvl", Label = "Pegel", Min = -60f, Max = -6f, Def = -24f, Step = 1f, Get = () => Level(), Set = (v, f) => SetLevel((int)Math.Round(v)), Fmt = v => Db(v, 0) + "FS" });
        }

        private static float SnapHz(float v)
        {
            foreach (var s in new[] { 50f, 60f, 100f, 250f, 440f, 500f, 1000f, 2000f, 4000f, 8000f, 10000f, 16000f })
                if (Math.Abs(Math.Log(v / s)) < 0.02) return s;
            return v < 100 ? (float)Math.Round(v) : v < 1000 ? (float)Math.Round(v / 5) * 5 : (float)Math.Round(v / 50) * 50;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            Btn.Clear();
            float y = r.Y + Theme.F(4), bw = (r.Width - Theme.F(18)) / 4f;
            string[] kinds = { "SINUS", "GLEITTON", "ROSA", "WEISS" };
            for (int i = 0; i < 4; i++) DrawBtn(g, "k" + i, new RectangleF(r.X + i * (bw + Theme.F(6)), y, bw, Theme.F(28)), kinds[i], Kind() == i, SliderColor);
            y += Theme.F(38);
            DrawSlider(g, Sliders[0], new RectangleF(r.X, y, r.Width, Theme.F(24)));
            y += Theme.F(30);
            DrawSlider(g, Sliders[1], new RectangleF(r.X, y, r.Width, Theme.F(24)));
            y += Theme.F(34);
            string[] sides = { "BEIDE", "LINKS", "RECHTS" };
            float sw = Theme.F(72);
            Label(g, "Seite", new RectangleF(r.X, y, Theme.F(100), Theme.F(26)), Theme.Text, 10f, false, VCenter);
            for (int i = 0; i < 3; i++) DrawBtn(g, "s" + i, new RectangleF(r.X + Theme.F(116) + i * (sw + Theme.F(6)), y, sw, Theme.F(26)), sides[i], Side() == i, SliderColor);
            y += Theme.F(34);
            Label(g, "Gerät", new RectangleF(r.X, y, Theme.F(100), Theme.F(26)), Theme.Text, 10f, false, VCenter);
            DrawBtn(g, "dev", new RectangleF(r.X + Theme.F(116), y, r.Width - Theme.F(116), Theme.F(26)), (DeviceName?.Invoke() ?? "Windows-Standardgerät") + " ▾", false, SliderColor);
            float by = r.Bottom - Theme.F(34);
            bool run = Running();
            DrawBtn(g, "run", new RectangleF(r.X, by, Theme.F(140), Theme.F(32)), run ? "■ STOPP" : "▶ START", run, Theme.MuteRed);
            Label(g, run ? (Kind() == 1 ? "Gleitton läuft (10 s, wiederholt)" : "Läuft – vorher leise stellen!") : "Tipp: vorher leise stellen – Hochtöner mögen keine lauten Töne.",
                new RectangleF(r.X + Theme.F(150), by, r.Width - Theme.F(150), Theme.F(32)), run ? Theme.MuteRed : Theme.TextFaint, 9f, false, VCenter);
        }

        protected override void OnButton(string id, MouseEventArgs e)
        {
            if (id.Length == 2 && id[0] == 'k') SetKind(id[1] - '0');
            else if (id.Length == 2 && id[0] == 's') SetSide(id[1] - '0');
            else if (id == "run") SetRunning(!Running());
            else if (id == "dev" && Btn.TryGetValue("dev", out var r)) PickDevice?.Invoke(PointToScreen(new Point((int)r.X, (int)r.Bottom)));
        }
    }
}
