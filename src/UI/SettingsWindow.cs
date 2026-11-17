using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Mischpult.UI
{
    // ====================================================================== Bausteine einer Einstellungsseite

    /// <summary>Eine Zeile im Einstellungsfenster (Schalter, Auswahl, Regler, Knopf …).</summary>
    internal abstract class SRow
    {
        public string Label;
        public Func<string> HintFunc;
        public Func<bool> Enabled, Visible;
        public string PageTitle;
        public string Hint => HintFunc?.Invoke() ?? "";
        public bool IsEnabled => Enabled == null || Enabled();
        public bool IsVisible => Visible == null || Visible();
        public virtual bool InCard => true;

        // Layout (vom Fenster berechnet)
        internal RectangleF Bounds, TextRect;
        internal float LabelH;
        internal readonly List<RectangleF> Parts = new List<RectangleF>();

        /// <summary>Zustand als Text – aendert er sich, wird neu gezeichnet.</summary>
        public virtual string State => IsEnabled ? "1" : "0";
    }

    internal sealed class SHeader : SRow { public override bool InCard => false; }
    internal sealed class SSection : SRow { public override bool InCard => false; }
    internal sealed class SInfo : SRow { }

    internal sealed class SToggle : SRow
    {
        public Func<bool> Get; public Action<bool> Set;
        public override string State => base.State + Get();
    }

    internal sealed class SChoice : SRow
    {
        public string[] Options; public Func<int> Get; public Action<int> Set;
        internal bool Below;
        public Func<string[]> OptionsFunc;   // 4.0: Auswahl, die sich aendert
        public override string State { get { if (OptionsFunc != null) Options = OptionsFunc(); return base.State + Get() + "/" + Options.Length; } }
    }

    internal sealed class SSlider : SRow
    {
        public float Min, Max, Step = 1f;
        public Func<float> Get; public Action<float, bool> Set; public Func<float, string> Format;
        public override string State => base.State + Get();
    }

    internal sealed class SButton : SRow
    {
        public string Text; public Func<string> Value; public Action<Rectangle> Click; public bool Danger, Primary;
        public Func<string> TextFunc;   // 4.0: Beschriftung, die sich aendert
        public override string State { get { if (TextFunc != null) Text = TextFunc(); return base.State + (Value?.Invoke() ?? "") + Text; } }
    }

    internal sealed class SSwatches : SRow
    {
        public Color[] Colors; public string[] Names; public Func<int> Get; public Action<int> Set;
        public override string State => base.State + Get();
    }

    /// <summary>Eine Seite (links in der Liste).</summary>
    internal sealed class SPage
    {
        public readonly string Title, Glyph, Subtitle;
        public readonly List<SRow> Rows = new List<SRow>();

        public SPage(string title, string glyph, string subtitle)
        {
            Title = title; Glyph = glyph; Subtitle = subtitle;
            Rows.Add(new SHeader { Label = title, HintFunc = () => subtitle, PageTitle = title });
        }

        private T Add<T>(T r) where T : SRow { r.PageTitle = Title; Rows.Add(r); return r; }

        public SSection Section(string title) => Add(new SSection { Label = title });
        public SInfo Info(string text, Func<string> hint = null) => Add(new SInfo { Label = text, HintFunc = hint });
        public SToggle Toggle(string label, string hint, Func<bool> get, Action<bool> set) =>
            Add(new SToggle { Label = label, HintFunc = hint == null ? null : (Func<string>)(() => hint), Get = get, Set = set });
        public SToggle Toggle(string label, Func<string> hint, Func<bool> get, Action<bool> set) =>
            Add(new SToggle { Label = label, HintFunc = hint, Get = get, Set = set });
        public SChoice Choice(string label, Func<string> hint, string[] options, Func<int> get, Action<int> set) =>
            Add(new SChoice { Label = label, HintFunc = hint, Options = options, Get = get, Set = set });
        /// <summary>4.0: Auswahl mit wechselnden Eintraegen.</summary>
        public SChoice Choice(string label, Func<string> hint, Func<string[]> options, Func<int> get, Action<int> set) =>
            Add(new SChoice { Label = label, HintFunc = hint, Options = options(), OptionsFunc = options, Get = get, Set = set });
        public SSlider Slider(string label, string hint, float min, float max, float step, Func<float> get, Action<float, bool> set, Func<float, string> format) =>
            Add(new SSlider { Label = label, HintFunc = hint == null ? null : (Func<string>)(() => hint), Min = min, Max = max, Step = step, Get = get, Set = set, Format = format });
        public SButton Button(string label, string hint, string text, Action<Rectangle> click, Func<string> value = null) =>
            Add(new SButton { Label = label, HintFunc = hint == null ? null : (Func<string>)(() => hint), Text = text, Click = click, Value = value });
        /// <summary>4.0: Knopf mit wechselnder Erklaerung und Beschriftung.</summary>
        public SButton Button(string label, Func<string> hint, Func<string> text, Action<Rectangle> click, Func<string> value = null) =>
            Add(new SButton { Label = label, HintFunc = hint, Text = text(), TextFunc = text, Click = click, Value = value });
        public SSwatches Swatches(string label, Color[] colors, string[] names, Func<int> get, Action<int> set) =>
            Add(new SSwatches { Label = label, Colors = colors, Names = names, Get = get, Set = set, HintFunc = () => names[Math.Max(0, Math.Min(names.Length - 1, get()))] });
    }

    // ====================================================================== Fenster

    /// <summary>
    /// Einstellungsfenster: links die Bereiche (mit Suche), rechts die Einstellungen als Karten mit Schaltern,
    /// Auswahlknoepfen und Reglern. Alles gilt sofort – kein OK/Abbrechen noetig.
    /// </summary>
    internal sealed class SettingsWindow : Form
    {
        private readonly List<SPage> _pages;
        private readonly SettingsNav _nav;
        private readonly SettingsView _view;
        private readonly TextBox _search;
        private readonly Timer _refresh = new Timer { Interval = 300 };
        private int _selected;
        private string _stateKey;

        public SettingsWindow(List<SPage> pages, string startPage)
        {
            _pages = pages;
            Text = "Einstellungen – Mischpult";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Font = Theme.Ui(12f);
            ClientSize = new Size(Theme.Px(880), Theme.Px(620));
            MinimumSize = new Size(Theme.Px(660), Theme.Px(440));

            _view = new SettingsView { Dock = DockStyle.Fill };
            _nav = new SettingsNav(this) { Dock = DockStyle.Left, Width = Theme.Px(224) };
            _search = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(12.5f) };
            _search.TextChanged += (s, e) => ShowCurrent();
            _search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && _search.Text.Length > 0) { _search.Text = ""; e.SuppressKeyPress = true; e.Handled = true; } };
            _search.GotFocus += (s, e) => _nav.Invalidate();
            _search.LostFocus += (s, e) => _nav.Invalidate();
            _nav.Controls.Add(_search);
            Controls.Add(_view);
            Controls.Add(_nav);

            _selected = Math.Max(0, pages.FindIndex(p => p.Title == startPage));
            ActiveControl = _view;   // Suchfeld erst beim Anklicken (dann ist der Hinweistext zu sehen)
            ApplyTheme();
            ShowCurrent();
            _refresh.Tick += (s, e) => RefreshIfChanged();
            _refresh.Start();
        }

        public IReadOnlyList<SPage> Pages => _pages;
        /// <summary>4.0: Schalter "Nur das Wichtigste" links unten (null = keiner).</summary>
        public Func<bool> SimpleGet;
        public Action<bool> SimpleSet;
        public int Selected => _search.Text.Trim().Length > 0 || _custom != null ? -1 : _selected;
        /// <summary>4.1: eigene Ansicht statt einer Seite (z. B. "Was ist eingeschaltet") – null = keine.</summary>
        private List<SRow> _custom;
        public bool ShowingCustom => _custom != null;

        /// <summary>4.1: eine eigene Zeilen-Liste zeigen (bis wieder eine Seite gewaehlt wird).</summary>
        public void ShowCustom(List<SRow> rows)
        {
            _custom = rows;
            if (_search.Text.Length > 0) _search.Text = "";   // loest ShowCurrent aus
            else ShowCurrent();
        }

        /// <summary>4.0 finale: Suche von aussen (Befehlssuche).</summary>
        public void SearchFor(string text) { _search.Text = text ?? ""; _search.Focus(); _search.SelectionStart = _search.Text.Length; }
        internal TextBox SearchBox => _search;

        /// <summary>Bereich anzeigen (Titel), z. B. beim erneuten Oeffnen.</summary>
        /// <summary>Suchtext setzen (nur Demo/Vorschau).</summary>
        public void SetSearch(string text) => _search.Text = text ?? "";

        public void SelectPage(string title)
        {
            int i = _pages.FindIndex(p => p.Title == title);
            if (i >= 0) SelectPage(i);
        }

        public void SelectPage(int i)
        {
            if (i < 0 || i >= _pages.Count) return;
            _selected = i;
            _custom = null;
            if (_search.Text.Length > 0) _search.Text = "";   // loest ShowCurrent aus
            else ShowCurrent();
        }

        private void ShowCurrent()
        {
            string q = _search.Text.Trim();
            if (q.Length == 0) _view.SetRows(_custom ?? _pages[_selected].Rows);
            else _view.SetRows(SearchRows(q));
            _nav.Invalidate();
            _stateKey = null;
        }

        /// <summary>Alle Einstellungen, deren Name oder Erklaerung den Suchtext enthaelt – nach Bereich sortiert.</summary>
        private List<SRow> SearchRows(string q)
        {
            var rows = new List<SRow> { new SHeader { Label = "Suche", HintFunc = () => "Ergebnisse für „" + q + "“" } };
            foreach (var p in _pages)
            {
                bool pageHit = p.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                var hits = new List<SRow>();
                string section = null;   // Abschnitt zaehlt mit: "VU" findet alles unter "VU-Meter"
                foreach (var r in p.Rows)
                {
                    if (r is SHeader) continue;
                    if (r is SSection) { section = r.Label; continue; }
                    if (pageHit || Contains(section, q) || Contains(r.Label, q) || Contains(r.Hint, q) || (r is SChoice c && c.Options.Any(o => Contains(o, q))))
                        hits.Add(r);
                }
                if (hits.Count == 0) continue;
                rows.Add(new SSection { Label = p.Title });
                rows.AddRange(hits);
            }
            if (rows.Count == 1) rows.Add(new SInfo { Label = "Nichts gefunden.", HintFunc = () => "Anderes Wort versuchen, z. B. „Pegel“, „Mikro“ oder „Design“." });
            return rows;
        }

        private static bool Contains(string s, string q) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;

        /// <summary>Farben neu (nach einem Design-Wechsel).</summary>
        public void ApplyTheme()
        {
            BackColor = SettingsView.WinBg;
            ForeColor = Theme.Text;
            _search.BackColor = SettingsView.InputBg;
            _search.ForeColor = Theme.Text;
            if (IsHandleCreated) DarkTitle.Apply(Handle);
            _nav.BackColor = SettingsView.NavBg;
            _view.BackColor = SettingsView.WinBg;
            _nav.Invalidate(true);
            _view.Relayout();
        }

        private void RefreshIfChanged()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            string key = _view.StateKey();
            if (key != _stateKey) { _stateKey = key; _view.Relayout(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle.Apply(Handle);
            try { if (Native.Win32.IsWindows) SendMessage(_search.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Einstellung suchen …"); } catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, string lp);

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // mittig ueber dem Mischpult (bzw. auf dem Bildschirm)
            Rectangle area = Owner != null && Owner.Visible && Owner.WindowState != FormWindowState.Minimized ? Owner.Bounds : Screen.FromPoint(Cursor.Position).WorkingArea;
            var wa = Screen.FromRectangle(area).WorkingArea;
            int w = Math.Min(Width, wa.Width - Theme.Px(20)), h = Math.Min(Height, wa.Height - Theme.Px(20));
            int x = area.X + (area.Width - w) / 2, y = area.Y + (area.Height - h) / 2;
            Bounds = new Rectangle(Math.Max(wa.X, Math.Min(wa.Right - w, x)), Math.Max(wa.Y, Math.Min(wa.Bottom - h, y)), w, h);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && !(_search.Focused && _search.Text.Length > 0)) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.F && e.Control) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && e.Control) { SelectPage(Math.Min(_pages.Count - 1, _selected + 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Up && e.Control) { SelectPage(Math.Max(0, _selected - 1)); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refresh.Dispose();
            base.Dispose(disposing);
        }
    }

    // ====================================================================== Liste links

    internal sealed class SettingsNav : Control
    {
        private readonly SettingsWindow _win;
        private int _hover = -1;

        public SettingsNav(SettingsWindow win)
        {
            _win = win;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private RectangleF SearchRect => new RectangleF(Theme.F(14), Theme.F(16), Width - Theme.F(28), Theme.F(32));
        private float ItemTop => Theme.F(64);
        private RectangleF SimpleRect => new RectangleF(Theme.F(10), Height - Theme.F(128), Width - Theme.F(20), Theme.F(36));
        private float ItemH => Theme.F(38);
        private RectangleF ItemRect(int i) => new RectangleF(Theme.F(10), ItemTop + i * ItemH, Width - Theme.F(20), ItemH - Theme.F(4));

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var tb = _win?.SearchBox;
            if (tb == null) return;   // noch im Aufbau
            var sr = SearchRect;
            int th = tb.PreferredHeight;
            tb.Bounds = new Rectangle((int)(sr.X + Theme.F(30)), (int)(sr.Y + (sr.Height - th) / 2f), (int)(sr.Width - Theme.F(38)), th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(SettingsView.NavBg);
            // Suchfeld
            var sr = SearchRect;
            Theme.FillRound(g, SettingsView.InputBg, sr, Theme.F(6));
            var sb = _win.SearchBox;
            if (sb == null) return;
            Theme.StrokeRound(g, sb.Focused ? Theme.Accent : SettingsView.CardEdge, Theme.F(1), sr, Theme.F(6));
            DrawGlyph(g, "", new RectangleF(sr.X + Theme.F(6), sr.Y, Theme.F(22), sr.Height), Theme.TextDim, '⌕');
            if (!Native.Win32.IsWindows && sb.Text.Length == 0 && !sb.Focused)
                using (var b = new SolidBrush(Theme.TextFaint)) Theme.Str(g, "Einstellung suchen …", Theme.Ui(12f), b, new RectangleF(sr.X + Theme.F(30), sr.Y, sr.Width - Theme.F(34), sr.Height), Theme.Left);

            for (int i = 0; i < _win.Pages.Count; i++)
            {
                var p = _win.Pages[i];
                var r = ItemRect(i);
                bool sel = i == _win.Selected, hov = i == _hover;
                if (sel || hov) Theme.FillRound(g, sel ? SettingsView.NavSel : SettingsView.NavHover, r, Theme.F(6));
                if (sel) Theme.FillRound(g, Theme.Accent, new RectangleF(r.X, r.Y + Theme.F(9), Theme.F(3), r.Height - Theme.F(18)), Theme.F(1.5f));
                DrawGlyph(g, p.Glyph, new RectangleF(r.X + Theme.F(10), r.Y, Theme.F(22), r.Height), sel ? Theme.AccentText : Theme.TextDim, '•');
                using (var b = new SolidBrush(sel ? Theme.Text : Theme.TextDim))
                    Theme.Str(g, p.Title, sel ? Theme.UiSemi(12.5f) : Theme.Ui(12.5f), b, new RectangleF(r.X + Theme.F(40), r.Y, r.Width - Theme.F(44), r.Height), Theme.Left);
            }

            // 4.0: einfache Einstellungen
            if (_win.SimpleGet != null)
            {
                var sr2 = SimpleRect;
                bool on = _win.SimpleGet();
                if (_hover == -2) Theme.FillRound(g, SettingsView.NavHover, sr2, Theme.F(6));
                SettingsView.DrawToggle(g, new RectangleF(sr2.X + Theme.F(8), sr2.Y + (sr2.Height - Theme.F(20)) / 2f, Theme.F(38), Theme.F(20)), on, true, _hover == -2);
                using (var b = new SolidBrush(on ? Theme.Text : Theme.TextDim))
                    Theme.Str(g, "Nur das Wichtigste", Theme.Ui(12f), b, new RectangleF(sr2.X + Theme.F(54), sr2.Y, sr2.Width - Theme.F(56), sr2.Height), Theme.Left);
            }
            // unten: Hinweise
            float by = Height - Theme.F(84);
            using (var b = new SolidBrush(Theme.TextFaint))
            {
                Theme.Str(g, "Änderungen gelten sofort.\nRechtsklick auf OPTIONEN öffnet das Schnellmenü.", Theme.Ui(10.5f), b,
                    new RectangleF(Theme.F(18), by, Width - Theme.F(30), Theme.F(48)), Theme.Wrap);
                Theme.Str(g, "Mischpult " + Program.Version + " · made by " + Program.Author, Theme.Ui(10.5f), b, new RectangleF(Theme.F(18), Height - Theme.F(30), Width - Theme.F(30), Theme.F(16)), Theme.Left);
            }
            using (var pen = new Pen(SettingsView.CardEdge, 1f)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        private static Font _icons;
        private static bool _iconsChecked;

        /// <summary>Symbol aus "Segoe MDL2 Assets" (Windows 10/11); fehlt die Schrift, ein einfacher Ersatz.</summary>
        internal static void DrawGlyph(Graphics g, string glyph, RectangleF r, Color c, char fallback)
        {
            if (!_iconsChecked)
            {
                _iconsChecked = true;
                try
                {
                    var f = new Font("Segoe MDL2 Assets", Theme.F(14), FontStyle.Regular, GraphicsUnit.Pixel);
                    if (string.Equals(f.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase)) _icons = f; else f.Dispose();
                }
                catch { }
            }
            using (var b = new SolidBrush(c))
            {
                if (_icons != null) Theme.Str(g, glyph, _icons, b, r, Theme.Center);
                else Theme.Str(g, fallback.ToString(), Theme.Ui(13f, FontStyle.Bold), b, r, Theme.Center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _win.Pages.Count; i++) if (ItemRect(i).Contains(e.Location)) h = i;
            if (h < 0 && _win.SimpleGet != null && SimpleRect.Contains(e.Location)) h = -2;
            if (h != _hover) { _hover = h; Cursor = h != -1 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != -1) { _hover = -1; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < _win.Pages.Count; i++) if (ItemRect(i).Contains(e.Location)) { _win.SelectPage(i); return; }
            if (_win.SimpleGet != null && SimpleRect.Contains(e.Location)) { _win.SimpleSet?.Invoke(!_win.SimpleGet()); Invalidate(); return; }
            if (SearchRect.Contains(e.Location)) _win.SearchBox.Focus();
        }
    }

    // ====================================================================== Einstellungen rechts

    internal sealed class SettingsView : Control
    {
        // Farben (aus dem aktuellen Design abgeleitet)
        public static Color WinBg => Theme.MenuBg;
        public static Color NavBg => Theme.IsLight ? Theme.Darken(Theme.MenuBg, 0.035f) : Theme.Darken(Theme.MenuBg, 0.18f);
        public static Color NavSel => Theme.IsLight ? Theme.Darken(Theme.MenuBg, 0.09f) : Theme.Lighten(Theme.MenuBg, 0.07f);
        public static Color NavHover => Theme.IsLight ? Theme.Darken(Theme.MenuBg, 0.06f) : Theme.Lighten(Theme.MenuBg, 0.035f);
        public static Color CardBg => Theme.IsLight ? Color.White : Theme.Lighten(Theme.MenuBg, 0.045f);
        public static Color CardEdge => Theme.IsLight ? Theme.Darken(Theme.MenuBg, 0.13f) : Theme.Lighten(Theme.MenuBg, 0.1f);
        public static Color InputBg => Theme.Input;

        private List<SRow> _rows = new List<SRow>();
        private readonly List<SRow> _shown = new List<SRow>();
        private readonly List<RectangleF> _cards = new List<RectangleF>();
        private float _scroll, _contentH;
        private SRow _hoverRow; private int _hoverPart = -1;
        private SSlider _dragSlider;
        private bool _dragThumb; private float _dragY0, _dragScroll0;
        private readonly ToolTip _tip = PultTip.Make(new ToolTip { InitialDelay = 500, AutoPopDelay = 5000 });
        private string _tipText;

        public SettingsView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public void SetRows(List<SRow> rows)
        {
            _rows = rows;
            _scroll = 0;
            _hoverRow = null; _hoverPart = -1;
            Relayout();
        }

        public string StateKey()
        {
            var sb = new StringBuilder();
            foreach (var r in _rows) { sb.Append(r.IsVisible ? 'v' : 'h'); try { sb.Append(r.State); } catch { } sb.Append(r.Hint).Append('|'); }
            return sb.ToString();
        }

        public void Relayout()
        {
            if (!IsHandleCreated || Width < 50) { Invalidate(); return; }
            using (var g = CreateGraphics())
            {
                Theme.Setup(g);
                DoLayout(g);
            }
            Invalidate();
        }

        // --------------------------------------------------------------- Layout

        private float Pad => Theme.F(30);
        private float ScrollW => Theme.F(12);
        private static Font LabelFont => Theme.Ui(12.5f);
        private static Font HintFont => Theme.Ui(10.5f);
        private static Font SegFont => Theme.Ui(11.5f);

        private static float TextH(Graphics g, string s, Font f, float w) =>
            Theme.TextHeight(g, s, f, w);

        private static float TextW(Graphics g, string s, Font f) => Theme.TextWidth(g, s ?? "", f);

        private void DoLayout(Graphics g)
        {
            _shown.Clear();
            _cards.Clear();
            foreach (var r in _rows) if (SafeVisible(r)) _shown.Add(r);

            float x0 = Pad, cw = Math.Max(Theme.F(200), Width - Pad * 2 - ScrollW);
            float inX = x0 + Theme.F(18), inW = cw - Theme.F(36);
            float y = Theme.F(22);
            float padV = Theme.F(12);
            RectangleF card = RectangleF.Empty;
            Action closeCard = () => { if (!card.IsEmpty) { _cards.Add(card); card = RectangleF.Empty; y += Theme.F(6); } };

            foreach (var r in _shown)
            {
                r.Parts.Clear();
                if (r is SHeader)
                {
                    closeCard();
                    float th = TextH(g, r.Label, Theme.Ui(21f, FontStyle.Bold), cw);
                    float sh = TextH(g, r.Hint, Theme.Ui(11.5f), cw);
                    r.Bounds = new RectangleF(x0, y, cw, th + sh + Theme.F(6));
                    r.LabelH = th;
                    y = r.Bounds.Bottom + Theme.F(10);
                    continue;
                }
                if (r is SSection)
                {
                    closeCard();
                    y += Theme.F(10);
                    r.Bounds = new RectangleF(x0 + Theme.F(4), y, cw, Theme.F(22));
                    y = r.Bounds.Bottom + Theme.F(4);
                    continue;
                }
                if (card.IsEmpty) card = new RectangleF(x0, y, cw, 0);

                float ctrlW = 0, ctrlH = 0, belowH = 0;
                bool below = false;
                switch (r)
                {
                    case SToggle _: ctrlW = Theme.F(46); ctrlH = Theme.F(24); break;
                    case SButton b:
                        {
                            float bw = Math.Max(Theme.F(120), TextW(g, b.Text, Theme.UiSemi(12f)) + Theme.F(34));
                            ctrlW = bw; ctrlH = Theme.F(32);
                            string v = b.Value?.Invoke();
                            if (!string.IsNullOrEmpty(v)) ctrlW += TextW(g, v, HintFont) + Theme.F(14);
                            break;
                        }
                    case SSwatches s: ctrlW = s.Colors.Length * Theme.F(32); ctrlH = Theme.F(28); break;
                    case SSlider sl:
                        {
                            string vt = "";
                            try { vt = sl.Format(sl.Get()); } catch { }
                            ctrlW = Math.Max(Theme.F(90), TextW(g, vt, Theme.UiSemi(12.5f)) + Theme.F(16));
                            ctrlH = Theme.F(20); below = true; belowH = Theme.F(26);
                            break;
                        }
                    case SChoice c:
                        {
                            float tot = 0;
                            foreach (var o in c.Options) tot += TextW(g, o, SegFont) + Theme.F(26);
                            tot += (c.Options.Length - 1) * Theme.F(4);
                            if (tot <= inW * 0.52f && c.Options.Length <= 5) { ctrlW = tot; ctrlH = Theme.F(30); c.Below = false; }
                            else
                            {
                                c.Below = true; below = true;
                                // Zeilen umbrechen
                                float lx = 0; int lines = 1;
                                foreach (var o in c.Options)
                                {
                                    float w = TextW(g, o, SegFont) + Theme.F(26);
                                    if (lx > 0 && lx + w > inW) { lines++; lx = 0; }
                                    lx += w + Theme.F(6);
                                }
                                belowH = lines * Theme.F(30) + (lines - 1) * Theme.F(6) + Theme.F(4);
                            }
                            break;
                        }
                }
                float textW = below ? inW - ctrlW - Theme.F(10) : inW - ctrlW - Theme.F(20);
                if (r is SInfo) textW = inW;
                float lh = TextH(g, r.Label, LabelFont, textW);
                float hh = TextH(g, r.Hint, HintFont, below ? inW : textW);
                r.LabelH = lh;
                float textH = lh + (hh > 0 ? hh + Theme.F(2) : 0);
                float topH = below ? Math.Max(lh, ctrlH) + (hh > 0 ? hh + Theme.F(2) : 0) : Math.Max(textH, ctrlH);
                float rowH = padV * 2 + topH + (below ? belowH + Theme.F(r is SSlider ? 2 : 8) : 0);
                r.Bounds = new RectangleF(x0, y, cw, rowH);
                r.TextRect = new RectangleF(inX, y + padV + (below ? 0 : Math.Max(0, (topH - textH) / 2f)), textW, textH);

                // Bedienelemente
                float cy = y + padV + (below ? 0 : (topH - ctrlH) / 2f);
                float cx = inX + inW - ctrlW;
                switch (r)
                {
                    case SToggle _: r.Parts.Add(new RectangleF(cx, cy, ctrlW, ctrlH)); break;
                    case SButton b:
                        {
                            float bw = Math.Max(Theme.F(120), TextW(g, b.Text, Theme.UiSemi(12f)) + Theme.F(34));
                            r.Parts.Add(new RectangleF(inX + inW - bw, cy, bw, ctrlH));
                            break;
                        }
                    case SSwatches s:
                        for (int i = 0; i < s.Colors.Length; i++)
                            r.Parts.Add(new RectangleF(cx + i * Theme.F(32) + Theme.F(2), cy, Theme.F(28), Theme.F(28)));
                        break;
                    case SSlider _:
                        r.Parts.Add(new RectangleF(inX + inW - ctrlW, y + padV, ctrlW, Math.Max(lh, ctrlH)));   // Wert
                        r.Parts.Add(new RectangleF(inX, y + padV + topH + Theme.F(4), inW, belowH));          // Bahn
                        break;
                    case SChoice c:
                        if (!c.Below)
                        {
                            float sx = cx;
                            foreach (var o in c.Options)
                            {
                                float w = TextW(g, o, SegFont) + Theme.F(26);
                                r.Parts.Add(new RectangleF(sx, cy, w, ctrlH));
                                sx += w + Theme.F(4);
                            }
                        }
                        else
                        {
                            float sx = 0, sy = y + padV + topH + Theme.F(8);
                            foreach (var o in c.Options)
                            {
                                float w = TextW(g, o, SegFont) + Theme.F(26);
                                if (sx > 0 && sx + w > inW) { sx = 0; sy += Theme.F(36); }
                                r.Parts.Add(new RectangleF(inX + sx, sy, w, Theme.F(30)));
                                sx += w + Theme.F(6);
                            }
                        }
                        break;
                }
                card = new RectangleF(card.X, card.Y, card.Width, r.Bounds.Bottom - card.Y);
                y = r.Bounds.Bottom;
            }
            closeCard();
            _contentH = y + Theme.F(24);
            _scroll = (float)Math.Round(Math.Max(0, Math.Min(MaxScroll, _scroll)));
        }

        private static bool SafeVisible(SRow r) { try { return r.IsVisible; } catch { return false; } }

        private float MaxScroll => Math.Max(0, _contentH - Height);

        // --------------------------------------------------------------- Zeichnen

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(WinBg);
            var st = g.Save();
            g.TranslateTransform(0, -_scroll);

            foreach (var c in _cards)
            {
                Theme.FillRound(g, CardBg, c, Theme.F(8));
                Theme.StrokeRound(g, CardEdge, Theme.F(1), c, Theme.F(8));
            }

            SRow prev = null;
            foreach (var r in _shown)
            {
                if (r.Bounds.Bottom - _scroll < 0 || r.Bounds.Y - _scroll > Height) { prev = r; continue; }
                // Trennlinie zwischen Zeilen derselben Karte
                if (r.InCard && prev != null && prev.InCard)
                    using (var pen = new Pen(CardEdge, 1f))
                        g.DrawLine(pen, r.Bounds.X + Theme.F(18), r.Bounds.Y, r.Bounds.Right - Theme.F(18), r.Bounds.Y);
                try { DrawRow(g, r); } catch { }
                prev = r;
            }
            g.Restore(st);

            // Scrollleiste
            var t = ThumbRect;
            if (!t.IsEmpty)
                Theme.FillRound(g, _dragThumb ? Theme.ScrollThumbHover : Theme.ScrollThumb, t, t.Width / 2f);
        }

        private RectangleF ThumbRect
        {
            get
            {
                if (MaxScroll <= 0) return RectangleF.Empty;
                float track = Height - Theme.F(16);
                float th = Math.Max(Theme.F(40), track * Height / _contentH);
                float ty = Theme.F(8) + (track - th) * (_scroll / MaxScroll);
                return new RectangleF(Width - Theme.F(9), ty, Theme.F(5), th);
            }
        }

        private void DrawRow(Graphics g, SRow r)
        {
            bool en = r.IsEnabled;
            Color text = en ? Theme.Text : Theme.TextFaint, hint = en ? Theme.TextDim : Theme.TextFaint;
            switch (r)
            {
                case SHeader h:
                    using (var b = new SolidBrush(Theme.Text))
                        Theme.Str(g, h.Label, Theme.Ui(21f, FontStyle.Bold), b, new RectangleF(h.Bounds.X, h.Bounds.Y, h.Bounds.Width, h.LabelH + Theme.F(4)), Theme.Wrap);
                    if (!string.IsNullOrEmpty(h.Hint))
                        using (var b = new SolidBrush(Theme.TextDim))
                            Theme.Str(g, h.Hint, Theme.Ui(11.5f), b, new RectangleF(h.Bounds.X, h.Bounds.Y + h.LabelH + Theme.F(2), h.Bounds.Width, h.Bounds.Height), Theme.Wrap);
                    return;
                case SSection s:
                    using (var b = new SolidBrush(Theme.TextFaint))
                        Theme.Str(g, s.Label.ToUpperInvariant(), Theme.UiSemi(10.5f), b, s.Bounds, Theme.Left);
                    return;
            }

            // Text links
            var tr = r.TextRect;
            using (var b = new SolidBrush(text))
                Theme.Str(g, r.Label, LabelFont, b, new RectangleF(tr.X, tr.Y, tr.Width, r.LabelH + Theme.F(2)), Theme.Wrap);
            string hs = r.Hint;
            if (!string.IsNullOrEmpty(hs))
            {
                float hw = r is SSlider || (r is SChoice ch && ch.Below) ? r.Bounds.Width - Theme.F(36) : tr.Width;
                using (var b = new SolidBrush(hint))
                    Theme.Str(g, hs, HintFont, b, new RectangleF(tr.X, tr.Y + r.LabelH + Theme.F(2), hw, r.Bounds.Bottom - tr.Y - r.LabelH), Theme.Wrap);
            }

            bool hov = r == _hoverRow;
            switch (r)
            {
                case SToggle t: DrawToggle(g, t.Parts[0], t.Get(), en, hov); break;
                case SButton b: DrawButton(g, b, en, hov && _hoverPart == 0); break;
                case SChoice c:
                    {
                        int sel = c.Get();
                        for (int i = 0; i < c.Parts.Count; i++)
                            DrawSegment(g, c.Parts[i], c.Options[i], i == sel, en, hov && _hoverPart == i);
                        break;
                    }
                case SSlider s: DrawSlider(g, s, en, hov || _dragSlider == s); break;
                case SSwatches w:
                    {
                        int sel = w.Get();
                        for (int i = 0; i < w.Parts.Count; i++)
                        {
                            var pr = w.Parts[i];
                            float cx = pr.X + pr.Width / 2, cy = pr.Y + pr.Height / 2;
                            if (i == sel) { Theme.FillEllipse(g, Theme.Text, cx, cy, Theme.F(13)); Theme.FillEllipse(g, CardBg, cx, cy, Theme.F(11)); }
                            else if (hov && _hoverPart == i) Theme.FillEllipse(g, CardEdge, cx, cy, Theme.F(13));
                            Theme.FillEllipse(g, en ? w.Colors[i] : Theme.Mix(w.Colors[i], CardBg, 0.6f), cx, cy, Theme.F(9));
                        }
                        break;
                    }
            }
        }

        internal static void DrawToggle(Graphics g, RectangleF r, bool on, bool en, bool hov)
        {
            var track = new RectangleF(r.X, r.Y + (r.Height - Theme.F(22)) / 2, r.Width, Theme.F(22));
            Color acc = Theme.Accent;
            Color tc = on ? acc : (Theme.IsLight ? Theme.Darken(CardBg, 0.12f) : Theme.Lighten(CardBg, 0.1f));
            if (!en) tc = Theme.Mix(tc, CardBg, 0.55f);
            else if (hov) tc = on ? Theme.Lighten(acc, 0.1f) : Theme.Lighten(tc, Theme.IsLight ? -0.05f : 0.06f);
            Theme.FillRound(g, tc, track, track.Height / 2);
            if (!on) Theme.StrokeRound(g, Theme.IsLight ? Theme.Darken(CardBg, 0.28f) : Theme.Lighten(CardBg, 0.22f), Theme.F(1), track, track.Height / 2);
            float kr = Theme.F(on ? 7.5f : 6.5f);
            float kx = on ? track.Right - track.Height / 2 : track.X + track.Height / 2;
            Color kc = on ? Theme.TextOn(acc) : (Theme.IsLight ? Theme.Darken(CardBg, 0.45f) : Theme.Lighten(CardBg, 0.55f));
            if (!en) kc = Theme.Mix(kc, CardBg, 0.5f);
            Theme.FillEllipse(g, kc, kx, track.Y + track.Height / 2, kr);
        }

        private static void DrawSegment(Graphics g, RectangleF r, string text, bool sel, bool en, bool hov)
        {
            Color bg = sel ? Theme.Accent : hov ? (Theme.IsLight ? Theme.Darken(CardBg, 0.08f) : Theme.Lighten(CardBg, 0.09f)) : (Theme.IsLight ? Theme.Darken(CardBg, 0.04f) : Theme.Lighten(CardBg, 0.05f));
            if (!en) bg = Theme.Mix(bg, CardBg, 0.5f);
            Theme.FillRound(g, bg, r, Theme.F(6));
            if (!sel) Theme.StrokeRound(g, CardEdge, Theme.F(1), r, Theme.F(6));
            Color tc = sel ? Theme.TextOn(Theme.Accent) : en ? Theme.Text : Theme.TextFaint;
            using (var b = new SolidBrush(tc)) Theme.Str(g, text, sel ? Theme.UiSemi(11.5f) : SegFont, b, r, Theme.Center);
        }

        private static void DrawButton(Graphics g, SButton b, bool en, bool hov)
        {
            var r = b.Parts[0];
            string v = b.Value?.Invoke();
            if (!string.IsNullOrEmpty(v))
                using (var br = new SolidBrush(en ? Theme.TextDim : Theme.TextFaint))
                    Theme.Str(g, v, HintFont, br, new RectangleF(r.X - Theme.F(300) - Theme.F(10), r.Y, Theme.F(300), r.Height), Theme.Right);
            Color bg = b.Primary ? (hov ? Theme.PrimaryHover : Theme.Primary) : hov ? Theme.ButtonHover : Theme.Button;
            if (!en) bg = Theme.Mix(bg, CardBg, 0.55f);
            Theme.FillRound(g, bg, r, Theme.F(6));
            Theme.StrokeRound(g, Theme.ButtonEdge, Theme.F(1), r, Theme.F(6));
            Color tc = !en ? Theme.TextFaint : b.Danger ? (Theme.IsLight ? Color.FromArgb(0xc0, 0x22, 0x1a) : Theme.MuteRed) : b.Primary ? Color.White : Theme.ButtonText;
            using (var br = new SolidBrush(tc)) Theme.Str(g, b.Text, Theme.UiSemi(12f), br, r, Theme.Center);
        }

        private static void DrawSlider(Graphics g, SSlider s, bool en, bool hov)
        {
            float v = s.Get();
            using (var b = new SolidBrush(en ? Theme.AccentText : Theme.TextFaint))
                Theme.Str(g, s.Format(v), Theme.UiSemi(12.5f), b, s.Parts[0], Theme.Right);
            var area = s.Parts[1];
            float frac = s.Max > s.Min ? (v - s.Min) / (s.Max - s.Min) : 0;
            frac = Math.Max(0, Math.Min(1, frac));
            var track = new RectangleF(area.X + Theme.F(8), area.Y + area.Height / 2 - Theme.F(2), area.Width - Theme.F(16), Theme.F(4));
            Theme.FillRound(g, Theme.IsLight ? Theme.Darken(CardBg, 0.14f) : Theme.Lighten(CardBg, 0.12f), track, Theme.F(2));
            Color acc = en ? Theme.Accent : Theme.Mix(Theme.Accent, CardBg, 0.6f);
            Theme.FillRound(g, acc, new RectangleF(track.X, track.Y, Math.Max(Theme.F(4), track.Width * frac), track.Height), Theme.F(2));
            float kx = track.X + track.Width * frac, ky = track.Y + track.Height / 2;
            if (Theme.IsLight) Theme.FillEllipse(g, Theme.Darken(CardBg, 0.22f), kx, ky, Theme.F(hov ? 10f : 9f));
            Theme.FillEllipse(g, Theme.IsLight ? Color.White : Theme.Lighten(CardBg, 0.8f), kx, ky, Theme.F(hov ? 9f : 8f));
            Theme.FillEllipse(g, acc, kx, ky, Theme.F(hov ? 5f : 4.5f));
        }

        // --------------------------------------------------------------- Maus

        private bool Hit(PointF p, out SRow row, out int part)
        {
            row = null; part = -1;
            var q = new PointF(p.X, p.Y + _scroll);
            foreach (var r in _shown)
            {
                if (!r.Bounds.Contains(q)) continue;
                row = r;
                for (int i = 0; i < r.Parts.Count; i++)
                {
                    var pr = r is SSlider && i == 1 ? RectangleF.Inflate(r.Parts[i], 0, Theme.F(4)) : r.Parts[i];
                    if (pr.Contains(q)) { part = i; break; }
                }
                return true;
            }
            return false;
        }

        private static bool Clickable(SRow r, int part)
        {
            if (r == null || !r.IsEnabled) return false;
            switch (r)
            {
                case SToggle _: return true;                       // ganze Zeile schaltet um
                case SSlider _: return part == 1;
                case SButton _: case SChoice _: case SSwatches _: return part >= 0;
            }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragThumb)
            {
                var t = ThumbRect;
                float track = Height - Theme.F(16) - t.Height;
                if (track > 0) { _scroll = (float)Math.Round(Math.Max(0, Math.Min(MaxScroll, _dragScroll0 + (e.Y - _dragY0) / track * MaxScroll))); Invalidate(); }
                return;
            }
            if (_dragSlider != null) { SlideTo(_dragSlider, e.X, false); return; }
            Hit(e.Location, out var row, out int part);
            if (row != _hoverRow || part != _hoverPart)
            {
                _hoverRow = row; _hoverPart = part;
                Cursor = Clickable(row, part) ? Cursors.Hand : Cursors.Default;
                Invalidate();
                string tip = row is SSwatches sw && part >= 0 ? sw.Names[part] : null;
                if (tip != _tipText) { _tipText = tip; if (tip == null) _tip.Hide(this); else _tip.SetToolTip(this, tip); }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != null && _dragSlider == null) { _hoverRow = null; _hoverPart = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            var t = ThumbRect;
            if (!t.IsEmpty && RectangleF.Inflate(t, Theme.F(4), 0).Contains(e.Location)) { _dragThumb = true; _dragY0 = e.Y; _dragScroll0 = _scroll; Capture = true; return; }
            if (!Hit(e.Location, out var row, out int part) || !Clickable(row, part)) return;
            try
            {
                switch (row)
                {
                    case SToggle tg: tg.Set(!tg.Get()); break;
                    case SChoice c: if (part != c.Get()) c.Set(part); break;
                    case SSwatches s: if (part != s.Get()) s.Set(part); break;
                    case SSlider sl: _dragSlider = sl; Capture = true; SlideTo(sl, e.X, false); break;
                    case SButton b:
                        {
                            var pr = b.Parts[0];
                            var screen = RectangleToScreen(Rectangle.Round(new RectangleF(pr.X, pr.Y - _scroll, pr.Width, pr.Height)));
                            BeginInvoke(new Action(() => b.Click?.Invoke(screen)));
                            break;
                        }
                }
            }
            catch { }
            Relayout();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragThumb) { _dragThumb = false; Capture = false; Invalidate(); }
            if (_dragSlider != null)
            {
                var s = _dragSlider;
                _dragSlider = null; Capture = false;
                try { s.Set(s.Get(), true); } catch { }
                Relayout();
            }
        }

        private void SlideTo(SSlider s, float mouseX, bool final)
        {
            var area = s.Parts[1];
            float x0 = area.X + Theme.F(8), w = area.Width - Theme.F(16);
            float frac = Math.Max(0, Math.Min(1, (mouseX - x0) / Math.Max(1, w)));
            float v = s.Min + frac * (s.Max - s.Min);
            if (s.Step > 0) v = (float)Math.Round(v / s.Step) * s.Step;
            v = Math.Max(s.Min, Math.Min(s.Max, v));
            if (Math.Abs(v - s.Get()) > 1e-4f || final) { try { s.Set(v, final); } catch { } Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float ns = (float)Math.Round(Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta * Theme.F(60) / 120f)));
            if (ns != _scroll) { _scroll = ns; Invalidate(); OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, e.X, e.Y, 0)); }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
