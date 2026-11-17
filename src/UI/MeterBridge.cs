using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Mischpult.UI
{
    /// <summary>
    /// Meterbridge: Leiste ueber dem Pult mit grossen Pegeln – jeder Pegel steht genau ueber seinem Kanal
    /// (folgt beim Scrollen und Verschieben). Mit Nummer, Name, Spitzenwert in dB sowie SIG/PEAK.
    /// Alles Statische liegt in einer Bitmap; pro Bild werden nur die Spalten neu gezeichnet, die sich geaendert haben.
    /// </summary>
    internal sealed partial class MeterBridge : Control
    {
        /// <summary>Hoehe (unskaliert).</summary>
        public const float BaseHeight = 104f;
        /// <summary>3.1: Hoehe der Meterbridge im Pult (logische Pixel): niedrig 80, normal 104, hoch 140.</summary>
        public static float HeightPx = BaseHeight;
        public static readonly float[] HeightValues = { 80f, 104f, 140f };
        public static readonly string[] HeightNames = { "Niedrig", "Normal", "Hoch" };

        /// <summary>Alle Zuege, die angezeigt werden sollen (in beiden Bereichen).</summary>
        public Func<IEnumerable<StripBase>> Strips;
        /// <summary>Rechtsklick: Menue (Bildschirmkoordinaten).</summary>
        public event Action<Point> MenuRequested;
        /// <summary>Klick aufs Spektrum (Ausgang waehlen).</summary>
        public event Action<Point> SpectrumClicked;

        private sealed class Col
        {
            public StripBase Strip;
            public RectangleF Win, Bar1, Bar2, Peak, Vu;
            public Rectangle Clip;
            public PointF Sig, Pk;
            public int Segs;
            public long Live = long.MinValue;
            public long Bars = long.MinValue, Info = long.MinValue;   // schnell: Pegel und Anzeigen getrennt
            public readonly Rectangle[] InfoRects = new Rectangle[3];
        }

        private List<Col> _cols = new List<Col>();
        private string _layoutKey;      // Anordnung der Spalten (aus dem letzten Aufbau)
        private string _cacheKey;       // Anordnung, fuer die die Bitmap gezeichnet ist
        private bool _needBuild = true;
        private readonly PaintCache _cache = new PaintCache();
        // 2.15.3 schnelles Zeichnen: _lit = festes Bild mit allen LEDs leuchtend (daraus wird kopiert),
        // _comp = fertiges Bild der ganzen Leiste – pro Bild wird nur erneuert, was sich geaendert hat, gezeichnet wird mit einem Kopieren.
        private readonly PaintCache _lit = new PaintCache();
        private readonly PaintCache _comp = new PaintCache();
        private readonly PaintCache _info = new PaintCache();   // festes Bild + dB-Wert und SIG/PEAK jeder Spalte (Text nur neu, wenn er sich aendert)
        private bool _litOk, _compValid, _gonioDirty;
        private readonly Rectangle[] _blit = new Rectangle[8];
        private int _blitN;
        private readonly List<Col> _changed = new List<Col>(), _infoChanged = new List<Col>();
        private static bool Fast => StripBase.FastDraw;
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly SolidBrush _brush = new SolidBrush(Color.Black);
        private readonly Dictionary<Control, Point> _origins = new Dictionary<Control, Point>();
        private readonly ToolTip _tip = PultTip.Make(new ToolTip { InitialDelay = 600, ReshowDelay = 200, AutoPopDelay = 6000 });

        // Spektrum: in der groessten freien Luecke der Leiste – ist keine da, ueber dem MASTER (statt seiner Pegel)
        public SpectrumModel Spectrum;
        public bool SpectrumOn;
        public Func<StripBase> SpectrumHost;
        private string _specCaption = "SPEKTRUM";
        /// <summary>Ueberschrift des Spektrums (welcher Ausgang).</summary>
        public string SpectrumCaption
        {
            get => _specCaption;
            set { if (value != _specCaption) { _specCaption = value; _needBuild = true; Invalidate(); } }
        }
        private readonly SpectrumView _specView = new SpectrumView();
        private Col _specCol;
        private bool _specShown;
        private int _specVersion = -1;
        /// <summary>Wird das Spektrum gerade angezeigt?</summary>
        public bool SpectrumShown => _specShown && Visible;

        // Studio-Uhr (rote LED-Uhr mit Sekundenkranz) – in der Luecke rechts neben dem Spektrum, wenn Platz ist
        public bool ClockOn;
        private RectangleF _clockRect;
        private int _clockSec = -1;
        private Bitmap _clockBmp;
        /// <summary>Steht die Studio-Uhr gerade in der Meterbridge? (dann nicht noch einmal in der Kopfleiste)</summary>
        public bool ClockShown => !_clockRect.IsEmpty && Visible;

        public MeterBridge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, !Fast);   // schnell: das fertige Bild wird direkt kopiert (kein Zwischenpuffer noetig)
            BackColor = MixerForm.BodyColor;
            Height = Theme.Px(HeightPx);
            _tip.SetToolTip(this, "Meterbridge: große Pegel aller Kanäle – jeder steht über seinem Kanal.\nRechtsklick: Menü (Pegel ein-/ausblenden, Anzeigen anordnen)");
        }

        /// <summary>Breite der Pegel: 0 schmal, 1 mittel, 2 breit (bis 2.14).</summary>
        public static int BarWidth;
        public static readonly string[] BarWidthNames = { "Schmal", "Mittel", "Breit" };
        private static readonly float[] BarMax = { 7f, 13f, 26f };

        // ---- 2.16
        /// <summary>Anzeige: 0 LED-Saeulen, 1 Zeigerinstrumente (VU) wie an alten Studiopulten.</summary>
        public static int Style;
        private static bool VuStyle => Style == 1;

        // ---- 2.16.2: Meterbridge nach Wunsch
        /// <summary>Pegel, die nicht in der Meterbridge stehen (Schluessel der Zuege, gilt auch fuers eigene Fenster).</summary>
        public static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Ausgeblendete lassen keine Luecke: Kanaele ruecken nach links, Ausgaenge nach rechts – die Mitte wird frei.</summary>
        public static bool Pack = true;
        /// <summary>Reihenfolge der Anzeigen von links: s Spektrum, l Lautheits-Verlauf, g Stereo-Bild, c Uhr.</summary>
        public static string Order = MixerSettings.BridgeOrderDefault;
        /// <summary>Uhr im LCD-Look (sonst rote Studio-Uhr).</summary>
        public static bool LcdClock;
        /// <summary>Zaehlt hoch, wenn sich etwas davon aendert (Spalten neu aufbauen).</summary>
        public static int ConfigVersion;
        public static bool IsHidden(StripBase s) { var k = s?.BridgeKey; return k != null && Hidden.Contains(k); }
        /// <summary>Worauf zuletzt rechts geklickt wurde (fuers Menue): Zug der Spalte bzw. Anzeige ('s', 'l', 'g', 'c').</summary>
        public StripBase MenuStrip { get; private set; }
        public char MenuExtra { get; private set; }
        /// <summary>Eigenes Fenster: Spalten gleichmaessig verteilt (nicht ueber den Zuegen), Anzeigen groesser.</summary>
        public bool FreeLayout;
        /// <summary>Lautheits-Verlauf in der Luecke (wenn Platz).</summary>
        public bool LoudOn;
        public LoudnessHistory Loud;
        private readonly LoudHistView _loudView = new LoudHistView();
        private RectangleF _loudRect;
        private int _loudVersion = -1, _loudMinutes;
        public bool LoudShown => !_loudRect.IsEmpty && Visible;
        /// <summary>Wo das Menue des Verlaufs aufgeht (nur Vorschau).</summary>
        public Point LoudMenuPoint => new Point((int)(_loudRect.X + _loudRect.Width * 0.3f), (int)(_loudRect.Y + _loudRect.Height * 0.5f));
        private string _loudCaption;
        /// <summary>2.16.1: Ueberschrift des Lautheits-Verlaufs (z. B. "LAUTHEIT · HEADSET · 5 MIN"), null = "LAUTHEIT · 5 MIN".</summary>
        public string LoudCaption
        {
            get => _loudCaption;
            set { if (value != _loudCaption) { _loudCaption = value; _needBuild = true; Invalidate(); } }
        }
        /// <summary>Ueberschrift des Goniometers (z. B. "STEREO · ALLE").</summary>
        public string GonioCaption
        {
            get => _gonio.Caption;
            set { if (value != _gonio.Caption) { _gonio.Caption = value; _needBuild = true; Invalidate(); } }
        }
        /// <summary>Klick aufs Goniometer bzw. auf den Lautheits-Verlauf (Menue).</summary>
        public event Action<Point> GonioClicked, LoudClicked;
        /// <summary>2.16.2: Klick auf die Uhr (Sleep-Timer).</summary>
        public event Action<Point> ClockClicked;

        private int SegH { get { Theme.BridgeSegSize(out int h, out _); return h; } }
        private int SegGap { get { Theme.BridgeSegSize(out _, out int gap); return gap; } }

        // Goniometer (Stereo-Bild): in der Luecke neben der Uhr, wenn Platz ist
        public bool GonioOn;
        private readonly GonioView _gonio = new GonioView();
        private RectangleF _gonioRect;
        /// <summary>Steht das Goniometer gerade in der Meterbridge?</summary>
        public bool GonioShown => !_gonioRect.IsEmpty && Visible;

        // ---- 2.17: Oszilloskop und "Jetzt laeuft" mit Album-Cover
        public bool OscOn, CoverOn;
        private readonly OscView _osc = new OscView();
        private readonly CoverView _coverV = new CoverView();
        private RectangleF _oscRect, _coverRect;
        private float _oscW, _coverW;
        private int _oscVer = -1, _coverVer = -1;
        public bool OscShown => !_oscRect.IsEmpty && Visible;
        public bool CoverShown => !_coverRect.IsEmpty && Visible;
        /// <summary>Klick aufs Oszilloskop bzw. aufs Cover (Menue).</summary>
        public event Action<Point> OscClicked, CoverClicked;

        /// <summary>Aus dem UI-Takt: Wellenform fuers Oszilloskop (gleiche Daten wie das Stereo-Bild).</summary>
        public void FeedOsc(float[] lr, float dt)
        {
            if (_oscRect.IsEmpty || !Visible) return;
            _osc.Update(lr, dt);
        }

        /// <summary>Aus dem UI-Takt: was gerade laeuft (fuers Cover).</summary>
        public void SetNow(NowPlayingInfo np) => _coverV.Set(np);

        // ---- 2.17: Spektrum zum Anfassen – unter der Maus Frequenz und Pegel
        public static bool SpecHover = true;
        private int _hoverX = -1;

        private void SetSpecHover(int x)
        {
            if (x == _hoverX) return;
            bool was = _hoverX >= 0;
            _hoverX = x;
            if (_specShown) { var r = Rectangle.Inflate(Rectangle.Ceiling(_specView.Bounds), 2, 2); if (_specCol != null) r = Rectangle.Intersect(r, _specCol.Clip); Invalidate(r); }
            if (!was && x < 0) return;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetSpecHover(-1);
        }

        private void DrawSpecHover(Graphics g)
        {
            if (Spectrum == null || !_specView.HoverInfo(Spectrum, _hoverX, out string text)) return;
            var area = _specView.Area;
            var st = g.Save();
            if (_specCol != null) g.SetClip(_specCol.Clip, CombineMode.Intersect);
            using (var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                g.DrawLine(pen, _hoverX + 0.5f, area.Y, _hoverX + 0.5f, area.Bottom);
            var font = Theme.Ui(8.5f, FontStyle.Bold);
            float tw = Theme.TextWidth(g, text, font) + Theme.F(12), th = Theme.F(16);
            float bx = _hoverX + Theme.F(8);
            if (bx + tw > area.Right) bx = _hoverX - Theme.F(8) - tw;
            var box = new RectangleF(bx, area.Y + Theme.F(2), tw, th);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.FromArgb(225, 0x0b, 0x0c, 0x0f), box, Theme.F(3));
            Theme.StrokeRound(g, Color.FromArgb(0x3a, 0x40, 0x4a), Theme.F(1), box, Theme.F(3));
            using (var b = new SolidBrush(Color.FromArgb(0xf2, 0xf4, 0xf6))) Theme.Str(g, text, font, b, box, Theme.Center);
            g.SmoothingMode = sm;
            g.Restore(st);
        }

        /// <summary>Nur Vorschau/Test: Maus ueber dem Spektrum an der Stelle x (Anteil 0..1 der Balken).</summary>
        public void SpecHoverDemo(float frac)
        {
            if (!_specShown) return;
            var a = _specView.Area;
            SetSpecHover((int)(a.X + a.Width * Math.Max(0f, Math.Min(1f, frac))));
        }

        /// <summary>Aus dem UI-Takt: neue Stereo-Werte fuers Goniometer.</summary>
        public void FeedGonio(float[] lr, float corr, float dt)
        {
            if (_gonioRect.IsEmpty || !Visible) return;
            if (_gonio.Update(lr, corr, dt)) { if (Fast) _gonioDirty = true; else Invalidate(_gonio.LiveBounds); }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); _needBuild = true; }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); _needBuild = true; }

        // --------------------------------------------------------------- Aufbau

        /// <summary>Spalten aus den aktuellen Positionen der Zuege berechnen (inkl. Schluessel fuer den Bild-Cache).</summary>
        private void BuildColumns()
        {
            _needBuild = false;
            var old = new Dictionary<StripBase, long>();
            foreach (var c in _cols) old[c.Strip] = c.Live;
            var cols = new List<Col>();
            _items.Clear();
            _spacerRect = RectangleF.Empty;
            _sb.Clear();
            _sb.Append(Width).Append('x').Append(Height).Append('|').Append(Theme.Style).Append('|').Append(Theme.Body.ToArgb())
               .Append('|').Append(Theme.Accent.ToArgb()).Append('|').Append(StripBase.ShowNumbers).Append('|').Append(Theme.S)
               .Append('|').Append(StripBase.SignalLeds).Append(StripBase.PeakLeds).Append('|').Append(BarWidth)
               .Append('|').Append(Theme.LedShape).Append(Theme.LedSize).Append('|').Append(Theme.BgVersion).Append('|').Append(Theme.PanelMaterial)
               .Append('|').Append(Style).Append(FreeLayout).Append(SpectrumView.Waterfall).Append(SpectrumView.ConfigKey).Append('|').Append(Theme.Design).Append('|').Append(Arrange);
            _origins.Clear();
            _specCol = null; _specShown = false; _clockRect = RectangleF.Empty; _gonioRect = RectangleF.Empty; _loudRect = RectangleF.Empty;
            _oscRect = RectangleF.Empty; _coverRect = RectangleF.Empty; _pcRect = RectangleF.Empty; _tubeRect = RectangleF.Empty; _radarRect = RectangleF.Empty;
            bool wantSpec = SpectrumOn && Spectrum != null;
            bool wantLoud = LoudOn && Loud != null;
            // Groessen der Anzeigen
            float pad = Theme.F(6);
            _clockW = LcdClock || (Theme.ClockKind >= 3 && Theme.ClockKind != 8)   // 4.0 finale: Oszilloskop-Uhr ist rund
                ? (float)Math.Round(Math.Max(Theme.F(120), Math.Min(Theme.F(FreeLayout ? 280 : 168), (Height - Theme.F(10)) * 1.8f)))
                : (float)Math.Round(Math.Min(Height - Theme.F(10), Theme.F(FreeLayout ? 170 : 108)));
            _gonioW = (float)Math.Round(Math.Min(Height - Theme.F(4), Theme.F(FreeLayout ? 220 : 112)));
            _loudW = (float)Math.Round(Math.Max(Theme.F(170), Math.Min(Theme.F(FreeLayout ? 320 : 210), (Height - Theme.F(10)) * (FreeLayout ? 1.6f : 2.2f))));
            _oscW = (float)Math.Round(Math.Max(Theme.F(140), Math.Min(Theme.F(FreeLayout ? 300 : 170), (Height - Theme.F(10)) * 1.8f)));
            _coverW = (float)Math.Round(Math.Max(Theme.F(170), Math.Min(Theme.F(FreeLayout ? 380 : 220), (Height - Theme.F(10)) * 2.3f)));
            _pcW = (float)Math.Round(Math.Max(Theme.F(150), Math.Min(Theme.F(FreeLayout ? 300 : 190), (Height - Theme.F(10)) * 1.9f)));   // 3.4
            _tubeW = (float)Math.Round(Math.Max(Theme.F(110), Math.Min(Theme.F(170), (Height - Theme.F(10)) * 1.3f)));   // 3.5
            _radarW = (float)Math.Round(Math.Max(Theme.F(60), Height - Theme.F(8)));   // 4.0: Radar (rund)
            RectangleF sr = RectangleF.Empty;
            bool showSpec = false;
            var list = new List<StripBase>();
            if (Strips != null && IsHandleCreated)
                foreach (var s in Strips())
                    if (s != null && !s.IsDisposed && (FreeLayout || (s.Visible && s.Parent != null))) list.Add(s);
            _candidates = list;
            int shownN = list.Count(s => !IsHidden(s));
            float colW = 0f;
            if (FreeLayout && shownN > 0)
            {
                bool extras = SpectrumOn || GonioOn || ClockOn || LoudOn;
                float avail = Width - Theme.F(12);
                colW = Math.Min(Theme.F(130), Math.Max(Theme.F(50), avail * (extras ? 0.45f : 1f) / shownN));
                if (colW * shownN > avail) colW = Math.Max(Theme.F(40), avail / shownN);
            }
            if ((Arrange || StripHost.AnyTwoRows) && IsHandleCreated)   // 4.0: zwei Reihen – Pegel nebeneinander statt ueber dem Kanal
            {
                // 2.16.3: frei angeordnet – alles in der eigenen Reihenfolge von links nach rechts
                showSpec = BuildArranged(list, colW, cols, old, wantSpec, wantLoud, ref sr);
            }
            else if (IsHandleCreated)
            {
                var pos = FreeLayout ? null : StripPositions(list);
                int idx = 0;
                foreach (var s in list)
                {
                    if (IsHidden(s)) continue;
                    Rectangle r, clip;
                    if (FreeLayout)
                    {
                        int x0 = (int)Math.Round(Theme.F(6) + idx * colW), x1 = (int)Math.Round(Theme.F(6) + (idx + 1) * colW);
                        idx++;
                        r = new Rectangle(x0, 0, x1 - x0, Height);
                        clip = r;
                    }
                    else
                    {
                        if (!pos.TryGetValue(s, out r)) continue;
                        var host = s.Parent;
                        var o = _origins[host];
                        clip = Rectangle.Intersect(new Rectangle(o.X, 0, host.ClientSize.Width, Height), new Rectangle(r.X, 0, r.Width, Height));
                    }
                    AddCol(cols, s, r, clip, old);
                }
                // Anzeigen: groesste freie Luecke zwischen den Spalten (z. B. zwischen Kanaelen und Ausgaengen)
                if (wantSpec || ClockOn || GonioOn || wantLoud || OscOn || CoverOn || PcOn || TubesOn || RadarOn)
                {
                    float left = Theme.F(8), right = Width - Theme.F(8), cur = left, best = 0f, bx = 0f;
                    foreach (var cl in cols.Select(c => c.Clip).OrderBy(c => c.X))
                    {
                        if (cl.X - cur > best) { best = cl.X - cur; bx = cur; }
                        cur = Math.Max(cur, cl.Right);
                    }
                    if (right - cur > best) { best = right - cur; bx = cur; }
                    // welche Anzeigen passen (Vorrang: Uhr, Stereo-Bild, Lautheit – das Spektrum bekommt den Rest),
                    // dann von links nach rechts in der gewaehlten Reihenfolge
                    float specMin = wantSpec ? Theme.F(150) : 0f;
                    float rem = best;
                    bool showClock = ClockOn && rem >= _clockW + pad + specMin;
                    if (showClock) rem -= _clockW + pad;
                    bool showGonio = GonioOn && rem >= _gonioW + pad + specMin;
                    if (showGonio) rem -= _gonioW + pad;
                    bool showLoud = wantLoud && rem >= _loudW + pad + specMin;
                    if (showLoud) rem -= _loudW + pad;
                    bool showOsc = OscOn && rem >= _oscW + pad + specMin;
                    if (showOsc) rem -= _oscW + pad;
                    bool showCover = CoverOn && rem >= _coverW + pad + specMin;
                    if (showCover) rem -= _coverW + pad;
                    bool showPc = PcOn && rem >= _pcW + pad + specMin;   // 3.4: PC-Auslastung (rechts in der Luecke)
                    if (showPc) rem -= _pcW + pad;
                    bool showTubes = TubesOn && rem >= _tubeW + pad + specMin;   // 3.5: Roehren
                    if (showTubes) rem -= _tubeW + pad;
                    bool showRadar = RadarOn && rem >= _radarW + pad + specMin;   // 4.0: Radar
                    if (showRadar) rem -= _radarW + pad;
                    showSpec = wantSpec && rem >= Theme.F(150);
                    float x = showSpec ? bx : bx + rem;   // ohne Spektrum stehen die Anzeigen rechts in der Luecke
                    if (!showSpec && rem > Theme.F(10)) AddItem("#_", _spacerRect = new RectangleF(bx, Theme.F(5), rem, Height - Theme.F(10)));
                    string order = MixerSettings.FullBridgeOrder(Order);
                    _sb.Append("|order:").Append(order);
                    foreach (char k in order)
                    {
                        if (k == 's' && showSpec) { sr = SlotRect(x, rem); AddItem("#s", new RectangleF(x, sr.Y, rem, sr.Height)); x += rem; }
                        else if (k == 'c' && showClock) { PlaceExtra('c', x); x += _clockW + pad; }
                        else if (k == 'g' && showGonio) { PlaceExtra('g', x); x += _gonioW + pad; }
                        else if (k == 'l' && showLoud) { PlaceExtra('l', x); x += _loudW + pad; }
                        else if (k == 'o' && showOsc) { PlaceExtra('o', x); x += _oscW + pad; }
                        else if (k == 'a' && showCover) { PlaceExtra('a', x); x += _coverW + pad; }
                    }
                    if (showPc) { PlacePc(x); x += _pcW + pad; }
                    if (showTubes) { PlaceTubes(x); x += _tubeW + pad; }
                    if (showRadar) { PlaceRadar(x); x += _radarW + pad; }
                }
            }
            if (wantSpec && !showSpec && IsHandleCreated)
            {
                // kein Platz: ueber dem MASTER bzw. Standard-Ausgang (statt seiner Pegel)
                var host = SpectrumHost?.Invoke();
                var hc = host == null ? null : cols.FirstOrDefault(c => c.Strip == host);
                if (hc != null) { _specCol = hc; sr = hc.Win; }
            }
            if (!sr.IsEmpty)
            {
                _specShown = true;
                _specView.Caption = _specCaption;
                _specView.Layout(sr);
                _sb.Append("|spec:").Append(sr.X).Append(',').Append(sr.Y).Append(',').Append(sr.Width).Append(',').Append(sr.Height).Append(',').Append(_specCaption);
            }
            _items.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));
            _cols = cols;
            _layoutKey = _sb.ToString();
        }

        // ---- Teile der Anordnung

        private sealed class Item
        {
            public string Key;
            public RectangleF Rect;   // ganzer Platz (fuers Verschieben)
        }

        private readonly List<Item> _items = new List<Item>();
        private List<StripBase> _candidates = new List<StripBase>();
        private RectangleF _spacerRect;
        private float _clockW, _gonioW, _loudW;

        private void AddItem(string key, RectangleF r) { if (key != null && r.Width > 0) _items.Add(new Item { Key = key, Rect = r }); }

        /// <summary>Anzeige in einem Platz ab x (Breite w + 6): 3 px Rand links.</summary>
        private RectangleF SlotRect(float x, float slotW) =>
            new RectangleF((float)Math.Round(x + Theme.F(3)), (float)Math.Round(Theme.F(5)), (float)Math.Round(slotW - Theme.F(6)), (float)Math.Round(Height - Theme.F(10)));

        private float ExtraW(char k) => k == 'c' ? _clockW : k == 'g' ? _gonioW : k == 'o' ? _oscW : k == 'a' ? _coverW : _loudW;

        /// <summary>Uhr ('c'), Stereo-Bild ('g'), Lautheits-Verlauf ('l'), Oszilloskop ('o') oder Cover ('a') ab x setzen.</summary>
        private void PlaceExtra(char k, float x)
        {
            float pad = Theme.F(6), top = (float)Math.Round(Theme.F(5)), hh = (float)Math.Round(Height - Theme.F(10));
            float w = ExtraW(k);
            var r = new RectangleF((float)Math.Round(x + Theme.F(3)), top, w, hh);
            AddItem("#" + k, new RectangleF(x, top, w + pad, hh));
            if (k == 'c')
            {
                _clockRect = r;
                _sb.Append("|clock:").Append(r.X).Append(',').Append(r.Width).Append(LcdClock ? 'L' : 'S').Append(Theme.ClockKind);
            }
            else if (k == 'g')
            {
                _gonioRect = r;
                _gonio.Layout(r);
                _sb.Append("|gonio:").Append(r.X).Append(',').Append(r.Width);
            }
            else if (k == 'o')
            {
                _oscRect = r;
                _osc.Layout(r);
                _sb.Append("|osc:").Append(r.X).Append(',').Append(r.Width);
            }
            else if (k == 'a')
            {
                _coverRect = r;
                _coverV.Layout(r);
                _sb.Append("|cover:").Append(r.X).Append(',').Append(r.Width);
            }
            else
            {
                _loudRect = r;
                _loudView.Layout(r);
                _loudMinutes = Loud.Minutes;
                _sb.Append("|loud:").Append(r.X).Append(',').Append(r.Width).Append(',').Append(_loudMinutes).Append(',').Append(_loudCaption);
            }
        }

        /// <summary>Spalte eines Zugs im Platz sr anlegen (clip = sichtbarer Teil).</summary>
        private void AddCol(List<Col> cols, StripBase s, Rectangle sr, Rectangle clip, Dictionary<StripBase, long> old)
        {
            if (clip.Width <= 2) return;
            float H = Height;
            var c = new Col { Strip = s, Clip = clip };
            if (old.TryGetValue(s, out long lv)) c.Live = lv;
            float x = sr.X + Theme.F(3), w = sr.Width - Theme.F(6);
            c.Win = new RectangleF((float)Math.Round(x), (float)Math.Round(Theme.F(5)), (float)Math.Round(w), (float)Math.Round(H - Theme.F(10)));
            // Pegel: zwei breite Balken, links davon die Skala
            float mTop = c.Win.Y + Theme.F(23), mBot = c.Win.Bottom - Theme.F(21);
            int step = SegH + SegGap;
            c.Segs = Math.Max(6, (int)((mBot - mTop + SegGap) / step));
            float mh = c.Segs * step - SegGap;
            float scaleW = Theme.F(16), gap = Theme.F(BarWidth == 0 ? 2 : 3);
            float barW = (float)Math.Max(2, Math.Floor(Math.Min(Theme.F(BarMax[Math.Max(0, Math.Min(2, BarWidth))]), (c.Win.Width - scaleW - gap - Theme.F(12)) / 2f)));
            float total = scaleW + barW * 2 + gap;
            float bx = (float)Math.Round(c.Win.X + (c.Win.Width - total) / 2f + scaleW);
            float by = (float)Math.Round(mBot - mh);
            c.Bar1 = new RectangleF(bx, by, barW, mh);
            c.Bar2 = new RectangleF(bx + barW + (float)Math.Round(gap), by, barW, mh);
            // unten: Spitzenwert, links SIG, rechts PEAK
            float pw = Theme.F(34), ph = Theme.F(14);
            float mid = (float)Math.Round((c.Bar1.X + c.Bar2.Right) / 2f);
            c.Peak = new RectangleF((float)Math.Round(mid - pw / 2), (float)Math.Round(c.Win.Bottom - Theme.F(18)), (float)Math.Round(pw), (float)Math.Round(ph));
            c.Sig = new PointF(c.Peak.X - Theme.F(11), c.Peak.Y + c.Peak.Height / 2);
            c.Pk = new PointF(c.Peak.Right + Theme.F(11), c.Peak.Y + c.Peak.Height / 2);
            if (VuStyle)
            {
                // Zeigerinstrument zwischen Name und dB-Wert, Seitenverhaeltnis wie ein echtes VU-Meter
                float vt = c.Win.Y + Theme.F(22), vb = c.Win.Bottom - Theme.F(22);
                float vw = Math.Min(c.Win.Width - Theme.F(10), (vb - vt) * 1.9f), vh = Math.Min(vb - vt, vw / 1.6f);
                c.Vu = new RectangleF((float)Math.Round(c.Win.X + (c.Win.Width - vw) / 2f), (float)Math.Round(vt + (vb - vt - vh) / 2f), (float)Math.Round(vw), (float)Math.Round(vh));
            }
            cols.Add(c);
            AddItem(s.BridgeKey, RectangleF.Intersect(new RectangleF(sr.X, c.Win.Y, sr.Width, c.Win.Height), clip));
            _sb.Append('|').Append(sr.X).Append(',').Append(sr.Width).Append(',').Append(clip.X).Append(',').Append(clip.Width)
               .Append(',').Append(s.Number).Append(',').Append(s.DisplayName).Append(',').Append(s.DisplayColor.ToArgb())
               .Append(',').Append(s.IsDimmed).Append(',').Append(s.IsHot);
        }

        // ---- 2.16.3: frei anordnen

        /// <summary>Frei angeordnet: eigene Reihenfolge statt "jeder Pegel ueber seinem Kanal".</summary>
        public static bool Arrange;
        /// <summary>Reihenfolge (Schluessel der Zuege, "#s" Spektrum, "#l" Lautheit, "#g" Stereo-Bild, "#c" Uhr, "#_" freier Platz).</summary>
        public static List<string> FreeOrder;
        /// <summary>Pegel und Anzeigen mit der Maus verschieben.</summary>
        public static bool DragOn = true;
        /// <summary>Neue Reihenfolge (nach Verschieben mit der Maus bzw. nach links/rechts im Menue).</summary>
        public event Action<List<string>> OrderChanged;

        public const string SpacerKey = "#_";
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        /// <summary>Wie bisher: Kanaele, freier Platz, Anzeigen (in der gewaehlten Reihenfolge), Ausgaenge.</summary>
        internal static List<string> DefaultOrder(IEnumerable<StripBase> list)
        {
            var res = new List<string>();
            var all = list.Where(s => s.BridgeKey != null).ToList();
            foreach (var s in all) if (!(s.Parent != null && s.Parent.Dock == DockStyle.Right)) res.Add(s.BridgeKey);
            res.Add(SpacerKey);
            foreach (char k in MixerSettings.FullBridgeOrder(Order)) res.Add("#" + k);
            foreach (var s in all) if (s.Parent != null && s.Parent.Dock == DockStyle.Right) res.Add(s.BridgeKey);
            return res;
        }

        /// <summary>
        /// Gespeicherte Reihenfolge mit dem, was es gerade gibt, zusammenfuehren: Unbekanntes faellt weg,
        /// Neues kommt hinter seinen Vorgaenger aus der Standard-Reihenfolge (neue Ausgaenge vor den naechsten Ausgang bzw. ans Ende).
        /// </summary>
        internal static List<string> MergeOrder(IList<string> saved, IList<string> defaults)
        {
            var known = new HashSet<string>(defaults, Cmp);
            var res = new List<string>();
            var have = new HashSet<string>(Cmp);
            if (saved != null) foreach (var k in saved) if (k != null && known.Contains(k) && have.Add(k)) res.Add(k);
            // rechts der Anzeigen stehen die Ausgaenge: Neues dort vor den naechsten Ausgang (sonst ans Ende)
            int lastExtra = -1;
            for (int i = 0; i < defaults.Count; i++) if (defaults[i].StartsWith("#")) lastExtra = i;
            for (int i = 0; i < defaults.Count; i++)
            {
                var d = defaults[i];
                if (have.Contains(d)) continue;
                int at = -1;
                if (lastExtra >= 0 && i > lastExtra)
                {
                    for (int j = i + 1; j < defaults.Count && at < 0; j++) { int p = res.FindIndex(x => Cmp.Equals(x, defaults[j])); if (p >= 0) at = p; }
                    for (int j = i - 1; j > lastExtra && at < 0; j--) { int p = res.FindIndex(x => Cmp.Equals(x, defaults[j])); if (p >= 0) at = p + 1; }
                    if (at < 0) at = res.Count;
                }
                else
                {
                    for (int j = i - 1; j >= 0 && at < 0; j--) { int p = res.FindIndex(x => Cmp.Equals(x, defaults[j])); if (p >= 0) at = p + 1; }
                    if (at < 0) at = 0;
                }
                res.Insert(at, d);
                have.Add(d);
            }
            return res;
        }

        /// <summary>Was in der alten Reihenfolge stand, gerade aber fehlt (z. B. Geraet abgesteckt), behaelt seinen Platz.</summary>
        internal static List<string> KeepMissing(List<string> now, IList<string> before)
        {
            if (before == null) return now;
            var res = new List<string>(now);
            for (int i = 0; i < before.Count; i++)
            {
                var k = before[i];
                if (k == null || res.Any(x => Cmp.Equals(x, k))) continue;
                int at = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int p = res.FindIndex(x => Cmp.Equals(x, before[j]));
                    if (p >= 0) { at = p + 1; break; }
                }
                res.Insert(at, k);
            }
            return res;
        }

        /// <summary>Die ganze Reihenfolge so, wie die Leiste gerade aussieht (auch im normalen Modus).</summary>
        public List<string> CurrentOrder()
        {
            if (_needBuild || _layoutKey == null) BuildColumns();
            var defs = DefaultOrder(_candidates);
            if (Arrange) return MergeOrder(FreeOrder, defs);
            var vis = _items.OrderBy(i => i.Rect.X).Select(i => i.Key).ToList();
            // Ausgeblendete und gerade nicht gezeigte an ihren Standard-Platz
            return MergeOrder(vis, defs);
        }

        /// <summary>Frei angeordnet: Platz verteilen und alles von links nach rechts setzen. true = Spektrum steht.</summary>
        private bool BuildArranged(List<StripBase> list, float colW, List<Col> cols, Dictionary<StripBase, long> old, bool wantSpec, bool wantLoud, ref RectangleF specRect)
        {
            var order = MergeOrder(FreeOrder, DefaultOrder(list));
            var byKey = new Dictionary<string, StripBase>(Cmp);
            foreach (var s in list) if (s.BridgeKey != null && !byKey.ContainsKey(s.BridgeKey)) byKey[s.BridgeKey] = s;
            float pad = Theme.F(6), margin = Theme.F(FreeLayout ? 6 : 8);
            float avail = Width - 2 * margin;
            var seq = new List<string>();
            float stripsW = 0f;
            foreach (var k in order)
            {
                if (k.StartsWith("#")) { seq.Add(k); continue; }
                if (byKey.TryGetValue(k, out var s) && !IsHidden(s)) { seq.Add(k); stripsW += FreeLayout ? colW : s.Width; }
            }
            bool c = ClockOn, g = GonioOn, l = wantLoud, o = OscOn, a = CoverOn, pc = PcOn, tu = TubesOn, ra = RadarOn;
            Func<float> rest = () => avail - stripsW - (c ? _clockW + pad : 0f) - (g ? _gonioW + pad : 0f) - (l ? _loudW + pad : 0f)
                                     - (o ? _oscW + pad : 0f) - (a ? _coverW + pad : 0f) - (pc ? _pcW + pad : 0f) - (tu ? _tubeW + pad : 0f) - (ra ? _radarW + pad : 0f);
            float need = wantSpec ? Theme.F(150) : 0f;
            // zu wenig Platz: zuerst Roehren, Cover, Oszilloskop, Lautheit, dann das Stereo-Bild, dann die Uhr weglassen
            if (rest() < need) ra = false;
            if (rest() < need) tu = false;
            if (rest() < need) pc = false;
            if (rest() < need) a = false;
            if (rest() < need) o = false;
            if (rest() < need) l = false;
            if (rest() < need) g = false;
            if (rest() < need) c = false;
            bool sp = wantSpec && rest() >= Theme.F(150);
            float leftover = Math.Max(0f, rest());
            float x = margin;
            _sb.Append("|arr:").Append(string.Join(",", seq));
            foreach (var k in seq)
            {
                switch (k)
                {
                    case "#s":
                        if (sp) { specRect = SlotRect(x, leftover); AddItem(k, new RectangleF(x, specRect.Y, leftover, specRect.Height)); x += leftover; }
                        break;
                    case SpacerKey:
                        if (!sp && leftover > 0f)
                        {
                            _spacerRect = new RectangleF(x, Theme.F(5), leftover, Height - Theme.F(10));
                            if (leftover > Theme.F(10)) AddItem(k, _spacerRect);
                            x += leftover;
                        }
                        break;
                    case "#c": if (c) { PlaceExtra('c', x); x += _clockW + pad; } break;
                    case "#g": if (g) { PlaceExtra('g', x); x += _gonioW + pad; } break;
                    case "#l": if (l) { PlaceExtra('l', x); x += _loudW + pad; } break;
                    case "#o": if (o) { PlaceExtra('o', x); x += _oscW + pad; } break;
                    case "#a": if (a) { PlaceExtra('a', x); x += _coverW + pad; } if (pc) { PlacePc(x); x += _pcW + pad; pc = false; } break;
                    default:
                        if (byKey.TryGetValue(k, out var s))
                        {
                            float w = FreeLayout ? colW : s.Width;
                            var r = new Rectangle((int)Math.Round(x), 0, (int)Math.Round(x + w) - (int)Math.Round(x), Height);
                            AddCol(cols, s, r, Rectangle.Intersect(r, new Rectangle(0, 0, Width, Height)), old);
                            x += w;
                        }
                        break;
                }
            }
            if (pc) { PlacePc(x); x += _pcW + pad; }   // 3.4: kein Platz fuer das Cover vorgesehen – PC-Auslastung ans Ende
            if (tu) { PlaceTubes(x); x += _tubeW + pad; }   // 3.5
            if (ra) PlaceRadar(x);   // 4.0
            return sp;
        }

        /// <summary>Neue Reihenfolge, wenn ein Teil um einen Platz nach links (-1) bzw. rechts (+1) rueckt – null = geht nicht.</summary>
        public List<string> OrderWithMove(string key, int dir)
        {
            var items = _items.OrderBy(i => i.Rect.X).ToList();
            int i0 = items.FindIndex(i => Cmp.Equals(i.Key, key));
            if (i0 < 0) return null;
            int to = i0 + Math.Sign(dir);
            if (to < 0 || to >= items.Count) return null;
            return OrderWithDrop(key, to);   // vor den linken bzw. hinter den rechten Nachbarn
        }

        /// <summary>Neue Reihenfolge: key an Platz idx unter den uebrigen sichtbaren Teilen.</summary>
        private List<string> OrderWithDrop(string key, int idx)
        {
            var others = _items.Where(i => !Cmp.Equals(i.Key, key)).OrderBy(i => i.Rect.X).ToList();
            var full = CurrentOrder();
            full.RemoveAll(k => Cmp.Equals(k, key));
            int at;
            if (others.Count == 0) at = full.Count;
            else if (idx < others.Count) at = full.FindIndex(k => Cmp.Equals(k, others[idx].Key));
            else { int p = full.FindIndex(k => Cmp.Equals(k, others[others.Count - 1].Key)); at = p < 0 ? full.Count : p + 1; }
            if (at < 0) at = full.Count;
            full.Insert(at, key);
            return full;
        }

        public bool CanMove(string key, int dir)
        {
            var items = _items.OrderBy(i => i.Rect.X).ToList();
            int i0 = items.FindIndex(i => Cmp.Equals(i.Key, key));
            int to = i0 + Math.Sign(dir);
            return i0 >= 0 && to >= 0 && to < items.Count;
        }

        public void MoveItem(string key, int dir)
        {
            var o = OrderWithMove(key, dir);
            if (o != null) OrderChanged?.Invoke(o);
        }

        // ---- Verschieben mit der Maus

        private string _dragKey;
        private Point _dragFrom;
        private bool _dragging;
        private int _dropIdx, _dragX, _dragOrig;
        private RectangleF _dragRect;

        /// <summary>Wird gerade etwas verschoben?</summary>
        public bool Dragging => _dragging;

        private List<Item> DragOthers() => _items.Where(i => !Cmp.Equals(i.Key, _dragKey)).OrderBy(i => i.Rect.X).ToList();

        private int DropIndex(int x) => DragOthers().Count(i => i.Rect.X + i.Rect.Width / 2f < x);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragKey = null;
            if (e.Button != MouseButtons.Left || !DragOn) return;
            var it = _items.FirstOrDefault(i => i.Rect.Contains(e.Location));
            if (it == null) return;
            _dragKey = it.Key;
            _dragFrom = e.Location;
            _dragRect = it.Rect;
            _dragOrig = _items.OrderBy(i => i.Rect.X).ToList().FindIndex(i => i == it);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_dragging && !Capture) CancelDrag();
        }

        private void CancelDrag()
        {
            bool was = _dragging;
            _dragging = false;
            _dragKey = null;
            if (was) { Cursor = Cursors.Default; Invalidate(); }
        }

        /// <summary>Name fuer die Anzeige beim Verschieben.</summary>
        private string ItemLabel(string key)
        {
            switch (key)
            {
                case "#s": return "SPEKTRUM";
                case "#l": return "LAUTHEIT";
                case "#g": return "STEREO";
                case "#c": return "UHR";
                case "#o": return "OSZILLOSKOP";
                case "#a": return "COVER";
                case SpacerKey: return "FREIER PLATZ";
            }
            var s = _candidates.FirstOrDefault(x => Cmp.Equals(x.BridgeKey, key));
            return s?.DisplayName ?? key;
        }

        /// <summary>Beim Verschieben: alter Platz abgedunkelt, das Teil am Mauszeiger, die Einfuegestelle als Linie.</summary>
        private void DrawDragOverlay(Graphics g)
        {
            var others = DragOthers();
            float lineX = others.Count == 0 ? _dragX
                : _dropIdx <= 0 ? others[0].Rect.X
                : _dropIdx >= others.Count ? others[others.Count - 1].Rect.Right
                : (others[_dropIdx - 1].Rect.Right + others[_dropIdx].Rect.X) / 2f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) g.FillRectangle(b, _dragRect);
            float gw = Math.Max(Theme.F(70), Math.Min(_dragRect.Width, Theme.F(200)));
            var gr = new RectangleF(_dragX - gw / 2f, _dragRect.Y + Theme.F(6), gw, _dragRect.Height - Theme.F(12));
            Theme.FillRound(g, Color.FromArgb(70, Theme.Accent), gr, Theme.F(5));
            Theme.StrokeRound(g, Theme.Accent, Theme.F(1.5f), gr, Theme.F(5));
            using (var b = new SolidBrush(Color.White))
                Theme.Str(g, ItemLabel(_dragKey), Theme.Ui(10f, FontStyle.Bold), b, gr, Theme.Center);
            float t = Theme.F(3);
            using (var pen = new Pen(Theme.Accent, t) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, lineX, Theme.F(7), lineX, Height - Theme.F(7));
            using (var b = new SolidBrush(Theme.Accent))
            {
                float a = Theme.F(5);
                g.FillPolygon(b, new[] { new PointF(lineX - a, Theme.F(3)), new PointF(lineX + a, Theme.F(3)), new PointF(lineX, Theme.F(3) + a) });
                g.FillPolygon(b, new[] { new PointF(lineX - a, Height - Theme.F(3)), new PointF(lineX + a, Height - Theme.F(3)), new PointF(lineX, Height - Theme.F(3) - a) });
            }
            g.SmoothingMode = sm;
        }

        /// <summary>Nur Vorschau/Test: Verschieben von einer Stelle zur anderen (wie mit der Maus).</summary>
        public void DragDemo(Point from, Point to, bool drop)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
            if (_dragKey == null) return;
            _dragging = true;
            _dragX = to.X;
            _dropIdx = DropIndex(to.X);
            Invalidate();
            if (drop) FinishDrag();
        }

        private void FinishDrag()
        {
            string key = _dragKey;
            int idx = _dropIdx;
            bool moved = idx != _dragOrig;
            CancelDrag();
            if (!moved || key == null) return;
            OrderChanged?.Invoke(OrderWithDrop(key, idx));
        }

        /// <summary>
        /// Wo die Spalten stehen (Koordinaten der Leiste): ueber ihrem Zug – oder (2.16.2, Zusammenruecken) ohne die Luecken
        /// der ausgeblendeten: im linken Bereich ruecken die Pegel nach links, im rechten (Ausgaenge) nach rechts.
        /// </summary>
        private Dictionary<StripBase, Rectangle> StripPositions(List<StripBase> list)
        {
            var pos = new Dictionary<StripBase, Rectangle>();
            int i = 0;
            while (i < list.Count)
            {
                var host = list[i].Parent;
                int j = i;
                while (j < list.Count && list[j].Parent == host) j++;
                if (!_origins.TryGetValue(host, out var o))
                {
                    try { o = PointToClient(host.PointToScreen(Point.Empty)); } catch { i = j; continue; }
                    _origins[host] = o;
                }
                int n = j - i;
                var rects = new Rectangle[n];
                var hid = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    var s = list[i + k];
                    rects[k] = new Rectangle(o.X + s.Left, o.Y + s.Top, s.Width, s.Height);
                    hid[k] = IsHidden(s);
                }
                var packed = Pack ? PackRow(rects, hid, host.Dock == DockStyle.Right) : rects;
                for (int k = 0; k < n; k++) if (!hid[k]) pos[list[i + k]] = packed[k];
                i = j;
            }
            return pos;
        }

        /// <summary>
        /// Eine Reihe Zuege ohne die Luecken der ausgeblendeten: links buendig (Kanaele) bzw. rechts buendig (Ausgaenge).
        /// Abstaende zwischen den Zuegen bleiben, Ausgeblendete behalten ihren Platz (werden nicht gezeichnet).
        /// </summary>
        internal static Rectangle[] PackRow(Rectangle[] rects, bool[] hidden, bool right)
        {
            int n = rects.Length;
            var r = (Rectangle[])rects.Clone();
            if (n == 0 || !hidden.Any(h => h)) return r;
            if (!right)
            {
                int cur = rects[0].X;
                for (int k = 0; k < n; k++)
                {
                    if (hidden[k]) continue;
                    int gapAfter = k + 1 < n ? Math.Max(0, rects[k + 1].X - rects[k].Right) : 0;
                    r[k] = new Rectangle(cur, rects[k].Y, rects[k].Width, rects[k].Height);
                    cur += rects[k].Width + gapAfter;
                }
            }
            else
            {
                int cur = rects[n - 1].Right;
                for (int k = n - 1; k >= 0; k--)
                {
                    if (hidden[k]) continue;
                    int gapBefore = k > 0 ? Math.Max(0, rects[k].X - rects[k - 1].Right) : 0;
                    r[k] = new Rectangle(cur - rects[k].Width, rects[k].Y, rects[k].Width, rects[k].Height);
                    cur -= rects[k].Width + gapBefore;
                }
            }
            return r;
        }

        /// <summary>Aussehen geaendert (Farben, Schrift): festen Teil neu zeichnen.</summary>
        public void Restyle()
        {
            _cacheKey = null;
            _compValid = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, !Fast);
            Invalidate();
        }

        /// <summary>Aus dem UI-Timer: nur die Spalten neu zeichnen, deren Anzeige sich geaendert hat.</summary>
        public void Tick()
        {
            if (!Visible || !IsHandleCreated) return;
            string before = _layoutKey;
            // Spalten nur neu berechnen, wenn sich etwas verschoben/geaendert hat (billiger Fingerabdruck statt jedes Bild alles)
            long sig = Signature();
            if (_needBuild || _layoutKey == null || sig != _sig) { _sig = sig; BuildColumns(); }
            if (!_coverRect.IsEmpty) _coverV.Animate();   // 3.3: Schallplatte/Bandmaschine drehen
            if (!_pcRect.IsEmpty) _pcV.Animate(PcDt());     // 3.4: PC-Auslastung
            if (!_tubeRect.IsEmpty) _tubeV.Animate(TubeDt());   // 3.5: Roehren
            if (!_radarRect.IsEmpty) _radarV.Animate(RadarDt());   // 4.0: Radar
            if (_duckVerShown != DuckVer) { _duckVerShown = DuckVer; Invalidate(); }   // 3.3: Ducking-Linie
            bool all = _layoutKey != before || _layoutKey != _cacheKey;
            if (Fast) { TickFast(all); return; }
            Rectangle dirty = Rectangle.Empty;
            if (!_clockRect.IsEmpty && ClockKeyNow != _clockSec && !all)
                dirty = Rectangle.Inflate(Rectangle.Ceiling(_clockRect), 1, 1);
            if (!_loudRect.IsEmpty && Loud != null && Loud.Version != _loudVersion)
            {
                _loudVersion = Loud.Version;
                if (!all) dirty = dirty.IsEmpty ? _loudView.LiveBounds : Rectangle.Union(dirty, _loudView.LiveBounds);
            }
            if (!_oscRect.IsEmpty && _osc.Version != _oscVer)
            {
                _oscVer = _osc.Version;
                if (!all) dirty = dirty.IsEmpty ? _osc.LiveBounds : Rectangle.Union(dirty, _osc.LiveBounds);
            }
            if (!_coverRect.IsEmpty && _coverV.Version != _coverVer)
            {
                _coverVer = _coverV.Version;
                if (!all) dirty = dirty.IsEmpty ? _coverV.LiveBounds : Rectangle.Union(dirty, _coverV.LiveBounds);
            }
            if (!_pcRect.IsEmpty && _pcV.Version != _pcVer)
            {
                _pcVer = _pcV.Version;
                if (!all) dirty = dirty.IsEmpty ? _pcV.LiveBounds : Rectangle.Union(dirty, _pcV.LiveBounds);
            }
            if (!_tubeRect.IsEmpty && _tubeV.Version != _tubeVer)
            {
                _tubeVer = _tubeV.Version;
                if (!all) dirty = dirty.IsEmpty ? _tubeV.LiveBounds : Rectangle.Union(dirty, _tubeV.LiveBounds);
            }
            if (!_radarRect.IsEmpty && _radarV.Version != _radarVer)
            {
                _radarVer = _radarV.Version;
                if (!all) dirty = dirty.IsEmpty ? _radarV.LiveBounds : Rectangle.Union(dirty, _radarV.LiveBounds);
            }
            if (_specShown && Spectrum != null && (SpectrumView.Waterfall ? _specView.Push(Spectrum) : Spectrum.Version != _specVersion || SpectrumView.ShowRunning))
            {
                _specVersion = Spectrum.Version;
                var r = _specView.LiveBounds;
                if (_specCol != null) r = Rectangle.Intersect(r, _specCol.Clip);
                if (!all && !r.IsEmpty) dirty = dirty.IsEmpty ? r : Rectangle.Union(dirty, r);
            }
            foreach (var c in _cols)
            {
                if (c == _specCol) continue;
                long live = LiveKey(c);
                if (live == c.Live) continue;
                c.Live = live;
                if (all) continue;
                var r = Rectangle.Intersect(Rectangle.Inflate(Rectangle.Ceiling(c.Win), 2, 2), c.Clip);
                dirty = dirty.IsEmpty ? r : Rectangle.Union(dirty, r);
            }
            if (all) Invalidate();
            else if (!dirty.IsEmpty) Invalidate(dirty);
        }

        private long _sig;

        /// <summary>Fingerabdruck von allem, was die Anordnung der Spalten bestimmt (ohne Windows-Aufrufe, ohne Text-Aufbau).</summary>
        private long Signature()
        {
            unchecked
            {
                long h = Width * 7919L + Height;
                h = h * 31 + Theme.Style;
                h = h * 31 + Theme.Body.ToArgb();
                h = h * 31 + Theme.Accent.ToArgb();
                h = h * 31 + (StripBase.ShowNumbers ? 1 : 0) + (StripBase.SignalLeds ? 2 : 0) + (StripBase.PeakLeds ? 4 : 0)
                     + (SpectrumOn ? 8 : 0) + (ClockOn ? 16 : 0) + (GonioOn ? 32 : 0) + (Spectrum != null ? 64 : 0);
                h = h * 31 + Theme.S.GetHashCode();
                h = h * 31 + BarWidth;
                h = h * 31 + Theme.LedShape * 7 + Theme.LedSize;
                h = h * 31 + Theme.BgVersion;
                h = h * 31 + Theme.PanelMaterial;
                h = h * 31 + (_specCaption?.GetHashCode() ?? 0);
                h = h * 31 + (_gonio.Caption?.GetHashCode() ?? 0);
                h = h * 31 + (_loudCaption?.GetHashCode() ?? 0);
                h = h * 31 + Style + (FreeLayout ? 2 : 0) + (LoudOn ? 4 : 0) + (Loud?.Minutes ?? 0) * 8;
                h = h * 31 + SpectrumView.Waterfall.GetHashCode() + SpectrumView.ConfigKey * 2;
                h = h * 31 + ConfigVersion * 32 + (Pack ? 1 : 0) + (LcdClock ? 2 : 0) + (Arrange ? 4 : 0) + (OscOn ? 8 : 0) + (CoverOn ? 16 : 0) + (PcOn ? 64 : 0) + (TubesOn ? 128 : 0) + (RadarOn ? 256 : 0) + (StripHost.AnyTwoRows ? 512 : 0);
                h = h * 31 + (Order?.GetHashCode() ?? 0);
                if (Strips != null)
                {
                    var me = Theme.FormOrigin(this);
                    Control lastHost = null;
                    foreach (var s in Strips())
                    {
                        if (s == null || s.IsDisposed) continue;
                        h = h * 31 + (FreeLayout || s.Visible ? 1 : 0);
                        var host = s.Parent;
                        if (host == null) continue;
                        if (host != lastHost)
                        {
                            lastHost = host;
                            var ho = Theme.FormOrigin(host);
                            h = h * 31 + (ho.X - me.X);
                            h = h * 31 + (ho.Y - me.Y);
                            h = h * 31 + host.ClientSize.Width;
                        }
                        h = h * 31 + s.Left;
                        h = h * 31 + s.Top;
                        h = h * 31 + s.Width;
                        h = h * 31 + s.Number;
                        h = h * 31 + (s.DisplayName?.GetHashCode() ?? 0);
                        h = h * 31 + s.DisplayColor.ToArgb();
                        h = h * 31 + (s.IsDimmed ? 1 : 0) + (s.IsHot ? 2 : 0);
                    }
                }
                return h;
            }
        }

        /// <summary>Nur die Pegel einer Spalte als Zahl.</summary>
        private static long BarsKey(Col c)
        {
            var s = c.Strip;
            int n = c.Segs;
            if (VuStyle) return unchecked((long)(s.BridgeVuPos * 600) * 7919 + (long)(s.BridgeVuPeak * 600) * 31 + (long)(s.BridgeVuPos2 * 600) * 104729 + (s.IsDimmed ? 1 : 0));
            unchecked
            {
                long h = (int)(s.ShowL * n + 0.5f);
                h = h * 131 + (int)(s.ShowR * n + 0.5f);
                h = h * 131 + HoldIdx(s.ShowHoldL, n) + 1;
                h = h * 131 + HoldIdx(s.ShowHoldR, n) + 1;
                h = h * 131 + (Theme.GhostOn ? (int)(s.ShowPreL * n + 0.5f) : 0);
                h = h * 131 + (Theme.GhostOn ? (int)(s.ShowPreR * n + 0.5f) : 0);
                h = h * 131 + (int)(s.ShowPkL * n + 0.5f);
                h = h * 131 + (int)(s.ShowPkR * n + 0.5f);
                h = h * 131 + (int)(s.ShowAgL * n * 4);
                h = h * 131 + (int)(s.ShowAgR * n * 4);
                return h;
            }
        }

        /// <summary>dB-Wert und SIG/PEAK einer Spalte als Zahl.</summary>
        private static long InfoKey(Col c)
        {
            var s = c.Strip;
            unchecked
            {
                long h = s.ShowPeakDb;
                h = h * 4 + (s.ShowClip ? 1 : 0) + (s.SigLit ? 2 : 0);
                h = h * 2 + (s.PeakLit ? 1 : 0);
                return h;
            }
        }

        /// <summary>Wo dB-Wert und SIG/PEAK stehen (drei Rechtecke, beruehren die Pegel nicht).</summary>
        private int InfoRectsOf(Col c)
        {
            var win = Rectangle.Intersect(Rectangle.Ceiling(c.Win), c.Clip);
            int k = 0;
            c.InfoRects[k++] = Rectangle.Intersect(Rectangle.Inflate(Rectangle.Ceiling(c.Peak), 1, 1), win);
            float lr = Theme.F(3.6f) * 2.3f + 1.5f;
            if (StripBase.SignalLeds) c.InfoRects[k++] = Rectangle.Intersect(Rectangle.Ceiling(new RectangleF(c.Sig.X - lr, c.Sig.Y - lr, lr * 2, lr * 2)), win);
            if (StripBase.PeakLeds) c.InfoRects[k++] = Rectangle.Intersect(Rectangle.Ceiling(new RectangleF(c.Pk.X - lr, c.Pk.Y - lr, lr * 2, lr * 2)), win);
            return k;
        }

        /// <summary>Was sichtbar ist (Anzahl leuchtender Segmente, Spitzenwert, LEDs) als Zahl.</summary>
        private static long LiveKey(Col c)
        {
            var s = c.Strip;
            int n = c.Segs;
            if (VuStyle) return unchecked(BarsKey(c) * 1000003 + InfoKey(c));
            unchecked
            {
                long h = (int)(s.ShowL * n + 0.5f);
                h = h * 131 + (int)(s.ShowR * n + 0.5f);
                h = h * 131 + HoldIdx(s.ShowHoldL, n) + 1;
                h = h * 131 + HoldIdx(s.ShowHoldR, n) + 1;
                h = h * 131 + (Theme.GhostOn ? (int)(s.ShowPreL * n + 0.5f) : 0);
                h = h * 131 + (Theme.GhostOn ? (int)(s.ShowPreR * n + 0.5f) : 0);
                h = h * 131 + (int)(s.ShowPkL * n + 0.5f);
                h = h * 131 + (int)(s.ShowPkR * n + 0.5f);
                h = h * 131 + (int)(s.ShowAgL * n * 4);
                h = h * 131 + (int)(s.ShowAgR * n * 4);
                h = h * 1009 + s.ShowPeakDb;
                h = h * 4 + (s.ShowClip ? 1 : 0) + (s.SigLit ? 2 : 0);
                h = h * 2 + (s.PeakLit ? 1 : 0);
                return h;
            }
        }

        private static int HoldIdx(float frac, int n) => frac > 0.02f ? Math.Min(n - 1, (int)(frac * n + 0.5f) - 1) : -1;

        private static Color SegColor(float frac, Col c) => Theme.SegColor(frac, c.Strip?.MeterTintColor ?? Theme.Accent);

        // --------------------------------------------------------------- Zeichnen

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 10 || Height < 10) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { PaintBridge(e); if (DuckLinesOn && DuckFrom != null) DrawDuckLines(e.Graphics); if (_hoverX >= 0 && _specShown) DrawSpecHover(e.Graphics); if (_dragging) DrawDragOverlay(e.Graphics); }
            finally { FrameStats.AddPaint(System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
        }

        private void PaintBridge(PaintEventArgs e)
        {
            if (_needBuild || _layoutKey == null) BuildColumns();
            if (!_cache.Fits(Width, Height) || _layoutKey != _cacheKey)
            {
                _cacheKey = _layoutKey;
                RenderStatic();
            }
            if (Fast)
            {
                if (!_compValid || !_comp.Fits(Width, Height)) RebuildComp();
                if (_compValid) { _comp.Draw(e.Graphics, e.ClipRectangle); return; }
            }
            var g = e.Graphics;
            _cache.Draw(g, e.ClipRectangle);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;
            foreach (var c in _cols)
            {
                if (c == _specCol || !e.ClipRectangle.IntersectsWith(c.Clip)) continue;
                var st = g.Save();
                g.SetClip(c.Clip, CombineMode.Intersect);
                DrawLive(g, c, c.Clip);
                g.Restore(st);
            }
            if (!_clockRect.IsEmpty && e.ClipRectangle.IntersectsWith(Rectangle.Ceiling(_clockRect))) DrawClockLive(g);
            if (!_gonioRect.IsEmpty && e.ClipRectangle.IntersectsWith(_gonio.LiveBounds)) _gonio.DrawLive(g);
            if (!_loudRect.IsEmpty && Loud != null && e.ClipRectangle.IntersectsWith(_loudView.LiveBounds)) _loudView.DrawLive(g, Loud);
            if (!_oscRect.IsEmpty && e.ClipRectangle.IntersectsWith(_osc.LiveBounds)) { _oscVer = _osc.Version; _osc.DrawLive(g); }
            if (!_coverRect.IsEmpty && e.ClipRectangle.IntersectsWith(_coverV.LiveBounds)) { _coverVer = _coverV.Version; _coverV.DrawLive(g); }
            if (!_pcRect.IsEmpty && e.ClipRectangle.IntersectsWith(_pcV.LiveBounds)) { _pcVer = _pcV.Version; _pcV.DrawLive(g); }
            if (!_tubeRect.IsEmpty && e.ClipRectangle.IntersectsWith(_tubeV.LiveBounds)) { _tubeVer = _tubeV.Version; _tubeV.DrawLive(g); }
            if (!_radarRect.IsEmpty && e.ClipRectangle.IntersectsWith(_radarV.LiveBounds)) { _radarVer = _radarV.Version; _radarV.DrawLive(g); }
            if (_specShown && e.ClipRectangle.IntersectsWith(_specView.LiveBounds))
            {
                var st = g.Save();
                if (_specCol != null) g.SetClip(_specCol.Clip, CombineMode.Intersect);
                _specView.DrawLive(g, Spectrum);
                g.Restore(st);
            }
        }

        /// <summary>Leuchtendes der Studio-Uhr (einmal pro Sekunde in ein kleines Bild).</summary>
        private void DrawClockLive(Graphics g)
        {
            {
                // Leuchtendes einmal pro Sekunde in ein kleines Bild – sonst wuerde es bei jedem Pegel-Bild neu gezeichnet
                int sec = ClockKeyNow;
                var cr = Rectangle.Ceiling(_clockRect);
                if (_clockBmp == null || _clockBmp.Width != cr.Width || _clockBmp.Height != cr.Height || sec != _clockSec)
                {
                    if (_clockBmp == null || _clockBmp.Width != cr.Width || _clockBmp.Height != cr.Height)
                    {
                        _clockBmp?.Dispose();
                        _clockBmp = new Bitmap(cr.Width, cr.Height, PixelFormat.Format32bppPArgb);
                    }
                    using (var cg = Graphics.FromImage(_clockBmp))
                    {
                        cg.Clear(Color.Transparent);
                        cg.TranslateTransform(-cr.X, -cr.Y);
                        if (Theme.ClockKind == 8) Theme.DrawScopeClock(cg, _clockRect, false);      // 4.0 finale
                        else if (Theme.ClockKind == 6) Theme.DrawWordClock(cg, _clockRect, false);       // 4.0
                        else if (Theme.ClockKind == 7) Theme.DrawBinaryClock(cg, _clockRect, false);  // 4.0
                        else if (Theme.ClockKind == 5) Theme.DrawRollClock(cg, _clockRect, false);       // 3.4
                        else if (Theme.ClockKind == 3) Theme.DrawFlipClock(cg, _clockRect, false);       // 3.3
                        else if (Theme.ClockKind == 4) Theme.DrawNixieClock(cg, _clockRect, false);
                        else if (LcdClock) Theme.DrawLcdClock(cg, _clockRect, false);
                        else if (Theme.AnalogClock) Theme.DrawAnalogClock(cg, _clockRect, false);
                        else Theme.DrawStudioClock(cg, _clockRect, false);
                    }
                    _clockSec = sec;
                }
                g.DrawImageUnscaled(_clockBmp, cr.X, cr.Y);
            }
        }

        private void RenderStatic()
        {
            _cache.Ensure(Width, Height);
            _cache.Render(g =>
            {
                Theme.Setup(g);
                Theme.FillBody(g, new RectangleF(0, 0, Width, Height), Theme.FormOrigin(this), MixerForm.BodyColor);
                // Leiste: dunkle, vertiefte Flaeche
                var band = new RectangleF(Theme.F(4), Theme.F(2), Width - Theme.F(8), Height - Theme.F(4));
                bool lightBody = Theme.Luma(Theme.Body) > 0.5f;   // Vintage: dunkles Holz trotz hellem Design
                Color b1 = lightBody ? Theme.Darken(Theme.Body, 0.12f) : Theme.Darken(Theme.Body, 0.35f);
                Color b2 = lightBody ? Theme.Darken(Theme.Body, 0.05f) : Theme.Darken(Theme.Body, 0.15f);
                using (var br = new LinearGradientBrush(new RectangleF(band.X, band.Y - 1, band.Width, band.Height + 2), b1, b2, LinearGradientMode.Vertical))
                    Theme.FillRound(g, br, band, Theme.F(6));
                Theme.StrokeRound(g, lightBody ? Theme.Darken(Theme.Body, 0.22f) : Color.FromArgb(0x03, 0x03, 0x04), Theme.F(1), band, Theme.F(6));
                using (var pen = new Pen(lightBody ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(18, 255, 255, 255), Theme.F(1)))
                    g.DrawLine(pen, band.X + Theme.F(6), band.Bottom + Theme.F(0.5f), band.Right - Theme.F(6), band.Bottom + Theme.F(0.5f));
                foreach (var c in _cols)
                {
                    var st = g.Save();
                    g.SetClip(c.Clip, CombineMode.Intersect);
                    if (c == _specCol) _specView.DrawStatic(g);
                    else DrawStatic(g, c);
                    g.Restore(st);
                }
                if (_specShown && _specCol == null) _specView.DrawStatic(g);
                if (!_clockRect.IsEmpty) { if (Theme.ClockKind == 8) Theme.DrawScopeClock(g, _clockRect, true); else if (Theme.ClockKind == 6) Theme.DrawWordClock(g, _clockRect, true); else if (Theme.ClockKind == 7) Theme.DrawBinaryClock(g, _clockRect, true); else if (Theme.ClockKind == 5) Theme.DrawRollClock(g, _clockRect, true); else if (Theme.ClockKind == 3) Theme.DrawFlipClock(g, _clockRect, true); else if (Theme.ClockKind == 4) Theme.DrawNixieClock(g, _clockRect, true); else if (LcdClock) Theme.DrawLcdClock(g, _clockRect, true); else if (Theme.AnalogClock) Theme.DrawAnalogClock(g, _clockRect, true); else Theme.DrawStudioClock(g, _clockRect, true); }
                if (!_gonioRect.IsEmpty) _gonio.DrawStatic(g);
                if (!_loudRect.IsEmpty) _loudView.DrawStatic(g, _loudMinutes, _loudCaption);
                if (!_oscRect.IsEmpty) _osc.DrawStatic(g);
                if (!_coverRect.IsEmpty) _coverV.DrawStatic(g);
                if (!_pcRect.IsEmpty) _pcV.DrawStatic(g);
                if (!_tubeRect.IsEmpty) _tubeV.DrawStatic(g);
                if (!_radarRect.IsEmpty) _radarV.DrawStatic(g);
                Theme.DimOverlay(g, band, Theme.F(6));   // Pult-Helligkeit (abends dunkler)
            });
            _compValid = false;
            RenderLit();
        }

        /// <summary>Festes Bild mit allen LEDs leuchtend (Spalten und Spektrum) – Untergrund ist das feste Bild, damit die Luecken stimmen.</summary>
        private void RenderLit()
        {
            _litOk = false;
            if (!Fast) return;
            _lit.Ensure(Width, Height);
            _lit.Render(g =>
            {
                _cache.Draw(g, new Rectangle(0, 0, Width, Height));
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;
                foreach (var c in _cols)
                {
                    if (c == _specCol || VuStyle) continue;
                    var st = g.Save();
                    g.SetClip(c.Clip, CombineMode.Intersect);
                    DrawBar(g, c, c.Bar1, c.Segs, false, c.Segs, -1, false);
                    DrawBar(g, c, c.Bar2, c.Segs, false, c.Segs, -1, false);
                    g.Restore(st);
                }
                if (_specShown)
                {
                    var st = g.Save();
                    if (_specCol != null) g.SetClip(_specCol.Clip, CombineMode.Intersect);
                    _specView.DrawLit(g);
                    g.Restore(st);
                }
            });
            _litOk = _lit.Ready;
        }

        private Rectangle ColRect(Col c) => Rectangle.Intersect(Rectangle.Inflate(Rectangle.Ceiling(c.Win), 2, 2), c.Clip);

        private Rectangle SpecRect()
        {
            var r = _specView.LiveBounds;
            return _specCol != null ? Rectangle.Intersect(r, _specCol.Clip) : r;
        }

        private Rectangle ClockRect => Rectangle.Inflate(Rectangle.Ceiling(_clockRect), 1, 1);

        /// <summary>Fertiges Bild der ganzen Leiste neu aufbauen (nach Aenderungen am festen Teil).</summary>
        private void RebuildComp()
        {
            _comp.Ensure(Width, Height);
            _info.Ensure(Width, Height);
            if (!_comp.Ready || !_info.Ready) { _compValid = false; return; }
            // 1) dB-Werte und SIG/PEAK aller Spalten (auf dem festen Bild)
            _info.Render(g =>
            {
                _cache.Draw(g, new Rectangle(0, 0, Width, Height));
                foreach (var c in _cols)
                {
                    if (c == _specCol) continue;
                    c.Info = InfoKey(c);
                    var st = g.Save();
                    g.SetClip(c.Clip, CombineMode.Intersect);
                    DrawLiveInfo(g, c);
                    g.Restore(st);
                }
            });
            // 2) fertiges Bild: festes Bild, Pegel, dazu die Anzeigen aus 1)
            _comp.Render(g =>
            {
                _cache.Draw(g, new Rectangle(0, 0, Width, Height));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.None;
                foreach (var c in _cols)
                {
                    if (c == _specCol) continue;
                    c.Bars = BarsKey(c);
                    var st = g.Save();
                    g.SetClip(c.Clip, CombineMode.Intersect);
                    DrawLiveBars(g, c, c.Clip);
                    g.Restore(st);
                    _info.DrawRects(g, c.InfoRects, InfoRectsOf(c), c.Clip);
                }
                if (!_clockRect.IsEmpty) DrawClockLive(g);
                if (!_gonioRect.IsEmpty) _gonio.DrawLive(g);
                if (!_loudRect.IsEmpty && Loud != null) { _loudVersion = Loud.Version; _loudView.DrawLive(g, Loud); }
                if (!_oscRect.IsEmpty) { _oscVer = _osc.Version; _osc.DrawLive(g); }
                if (!_coverRect.IsEmpty) { _coverVer = _coverV.Version; _coverV.DrawLive(g); }
                if (!_pcRect.IsEmpty) { _pcVer = _pcV.Version; _pcV.DrawLive(g); }
                if (!_tubeRect.IsEmpty) { _tubeVer = _tubeV.Version; _tubeV.DrawLive(g); }
                if (!_radarRect.IsEmpty) { _radarVer = _radarV.Version; _radarV.DrawLive(g); }
                if (_specShown)
                {
                    if (Spectrum != null) _specVersion = Spectrum.Version;
                    var r = SpecRect();
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _specView.DrawLive(g, Spectrum, _litOk ? _lit : null, r);
                    g.Restore(st);
                }
            });
            _gonioDirty = false;
            _compValid = true;
        }

        /// <summary>Schnell: nur Geaendertes ins fertige Bild zeichnen, dann genau diese Stellen auf den Schirm kopieren.</summary>
        private void TickFast(bool all)
        {
            if (all || !_compValid)
            {
                _compValid = false;
                Invalidate();   // beim naechsten Zeichnen wird alles neu aufgebaut
                return;
            }
            _changed.Clear();
            _infoChanged.Clear();
            foreach (var c in _cols)
            {
                if (c == _specCol) continue;
                long bars = BarsKey(c), info = InfoKey(c);
                if (bars != c.Bars) { c.Bars = bars; _changed.Add(c); }
                if (info != c.Info) { c.Info = info; _infoChanged.Add(c); }
            }
            bool spec = _specShown && Spectrum != null && (SpectrumView.Waterfall ? _specView.Push(Spectrum) : Spectrum.Version != _specVersion || SpectrumView.ShowRunning);
            bool clock = !_clockRect.IsEmpty && ClockKeyNow != _clockSec;
            bool gonio = _gonioDirty && !_gonioRect.IsEmpty;
            _gonioDirty = false;
            bool loud = !_loudRect.IsEmpty && Loud != null && Loud.Version != _loudVersion;
            if (loud) _loudVersion = Loud.Version;
            bool osc = !_oscRect.IsEmpty && _osc.Version != _oscVer;
            if (osc) _oscVer = _osc.Version;
            bool cov = !_coverRect.IsEmpty && _coverV.Version != _coverVer;
            if (cov) _coverVer = _coverV.Version;
            bool pcv = !_pcRect.IsEmpty && _pcV.Version != _pcVer;
            if (pcv) _pcVer = _pcV.Version;
            bool tuv = !_tubeRect.IsEmpty && _tubeV.Version != _tubeVer;   // 3.5
            if (tuv) _tubeVer = _tubeV.Version;
            bool rav = !_radarRect.IsEmpty && _radarV.Version != _radarVer;   // 4.0
            if (rav) _radarVer = _radarV.Version;
            if (_changed.Count == 0 && _infoChanged.Count == 0 && !spec && !clock && !gonio && !loud && !osc && !cov && !pcv && !tuv && !rav) return;
            if (spec) _specVersion = Spectrum.Version;
            if (_infoChanged.Count > 0)
                _info.Render(g =>
                {
                    foreach (var c in _infoChanged)
                    {
                        int k = InfoRectsOf(c);
                        if (k == 0) continue;
                        var u = c.InfoRects[0];
                        for (int i = 0; i < k; i++) { _cache.Draw(g, c.InfoRects[i]); u = Rectangle.Union(u, c.InfoRects[i]); }
                        var st = g.Save();
                        g.SetClip(Rectangle.Intersect(u, c.Clip), CombineMode.Intersect);
                        DrawLiveInfo(g, c);
                        g.Restore(st);
                    }
                });
            _comp.Render(g =>
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.None;
                foreach (var c in _changed)
                {
                    var r = ColRect(c);
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    DrawLiveBars(g, c, r);
                    g.Restore(st);
                    _info.DrawRects(g, c.InfoRects, InfoRectsOf(c), r);   // dB-Wert und LEDs fertig aus dem Zwischenbild
                }
                foreach (var c in _infoChanged)
                    if (!_changed.Contains(c)) _info.DrawRects(g, c.InfoRects, InfoRectsOf(c), c.Clip);
                if (spec)
                {
                    var r = SpecRect();
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _specView.DrawLive(g, Spectrum, _litOk ? _lit : null, r);
                    g.Restore(st);
                }
                if (gonio)
                {
                    var r = _gonio.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _gonio.DrawLive(g);
                    g.Restore(st);
                }
                if (clock)
                {
                    var r = ClockRect;
                    _cache.Draw(g, r);
                    DrawClockLive(g);
                }
                if (loud)
                {
                    var r = _loudView.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _loudView.DrawLive(g, Loud);
                    g.Restore(st);
                }
                if (osc)
                {
                    var r = _osc.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _osc.DrawLive(g);
                    g.Restore(st);
                }
                if (cov)
                {
                    var r = _coverV.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _coverV.DrawLive(g);
                    g.Restore(st);
                }
                if (pcv)
                {
                    var r = _pcV.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _pcV.DrawLive(g);
                    g.Restore(st);
                }
                if (tuv)
                {
                    var r = _tubeV.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _tubeV.DrawLive(g);
                    g.Restore(st);
                }
                if (rav)
                {
                    var r = _radarV.LiveBounds;
                    _cache.Draw(g, r);
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    _radarV.DrawLive(g);
                    g.Restore(st);
                }
            });
            foreach (var c in _changed) Invalidate(ColRect(c));
            foreach (var c in _infoChanged)
                if (!_changed.Contains(c)) { int k = InfoRectsOf(c); for (int i = 0; i < k; i++) Invalidate(c.InfoRects[i]); }
            if (spec) Invalidate(SpecRect());
            if (gonio) Invalidate(_gonio.LiveBounds);
            if (clock) Invalidate(ClockRect);
            if (loud) Invalidate(_loudView.LiveBounds);
            if (osc) Invalidate(_osc.LiveBounds);
            if (cov) Invalidate(_coverV.LiveBounds);
            if (pcv) Invalidate(_pcV.LiveBounds);
            if (tuv) Invalidate(_tubeV.LiveBounds);
            if (rav) Invalidate(_radarV.LiveBounds);
        }

        private void DrawStatic(Graphics g, Col c)
        {
            var s = c.Strip;
            Color col = s.DisplayColor;
            var w = c.Win;
            // Fenster
            Theme.FillRound(g, Color.FromArgb(0x0b, 0x0c, 0x0f), w, Theme.F(5));
            if (s.IsHot)
                using (var b = new SolidBrush(Color.FromArgb(14, 255, 255, 255))) Theme.FillRound(g, b, w, Theme.F(5));
            Theme.StrokeRound(g, s.IsHot ? Theme.Alpha(Theme.Lighten(col, 0.2f), 170) : Color.FromArgb(0x24, 0x27, 0x2d), Theme.F(s.IsHot ? 1.5f : 1f), w, Theme.F(5));
            // Farbkante oben
            using (var path = Theme.RoundTopBand(w, Theme.F(5), Theme.F(3)))
            using (var b = new SolidBrush(s.IsDimmed ? Theme.Darken(col, 0.5f) : col)) g.FillPath(b, path);
            // Nummer + Name
            string name = s.DisplayName ?? "";
            var nr = new RectangleF(w.X + Theme.F(4), w.Y + Theme.F(5), w.Width - Theme.F(8), Theme.F(15));
            float numW = 0;
            if (StripBase.ShowNumbers && s.Number > 0)
            {
                string num = s.Number.ToString();
                numW = Theme.F(num.Length > 1 ? 18 : 14);
                var chip = new RectangleF((float)Math.Round(nr.X), (float)Math.Round(nr.Y + Theme.F(1)), (float)Math.Round(numW), (float)Math.Round(Theme.F(13)));
                Theme.FillRound(g, s.IsDimmed ? Theme.Darken(col, 0.5f) : col, chip, Theme.F(3));
                using (var b = new SolidBrush(Theme.TextOn(col))) Theme.Str(g, num, Theme.Ui(9f, FontStyle.Bold), b, chip, Theme.Center);
                numW += Theme.F(4);
            }
            var tr = new RectangleF(nr.X + numW, nr.Y, nr.Width - numW, nr.Height);
            if (tr.Width > Theme.F(8))
                using (var b = new SolidBrush(s.IsDimmed ? Color.FromArgb(0x7a, 0x80, 0x89) : Color.FromArgb(0xe8, 0xeb, 0xef)))
                    Theme.Str(g, name, Theme.Fit(g, name, tr.Width - Theme.F(2), 10f, 7.5f), b, tr, numW > 0 ? Theme.Left : Theme.Center);
            if (VuStyle && !c.Vu.IsEmpty)
            {
                Theme.DrawVuFace(g, c.Vu, s.IsDimmed);
                if (StripBase.SignalLeds) Theme.FillEllipse(g, Color.FromArgb(0x02, 0x02, 0x03), c.Sig.X, c.Sig.Y, Theme.F(5.2f));
                if (StripBase.PeakLeds) Theme.FillEllipse(g, Color.FromArgb(0x02, 0x02, 0x03), c.Pk.X, c.Pk.Y, Theme.F(5.2f));
                return;
            }
            // unbeleuchtete Segmente
            DrawBar(g, c, c.Bar1, c.Segs, true, 0, -1, s.IsDimmed);
            DrawBar(g, c, c.Bar2, c.Segs, true, 0, -1, s.IsDimmed);
            // Skala
            var font = Theme.Ui(7.5f, FontStyle.Bold);
            using (var b = new SolidBrush(Color.FromArgb(0x7c, 0x85, 0x91)))
                foreach (int db in new[] { 0, -6, -18, -40 })
                {
                    float y = c.Bar1.Bottom - MeterBallistics.Iec(db) * c.Bar1.Height;
                    Theme.Str(g, db.ToString(), font, b, new RectangleF(c.Bar1.X - Theme.F(20), y - Theme.F(5), Theme.F(17), Theme.F(10)), Theme.Right);
                }
            // Fassungen der LEDs
            if (StripBase.SignalLeds) Theme.FillEllipse(g, Color.FromArgb(0x02, 0x02, 0x03), c.Sig.X, c.Sig.Y, Theme.F(5.2f));
            if (StripBase.PeakLeds) Theme.FillEllipse(g, Color.FromArgb(0x02, 0x02, 0x03), c.Pk.X, c.Pk.Y, Theme.F(5.2f));
        }

        private void DrawBar(Graphics g, Col c, RectangleF bar, int n, bool dim, int lit, int hold, bool dimmed, int pre = 0, int pk = 0, float ag = 0f, bool skipLit = false)
        {
            int step = SegH + SegGap;
            for (int i = 0; i < n; i++)
            {
                bool on = !dim && i < lit, isHold = !dim && i == hold, isPk = !dim && !on && i < pk, isPre = !dim && !on && !isPk && i < pre;
                if (skipLit && (on || isHold)) continue;   // kommen aus dem Bildspeicher
                float agf = !dim && !on && !isPk && ag > i ? (ag - i) / Math.Max(1f, ag - Math.Max(lit, pk)) * Math.Min(1f, ag - i) : 0f;   // Nachgluehen
                if (!dim && !on && !isHold && !isPre && !isPk && agf <= 0.02f) continue;
                float y = bar.Bottom - (i + 1) * step + SegGap;
                var col = SegColor((i + 1f) / n, c);
                _brush.Color = dim ? Theme.Mix(Color.FromArgb(0x13, 0x15, 0x18), col, dimmed ? 0.06f : 0.14f)
                             : isPk && !isHold ? Theme.Mix(Color.FromArgb(0x13, 0x15, 0x18), col, 0.5f)
                             : !on && !isHold && agf > 0.02f ? Theme.Mix(Color.FromArgb(0x13, 0x15, 0x18), col, 0.12f + 0.5f * agf)
                             : isPre && !on && !isHold ? Theme.Mix(Theme.MeterGhost, col, 0.14f) : col;
                Theme.FillSeg(g, _brush, bar.X, y, bar.Width, SegH);
            }
        }

        private void HaloBar(Graphics g, Col c, RectangleF bar, int n, int lit)
        {
            if (lit <= 0) return;
            int step = SegH + SegGap;
            float top = bar.Bottom - Math.Min(n, lit) * step + SegGap;
            Theme.DrawLedHalo(g, new RectangleF(bar.X, top, bar.Width, bar.Bottom - top), SegColor(Math.Min(n, lit) / (float)n, c));
        }

        private void AddBlit(RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0 || _blitN >= _blit.Length) return;
            _blit[_blitN++] = Rectangle.FromLTRB((int)Math.Floor(r.X), (int)Math.Floor(r.Y), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
        }

        private RectangleF LitColumn(RectangleF bar, int n, int lit)
        {
            if (lit <= 0) return RectangleF.Empty;
            int step = SegH + SegGap;
            float top = bar.Bottom - Math.Min(n, lit) * step + SegGap;
            return new RectangleF(bar.X, top, bar.Width, bar.Bottom - top);
        }

        private RectangleF SegRect(RectangleF bar, int i)
        {
            int step = SegH + SegGap;
            return new RectangleF(bar.X, bar.Bottom - (i + 1) * step + SegGap, bar.Width, SegH);
        }

        /// <summary>Leuchtendes einer Spalte. area: hierhin darf gezeichnet werden (gilt auch fuers Kopieren aus dem Bildspeicher).</summary>
        private void DrawLive(Graphics g, Col c, Rectangle area)
        {
            DrawLiveBars(g, c, area);
            DrawLiveInfo(g, c);
        }

        private void DrawLiveBars(Graphics g, Col c, Rectangle area)
        {
            var s = c.Strip;
            if (VuStyle)
            {
                if (c.Vu.IsEmpty) return;
                if (Theme.VuDual) Theme.DrawVuNeedle2(g, c.Vu, s.BridgeVuPos, s.BridgeVuPos2, s.IsDimmed, 0);   // 3.1
                else Theme.DrawVuNeedle(g, c.Vu, s.BridgeVuPos, s.IsDimmed, StripBase.VuPeakNeedle ? s.BridgeVuPeak : -1f, 0);
                return;
            }
            int n = c.Segs;
            g.SmoothingMode = SmoothingMode.None;
            int preL = Theme.GhostOn ? (int)(s.ShowPreL * n + 0.5f) : 0, preR = Theme.GhostOn ? (int)(s.ShowPreR * n + 0.5f) : 0;
            int litL = (int)(s.ShowL * n + 0.5f), litR = (int)(s.ShowR * n + 0.5f), pkL = (int)(s.ShowPkL * n + 0.5f), pkR = (int)(s.ShowPkR * n + 0.5f);
            int holdL = HoldIdx(s.ShowHoldL, n), holdR = HoldIdx(s.ShowHoldR, n);
            if (Fast && _litOk)
            {
                // leuchtende Saeulen aus dem Bildspeicher, Lichtschein drumherum, Rest live, stehende Spitze obendrauf
                _blitN = 0;
                AddBlit(LitColumn(c.Bar1, n, litL));
                AddBlit(LitColumn(c.Bar2, n, litR));
                int cols = _blitN;
                _lit.DrawRects(g, _blit, _blitN, area);
                if (Theme.LedHalo && (Math.Max(litL, pkL) > 0 || Math.Max(litR, pkR) > 0))
                {
                    var st = g.Save();
                    for (int i = 0; i < cols; i++) g.SetClip(_blit[i], CombineMode.Exclude);
                    HaloBar(g, c, c.Bar1, n, Math.Max(litL, pkL));
                    HaloBar(g, c, c.Bar2, n, Math.Max(litR, pkR));
                    g.Restore(st);
                }
                DrawBar(g, c, c.Bar1, n, false, litL, holdL, false, preL, pkL, s.ShowAgL * n, true);
                DrawBar(g, c, c.Bar2, n, false, litR, holdR, false, preR, pkR, s.ShowAgR * n, true);
                _blitN = 0;
                if (holdL >= litL && holdL >= 0) AddBlit(SegRect(c.Bar1, holdL));
                if (holdR >= litR && holdR >= 0) AddBlit(SegRect(c.Bar2, holdR));
                if (_blitN > 0) _lit.DrawRects(g, _blit, _blitN, area);
            }
            else
            {
                if (Theme.LedHalo)
                {
                    HaloBar(g, c, c.Bar1, n, Math.Max(litL, pkL));
                    HaloBar(g, c, c.Bar2, n, Math.Max(litR, pkR));
                }
                DrawBar(g, c, c.Bar1, n, false, litL, holdL, false, preL, pkL, s.ShowAgL * n);
                DrawBar(g, c, c.Bar2, n, false, litR, holdR, false, preR, pkR, s.ShowAgR * n);
            }
        }

        /// <summary>dB-Wert und SIG/PEAK einer Spalte.</summary>
        private void DrawLiveInfo(Graphics g, Col c)
        {
            var s = c.Strip;
            // Spitzenwert
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool clip = s.ShowClip;
            int pv = s.ShowPeakDb;
            Theme.LcdColors(out var lbg, out var lfg);
            Theme.FillRound(g, clip ? Theme.MeterRed : Theme.EffLcd == 0 ? Color.FromArgb(0x16, 0x18, 0x1c) : lbg, c.Peak, Theme.F(2.5f));
            string t = pv == int.MaxValue ? "88" : pv == int.MinValue ? "–" : (pv > 0 ? "+" : "") + pv;
            _brush.Color = clip ? Color.White : pv == int.MinValue ? Theme.LcdOffText() : lfg;
            Theme.Str(g, t, Theme.Mono(9.5f), _brush, c.Peak, Theme.Center);
            // SIG / PEAK
            if (StripBase.SignalLeds) Theme.DrawLed(g, c.Sig.X, c.Sig.Y, Theme.F(3.6f), Theme.LedGreen, s.SigLit);
            if (StripBase.PeakLeds) Theme.DrawLed(g, c.Pk.X, c.Pk.Y, Theme.F(3.6f), Theme.MeterRed, s.PeakLit);
        }

        // --------------------------------------------------------------- Maus

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && e.Button == MouseButtons.Left) { FinishDrag(); Capture = false; return; }   // verschoben: kein Klick (erst uebernehmen, dann loslassen)
            _dragKey = null;
            if (e.Button == MouseButtons.Right)
            {
                // worauf geklickt wurde, steht dem Menue zur Verfuegung (Pegel ausblenden, Anzeige verschieben)
                MenuExtra = HitExtra(e.Location);
                MenuStrip = MenuExtra != '\0' ? null : _cols.FirstOrDefault(c => c != _specCol && c.Clip.Contains(e.Location) && c.Win.Contains(e.Location))?.Strip;
                MenuRequested?.Invoke(PointToScreen(e.Location));
            }
            else if (e.Button == MouseButtons.Left && OverSpectrum(e.Location)) SpectrumClicked?.Invoke(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && !_gonioRect.IsEmpty && _gonioRect.Contains(e.Location) && GonioClicked != null) GonioClicked(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && !_loudRect.IsEmpty && _loudRect.Contains(e.Location) && LoudClicked != null) LoudClicked(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && !_clockRect.IsEmpty && _clockRect.Contains(e.Location) && ClockClicked != null) ClockClicked(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && !_oscRect.IsEmpty && _oscRect.Contains(e.Location) && OscClicked != null) OscClicked(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && !_coverRect.IsEmpty && _coverRect.Contains(e.Location) && CoverClicked != null) CoverClicked(PointToScreen(e.Location));
            else if (e.Button == MouseButtons.Left && MeterBallistics.HoldsUntilClick)
            {
                // Spitze halten "bis Klick": Klick auf eine Spalte loescht ihre Spitze
                var col = _cols.FirstOrDefault(c => c != _specCol && c.Clip.Contains(e.Location) && c.Win.Contains(e.Location));
                col?.Strip.ResetMeterHold();
            }
        }

        /// <summary>Welche Anzeige liegt an der Stelle? 's' Spektrum (nicht ueber dem MASTER), 'l', 'g', 'c' – sonst '\0'.</summary>
        public char HitExtra(Point p)
        {
            if (_specShown && _specCol == null && _specView.Bounds.Contains(p)) return 's';
            if (!_loudRect.IsEmpty && _loudRect.Contains(p)) return 'l';
            if (!_gonioRect.IsEmpty && _gonioRect.Contains(p)) return 'g';
            if (!_clockRect.IsEmpty && _clockRect.Contains(p)) return 'c';
            if (!_oscRect.IsEmpty && _oscRect.Contains(p)) return 'o';
            if (!_coverRect.IsEmpty && _coverRect.Contains(p)) return 'a';
            return '\0';
        }

        /// <summary>Nur Vorschau: Menue wie bei einem Rechtsklick an dieser Stelle.</summary>
        public void RightClickDemo(Point p) => OnMouseUp(new MouseEventArgs(MouseButtons.Right, 1, p.X, p.Y, 0));

        /// <summary>Nur Vorschau/Test: Mitte einer Spalte bzw. Anzeige (leer = nicht da).</summary>
        public Point PointOf(StripBase s)
        {
            var c = _cols.FirstOrDefault(x => x.Strip == s && x != _specCol);
            return c == null ? Point.Empty : new Point((int)(c.Win.X + c.Win.Width / 2), (int)(c.Win.Y + c.Win.Height / 2));
        }

        public RectangleF ExtraRect(char k) =>
            k == 's' ? (_specShown && _specCol == null ? _specView.Bounds : RectangleF.Empty) : k == 'l' ? _loudRect : k == 'g' ? _gonioRect : k == 'c' ? _clockRect
            : k == 'o' ? _oscRect : k == 'a' ? _coverRect : RectangleF.Empty;

        /// <summary>Zuege, deren Pegel gerade in der Leiste stehen (links nach rechts).</summary>
        public IEnumerable<StripBase> ShownStrips => _cols.Where(c => c != _specCol).OrderBy(c => c.Win.X).Select(c => c.Strip);

        private bool OverSpectrum(Point p)
        {
            if (!_specShown) return false;
            var r = _specView.Bounds;
            if (_specCol != null) r = RectangleF.Intersect(r, _specCol.Clip);
            return r.Contains(p);
        }

        private int _tipOver;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 2.17: Frequenz und Pegel unter der Maus
            if (SpecHover && !_dragging && OverSpectrum(e.Location) && _specView.Area.Contains(e.Location)) SetSpecHover(e.X);
            else if (_hoverX >= 0) SetSpecHover(-1);
            if (_dragKey != null && (MouseButtons & MouseButtons.Left) != 0)
            {
                if (!_dragging && Math.Abs(e.X - _dragFrom.X) >= Theme.F(8))
                {
                    _dragging = true;
                    Capture = true;
                    Cursor = Cursors.SizeWE;
                    _tip.Hide(this);
                }
                if (_dragging)
                {
                    _dragX = e.X;
                    _dropIdx = DropIndex(e.X);
                    Invalidate();
                    return;
                }
            }
            int over = OverSpectrum(e.Location) ? 1 : !_gonioRect.IsEmpty && _gonioRect.Contains(e.Location) ? 2 : !_loudRect.IsEmpty && _loudRect.Contains(e.Location) ? 3
                     : !_clockRect.IsEmpty && _clockRect.Contains(e.Location) && ClockClicked != null ? 4
                     : !_oscRect.IsEmpty && _oscRect.Contains(e.Location) ? 5 : !_coverRect.IsEmpty && _coverRect.Contains(e.Location) ? 6 : 0;
            if (over == _tipOver) return;
            _tipOver = over;
            Cursor = over != 0 ? Cursors.Hand : Cursors.Default;
            _tip.SetToolTip(this, over == 1 ? "Spektrum – Klick: welcher Ausgang (Standard, ein bestimmtes Gerät oder alle zusammen)" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : over == 2 ? "Stereo-Bild – Klick: welcher Ausgang" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : over == 3 ? "Lautheits-Verlauf – Klick: Ausgang, Zeitraum, löschen" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : over == 5 ? "Oszilloskop – die Wellenform live (gleicher Ausgang wie das Stereo-Bild)\nKlick: welcher Ausgang" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : over == 6 ? "Jetzt läuft – Cover, Titel und Interpret (Windows-Mediensteuerung, z. B. Spotify)\nKlick: zuletzt gespielte Songs" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : over == 4 ? DateTime.Now.ToString("dddd, d. MMMM", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " – Klick: Sleep-Timer" + (DragOn ? "\nZiehen: verschieben" : "") + "\nRechtsklick: Meterbridge-Menü (verschieben, ausblenden)"
                                : "Meterbridge: große Pegel aller Kanäle" + (FreeLayout || Arrange ? "." : " – jeder steht über seinem Kanal.") + (DragOn ? "\nZiehen: verschieben (frei anordnen)" : "") + "\nRechtsklick: Menü (Pegel ein-/ausblenden, anordnen)");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _cache.Dispose(); _lit.Dispose(); _comp.Dispose(); _info.Dispose(); _tip.Dispose(); _brush.Dispose(); _clockBmp?.Dispose(); _gonio.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
