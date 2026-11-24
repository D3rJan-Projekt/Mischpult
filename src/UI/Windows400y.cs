using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    // ======================================================================== 256 Szenen-Galerie

    /// <summary>4.0 (finale): Szenen als Karten mit einem kleinen Bild des Mixes – Klick laedt, Maus drueber zeigt die Vorschau im Pult.</summary>
    internal sealed class SceneGalleryWindow : PultWin
    {
        public sealed class Card
        {
            public string Id, Name;
            public List<KeyValuePair<Color, float>> Bars = new List<KeyValuePair<Color, float>>();   // Kanalfarbe, Faderposition (−1 = stumm)
            public float Master = -1f;
            public bool Lit, Partial;
        }

        private List<Card> _cards = new List<Card>();
        private readonly List<RectangleF> _rects = new List<RectangleF>();
        private int _hover = -1;
        private float _scroll, _contentH;
        public event Action<string> Load;
        public event Action<string> Hover2;   // Szene-ID oder null

        public SceneGalleryWindow() : base("Szenen-Galerie", 660, 440) { }

        public void SetCards(List<Card> cards) { _cards = cards ?? new List<Card>(); Invalidate(); }

        private const float CardW = 196, CardH = 128, Gap = 12;

        protected override string ContentZone(Point p)
        {
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return "c" + i;
            return null;
        }

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            _rects.Clear();
            if (_cards.Count == 0) { Label(g, "Noch keine Szenen. SZENEN → Aktuellen Mix als neue Szene speichern.", r, Theme.TextDim, 10f, false, Theme.Center); return; }
            float cw = Theme.F(CardW), ch = Theme.F(CardH), gap = Theme.F(Gap);
            int cols = Math.Max(1, (int)((r.Width + gap) / (cw + gap)));
            float x0 = r.X + (r.Width - (cols * cw + (cols - 1) * gap)) / 2f;
            int rows = (_cards.Count + cols - 1) / cols;
            _contentH = rows * (ch + gap);
            _scroll = Math.Max(0f, Math.Min(_scroll, Math.Max(0f, _contentH - r.Height)));
            var st = g.Save();
            g.SetClip(new RectangleF(r.X, r.Y, r.Width, r.Height - Theme.F(16)), CombineMode.Intersect);
            for (int i = 0; i < _cards.Count; i++)
            {
                var rc = new RectangleF(x0 + (i % cols) * (cw + gap), r.Y + (i / cols) * (ch + gap) - _scroll, cw, ch);
                _rects.Add(rc);
                DrawCard(g, rc, _cards[i], i == _hover);
            }
            g.Restore(st);
            Label(g, "Klick: laden · Maus drüber: Vorschau im Pult", new RectangleF(r.X, r.Bottom - Theme.F(14), r.Width, Theme.F(14)), Theme.TextFaint, 8f, false, Theme.Center);
        }

        private static void DrawCard(Graphics g, RectangleF r, Card c, bool hover)
        {
            Theme.FillRound(g, hover ? Theme.Lighten(Theme.Well, 0.08f) : Theme.Well, r, Theme.F(6));
            Theme.StrokeRound(g, c.Lit ? Theme.Accent : hover ? Theme.Lighten(Theme.PanelEdge, 0.4f) : Theme.PanelEdge, c.Lit ? Theme.F(2) : Theme.F(1), r, Theme.F(6));
            // Mini-Bild: Fader als Balken in Kanalfarbe, MASTER rechts
            var mix = new RectangleF(r.X + Theme.F(10), r.Y + Theme.F(10), r.Width - Theme.F(20), r.Height - Theme.F(44));
            Theme.FillRound(g, Theme.Darken(Theme.Well, 0.35f), mix, Theme.F(3));
            int n = c.Bars.Count + (c.Master >= 0f ? 1 : 0);
            if (n > 0)
            {
                float slot = (mix.Width - Theme.F(8)) / n, bw = Math.Max(2f, Math.Min(Theme.F(10), slot * 0.55f));
                for (int i = 0; i < n; i++)
                {
                    bool master = i == c.Bars.Count;
                    float pos = master ? c.Master : c.Bars[i].Value;
                    Color col = master ? Theme.MuteRed : c.Bars[i].Key;
                    float x = mix.X + Theme.F(4) + i * slot + (slot - bw) / 2f;
                    var track = new RectangleF(x + bw / 2f - Theme.F(0.75f), mix.Y + Theme.F(4), Theme.F(1.5f), mix.Height - Theme.F(8));
                    using (var b = new SolidBrush(Theme.Alpha(Theme.TextFaint, 90))) g.FillRectangle(b, track);
                    bool mute = pos < 0f;
                    float p = Math.Max(0f, Math.Min(1f, mute ? 0f : pos));
                    float y = track.Bottom - track.Height * p;
                    using (var b = new SolidBrush(Theme.Alpha(col, mute ? 50 : 150))) g.FillRectangle(b, track.X - Theme.F(0.5f), y, Theme.F(2.5f), track.Bottom - y);
                    var cap = new RectangleF(x, y - Theme.F(2.5f), bw, Theme.F(5));
                    Theme.FillRound(g, mute ? Theme.Disabled : col, cap, Theme.F(1.5f));
                }
            }
            Label(g, c.Name, new RectangleF(r.X + Theme.F(10), r.Bottom - Theme.F(30), r.Width - Theme.F(20), Theme.F(18)), Theme.Text, 10.5f, true);
            Label(g, (c.Lit ? "zuletzt geladen" : "") + (c.Partial ? (c.Lit ? " · " : "") + "Teil-Szene" : ""), new RectangleF(r.X + Theme.F(10), r.Bottom - Theme.F(14), r.Width - Theme.F(20), Theme.F(12)), c.Lit ? Theme.Accent : Theme.TextFaint, 8f);
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;
            for (int i = 0; i < _rects.Count; i++)
                if (_rects[i].Contains(e.Location) && i < _cards.Count) { Load?.Invoke(_cards[i].Id); return true; }
            return false;
        }

        protected override void ContentMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(e.Location)) h = i;
            if (h == _hover) return;
            _hover = h;
            Hover2?.Invoke(h >= 0 && h < _cards.Count ? _cards[h].Id : null);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover >= 0) { _hover = -1; Hover2?.Invoke(null); Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll -= Math.Sign(e.Delta) * Theme.F(60);
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { Hover2?.Invoke(null); base.OnFormClosed(e); }
    }

    // ======================================================================== 244/245 EQ-Uebersicht, Maskierung

    /// <summary>4.0 (finale): alle EQ-Kurven in einem Bild, dazu der Ausgang und das Spektrum dahinter; Maskierung mit Vorschlag.</summary>
    internal sealed class EqOverviewWindow : PultWin
    {
        public sealed class Curve { public string Name; public Color Color; public double[] Db; public bool Off; }
        public sealed class Mask { public string Text; public string ChannelId; public float Hz, Db; public Color Color; }

        public static readonly double[] Freqs = MakeFreqs();
        private static double[] MakeFreqs() { var f = new double[121]; for (int i = 0; i < f.Length; i++) f[i] = 20.0 * Math.Pow(1000.0, i / 120.0); return f; }

        public Func<List<Curve>> Curves;
        public Func<Curve> Output;
        public Func<float[]> Spectrum;          // 96 Baender, dBFS
        public Func<List<Mask>> Masks;          // null = Maskierung aus
        public event Action<Mask> ApplyMask;
        private readonly List<KeyValuePair<RectangleF, Mask>> _maskBtns = new List<KeyValuePair<RectangleF, Mask>>();
        private const double Range = 15;

        public EqOverviewWindow() : base("EQ-Übersicht", 820, 520) { }

        protected override string ContentZone(Point p) { foreach (var kv in _maskBtns) if (kv.Key.Contains(p)) return "m"; return null; }

        private static float X(RectangleF r, double f) => (float)(r.X + r.Width * Math.Log(Math.Max(20, f) / 20.0) / Math.Log(1000.0));
        private static float Y(RectangleF r, double db) => (float)(r.Y + r.Height / 2 - Math.Max(-Range, Math.Min(Range, db)) / (Range * 2) * r.Height);

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            _maskBtns.Clear();
            var masks = Masks?.Invoke();
            float maskH = masks == null ? 0f : Theme.F(Math.Min(4, Math.Max(1, masks.Count)) * 26 + 24);
            var gr = new RectangleF(r.X + Theme.F(34), r.Y + Theme.F(6), r.Width - Theme.F(44), r.Height - Theme.F(60) - maskH);
            Theme.FillRound(g, Theme.Darken(Theme.Well, 0.3f), RectangleF.Inflate(gr, Theme.F(4), Theme.F(4)), Theme.F(4));
            // Spektrum dahinter
            var sp = Spectrum?.Invoke();
            if (sp != null && sp.Length > 0)
                using (var b = new SolidBrush(Theme.Alpha(Theme.Accent, 38)))
                    for (int i = 0; i < sp.Length; i++)
                    {
                        float x0 = X(gr, Audio.SpectrumAnalyzer.BandFreq(i)), x1 = X(gr, Audio.SpectrumAnalyzer.BandFreq(i + 1));
                        float v = Math.Max(0f, Math.Min(1f, (sp[i] + 80f) / 80f));
                        g.FillRectangle(b, x0, gr.Bottom - gr.Height * v, Math.Max(1f, x1 - x0 - 0.5f), gr.Height * v);
                    }
            // Raster
            using (var pen = new Pen(Theme.Alpha(Theme.TextFaint, 50), 1f))
            using (var b = new SolidBrush(Theme.TextFaint))
            {
                foreach (var f in new[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
                {
                    float x = X(gr, f); g.DrawLine(pen, x, gr.Y, x, gr.Bottom);
                    Theme.Str(g, f >= 1000 ? f / 1000 + "k" : f.ToString(CultureInfo.InvariantCulture), Theme.Ui(8f), b, new RectangleF(x - Theme.F(15), gr.Bottom + Theme.F(4), Theme.F(30), Theme.F(12)), Theme.Center);
                }
                foreach (var d in new[] { -12, -6, 0, 6, 12 })
                {
                    float y = Y(gr, d); g.DrawLine(pen, gr.X, y, gr.Right, y);
                    Theme.Str(g, (d > 0 ? "+" : "") + d, Theme.Ui(8f), b, new RectangleF(r.X, y - Theme.F(6), Theme.F(30), Theme.F(12)), Theme.Right);
                }
            }
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var curves = Curves?.Invoke() ?? new List<Curve>();
            foreach (var c in curves) DrawCurve(g, gr, c.Db, c.Off ? Theme.Alpha(c.Color, 70) : c.Color, Theme.F(1.6f), false);
            var outC = Output?.Invoke();
            if (outC != null) DrawCurve(g, gr, outC.Db, Theme.Text, Theme.F(2.4f), true);
            g.SmoothingMode = sm;
            // Legende
            float lx = gr.X, ly = gr.Bottom + Theme.F(20);
            foreach (var c in curves.Concat(outC != null ? new[] { outC } : new Curve[0]))
            {
                string lt = c.Name + (c.Off ? " (EQ aus)" : "");
                float tw = Theme.TextWidth(g, lt, Theme.Ui(9f)) + Theme.F(22);
                if (lx + tw > gr.Right) { lx = gr.X; ly += Theme.F(15); }
                using (var b = new SolidBrush(c == outC ? Theme.Text : c.Color)) g.FillRectangle(b, lx, ly + Theme.F(5), Theme.F(12), Theme.F(3));
                Label(g, lt, new RectangleF(lx + Theme.F(16), ly, tw, Theme.F(14)), c.Off ? Theme.TextFaint : Theme.TextDim, 9f);
                lx += tw + Theme.F(10);
            }
            // Maskierung
            if (masks != null)
            {
                float my = r.Bottom - maskH + Theme.F(4);
                Label(g, "MASKIERUNG – wo sich Kanäle gegenseitig verdecken", new RectangleF(r.X, my, r.Width, Theme.F(16)), Theme.TextDim, 9f, true);
                my += Theme.F(20);
                if (masks.Count == 0) Label(g, "Gerade nichts – oder es spielt zu wenig gleichzeitig.", new RectangleF(r.X, my, r.Width, Theme.F(18)), Theme.TextFaint, 9.5f);
                foreach (var m in masks.Take(4))
                {
                    using (var b = new SolidBrush(m.Color)) g.FillEllipse(b, r.X, my + Theme.F(5), Theme.F(8), Theme.F(8));
                    Label(g, m.Text, new RectangleF(r.X + Theme.F(14), my, r.Width - Theme.F(150), Theme.F(20)), Theme.Text, 9.5f);
                    var br = new RectangleF(r.Right - Theme.F(130), my - Theme.F(1), Theme.F(130), Theme.F(22));
                    Theme.DrawKey(g, br, "DELLE SETZEN", false, Theme.Accent, Hover == "m" && br.Contains(PointToClient(Cursor.Position)));
                    _maskBtns.Add(new KeyValuePair<RectangleF, Mask>(br, m));
                    my += Theme.F(26);
                }
            }
        }

        private static void DrawCurve(Graphics g, RectangleF gr, double[] db, Color c, float w, bool dashed)
        {
            if (db == null || db.Length != Freqs.Length) return;
            var pts = new PointF[db.Length];
            for (int i = 0; i < db.Length; i++) pts[i] = new PointF(X(gr, Freqs[i]), Y(gr, db[i]));
            using (var p = new Pen(c, w) { LineJoin = LineJoin.Round })
            {
                if (dashed) p.DashStyle = DashStyle.Dash;
                g.DrawLines(p, pts);
            }
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            foreach (var kv in _maskBtns) if (kv.Key.Contains(e.Location) && e.Button == MouseButtons.Left) { ApplyMask?.Invoke(kv.Value); return true; }
            return false;
        }
    }

    // ======================================================================== 263 Befehlssuche

    /// <summary>4.0 (finale): Befehlssuche (Strg+K) – tippen, Pfeiltasten, Enter fuehrt aus.</summary>
    internal sealed class CommandPalette : Form
    {
        public sealed class Cmd { public string Text, Group; public Action Run; }
        private readonly TextBox _box;
        private readonly List<Cmd> _all;
        private List<Cmd> _hits = new List<Cmd>();
        private int _sel, _top;
        private const int Rows = 10;

        public CommandPalette(List<Cmd> cmds)
        {
            _all = cmds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = Theme.MenuBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            ClientSize = new Size(Theme.Px(640), Theme.Px(60 + Rows * 30));
            _box = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Ui(13f), Bounds = new Rectangle(Theme.Px(14), Theme.Px(12), Theme.Px(612), Theme.Px(30)) };
            _box.TextChanged += (s, e) => Filter();
            Controls.Add(_box);
            Filter();
        }

        public string Query { get => _box.Text; set => _box.Text = value; }
        public IReadOnlyList<Cmd> Hits => _hits;

        /// <summary>Treffer: alle Woerter muessen vorkommen (Gross/klein egal); Anfangs-Treffer zuerst.</summary>
        internal static List<Cmd> Match(List<Cmd> all, string q)
        {
            var words = (q ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return all.Take(200).ToList();
            return all.Select(c => new { c, t = (c.Text + " " + c.Group).ToLowerInvariant() })
                      .Where(x => words.All(w => x.t.Contains(w)))
                      .OrderBy(x => x.c.Text.ToLowerInvariant().StartsWith(words[0]) ? 0 : x.t.Contains(" " + words[0]) ? 1 : 2)
                      .ThenBy(x => x.c.Text.Length)
                      .Select(x => x.c).Take(200).ToList();
        }

        private void Filter() { _hits = Match(_all, _box.Text); _sel = 0; _top = 0; Invalidate(); }

        protected override void OnShown(EventArgs e) { base.OnShown(e); _box.Focus(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); BeginInvoke(new Action(() => { try { Close(); } catch { } })); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; Move(1); }
            else if (e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; Move(-1); }
            else if (e.KeyCode == Keys.PageDown) { e.SuppressKeyPress = true; Move(Rows); }
            else if (e.KeyCode == Keys.PageUp) { e.SuppressKeyPress = true; Move(-Rows); }
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunSel(); }
        }

        private void Move(int d)
        {
            if (_hits.Count == 0) return;
            _sel = Math.Max(0, Math.Min(_hits.Count - 1, _sel + d));
            if (_sel < _top) _top = _sel; else if (_sel >= _top + Rows) _top = _sel - Rows + 1;
            Invalidate();
        }

        private void RunSel()
        {
            if (_sel < 0 || _sel >= _hits.Count) return;
            var c = _hits[_sel];
            Close();
            try { c.Run?.Invoke(); } catch { }
        }

        private Rectangle RowRect(int i) => new Rectangle(Theme.Px(8), Theme.Px(52) + i * Theme.Px(30), ClientSize.Width - Theme.Px(16), Theme.Px(28));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            for (int i = 0; i < Rows; i++) if (RowRect(i).Contains(e.Location) && _top + i < _hits.Count && _sel != _top + i) { _sel = _top + i; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < Rows; i++) if (RowRect(i).Contains(e.Location) && _top + i < _hits.Count) { _sel = _top + i; RunSel(); return; }
        }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Move(-Math.Sign(e.Delta) * 3); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            using (var b = new SolidBrush(Theme.MenuBg)) g.FillRectangle(b, ClientRectangle);
            Theme.StrokeRound(g, Theme.Accent, Theme.F(1.5f), new RectangleF(0.5f, 0.5f, ClientSize.Width - 1.5f, ClientSize.Height - 1.5f), Theme.F(6));
            if (_hits.Count == 0)
            {
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, "Nichts gefunden", Theme.Ui(11f), b, RowRect(0), Theme.Center);
                return;
            }
            for (int i = 0; i < Rows && _top + i < _hits.Count; i++)
            {
                var c = _hits[_top + i];
                var rr = RowRect(i);
                if (_top + i == _sel) Theme.FillRound(g, Theme.MenuHover, rr, Theme.F(4));
                using (var b = new SolidBrush(Theme.Text)) Theme.Str(g, c.Text, Theme.Ui(11f), b, new RectangleF(rr.X + Theme.F(10), rr.Y, rr.Width - Theme.F(220), rr.Height), Theme.Left);
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, c.Group, Theme.Ui(8.5f), b, new RectangleF(rr.Right - Theme.F(210), rr.Y, Theme.F(200), rr.Height), Theme.Right);
            }
            if (_hits.Count > Rows)
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, (_sel + 1) + " / " + _hits.Count, Theme.Ui(8f), b, new RectangleF(0, ClientSize.Height - Theme.F(14), ClientSize.Width - Theme.F(10), Theme.F(12)), Theme.Right);
        }
    }

    // ======================================================================== 248 Ueber-Fenster mit Versions-Chronik

    /// <summary>4.0 (finale): Ueber-Fenster mit zwei Seiten – Ueber Mischpult und die Versions-Chronik.</summary>
    internal sealed class AboutWindow : PultWin
    {
        private readonly string _about;
        private int _page;
        private float _scroll, _contentH;
        private RectangleF _tab0, _tab1, _gh, _bug;
        /// <summary>4.2.6: GitHub-Links (null = ausgeblendet).</summary>
        public Action OpenGitHub, ReportBug;

        internal static readonly string[][] Chronicle =
        {
            new[] { "4.7", "Studio-Fenster am PC: die Studio-Ansicht (MIX, EDIT mit EQ-Kurve, PEGEL, Szenen, DUCKING, Musik-Tasten) als eigenes Fenster hier am PC – ohne Handy und auch, wenn die Fernbedienung aus ist. Nur auf diesem PC, über Edge bzw. Chrome ohne Adressleiste." },
            new[] { "4.6", "Fernbedienung als Studio-Pult fürs Tablet: MIX mit Kanalzügen (Pegel, Balance, MUTE, SOLO) und Master rechts, EDIT mit EQ-Kurve zum Ziehen, Dynamik (COMP, AUTO), Apps und Stream-Anteil, PEGEL-Übersicht, Ebenen (Kanäle, Gruppen, Ausgänge), Szenen, DUCKING, ALLE STUMM und Musik-Tasten mit „Jetzt läuft“." },
            new[] { "4.5.2", "Fernbedienung: schmale Kanäle im Pult (Standard) – oben rechts S, M, L für die Breite. Schmal passen mehr Kanäle auf den Bildschirm." },
            new[] { "4.5.1", "Fernbedienung: alle Ausgänge (Lautsprecher, Kopfhörer, HDMI …) mit Lautstärke, MUTE und Pegel. Taste FEIN (Fader und Drehregler 4× langsamer, EQ in 0,1-dB-Schritten), Wert antippen = fein einstellen in 0,1-dB-Schritten. Pult feiner gezeichnet: Stereo-LED-Pegel mit Spitzenwert, Skalenstriche, Kanalnummern." },
            new[] { "4.5", "Fernbedienung als Pult fürs Tablet: senkrechte Fader mit Skala und Pegelanzeige, pro Kanal EQ (LO, MID, HI) mit EQ-Taste, PAN, SOLO und COMP, Apps per Tipp auf die Symbole. Umschalter „Pult | Liste“, am Handy bleibt die Liste. Beides unter der Fernbedienung abschaltbar." },
            new[] { "4.4.1", "Fernbedienung: zuverlässigere Verbindung mit Handy-Browsern, Fenster „Fernbedienung“ zeigt die letzten Verbindungsversuche und den Zustand der Windows-Firewall – mit Knopf „Firewall freigeben“ (nur fürs eigene Heimnetz). Knopf „Am PC testen“, Protokoll fernbedienung.log." },
            new[] { "4.4", "Fernbedienung im Browser: Handy, Tablet oder ein anderer PC im selben WLAN regeln Kanäle, Ausgang, MUTE, einzelne Apps, Szenen und DUCKING – ohne App, Verbinden per QR-Code, nur im Heimnetz und mit Zugangscode." },
            new[] { "4.3", "Ducking zeigt, wer auslöst (Taste und Kanal), Halte-Taste, Soundboard als Auslöser, Ducking-Vorlagen und „nur wenn ein Spiel läuft / zu bestimmten Uhrzeiten“. Profil pro App, Timer pro Kanal, Windows-Töne stumm beim Zocken, Kopfhörer-Akku. Designs Steampunk, Arktis und Japan, Hintergründe Gewitter, Sternenwirbel und Glühwürmchen, Wände Graffiti, Bücherregal und Serverraum, Plattenspieler als Deko." },
            new[] { "4.2.7", "Installieren wahlweise in den Programme-Ordner (für alle Benutzer, Windows fragt nach Admin-Rechten) oder nur für dich wie bisher. Eine vorhandene Installation zieht beim Update auf Wunsch um." },
            new[] { "4.2.6", "GitHub-Links: „Auf GitHub“ und „Fehler melden“ im Über-Fenster (Schalter unter OPTIONEN → Allgemein). Neue Versionen baut GitHub jetzt selbst aus dem öffentlichen Quellcode." },
            new[] { "4.2.5", "Updates automatisch installieren: Schalter unter OPTIONEN → Allgemein – neue Versionen kommen beim Start von selbst, ohne Klick." },
            new[] { "4.2.4", "Aufräumen nach dem Installieren und Deinstallieren ohne Hilfsbefehl im Hintergrund – weniger, was ein Virenscanner fälschlich für verdächtig halten kann. „Online nach Updates suchen“ kennt jetzt das GitHub-Projekt D3rJan-Projekt/Mischpult." },
            new[] { "4.2.3", "Ducking-Feinschliff: Lämpchen bei den Auslösern, kurze Geräusche ignorieren, Übergang einstellbar, MIKRO auch in den Ducking-Regeln." },
            new[] { "4.2.2", "Ducking: auch dein Mikrofon kann Auslöser sein – Musik wird leiser, wenn du selbst sprichst." },
            new[] { "4.2.1", "Ducking mit mehreren Auslösern: im Ducking-Fenster einfach mehrere Kanäle anhaken." },
            new[] { "4.2", "Optik und Extras: Synthwave, Retro-Game und Cockpit, Nordlicht, Unterwasser, Kaminfeuer, Matrix-Regen, neue Wände, spiegelnder Tisch, RGB-Welle, leuchtender Fensterrand, Startbild, App-Symbol auf der Kappe, Klang-Kunst, Funktions-Pakete." },
            new[] { "4.1", "Einfach installieren und aktualisieren: Ein-Klick-Fenster, Update per Ziehen, Download-Ordner, online nachsehen, vorige Version, Start-Schutz, abgesicherter Start, Umzugs-Paket, Reparieren, Übersicht „Was ist eingeschaltet“." },
            new[] { "4.0 finale", "Lichtshow, Neon-Schild, Himmel, vier neue Designs, Kompressor-Fenster, De-Esser, Wärme, Ducking-Regeln, Auto-Mix, Hall-Bus, Crossfader, Mute-Gruppen, Stream-Limiter, Signalgenerator, Szenen-Galerie, EQ-Übersicht, Befehlssuche – 42 neue Sachen." },
            new[] { "4.0", "Das große Update: parametrischer EQ, Kopfhörer-Profile, Hörtest, Fader-Automation, Overlay, Widget, Mini-Player, Raum-Ansicht, OBS, WLED, MIDI-Rückmeldung, Carbon und Holz & Messing – 56 neue Sachen." },
            new[] { "3.5", "33 neue Sachen: Spektrum hinter den Kanälen, Einschlaf-Geräusche, zweites Gerät, Stimmen nach vorne, Höhen-Filter." },
            new[] { "3.4.6", "27 neue Sachen: Deko auf dem Pult, Kabel, Regen und Schnee, Takt-Lampe, Stereo-Breite, Bass-Filter, Knall-Bremse." },
            new[] { "3.4.1 – 3.4.5", "Kein lauter Anfang, kein Knacken beim Umschalten, Ausgänge in dB, Kanäle breiter ziehen." },
            new[] { "3.4", "23 neue Sachen zum Ein- und Ausschalten." },
            new[] { "3.3", "Noch mehr Optik: Fallblatt- und Nixie-Uhr, Kippschalter, bewegter Hintergrund, Wand hinter dem Pult." },
            new[] { "3.2", "Szenen weich überblenden, Lautstärke-Deckel, Verzögerung, mono/getauscht, Hintergrund-Apps leiser." },
            new[] { "3.1", "Das große Optik-Update: Kurve und runde Spektren, Tasten-Formen und -Farben, Fader-Show." },
            new[] { "3.0 – 3.0.3", "Hardware-Beschleunigung, flüssige Pegel beim Spielen, kein Knacken wenn Musik anfängt." },
            new[] { "2.17 – 2.20", "Weg zur 3.0: Anzeigen, Bedienung, Ton (Hall/Echo, Vorhören, Mehrspur) und Aussehen." },
            new[] { "2.12 – 2.16", "Vintage-Design, Meterbridge nach Wunsch, Lautheit jedes Ausgangs, Schwarz-Gold, Broadcast-Blau, Retro-Hi-Fi." },
            new[] { "2.10 – 2.11", "VU-Meter, FADE-Taste, DIM und LOUD, Motorfader beim Szenenwechsel." },
            new[] { "bis 2.9", "Die Grundlagen: Apps in Kanälen, Fader, EQ, Kompressor, Szenen, Hotkeys, Mikrofon, Ducking, Gehörschutz, Mini-Ansicht, Aufnahme." },
        };

        public AboutWindow(string about) : base("Über Mischpult", 620, 560) { _about = about; }

        protected override string ContentZone(Point p) => _tab0.Contains(p) ? "t0" : _tab1.Contains(p) ? "t1" :
            OpenGitHub != null && _gh.Contains(p) ? "gh" : ReportBug != null && _bug.Contains(p) ? "bug" : null;

        protected override void PaintContent(Graphics g, RectangleF r)
        {
            _tab0 = new RectangleF(r.X, r.Y, Theme.F(110), Theme.F(26));
            _tab1 = new RectangleF(r.X + Theme.F(118), r.Y, Theme.F(140), Theme.F(26));
            Theme.DrawKey(g, _tab0, "ÜBER", _page == 0, Theme.Accent, Hover == "t0");
            Theme.DrawKey(g, _tab1, "VERSIONS-CHRONIK", _page == 1, Theme.Accent, Hover == "t1");
            // 4.2.6: rechts die GitHub-Links
            _bug = ReportBug != null ? new RectangleF(r.Right - Theme.F(124), r.Y, Theme.F(124), Theme.F(26)) : RectangleF.Empty;
            _gh = OpenGitHub != null ? new RectangleF((ReportBug != null ? _bug.X - Theme.F(8) : r.Right) - Theme.F(108), r.Y, Theme.F(108), Theme.F(26)) : RectangleF.Empty;
            if (OpenGitHub != null) Theme.DrawKey(g, _gh, "AUF GITHUB", false, Theme.Accent, Hover == "gh");
            if (ReportBug != null) Theme.DrawKey(g, _bug, "FEHLER MELDEN", false, Theme.Accent, Hover == "bug");
            var body = new RectangleF(r.X, r.Y + Theme.F(36), r.Width, r.Height - Theme.F(36));
            var st = g.Save();
            g.SetClip(body, CombineMode.Intersect);
            float y = body.Y - _scroll;
            if (_page == 0)
            {
                using (var b = new SolidBrush(Theme.Text))
                {
                    var sz = g.MeasureString(_about, Theme.Ui(10f), (int)body.Width);
                    g.DrawString(_about, Theme.Ui(10f), b, new RectangleF(body.X, y, body.Width, sz.Height + 10));
                    _contentH = sz.Height + Theme.F(10);
                }
            }
            else
            {
                float h0 = 0;
                foreach (var v in Chronicle)
                {
                    using (var b = new SolidBrush(Theme.Accent)) Theme.Str(g, v[0], Theme.UiSemi(11f), b, new RectangleF(body.X, y, Theme.F(120), Theme.F(20)), Theme.Left);
                    var sz = g.MeasureString(v[1], Theme.Ui(10f), (int)(body.Width - Theme.F(124)));
                    using (var b = new SolidBrush(Theme.Text)) g.DrawString(v[1], Theme.Ui(10f), b, new RectangleF(body.X + Theme.F(124), y + Theme.F(1), body.Width - Theme.F(124), sz.Height + 4));
                    float step = Math.Max(Theme.F(24), sz.Height + Theme.F(10));
                    y += step; h0 += step;
                }
                _contentH = h0;
            }
            g.Restore(st);
            _scroll = Math.Max(0f, Math.Min(_scroll, Math.Max(0f, _contentH - body.Height)));
        }

        protected override bool ContentDown(MouseEventArgs e)
        {
            if (_tab0.Contains(e.Location)) { _page = 0; _scroll = 0; Invalidate(); return true; }
            if (_tab1.Contains(e.Location)) { _page = 1; _scroll = 0; Invalidate(); return true; }
            if (OpenGitHub != null && _gh.Contains(e.Location)) { OpenGitHub(); return true; }   // 4.2.6
            if (ReportBug != null && _bug.Contains(e.Location)) { ReportBug(); return true; }
            return false;
        }

        public void ShowChronicle() { _page = 1; _scroll = 0; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _scroll -= Math.Sign(e.Delta) * Theme.F(50); Invalidate(); }
    }
}
