using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>Waagerechte Reihe von Kanalzuegen mit eigener, schlanker Scrollleiste.</summary>
    internal sealed class StripHost : Control
    {
        private readonly List<Control> _items = new List<Control>();
        private int _scroll;
        private bool _dragThumb;
        private int _dragStartX, _dragStartScroll;
        private bool _hoverThumb;
        public string Title;
        public Func<Control, int> WidthOf;

        // ---- 3.1: Kanaele gleiten weich an ihre neue Stelle (neu, geloescht, umsortiert, ein-/ausgeklappt)
        public static bool AnimateOn = true;
        private bool _animateNext;
        private readonly Dictionary<Control, int> _animFrom = new Dictionary<Control, int>();
        private readonly Dictionary<Control, Rectangle> _animTo = new Dictionary<Control, Rectangle>();
        private readonly System.Diagnostics.Stopwatch _animClock = new System.Diagnostics.Stopwatch();
        private readonly Timer _animTimer = new Timer { Interval = 15 };
        private const double AnimMs = 220;

        /// <summary>Beim naechsten Neuordnen weich gleiten (statt zu springen).</summary>
        public void AnimateNextLayout() { if (AnimateOn) _animateNext = true; }

        private void AnimTick()
        {
            double u = Math.Min(1.0, _animClock.Elapsed.TotalMilliseconds / AnimMs);
            float e = (float)(1 - Math.Pow(1 - u, 3));
            SuspendLayout();
            foreach (var kv in _animTo)
            {
                var c = kv.Key;
                if (c.IsDisposed || c.Parent != this) continue;
                int from = _animFrom.TryGetValue(c, out int f) ? f : kv.Value.X;
                var b = kv.Value;
                b.X = (int)Math.Round(from + (b.X - from) * e);
                if (c.Bounds != b) c.Bounds = b;
            }
            ResumeLayout(false);
            if (u >= 1.0) StopAnim();
        }

        private void StopAnim()
        {
            _animTimer.Stop();
            foreach (var kv in _animTo) if (!kv.Key.IsDisposed && kv.Key.Parent == this && kv.Key.Bounds != kv.Value) kv.Key.Bounds = kv.Value;
            _animTo.Clear(); _animFrom.Clear();
        }
        /// <summary>Zuege hier durchnummerieren (1, 2, 3 … wie am echten Pult).</summary>
        public bool Numbered;

        /// <summary>Art eines Zugs fuers Verschieben ("ch", "grp" ...), null = nicht verschiebbar. Verschoben wird nur innerhalb derselben Art.</summary>
        public Func<Control, string> KindOf;
        /// <summary>Neue Reihenfolge nach dem Verschieben per Maus.</summary>
        public Action<List<Control>> Reordered;

        // 2.18: Reiter fuer Kanal-Baenke rechts neben dem Titel (null = keine)
        public string[] Tabs;
        public int ActiveTab;
        /// <summary>Reiter angeklickt (Index).</summary>
        public event Action<int> TabClicked;
        /// <summary>Rechtsklick auf einen Reiter (Index, Bildschirmpunkt).</summary>
        public event Action<int, Point> TabMenu;
        private RectangleF[] _tabRects = new RectangleF[0];
        private int _tabHover = -1;

        /// <summary>Wo der Reiter steht (eigene Koordinaten, leer = nicht da).</summary>
        public RectangleF TabRect(int i) => i >= 0 && i < _tabRects.Length ? _tabRects[i] : RectangleF.Empty;

        private int TabAt(Point p)
        {
            if (Tabs == null || p.Y > TitleH) return -1;
            for (int i = 0; i < _tabRects.Length; i++) if (RectangleF.Inflate(_tabRects[i], Theme.F(1.5f), Theme.F(2.5f)).Contains(p)) return i;
            return -1;
        }

        private void DrawTabs(Graphics g, float x)
        {
            if (Tabs == null || Tabs.Length == 0) { _tabRects = new RectangleF[0]; return; }
            var font = Theme.UiSemi(8.5f);
            float h = Theme.F(16), y = Theme.F(3), padX = Theme.F(8), gap = Theme.F(4);
            float right = ClientSize.Width - Theme.F(12);
            // Passt es nicht, nur die Buchstaben zeigen
            string[] labels = Tabs;
            float total = labels.Sum(t => Theme.TextWidth(g, t, font) + padX * 2 + gap);
            if (x + total > right) labels = Tabs.Select(t => { int k = t.IndexOf(" · ", StringComparison.Ordinal); return k > 0 ? t.Substring(0, k) : t; }).ToArray();
            var rects = new RectangleF[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                float w = (float)Math.Ceiling(Theme.TextWidth(g, labels[i], font) + padX * 2);
                var r = new RectangleF((float)Math.Round(x), y, w, h);
                rects[i] = r;
                x += w + gap;
                if (r.Right > right) { rects[i] = RectangleF.Empty; continue; }
                bool on = i == ActiveTab, hover = i == _tabHover;
                var fill = on ? Theme.Accent : Color.FromArgb(hover ? 70 : 38, Theme.BodyText);
                Theme.FillRound(g, fill, r, h / 2f);
                if (!on) Theme.StrokeRound(g, Color.FromArgb(hover ? 120 : 70, Theme.BodyText), 1f, r, h / 2f);
                var ink = on ? (Theme.Accent.GetBrightness() > 0.55f ? Color.FromArgb(24, 22, 20) : Color.White) : Theme.BodyText;
                using (var b = new SolidBrush(ink)) Theme.Str(g, labels[i], font, b, r, Theme.Center);
            }
            _tabRects = rects;
        }

        private Control _moveItem;
        private int _moveGrab, _moveX;
        private readonly Timer _autoScroll = new Timer { Interval = 30 };

        public StripHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = MixerForm.BodyColor;
            _autoScroll.Tick += (s, e) => AutoScrollTick();
            _animTimer.Tick += (s, e) => AnimTick();
        }

        // --------------------------------------------------------------- Verschieben per Maus

        public bool CanMove(Control c) => KindOf != null && KindOf(c) != null && _items.Contains(c) && !_two;   // 4.0: in zwei Reihen nicht verschiebbar

        // ---- 4.0: zwei Reihen – passen die Zuege nicht nebeneinander und ist das Fenster hoch genug, stehen sie in zwei Reihen
        public bool TwoRows;
        /// <summary>Mindesthoehe eines Zugs in einer Reihe (Pixel).</summary>
        public int MinRowHeight;
        private bool _two;
        /// <summary>Gerade in zwei Reihen? (fuer die Meterbridge)</summary>
        public bool TwoRowsNow => _two;
        public static bool AnyTwoRows;

        private int SplitIndex()
        {
            int total = 0;
            foreach (var c in _items) total += WidthOf(c) + Gap;
            int acc = 0;
            for (int i = 0; i < _items.Count; i++) { acc += WidthOf(_items[i]) + Gap; if (acc * 2 >= total) return Math.Min(_items.Count - 1, i + 1); }
            return _items.Count - 1;
        }

        private int RowWidth(int from, int to)
        {
            int w = Pad;
            for (int i = from; i < to; i++) w += WidthOf(_items[i]) + Gap;
            return w - Gap + Pad;
        }

        private bool WantTwoRows(int h)
        {
            if (!TwoRows || _items.Count < 2 || _moveItem != null) return false;
            if (RowWidth(0, _items.Count) <= ClientSize.Width) return false;
            return (h - Gap) / 2 >= Math.Max(Theme.Px(200), MinRowHeight);
        }

        public bool Moving => _moveItem != null;

        /// <summary>Zug packen. grab = Abstand der Maus zur linken Kante des Zugs.</summary>
        public void BeginMove(Control c, int grab)
        {
            if (!CanMove(c)) return;
            _moveItem = c;
            _moveGrab = grab;
            _moveX = c.Left;
            c.BringToFront();
            _autoScroll.Start();
            Relayout();
        }

        /// <summary>Maus steht bei x (Koordinaten dieses Bereichs).</summary>
        public void MoveTo(int x)
        {
            if (_moveItem == null) return;
            int w = WidthOf(_moveItem);
            _moveX = Math.Max(0, Math.Min(ClientSize.Width - w, x - _moveGrab));
            Relayout();
        }

        /// <summary>Loslassen (commit) oder abbrechen.</summary>
        public void EndMove(bool commit)
        {
            var c = _moveItem;
            if (c == null) return;
            _autoScroll.Stop();
            List<Control> order = null;
            if (commit && _items.Contains(c))
            {
                var rest = _items.Where(i => i != c).ToList();
                rest.Insert(DropIndex(rest), c);
                if (!rest.SequenceEqual(_items)) order = rest;
            }
            _moveItem = null;
            if (order != null)
            {
                _items.Clear();
                _items.AddRange(order);
                Renumber();
            }
            Relayout();
            if (order != null) Reordered?.Invoke(order);
        }

        /// <summary>Wo der gezogene Zug landen wuerde (Index in der Liste ohne ihn).</summary>
        private int DropIndex(List<Control> rest)
        {
            int w = WidthOf(_moveItem);
            int cx = _moveX + w / 2;
            int x = Pad - _scroll, idx = rest.Count;
            for (int k = 0; k < rest.Count; k++)
            {
                int wk = WidthOf(rest[k]);
                if (cx < x + wk / 2) { idx = k; break; }
                x += wk + Gap;
            }
            // nur zwischen Zuegen derselben Art (Kanaele bleiben bei Kanaelen, Gruppen bei Gruppen)
            string kind = KindOf(_moveItem);
            int lo = -1, hi = -1;
            for (int k = 0; k < rest.Count; k++)
                if (KindOf(rest[k]) == kind) { if (lo < 0) lo = k; hi = k + 1; }
            if (lo < 0)
            {
                int orig = _items.IndexOf(_moveItem);
                return Math.Max(0, Math.Min(rest.Count, orig));
            }
            return Math.Max(lo, Math.Min(hi, idx));
        }

        private void AutoScrollTick()
        {
            if (_moveItem == null || !IsHandleCreated) { _autoScroll.Stop(); return; }
            int mx = PointToClient(MousePosition).X;
            int edge = Theme.Px(56), d = 0;
            if (mx < edge) d = mx - edge;
            else if (mx > ClientSize.Width - edge) d = mx - (ClientSize.Width - edge);
            if (d == 0) return;
            int step = Math.Sign(d) * Math.Min(Theme.Px(28), Math.Max(Theme.Px(4), Math.Abs(d) / 2));
            int old = _scroll;
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll + step));
            if (_scroll != old) Relayout();
        }

        private int Pad => Theme.Px(8);
        private int Gap => Theme.Px(6);

        // ---- 3.4.6: Deko und Kabel (vom Pult gezeichnet), Platz unter den Zuegen
        public Action<StripHost, Graphics> PaintExtra;
        public Func<StripHost, MouseEventArgs, bool> ExtraMouseDown, ExtraMouseMove, ExtraMouseUp;
        public int ScrollX => _scroll;
        /// <summary>3.5: angepinnte Zuege (vorne in der Liste) bleiben beim Scrollen links stehen.</summary>
        public Func<Control, bool> IsPinned;
        private int _bottomReserve;
        /// <summary>So viel Platz bleibt unter den Zuegen frei (fuer die Kabel).</summary>
        public int BottomReserve { get => _bottomReserve; set { if (value == _bottomReserve) return; _bottomReserve = value; Relayout(); Invalidate(); } }
        /// <summary>Unterkante der Zuege (Client-Koordinaten).</summary>
        public int StripBottom => TitleH + Math.Max(Theme.Px(200), ClientSize.Height - TitleH - BarH - Theme.Px(2) - _bottomReserve);
        private int TitleH => Theme.Px(22 + ExtraTopPx);
        /// <summary>4.0 (finale): mehr Wand oben (Neon-Schild, Scheinwerfer der Lichtshow) – logische Pixel.</summary>
        public static int ExtraTopPx;
        /// <summary>Hoehe der Wand oben (Titelzeile + Extra).</summary>
        public int WallH => TitleH;
        private int BarH => Theme.Px(10);

        public IReadOnlyList<Control> Items => _items;

        public void SetItems(IList<Control> items)
        {
            SuspendLayout();
            foreach (var c in _items) if (!items.Contains(c) && c.Parent == this) { Controls.Remove(c); }
            if (_moveItem != null && !items.Contains(_moveItem)) { _moveItem = null; _autoScroll.Stop(); }
            bool changed = _items.Count != items.Count;
            if (!changed) for (int i = 0; i < items.Count; i++) if (_items[i] != items[i]) { changed = true; break; }
            _items.Clear();
            _items.AddRange(items);
            foreach (var c in _items) if (c.Parent != this) Controls.Add(c);
            ResumeLayout(false);
            Renumber();
            if (changed) AnimateNextLayout();   // 3.1: neu/geloescht/umsortiert: weich gleiten
            Relayout();
        }

        /// <summary>Kanalnummern neu vergeben (nach Aendern der Reihenfolge).</summary>
        public void Renumber()
        {
            int n = 0;
            foreach (var c in _items)
                if (c is StripBase s) s.Number = Numbered ? ++n : 0;
        }

        public int ContentWidth
        {
            get
            {
                if (_two) { int k = SplitIndex(); return Math.Max(RowWidth(0, k), RowWidth(k, _items.Count)); }   // 4.0
                int w = Pad;
                foreach (var c in _items) w += WidthOf(c) + Gap;
                return w - Gap + Pad;
            }
        }

        private int MaxScroll => Math.Max(0, ContentWidth - ClientSize.Width);

        public void ScrollBy(int delta)
        {
            int ns = Math.Max(0, Math.Min(MaxScroll, _scroll + delta));
            if (ns != _scroll) { _scroll = ns; Relayout(); }
        }

        public void ScrollIntoView(Control c)
        {
            int x = Pad;
            int split = _two ? SplitIndex() : -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (i == split) x = Pad;   // 4.0: zweite Reihe faengt wieder links an
                if (it == c)
                {
                    int w = WidthOf(it);
                    if (x < _scroll) _scroll = x - Pad;
                    else if (x + w > _scroll + ClientSize.Width) _scroll = x + w + Pad - ClientSize.Width;
                    _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll));
                    Relayout();
                    return;
                }
                x += WidthOf(it) + Gap;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        public void Relayout()
        {
            int top = TitleH;
            int h = Math.Max(Theme.Px(200), ClientSize.Height - top - BarH - Theme.Px(2) - _bottomReserve);
            bool wasTwo = _two;
            _two = WantTwoRows(h);   // 4.0
            if (TwoRows || wasTwo) AnyTwoRows = _two;
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll));
            int x = Pad - _scroll;
            if (_two)
            {
                if (_animTo.Count > 0) StopAnim();
                _animateNext = false;
                int rowH = (h - Gap) / 2, split = SplitIndex();
                for (int i = 0; i < _items.Count; i++)
                {
                    var c = _items[i];
                    if (i == split) x = Pad - _scroll;
                    int w = WidthOf(c);
                    if (c.Parent == this)
                    {
                        var b = new Rectangle(x, i < split ? top : top + rowH + Gap, w, rowH);
                        if (c.Bounds != b) c.Bounds = b;
                    }
                    x += w + Gap;
                }
                Invalidate();
                return;
            }
            if (_moveItem != null && _items.Contains(_moveItem))
            {
                // Beim Ziehen: die anderen Zuege machen an der Zielstelle Platz, der gezogene folgt der Maus
                var rest = _items.Where(i => i != _moveItem).ToList();
                int target = DropIndex(rest);
                int mw = WidthOf(_moveItem);
                for (int k = 0; k <= rest.Count; k++)
                {
                    if (k == target) x += mw + Gap;
                    if (k == rest.Count) break;
                    var c = rest[k];
                    int w = WidthOf(c);
                    if (c.Parent == this)
                    {
                        var b = new Rectangle(x, top, w, h);
                        if (c.Bounds != b) c.Bounds = b;
                    }
                    x += w + Gap;
                }
                var mb = new Rectangle(_moveX, top, mw, h);
                if (_moveItem.Bounds != mb) _moveItem.Bounds = mb;
                Invalidate();
                return;
            }
            bool anim = _animateNext && AnimateOn && Visible && IsHandleCreated;
            _animateNext = false;
            if (!anim && _animTo.Count > 0) StopAnim();   // Scrollen/Groesse aendern: sofort an die Stelle
            int pinX = Pad;
            bool leading = true;
            List<Control> pinned = null;
            foreach (var c in _items)
            {
                int w = WidthOf(c);
                bool pin = leading && IsPinned != null && IsPinned(c);   // 3.5
                if (!pin) leading = false;
                if (c.Parent != this) { x += w + Gap; if (pin) pinX += w + Gap; continue; } // gerade in den anderen Bereich gewandert
                var b = new Rectangle(pin ? pinX : x, top, w, h);
                if (pin) { pinX += w + Gap; (pinned ?? (pinned = new List<Control>())).Add(c); }
                if (anim)
                {
                    // 3.1: von der bisherigen Stelle hingleiten; neue Zuege kommen ein Stueck von rechts
                    bool known = c.Width > 1 && (_animTo.ContainsKey(c) || c.Bounds.Height == h);
                    int from = known ? c.Left : b.X + Math.Max(Theme.Px(40), w / 2);
                    _animFrom[c] = from;
                    _animTo[c] = b;
                    var start = new Rectangle(from, top, w, h);
                    if (c.Bounds != start) c.Bounds = start;
                }
                else if (c.Bounds != b) c.Bounds = b;
                x += w + Gap;
            }
            if (anim && _animTo.Count > 0) { _animClock.Restart(); _animTimer.Start(); }
            if (pinned != null) foreach (var c in pinned) c.BringToFront();   // 3.5: ueber den gescrollten Zuegen
            Invalidate();
        }

        private Rectangle ThumbRect
        {
            get
            {
                int cw = ContentWidth, vw = ClientSize.Width;
                if (cw <= vw) return Rectangle.Empty;
                int track = vw - Pad * 2;
                int tw = Math.Max(Theme.Px(40), track * vw / cw);
                int tx = Pad + (int)((long)(track - tw) * _scroll / Math.Max(1, cw - vw));
                return new Rectangle(tx, ClientSize.Height - BarH + Theme.Px(2), tw, Theme.Px(5));
            }
        }

        /// <summary>3.4: eigenes Typenschild (z. B. "Jans Studio"); null = keins. Nur wo ShowTypePlate.</summary>
        public static string TypePlateText;
        public bool ShowTypePlate;

        /// <summary>Hier haengt die Pultlampe (Kanalbereich).</summary>
        public bool ShowLamp;
        /// <summary>Wo die Lampe haengt (0 = links … 1 = rechts im sichtbaren Bereich).</summary>
        public float LampFrac = 0.5f;
        /// <summary>Mitte der Lampen-Oeffnung in eigenen Koordinaten.</summary>
        public PointF LampPoint => new PointF((float)Math.Round(Math.Max(Theme.F(130), Math.Min(ClientSize.Width - Theme.F(40), ClientSize.Width * LampFrac))), TitleH - Theme.F(3));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            Theme.FillBody(g, ClientRectangle, Theme.FormOrigin(this), MixerForm.BodyColor);   // Pult-Grund (auch eigenes Bild)
            if (Theme.LiveBg > 0 && !StripBase.Calm) Theme.DrawLiveBg(g, ClientRectangle, Theme.FormOrigin(this));   // 3.3: bewegter Hintergrund
            if (!string.IsNullOrEmpty(Title))
            {
                var font = Theme.UiSemi(9.5f);
                Theme.DrawSpaced(g, Title, font, Theme.BodyText, Pad + Theme.F(2), Theme.F(5), Theme.F(2.2f));
                DrawTabs(g, Pad + Theme.F(2) + Theme.MeasureSpaced(g, Title, font, Theme.F(2.2f)) + Theme.F(14));
            }
            Theme.TiltShade(g, ClientRectangle, Theme.FormOrigin(this));   // 2.20
            Theme.LampLight(g, ClientRectangle, Theme.FormOrigin(this));
            PaintExtra?.Invoke(this, g);   // 3.4.6: Deko, Kabel
            if (ShowTypePlate && !string.IsNullOrEmpty(TypePlateText))
            {
                // 3.4: eigenes Typenschild rechts in der Titelzeile
                var font = Theme.Ui(10f, FontStyle.Bold);
                float w = Math.Min(Theme.F(220), Theme.TextWidth(g, TypePlateText, font) + Theme.F(36));
                float right = ClientSize.Width - Pad;
                if (w > Theme.F(50) && right - w > Theme.F(160))
                    Theme.DrawTypePlate(g, new RectangleF((float)Math.Round(right - w), Theme.F(2.5f), (float)Math.Round(w), Theme.F(17)), TypePlateText);
            }
            if (ShowLamp)
            {
                var lp = LampPoint;
                Theme.DrawLampGlow(g, lp);
                Theme.DrawLamp(g, lp, lp.X + Theme.F(34));
            }
            var t = ThumbRect;
            if (!t.IsEmpty)
            {
                var track = new RectangleF(Pad, t.Y, ClientSize.Width - Pad * 2, t.Height);
                Theme.FillRound(g, Theme.ScrollTrack, track, t.Height / 2f);
                Theme.FillRound(g, _hoverThumb || _dragThumb ? Theme.ScrollThumbHover : Theme.ScrollThumb, t, t.Height / 2f);
            }
        }

        /// <summary>3.4.6: Zug, dessen rechte Kante bzw. Luecke gerade unter der Maus liegt (zum Breiter-Ziehen).</summary>
        private StripBase GapStripAt(Point p)
        {
            foreach (Control c in Controls)
            {
                if (!(c is StripBase s) || !c.Visible || !s.ResizeAllowed) continue;
                if (p.Y < c.Top + Theme.Px(12) || p.Y > c.Bottom - Theme.Px(12)) continue;
                if (p.X >= c.Right - Theme.Px(2) && p.X <= c.Right + Gap + Theme.Px(1)) return s;
            }
            return null;
        }
        private bool _gapHover;

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) GapStripAt(e.Location)?.ResetResizeFromHost((ModifierKeys & Keys.Control) != 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ChannelStrip.Renaming?.EndRename(true);
            if (e.Button == MouseButtons.Left && GapStripAt(e.Location) is StripBase gs && gs.StartResizeFromHost()) return;   // 3.4.6
            if (ExtraMouseDown != null && ExtraMouseDown(this, e)) return;   // 3.4.6: Deko
            int tab = TabAt(e.Location);
            if (tab >= 0 && e.Button == MouseButtons.Left) { TabClicked?.Invoke(tab); return; }
            var t = ThumbRect;
            if (t.IsEmpty) return;
            var hit = Rectangle.Inflate(t, 0, Theme.Px(4));
            if (hit.Contains(e.Location))
            {
                _dragThumb = true; _dragStartX = e.X; _dragStartScroll = _scroll; Capture = true;
            }
            else if (e.Y >= ClientSize.Height - BarH - Theme.Px(2))
            {
                ScrollBy(e.X < t.X ? -ClientSize.Width * 3 / 4 : ClientSize.Width * 3 / 4);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (ExtraMouseMove != null && ExtraMouseMove(this, e)) return;   // 3.4.6: Deko ziehen
            if (_dragThumb)
            {
                int cw = ContentWidth, vw = ClientSize.Width;
                int track = vw - Pad * 2, tw = ThumbRect.Width;
                if (track - tw <= 0) return;
                int ns = _dragStartScroll + (int)((long)(e.X - _dragStartX) * (cw - vw) / (track - tw));
                _scroll = Math.Max(0, Math.Min(MaxScroll, ns));
                Relayout();
                return;
            }
            bool h = !ThumbRect.IsEmpty && Rectangle.Inflate(ThumbRect, 0, Theme.Px(4)).Contains(e.Location);
            if (h != _hoverThumb) { _hoverThumb = h; Invalidate(); }
            int tab = TabAt(e.Location);
            if (tab != _tabHover) { _tabHover = tab; Cursor = tab >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH)); }
            bool gap = tab < 0 && GapStripAt(e.Location) != null;   // 3.4.6: Luecke rechts neben einem Kanal: Doppelpfeil
            if (gap != _gapHover) { _gapHover = gap; Cursor = gap ? Cursors.SizeWE : Cursors.Default; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (ExtraMouseUp != null && ExtraMouseUp(this, e)) return;   // 3.4.6
            if (_dragThumb) { _dragThumb = false; Capture = false; Invalidate(); }
            int tab = TabAt(e.Location);
            if (tab >= 0 && e.Button == MouseButtons.Right) TabMenu?.Invoke(tab, PointToScreen(new Point((int)_tabRects[tab].X, (int)_tabRects[tab].Bottom + Theme.Px(2))));
        }

        /// <summary>Nur Vorschau: Rechtsklick auf einen Reiter.</summary>
        public void TabMenuDemo(int i)
        {
            if (i >= 0 && i < _tabRects.Length && !_tabRects[i].IsEmpty) TabMenu?.Invoke(i, PointToScreen(new Point((int)_tabRects[i].X, (int)_tabRects[i].Bottom + Theme.Px(2))));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverThumb) { _hoverThumb = false; Invalidate(); }
            if (_tabHover >= 0) { _tabHover = -1; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Tabs != null && Tabs.Length > 1 && TabAt(e.Location) >= 0)   // Mausrad auf den Reitern: Bank wechseln
            {
                TabClicked?.Invoke(((ActiveTab + (e.Delta < 0 ? 1 : -1)) % Tabs.Length + Tabs.Length) % Tabs.Length);
                return;
            }
            ScrollBy(-e.Delta * Theme.Px(60) / 120);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _autoScroll.Dispose(); _animTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Platzhalter-Zug zum Hinzufuegen eines Kanals.</summary>
    internal sealed class AddStripButton : Control
    {
        private bool _hover;
        public int StripWidth => Theme.Px(44);

        public AddStripButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            BackColor = MixerForm.BodyColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            Theme.FillBody(g, ClientRectangle, Theme.FormOrigin(this), MixerForm.BodyColor);
            // auf ganze Pixel, damit Striche und Ecken sauber sitzen
            float x0 = (float)Math.Round(Theme.F(2)), y0 = (float)Math.Round(Theme.F(2));
            var r = new RectangleF(x0, y0, (float)Math.Round(Width - Theme.F(2)) - x0, (float)Math.Round(Height - Theme.F(4)) - y0);
            Color c = _hover ? Theme.Accent : Theme.AddIdle;
            if (_hover) Theme.FillRound(g, Theme.AddHoverBg, r, Theme.PanelRad);
            DrawDashedFrame(g, r, Theme.PanelRad, c);
            float cx = (float)Math.Round(r.X + r.Width / 2), cy = (float)Math.Round(Theme.F(40));
            using (var pen = new Pen(c, Theme.F(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - Theme.F(8), cy, cx + Theme.F(8), cy);
                g.DrawLine(pen, cx, cy - Theme.F(8), cx, cy + Theme.F(8));
            }
            var state = g.Save();
            g.TranslateTransform(cx, Theme.F(64));
            g.RotateTransform(90);
            var font = Theme.UiSemi(10f);
            Theme.DrawSpaced(g, "KANAL HINZUFÜGEN", font, c, 0, -Theme.F(7), Theme.F(2.5f));
            g.Restore(state);
            Theme.TiltShade(g, ClientRectangle, Theme.FormOrigin(this));   // 2.20
            Theme.LampLight(g, ClientRectangle, Theme.FormOrigin(this));
        }

        /// <summary>
        /// Gestrichelter Rahmen ohne Treppen: Striche als exakte Pixel-Rechtecke auf den geraden Seiten,
        /// die runden Ecken als durchgehende, geglaettete Boegen (ein gestrichelter Pfad wuerde an den Ecken stufig).
        /// </summary>
        private static void DrawDashedFrame(Graphics g, RectangleF r, float rad, Color c)
        {
            float w = Math.Max(1f, (float)Math.Round(Theme.F(1)));
            float dash = Math.Max(2f, (float)Math.Round(Theme.F(5))), gap = Math.Max(2f, (float)Math.Round(Theme.F(4)));
            rad = (float)Math.Round(rad);
            // Ecken
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = w / 2f, d = (rad - h) * 2f;
            using (var pen = new Pen(c, w))
            {
                g.DrawArc(pen, r.X + h, r.Y + h, d, d, 180, 90);
                g.DrawArc(pen, r.Right - h - d, r.Y + h, d, d, 270, 90);
                g.DrawArc(pen, r.Right - h - d, r.Bottom - h - d, d, d, 0, 90);
                g.DrawArc(pen, r.X + h, r.Bottom - h - d, d, d, 90, 90);
            }
            g.SmoothingMode = SmoothingMode.None;
            // gerade Seiten: Striche gleichmaessig verteilt, an beiden Enden gleich viel Abstand
            using (var b = new SolidBrush(c))
            {
                Action<float, float, bool, float> side = (start, len, vertical, fixedPos) =>
                {
                    int n = Math.Max(1, (int)((len + gap) / (dash + gap)));
                    float used = n * dash + (n - 1) * gap;
                    float off = (float)Math.Floor((len - used) / 2f);
                    for (int i = 0; i < n; i++)
                    {
                        float p = start + off + i * (dash + gap);
                        if (vertical) g.FillRectangle(b, fixedPos, p, w, dash);
                        else g.FillRectangle(b, p, fixedPos, dash, w);
                    }
                };
                float sy = r.Y + rad + gap / 2f, ly = r.Height - rad * 2 - gap;
                float sx = r.X + rad + gap / 2f, lx = r.Width - rad * 2 - gap;
                if (ly > dash) { side(sy, ly, true, r.X); side(sy, ly, true, r.Right - w); }
                if (lx > dash) { side(sx, lx, false, r.Y); side(sx, lx, false, r.Bottom - w); }
            }
            g.SmoothingMode = sm;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            (Parent as StripHost)?.ScrollBy(-e.Delta * Theme.Px(60) / 120);
        }
    }
}
