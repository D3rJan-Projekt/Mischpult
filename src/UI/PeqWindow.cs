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
    /// 4.0: parametrischer EQ eines Kanals oder Ausgangs. Punkte in der Kurve ziehen (links/rechts = Frequenz,
    /// hoch/runter = Staerke), Mausrad = Guete, Doppelklick = neues Band, Rechtsklick = Art/Loeschen.
    /// </summary>
    internal sealed class PeqWindow : Form
    {
        internal sealed class Target
        {
            public string Key, Title;
            public Color Color;
            public Func<List<PeqBand>> Get;
            public Action<List<PeqBand>, bool> Set;     // (Baender, fertig)
            public Func<bool> Bypass;
            public Action<bool> SetBypass;
            public Func<PeqSet> Extra;                  // weitere Kurve (Kopfhoerer-Profil, Hoer-EQ) – nur Anzeige
            public string ExtraLabel;
            public bool Pro;                            // 4.0 finale: Kanal-EQ (Mitte/Seite, dynamische Baender moeglich)
        }

        /// <summary>4.0 (finale): Mitte/Seite-EQ bzw. dynamische Baender eingeschaltet (Einstellungen → Profi-Ton).</summary>
        public static bool MsOn, DynOn;
        private bool MsHere => MsOn && _t.Pro;
        private bool DynHere => DynOn && _t.Pro;

        private string ProTag(PeqBand b)
        {
            string s = "";
            if (MsHere && b.Mode == 1) s += " · Mitte";
            else if (MsHere && b.Mode == 2) s += " · Seite";
            if (DynHere && b.IsDyn) s += " · dyn. ab " + Math.Round(b.DynThrNow) + " dB";
            return s;
        }

        private readonly Target _t;
        private int _sel = -1, _drag = -1, _hoverPt = -1;
        private string _hoverBtn;
        private Point _dragStart;
        private float _dragHz, _dragDb;
        private static readonly Color[] BandColors =
        {
            Color.FromArgb(0xff, 0xb0, 0x30), Color.FromArgb(0x4c, 0xc9, 0xf0), Color.FromArgb(0x7d, 0xd8, 0x6a), Color.FromArgb(0xff, 0x6b, 0x9a),
            Color.FromArgb(0xb4, 0x8c, 0xff), Color.FromArgb(0xff, 0x8a, 0x4c), Color.FromArgb(0x3c, 0xd6, 0xc8), Color.FromArgb(0xe8, 0xe0, 0x60),
        };
        private const float RangeDb = 18f;

        public string Key => _t.Key;

        public PeqWindow(Target t)
        {
            _t = t;
            Text = "Parametrischer EQ – " + t.Title + " – Mischpult";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.Px(720), Theme.Px(470));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            if (Bands.Count > 0) _sel = 0;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }

        private List<PeqBand> Bands => _t.Get() ?? new List<PeqBand>();

        // ---------------------------------------------------------------- Bereiche
        private RectangleF Graph => new RectangleF(Theme.F(52), Theme.F(16), ClientSize.Width - Theme.F(70), Theme.F(250));
        private RectangleF ChipRect(int i)
        {
            float w = (ClientSize.Width - Theme.F(32)) / MixerSettings.PeqMax;
            return new RectangleF(Theme.F(16) + i * w, Theme.F(300), w - Theme.F(6), Theme.F(44));
        }
        private RectangleF DetailRect => new RectangleF(Theme.F(16), Theme.F(352), ClientSize.Width - Theme.F(32), Theme.F(52));
        private RectangleF BtnType => new RectangleF(DetailRect.X + Theme.F(8), DetailRect.Y + Theme.F(11), Theme.F(170), Theme.F(30));
        private RectangleF BtnOff => new RectangleF(DetailRect.Right - Theme.F(200), DetailRect.Y + Theme.F(11), Theme.F(90), Theme.F(30));
        private RectangleF BtnDel => new RectangleF(DetailRect.Right - Theme.F(102), DetailRect.Y + Theme.F(11), Theme.F(94), Theme.F(30));
        private RectangleF BtnAdd => new RectangleF(Theme.F(16), ClientSize.Height - Theme.F(46), Theme.F(120), Theme.F(30));
        private RectangleF BtnBypass => new RectangleF(Theme.F(144), ClientSize.Height - Theme.F(46), Theme.F(140), Theme.F(30));
        private RectangleF BtnClear => new RectangleF(Theme.F(292), ClientSize.Height - Theme.F(46), Theme.F(130), Theme.F(30));
        private RectangleF BtnClose => new RectangleF(ClientSize.Width - Theme.F(126), ClientSize.Height - Theme.F(46), Theme.F(110), Theme.F(30));

        private static double FreqToX(RectangleF r, double f) => r.X + r.Width * Math.Log(Math.Max(20, f) / 20.0) / Math.Log(1000.0);
        private static double XToFreq(RectangleF r, float x) => 20.0 * Math.Pow(1000.0, Math.Max(0, Math.Min(1, (x - r.X) / r.Width)));
        private static float DbToY(RectangleF r, double db) => (float)(r.Y + r.Height / 2 - db / (RangeDb * 2.15) * r.Height);
        private static float YToDb(RectangleF r, float y) => (float)((r.Y + r.Height / 2 - y) / r.Height * (RangeDb * 2.15));

        private PointF PointOf(PeqBand b)
        {
            var gr = Graph;
            double db = b.Type >= 3 ? 0 : b.Db;
            return new PointF((float)FreqToX(gr, b.Hz), DbToY(gr, Math.Max(-RangeDb, Math.Min(RangeDb, db))));
        }

        // ---------------------------------------------------------------- Zeichnen
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Theme.MenuBg);
            var gr = Graph;
            var bands = Bands;
            bool bypass = _t.Bypass?.Invoke() ?? false;
            Theme.FillRound(g, Theme.Deep, RectangleF.Inflate(gr, Theme.F(4), Theme.F(4)), Theme.F(6));
            using (var pen = new Pen(Theme.Grid, Theme.F(1)))
            using (var lb = new SolidBrush(Theme.TextFaint))
            {
                foreach (var f in new[] { 30, 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
                {
                    float x = (float)FreqToX(gr, f);
                    g.DrawLine(pen, x, gr.Y, x, gr.Bottom);
                    Theme.Str(g, f >= 1000 ? (f / 1000) + "k" : f.ToString(), Theme.Ui(9f), lb, new RectangleF(x - Theme.F(20), gr.Bottom + Theme.F(4), Theme.F(40), Theme.F(14)), Theme.Center);
                }
                for (int db = -18; db <= 18; db += 6)
                {
                    float y = DbToY(gr, db);
                    using (var p2 = new Pen(db == 0 ? Theme.GridZero : Theme.Grid, Theme.F(1))) g.DrawLine(p2, gr.X, y, gr.Right, y);
                    Theme.Str(g, (db > 0 ? "+" : "") + db, Theme.Ui(9f), lb, new RectangleF(gr.X - Theme.F(44), y - Theme.F(7), Theme.F(38), Theme.F(14)), Theme.Right);
                }
            }
            var clip = g.Clip;
            g.SetClip(gr);
            int steps = Math.Max(60, (int)(gr.Width / 2));
            // weitere Kurve (Profil / Hoer-EQ)
            var extra = _t.Extra?.Invoke();
            if (extra != null && !extra.IsFlat)
            {
                bool lr = extra.Left.Length > 0 || extra.Right.Length > 0;
                for (int ear = 0; ear < (lr ? 2 : 1); ear++)
                {
                    var pts = new PointF[steps];
                    for (int i = 0; i < steps; i++)
                    {
                        double f = 20.0 * Math.Pow(1000.0, i / (double)(steps - 1));
                        pts[i] = new PointF(gr.X + gr.Width * i / (steps - 1), DbToY(gr, Math.Max(-RangeDb * 1.2, Math.Min(RangeDb * 1.2, extra.MagnitudeDb(f, ear)))));
                    }
                    using (var pen = new Pen(Theme.Alpha(ear == 0 ? Theme.TextDim : Color.FromArgb(0xff, 0x8a, 0x80), 170), Theme.F(1.5f)) { DashStyle = DashStyle.Dash })
                        g.DrawLines(pen, pts);
                }
            }
            // einzelne Baender (gefuellt)
            for (int bi = 0; bi < bands.Count; bi++)
            {
                var b = bands[bi];
                if (b.Off) continue;
                var spec = new PeqSpec(b.Type, b.Hz, b.Db, b.QNow);
                var pts = new List<PointF> { new PointF(gr.X, DbToY(gr, 0)) };
                for (int i = 0; i < steps; i++)
                {
                    double f = 20.0 * Math.Pow(1000.0, i / (double)(steps - 1));
                    pts.Add(new PointF(gr.X + gr.Width * i / (steps - 1), DbToY(gr, Math.Max(-RangeDb * 1.2, Math.Min(RangeDb * 1.2, PeqSet.BandDb(spec, f))))));
                }
                pts.Add(new PointF(gr.Right, DbToY(gr, 0)));
                using (var br = new SolidBrush(Theme.Alpha(BandColors[bi % BandColors.Length], bi == _sel ? 60 : 28))) g.FillPolygon(br, pts.ToArray());
            }
            // Summe
            var set = PeqSet.From(bands);
            {
                var pts = new PointF[steps];
                for (int i = 0; i < steps; i++)
                {
                    double f = 20.0 * Math.Pow(1000.0, i / (double)(steps - 1));
                    double db = set == null ? 0 : set.MagnitudeDb(f, 0);
                    pts[i] = new PointF(gr.X + gr.Width * i / (steps - 1), DbToY(gr, Math.Max(-RangeDb * 1.3, Math.Min(RangeDb * 1.3, db))));
                }
                using (var pen = new Pen(bypass ? Theme.TextDim : _t.Color, Theme.F(2.4f)) { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, pts);
            }
            g.Clip = clip;
            // Punkte
            for (int bi = 0; bi < bands.Count; bi++)
            {
                var p = PointOf(bands[bi]);
                float r = Theme.F(bi == _sel ? 8 : 6.5f);
                Color c = bands[bi].Off ? Theme.TextFaint : BandColors[bi % BandColors.Length];
                using (var br = new SolidBrush(c)) g.FillEllipse(br, p.X - r, p.Y - r, 2 * r, 2 * r);
                using (var pen = new Pen(bi == _hoverPt || bi == _drag ? Color.White : Theme.Darken(c, 0.5f), Theme.F(1.4f))) g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
                Theme.Label(g, (bi + 1).ToString(), new RectangleF(p.X - r, p.Y - r, 2 * r, 2 * r), Theme.Center, Color.Black, 8.5f);
            }
            string top = bypass ? "EQ ist gerade aus (A/B) – Klick auf „EQ AUS“ schaltet ihn wieder ein"
                : bands.Count == 0 ? "Doppelklick in die Kurve: neues Band" : "Punkt ziehen · Mausrad = Güte · Rechtsklick = Art/Löschen · Doppelklick = neues Band";
            Theme.Label(g, top, new RectangleF(gr.X + Theme.F(8), gr.Y + Theme.F(4), gr.Width - Theme.F(16), Theme.F(14)), Theme.Left, bypass ? Theme.WarnText : Theme.TextDim, 9f);
            if (extra != null && !extra.IsFlat && _t.ExtraLabel != null)
                Theme.Label(g, "- - " + _t.ExtraLabel, new RectangleF(gr.X + Theme.F(8), gr.Bottom - Theme.F(18), gr.Width - Theme.F(16), Theme.F(14)), Theme.Left, Theme.TextDim, 9f);

            // Chips
            for (int i = 0; i < MixerSettings.PeqMax; i++)
            {
                var r = ChipRect(i);
                if (i >= bands.Count)
                {
                    using (var pen = new Pen(Theme.Alpha(Theme.TextFaint, 90), Theme.F(1)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    continue;
                }
                var b = bands[i];
                bool sel = i == _sel;
                Theme.FillRound(g, sel ? Theme.Lighten(Theme.MenuBg, 0.09f) : Theme.Lighten(Theme.MenuBg, 0.04f), r, Theme.F(5));
                if (sel) Theme.StrokeRound(g, BandColors[i % BandColors.Length], Theme.F(1.5f), r, Theme.F(5));
                using (var br = new SolidBrush(b.Off ? Theme.TextFaint : BandColors[i % BandColors.Length])) g.FillEllipse(br, r.X + Theme.F(6), r.Y + Theme.F(7), Theme.F(8), Theme.F(8));
                Theme.Label(g, (i + 1) + " " + ShortType(b.Type) + (MsHere && b.Mode == 1 ? " M" : MsHere && b.Mode == 2 ? " S" : "") + (DynHere && b.IsDyn ? " ~" : ""), new RectangleF(r.X + Theme.F(17), r.Y + Theme.F(3), r.Width - Theme.F(18), Theme.F(16)), Theme.Left, b.Off ? Theme.TextFaint : Theme.Text, 9f);
                Theme.Label(g, HzText(b.Hz) + (b.Type >= 3 ? "" : "  " + DbText(b.Db)), new RectangleF(r.X + Theme.F(5), r.Y + Theme.F(22), r.Width - Theme.F(6), Theme.F(16)), Theme.Left, b.Off ? Theme.TextFaint : Theme.ValueText, 9f);
            }
            // Details des gewaehlten Bandes
            var dr = DetailRect;
            Theme.FillRound(g, Theme.Lighten(Theme.MenuBg, 0.035f), dr, Theme.F(6));
            if (_sel >= 0 && _sel < bands.Count)
            {
                var b = bands[_sel];
                DrawButton(g, BtnType, PeqBand.TypeNames[b.Type] + " ▾", "type", null);
                string info = HzText(b.Hz) + (b.Type >= 3 ? "" : "   " + DbText(b.Db)) + "   Güte " + b.QNow.ToString("0.00", CultureInfo.GetCultureInfo("de-DE")) + ProTag(b);
                Theme.Label(g, info, new RectangleF(BtnType.Right + Theme.F(14), dr.Y, BtnOff.X - BtnType.Right - Theme.F(20), dr.Height), Theme.Left, Theme.Text, 12f);
                DrawButton(g, BtnOff, b.Off ? "AUS" : "AN", "off", b.Off ? (Color?)null : Theme.LedGreen);
                DrawButton(g, BtnDel, "LÖSCHEN", "del", null);
            }
            else Theme.Label(g, "Kein Band gewählt", dr, Theme.Center, Theme.TextFaint, 11f);

            DrawButton(g, BtnAdd, "BAND +", "add", null);
            DrawButton(g, BtnBypass, bypass ? "EQ AUS" : "EQ AN", "bypass", bypass ? (Color?)null : Theme.LedGreen);
            DrawButton(g, BtnClear, "ALLE LÖSCHEN", "clear", null);
            DrawButton(g, BtnClose, "SCHLIESSEN", "close", null);
        }

        private void DrawButton(Graphics g, RectangleF r, string text, string id, Color? lit)
        {
            bool hover = _hoverBtn == id;
            if (lit.HasValue) Theme.DrawKey(g, r, text, true, lit.Value, hover);
            else Theme.DrawKey(g, r, text, false, Theme.TextDim, hover);
        }

        internal static string HzText(float hz) => hz >= 1000 ? (hz / 1000f).ToString(hz >= 10000 ? "0.#" : "0.##", CultureInfo.GetCultureInfo("de-DE")) + " kHz" : Math.Round(hz).ToString(CultureInfo.InvariantCulture) + " Hz";
        internal static string DbText(float db) => (db > 0.04f ? "+" : db < -0.04f ? "−" : "") + Math.Abs(db).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " dB";
        private static string ShortType(int t) => t == 0 ? "Glocke" : t == 1 ? "Bass" : t == 2 ? "Höhen" : t == 3 ? "Hochpass" : "Tiefpass";

        // ---------------------------------------------------------------- Aendern
        private void Commit(List<PeqBand> l, bool final)
        {
            _t.Set(l, final);
            Invalidate();
        }

        private int PointAt(Point p)
        {
            var bands = Bands;
            for (int i = bands.Count - 1; i >= 0; i--)
            {
                var q = PointOf(bands[i]);
                if (Math.Abs(q.X - p.X) <= Theme.F(10) && Math.Abs(q.Y - p.Y) <= Theme.F(10)) return i;
            }
            return -1;
        }

        private int ChipAt(Point p)
        {
            for (int i = 0; i < Bands.Count; i++) if (ChipRect(i).Contains(p)) return i;
            return -1;
        }

        private void AddBand(float hz, float db)
        {
            var l = PeqBand.CloneList(Bands) ?? new List<PeqBand>();
            if (l.Count >= MixerSettings.PeqMax) return;
            l.Add(new PeqBand { Type = PeqBand.Bell, Hz = (float)Math.Round(hz), Db = (float)Math.Round(db * 2) / 2f, Q = 1f });
            _sel = l.Count - 1;
            Commit(l, true);
        }

        private string ButtonAt(Point p) =>
            BtnClose.Contains(p) ? "close" : BtnAdd.Contains(p) ? "add" : BtnBypass.Contains(p) ? "bypass" : BtnClear.Contains(p) ? "clear" :
            _sel >= 0 && _sel < Bands.Count && BtnType.Contains(p) ? "type" : _sel >= 0 && _sel < Bands.Count && BtnOff.Contains(p) ? "off" :
            _sel >= 0 && _sel < Bands.Count && BtnDel.Contains(p) ? "del" : null;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string btn = ButtonAt(e.Location);
            if (e.Button == MouseButtons.Left && btn != null)
            {
                var l = PeqBand.CloneList(Bands) ?? new List<PeqBand>();
                switch (btn)
                {
                    case "close": Close(); return;
                    case "add":
                        {
                            float[] tryHz = { 1000, 250, 4000, 100, 8000, 500, 2000, 60 };
                            float hz = tryHz.FirstOrDefault(h => l.All(b => Math.Abs(Math.Log(b.Hz / h)) > 0.3)); if (hz <= 0) hz = 1000;
                            AddBand(hz, 0f); return;
                        }
                    case "bypass": _t.SetBypass?.Invoke(!(_t.Bypass?.Invoke() ?? false)); Invalidate(); return;
                    case "clear":
                        if (l.Count == 0) return;
                        if (MessageBox.Show(this, "Alle Bänder löschen?", "Parametrischer EQ", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                        _sel = -1; Commit(new List<PeqBand>(), true); return;
                    case "type": ShowTypeMenu(_sel, PointToScreen(new Point((int)BtnType.X, (int)BtnType.Bottom))); return;
                    case "off": l[_sel].Off = !l[_sel].Off; Commit(l, true); return;
                    case "del": l.RemoveAt(_sel); _sel = Math.Min(_sel, l.Count - 1); Commit(l, true); return;
                }
            }
            int chip = ChipAt(e.Location);
            if (chip >= 0) { _sel = chip; Invalidate(); if (e.Button == MouseButtons.Right) ShowTypeMenu(chip, PointToScreen(e.Location)); return; }
            int pt = PointAt(e.Location);
            if (pt >= 0)
            {
                _sel = pt;
                if (e.Button == MouseButtons.Right) { Invalidate(); ShowTypeMenu(pt, PointToScreen(e.Location)); return; }
                if (e.Button == MouseButtons.Left)
                {
                    _drag = pt; _dragStart = e.Location;
                    _dragHz = Bands[pt].Hz; _dragDb = Bands[pt].Db;
                    Capture = true;
                }
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag >= 0)
            {
                var gr = Graph;
                var l = PeqBand.CloneList(Bands);
                if (_drag >= l.Count) { _drag = -1; return; }
                var b = l[_drag];
                bool fine = (ModifierKeys & Keys.Shift) != 0;
                float dx = (e.X - _dragStart.X) * (fine ? 0.25f : 1f), dy = (e.Y - _dragStart.Y) * (fine ? 0.25f : 1f);
                double x0 = FreqToX(gr, _dragHz);
                b.Hz = (float)Math.Round(Math.Max(20, Math.Min(20000, XToFreq(gr, (float)(x0 + dx)))));
                if (b.Type < 3)
                {
                    float db = _dragDb - dy / gr.Height * (RangeDb * 2.15f);
                    b.Db = Math.Max(-RangeDb, Math.Min(RangeDb, (float)Math.Round(db * 2) / 2f));
                }
                Commit(l, false);
                return;
            }
            int hp = PointAt(e.Location);
            string hb = ButtonAt(e.Location);
            if (hp != _hoverPt || hb != _hoverBtn)
            {
                _hoverPt = hp; _hoverBtn = hb;
                Cursor = hp >= 0 ? Cursors.SizeAll : hb != null || ChipAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag >= 0) { _drag = -1; Capture = false; Commit(PeqBand.CloneList(Bands), true); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var gr = Graph;
            if (!gr.Contains(e.Location) || PointAt(e.Location) >= 0) return;
            AddBand((float)XToFreq(gr, e.X), Math.Max(-RangeDb, Math.Min(RangeDb, YToDb(gr, e.Y))));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int pt = PointAt(e.Location);
            if (pt < 0) pt = ChipAt(e.Location);
            if (pt < 0 && Graph.Contains(e.Location)) pt = _sel;
            if (pt < 0) return;
            var l = PeqBand.CloneList(Bands);
            if (pt >= l.Count) return;
            float q = l[pt].QNow * (float)Math.Pow(1.12, e.Delta / 120.0);
            l[pt].Q = (float)Math.Round(Math.Max(0.1f, Math.Min(12f, q)), 2);
            _sel = pt;
            Commit(l, true);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Delete && _sel >= 0 && _sel < Bands.Count)
            {
                var l = PeqBand.CloneList(Bands); l.RemoveAt(_sel); _sel = Math.Min(_sel, l.Count - 1); Commit(l, true);
            }
        }

        private void ShowTypeMenu(int index, Point screen)
        {
            var m = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Ui(12f), BackColor = Theme.MenuBg, ForeColor = Theme.Text };
            var cur = Bands;
            if (index < 0 || index >= cur.Count) return;
            for (int t = 0; t < PeqBand.TypeNames.Length; t++)
            {
                int tt = t;
                m.Items.Add(new ToolStripMenuItem(PeqBand.TypeNames[t], null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Type = tt; Commit(l, true); } }) { Checked = cur[index].Type == t });
            }
            m.Items.Add(new ToolStripSeparator());
            foreach (var q in new[] { 0.5f, 0.71f, 1f, 1.41f, 2f, 4f, 8f })
            {
                float qq = q;
                m.Items.Add(new ToolStripMenuItem("Güte " + q.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")) + (q <= 0.71f ? "  (breit)" : q >= 4f ? "  (schmal)" : ""), null,
                    (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Q = qq; Commit(l, true); } }) { Checked = Math.Abs(cur[index].QNow - q) < 0.01f });
            }
            if (MsHere)
            {
                // 4.0 (finale): Mitte/Seite
                m.Items.Add(new ToolStripSeparator());
                for (int md = 0; md < PeqBand.ModeNames.Length; md++)
                {
                    int mm = md;
                    m.Items.Add(new ToolStripMenuItem("Wirkt auf: " + PeqBand.ModeNames[md], null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Mode = mm; Commit(l, true); } }) { Checked = cur[index].Mode == md });
                }
            }
            if (DynHere && cur[index].Type == PeqBand.Bell)
            {
                // 4.0 (finale): dynamisches Band
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Dynamisch (greift nur, wenn das Band zu laut ist)", null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Dyn = !l[index].Dyn; Commit(l, true); } }) { Checked = cur[index].Dyn });
                var thr = new ToolStripMenuItem("Schwelle: " + Math.Round(cur[index].DynThrNow) + " dB") { Enabled = cur[index].Dyn };
                foreach (var v in PeqBand.DynThrValues)
                {
                    int vv = v;
                    thr.DropDownItems.Add(new ToolStripMenuItem(v + " dB", null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].DynThr = vv; Commit(l, true); } }) { Checked = Math.Abs(cur[index].DynThrNow - v) < 0.5f });
                }
                m.Items.Add(thr);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(cur[index].Off ? "Band einschalten" : "Band ausschalten", null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Off = !l[index].Off; Commit(l, true); } }));
            m.Items.Add(new ToolStripMenuItem("Stärke auf 0 dB", null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l[index].Db = 0f; Commit(l, true); } }));
            m.Items.Add(new ToolStripMenuItem("Band löschen", null, (s, e) => { var l = PeqBand.CloneList(Bands); if (index < l.Count) { l.RemoveAt(index); _sel = Math.Min(_sel, l.Count - 1); Commit(l, true); } }));
            m.Closed += (s, e) => BeginInvoke(new Action(() => { try { m.Dispose(); } catch { } }));
            m.Show(screen);
        }
    }
}
