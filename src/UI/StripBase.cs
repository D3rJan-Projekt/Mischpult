using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>
    /// Gemeinsame Basis fuer Kanalzuege: Gehaeuse, Fader mit Skala, Stereo-Pegelanzeige, LCD.
    /// Alles Statische wird in eine Bitmap gecacht; nur die Pegel werden jedes Frame neu gezeichnet.
    /// </summary>
    internal abstract partial class StripBase : Control
    {
        protected sealed class Zone
        {
            public RectangleF Rect; public string Id; public bool Clickable;
            public Zone(RectangleF r, string id, bool clickable) { Rect = r; Id = id; Clickable = clickable; }
        }

        /// <summary>Ab hier beginnt der Faderbereich (unskaliert) – bei allen Zuegen gleich, damit die Fader fluchten.</summary>
        protected float TopSection => 268f + DynRow + RowOffset + LedRowH - (Compact && !_topFull ? CutLen : 0f);

        /// <summary>Hoehe eines Zugs (Pixel) mit Faderbereich 'fader' (unskaliert) – fuer die Mindestgroesse des Fensters.</summary>
        public static int HeightFor(bool multiOutput, float fader) =>
            Theme.Px(268f + DynRow + (multiOutput ? 24f : 0f) + LedRowH + fader + 42f - (Compact ? 237f + DynRow + (multiOutput ? 24f : 0f) - CutKeep : 0f));

        // ---- 2.16 Kompakt-Ansicht: zwischen Namensschild und Tastenreihe wird alles ausgeblendet (Apps, EQ, Knoepfe, VU …),
        // Tasten, SIG/PEAK und Fader ruecken nach oben – die Fader werden laenger. Ein Streifen fuer alle Zuege, damit die Fader fluchten.
        /// <summary>Kompakt-Ansicht an (OPTIONEN → Aussehen).</summary>
        public static bool Compact;
        private const float CutTop = 40f, CutKeep = 44f;
        /// <summary>Tastenreihe (unskaliert, voller Zug).</summary>
        private float CutEnd => 237f + DynRow + RowOffset;
        /// <summary>So viel (unskaliert) faellt in der Kompakt-Ansicht weg.</summary>
        protected float CutLen => CutEnd - CutKeep;
        private bool _topFull;

        /// <summary>Wo ein Element des oberen Teils in der Kompakt-Ansicht steht (leer = ausgeblendet).</summary>
        protected RectangleF MapTop(RectangleF r)
        {
            if (!Compact || r.IsEmpty) return r;
            if (r.Bottom <= Theme.F(CutTop) + 0.5f) return r;
            if (r.Y >= Theme.F(CutEnd) - 0.5f) { r.Y -= Theme.F(CutLen); return r; }
            return RectangleF.Empty;
        }

        /// <summary>Oberen Teil zeichnen – kompakt: Namensschild und (hochgerueckt) Tastenreihe.</summary>
        private void DrawTopMapped(Graphics g)
        {
            _topFull = true;
            try
            {
                if (!Compact) { DrawTop(g); return; }
                float y0 = Theme.F(CutTop), y1 = Theme.F(CutEnd), cut = Theme.F(CutLen), full = Theme.F(TopSection);
                var clip1 = new RectangleF(0, 0, Width, y0);
                var clip2 = new RectangleF(0, y1, Width, full - y1 + Theme.F(2));
                var lit = Theme.LitRedraw;   // abends hell nachgezeichnete Tasten: gleicher Ausschnitt/Versatz wie hier
                int n0 = lit?.Count ?? 0;
                var st = g.Save();
                g.SetClip(clip1, CombineMode.Intersect);
                DrawTop(g);
                g.Restore(st);
                if (lit != null)
                    for (int i = n0; i < lit.Count; i++) { var a = lit[i]; lit[i] = () => { var s2 = g.Save(); g.SetClip(clip1, CombineMode.Intersect); a(); g.Restore(s2); }; }
                n0 = lit?.Count ?? 0;
                st = g.Save();
                g.TranslateTransform(0, -cut);
                g.SetClip(clip2, CombineMode.Intersect);
                DrawTop(g);
                g.Restore(st);
                if (lit != null)
                    for (int i = n0; i < lit.Count; i++) { var a = lit[i]; lit[i] = () => { var s2 = g.Save(); g.TranslateTransform(0, -cut); g.SetClip(clip2, CombineMode.Intersect); a(); g.Restore(s2); }; }
            }
            finally { _topFull = false; }
        }

        /// <summary>Zeile mit den SIG/PEAK-Leuchten ueber dem Fader (unskaliert).</summary>
        public static float LedRowH => SignalLeds || PeakLeds ? 20f : 0f;

        /// <summary>Zeile fuer COMP/AUTO (Kanaele) bzw. Zusatztasten (Mikro, Ausgang) unter dem EQ (unskaliert).</summary>
        protected const float DynRow = 24f;

        /// <summary>Schmale Darstellung (nur Kanal- und Gruppenzuege).</summary>
        protected virtual bool Narrow => false;

        /// <summary>Zusaetzliche Zeile fuer die Ausgangswahl im Modus "Mehrere Ausgaenge" (unskaliert).</summary>
        protected float RowOffset => Mixer.MultiOutput ? 24f : 0f;

        /// <summary>Layout neu berechnen (z. B. nach Umschalten des Ausgangsmodus).</summary>
        public void Relayout() => DoLayout();

        protected readonly MixerForm Mixer;
        protected readonly MeterBallistics Meter = new MeterBallistics();
        /// <summary>Pegel vor Fader/Stumm (grau hinterlegt): so laut waere es eigentlich.</summary>
        protected readonly MeterBallistics PreMeter = new MeterBallistics();
        /// <summary>Wird bei jeder Aenderung der Grau-Helligkeit erhoeht.</summary>
        public static int GhostVersion;
        /// <summary>SIG-Leuchte ueber dem Fader (OPTIONEN → Ansicht).</summary>
        public static bool SignalLeds = true;
        /// <summary>PEAK-Leuchte ueber dem Fader (OPTIONEN → Ansicht), unabhaengig von SIG.</summary>
        public static bool PeakLeds = true;
        /// <summary>SIG leuchtet ab diesem Pegel (dBFS) – einstellbar unter OPTIONEN → Ansicht.</summary>
        public static float SigDb { get; private set; } = -50f;
        /// <summary>PEAK leuchtet ab diesem Pegel (dBFS).</summary>
        public static float PeakDbLimit { get; private set; } = -2f;
        private static float _sigLin = 0.00316f, _peakLin = 0.794f;

        /// <summary>Schwellen fuer SIG und PEAK setzen (dBFS).</summary>
        public static void SetLedThresholds(float sigDb, float peakDb)
        {
            SigDb = sigDb; PeakDbLimit = peakDb;
            _sigLin = (float)Math.Pow(10, sigDb / 20.0);
            _peakLin = (float)Math.Pow(10, peakDb / 20.0);
        }

        /// <summary>Orange Absenkungs-Anzeige (GR) neben dem Pegel der Kanaele.</summary>
        public static bool GrOn = true;
        /// <summary>Namensschild leuchtet mit dem Pegel mit.</summary>
        public static bool GlowOn = true;
        /// <summary>PEAK zaehlt mit, wie oft es zu laut war (Klick auf PEAK = 0).</summary>
        public static bool PeakCountOn = false;

        /// <summary>Ist mindestens eine der beiden Leuchten an (dann gibt es die Zeile ueber dem Fader)?</summary>
        public static bool AnyLeds => SignalLeds || PeakLeds;
        /// <summary>Kanalnummer im Namensschild.</summary>
        public static bool ShowNumbers = true;
        /// <summary>Zug unter der Maus leicht beleuchtet.</summary>
        public static bool HoverLight = true;
        /// <summary>Analoges VU-Meter mit Zeiger in den Ausgangszuegen.</summary>
        public static bool VuOn = true;
        /// <summary>Selbsttest beim Start (LEDs laufen durch, Fader fahren an ihre Stelle).</summary>
        public static bool SelfTestOn = true;
        private bool _ledOn, _peakLedOn;
        private float _ledT, _peakLedT;
        private bool _hot;
        private int _number;
        private RectangleF _sigRect, _pkRect;
        private float _vuP, _vuV, _vuShown = -9f, _vuHold;
        private float _vuPk = -0.02f, _vuPkHold, _vuPkShown = -9f;   // Schleppzeiger
        private bool _vuLedShown;                                     // PEAK-Leuchte im VU-Meter
        private float _motorPos = -1f, _motorFrom, _motorT, _motorLen;  // Motorfader (-1 = steht)
        /// <summary>Wie schnell der VU-Zeiger zurueckgeht: 0 = wie ein echtes VU (0,3 s), 1 = langsam, 2 = sehr langsam.</summary>
        public static int VuFall = 1;
        /// <summary>VU-Meter: Schleppzeiger (merkt sich kurz den lautesten Punkt) und PEAK-Leuchte im Instrument.</summary>
        public static bool VuPeakNeedle = true, VuPeakLed = true;
        /// <summary>Motorfader: beim Laden einer Szene fahren die Fader sichtbar an ihre Stelle.</summary>
        public static bool MotorFaders = true;
        /// <summary>3.2: Tempo der Motorfader (1 = normal, 2 = langsam, 0,5 = schnell).</summary>
        public static float MotorSpeedFactor = 1f;
        /// <summary>3.2: feste Fahrzeit (s) fuer die naechsten StartMotor-Aufrufe (Szenen ueberblenden), 0 = normal.</summary>
        public static float MotorOverrideSec;
        /// <summary>4.0: ein Motorfader faehrt los (Fahrzeit in s, Weg 0..1) – fuer das Motor-Surren.</summary>
        public static event Action<float, float> MotorStarted;
        /// <summary>Kanalfarbe auf Faderkappen (und Knoepfen in den Kanaelen).</summary>
        public static bool ColorCaps = true;
        /// <summary>Zeitkonstante beim Zurueckgehen (Sekunden) je Stufe.</summary>
        public static readonly float[] VuFallTau = { 0f, 0.9f, 1.8f };
        /// <summary>MUTE blinkt, wenn stumm geschaltet ist, aber Ton ankommt.</summary>
        public static bool MuteBlink = true;
        /// <summary>dB-Spitzenwert ueber dem Pegel und dB-Skala daneben.</summary>
        public static bool PeakDb = true;
        private float _blinkT;
        private bool _blinkDark;
        private int _peakShown = int.MinValue;
        private bool _peakClip;
        protected RectangleF PeakRect;

        /// <summary>MUTE-Taste dieses Zugs (fuer das Blinken).</summary>
        protected virtual RectangleF MuteKey => RectangleF.Empty;
        /// <summary>Ist der Zug gerade stumm geschaltet?</summary>
        protected virtual bool MuteLit => false;
        private int _ghostVersion;
        protected readonly List<Zone> Zones = new List<Zone>();
        protected string HoverZone;

        private readonly PaintCache _chrome = new PaintCache();
        /// <summary>2.15.3: dasselbe Bild mit allen LEDs leuchtend – leuchtende Teile werden daraus kopiert statt LED fuer LED gezeichnet.</summary>
        private readonly PaintCache _lit = new PaintCache();
        private bool _litOk;
        /// <summary>Schnelles Zeichnen: LEDs aus dem Bildspeicher (OPTIONEN → Allgemein → Leistung).</summary>
        public static bool FastDraw = true;
        private readonly Rectangle[] _blit = new Rectangle[8];
        private int _blitN;
        private bool _chromeDirty = true;
        private int _litL = -1, _litR = -1, _holdL = -1, _holdR = -1, _preL = -1, _preR = -1, _pkL = -1, _pkR = -1;
        private bool _clipShown;

        protected RectangleF PanelRect, FaderArea, SlotRect, MeterL, MeterR, ClipRect, ReadoutRect, FaderHit;
        protected float TravelTop, TravelBottom, SlotX, CapW, CapH;
        private int _segH, _segGap, _segCount;

        private bool _dragging;
        private float _dragVirtual;
        private int _lastY;

        private readonly ToolTip _tip = PultTip.Make(new ToolTip { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 6000, UseAnimation = false, UseFading = false });
        private readonly Timer _tipTimer = new Timer { Interval = 550 };
        private string _tipZone;
        private Point _tipPoint;

        protected StripBase(MixerForm mixer)
        {
            Mixer = mixer;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = MixerForm.BodyColor;
            _tipTimer.Tick += (s, e) => ShowTip();
        }

        // --------------------------------------------------------------- Unterklassen
        public abstract int StripWidth { get; }
        protected abstract float FaderPos { get; }
        protected abstract void OnFaderMoved(float pos, bool final);
        protected abstract string ReadoutText { get; }
        protected abstract void LayoutTop(float w);
        protected abstract void DrawTop(Graphics g);
        protected abstract IEnumerable<KeyValuePair<float, string>> ScaleMarks { get; }
        protected virtual bool MasterCap => false;
        protected virtual Color AccentColor => Theme.TextDim;
        /// <summary>Farbe der Leuchtkante im Neon-Stil.</summary>
        protected virtual Color PanelTint => AccentColor;
        protected virtual float MeterBarWidth => 6f;
        protected virtual float DefaultFaderPos => float.NaN;

        // --------------------------------------------------------------- Fader sperren / Fader-Marke
        /// <summary>Fader gesperrt: Ziehen und Mausrad aendern nichts (Szenen, MIDI und Hotkeys schon).</summary>
        protected virtual bool FaderLocked => false;
        /// <summary>Fader-Marke (Lieblingsstellung 0..1) oder NaN.</summary>
        protected virtual float FaderMarkPos => float.NaN;
        /// <summary>3.2: hoechste erlaubte Faderstellung (Lautstaerke-Deckel) oder NaN.</summary>
        protected virtual float FaderLimitPos => float.NaN;

        /// <summary>3.2: Deckel als roter Strich quer ueber den Schlitz – hoeher geht der Fader nicht.</summary>
        private void DrawFaderLimit(Graphics g)
        {
            float m = FaderLimitPos;
            if (float.IsNaN(m) || m >= 0.999f) return;
            float y = (float)Math.Round(PosToY(Math.Max(0f, Math.Min(1f, m)))) + 0.5f;
            float x1 = SlotX - Theme.F(10), x2 = SlotX + Theme.F(10);
            using (var pen = new Pen(Theme.MeterRed, Math.Max(1.5f, Theme.F(2f))))
                g.DrawLine(pen, x1, y, x2, y);
            using (var pen = new Pen(Theme.Alpha(Theme.MeterRed, 120), Math.Max(1f, Theme.F(1f))))
            {
                g.DrawLine(pen, x1, y - Theme.F(3), x1, y + Theme.F(3));
                g.DrawLine(pen, x2, y - Theme.F(3), x2, y + Theme.F(3));
            }
        }
        /// <summary>Gesperrten Fader angefasst: kurz zeigen, warum nichts passiert.</summary>
        protected virtual void OnLockedTouch() { }
        public bool IsFaderLocked => FaderLocked;

        /// <summary>Kleines Schloss (fuer gesperrte Fader).</summary>
        protected static void DrawPadlock(Graphics g, RectangleF r, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float bw = r.Width * 0.8f, bh = r.Height * 0.52f;
            var body = new RectangleF(r.X + (r.Width - bw) / 2f, r.Bottom - bh, bw, bh);
            using (var pen = new Pen(c, Math.Max(1.2f, r.Width * 0.14f)))
                g.DrawArc(pen, r.X + r.Width * 0.27f, r.Y + r.Height * 0.04f, r.Width * 0.46f, r.Height * 0.66f, 180, 180);
            Theme.FillRound(g, c, body, Math.Max(1f, bw * 0.15f));
            g.SmoothingMode = sm;
        }
        protected virtual float WheelStep => 0.02f;
        protected virtual bool Dimmed => false;
        /// <summary>3.1: ganzen Zug abdunkeln (Deckkraft 0..255, 0 = nein).</summary>
        protected virtual int WholeDimAlpha => 0;
        protected virtual bool OnZoneMouseDown(string zone, MouseEventArgs e) => false;
        protected virtual bool OnZoneDoubleClick(string zone, MouseEventArgs e) => false;
        protected virtual bool OnZoneWheel(string zone, int delta) => false;
        protected virtual bool OnZoneDrag(string zone, int dy, bool fine) => false;
        protected virtual void OnZoneDragEnd(string zone) { }
        protected virtual string GetTooltip(string zone) => null;

        // ---- 3.4.5: an der rechten Kante breiter/schmaler ziehen
        /// <summary>Darf dieser Zug gerade an der rechten Kante in der Breite gezogen werden?</summary>
        protected virtual bool CanResizeWidth => false;
        /// <summary>3.4.5: so weit (logische Pixel) rueckt der Fader in breiten Zuegen nach rechts.</summary>
        protected virtual float FaderShift => 0f;
        /// <summary>Jetzige Breite in logischen Pixeln (bei 100 %).</summary>
        protected virtual int ResizeBaseWidth => (int)Math.Round(Width / Theme.F(1));
        /// <summary>Neue Breite (logisch). all = Strg gedrueckt (alle gleich), final = Maus losgelassen.</summary>
        protected virtual void OnResizeWidth(int logicalPx, bool all, bool final) { }
        /// <summary>Doppelklick auf die Kante: Breite zuruecksetzen.</summary>
        protected virtual void OnResizeReset(bool all) { }
        private bool _resizing;
        private int _resizeStartX, _resizeStartW, _resizeLast;
        private bool _onEdgeShown;
        /// <summary>Greifbereich an der rechten Kante (6 px, oben und unten etwas frei).</summary>
        private bool OnEdge(Point p) => CanResizeWidth && p.X >= Width - Theme.Px(8) && p.X < Width && p.Y > Theme.Px(12) && p.Y < Height - Theme.Px(12);
        /// <summary>3.4.6: Darf gerade gezogen werden? (fuer die Luecke zwischen den Zuegen)</summary>
        public bool ResizeAllowed => CanResizeWidth;
        /// <summary>3.4.6: Ziehen aus der Luecke rechts neben dem Zug beginnen (Maus ist gedrueckt).</summary>
        public bool StartResizeFromHost()
        {
            if (!CanResizeWidth) return false;
            _resizing = true;
            _resizeStartX = MousePosition.X;
            _resizeStartW = _resizeLast = ResizeBaseWidth;
            Capture = true;
            Cursor = Cursors.SizeWE;
            return true;
        }
        /// <summary>3.4.6: Doppelklick in die Luecke.</summary>
        public void ResetResizeFromHost(bool all) { if (CanResizeWidth) OnResizeReset(all); }
        protected virtual void DrawOverlay(Graphics g) { }

        /// <summary>Zone, an der man den Zug packen und per Maus verschieben kann (z. B. der Name).</summary>
        protected virtual bool IsMoveHandle(string zone) => false;

        /// <summary>Zonen, die echte Tasten sind (fuer das Tasten-Klicken).</summary>
        private static readonly System.Collections.Generic.HashSet<string> KeyZones = new System.Collections.Generic.HashSet<string>
            { "eq", "comp", "auto", "solo", "fade", "mute", "cap", "dim", "loud", "live", "fx", "soundboard" };
        /// <summary>Klick auf den Griff, ohne zu ziehen.</summary>
        protected virtual void OnHandleClick(string zone, MouseEventArgs e) { }

        // --------------------------------------------------------------- Layout
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        private void DoLayout()
        {
            float W = Width, H = Height;
            if (W < 10 || H < 10) return;
            Zones.Clear();

            // auf ganze Pixel, damit die Kanten scharf sind (bei 125 % usw. sonst halbe Pixel)
            float px1 = (float)Math.Round(Theme.F(1)), pr = (float)Math.Round(W - Theme.F(2)), pb = (float)Math.Round(H - Theme.F(4));
            PanelRect = new RectangleF(px1, px1, pr - px1, pb - px1);
            float readoutH = Theme.F(22);
            ReadoutRect = new RectangleF(Theme.F(8), PanelRect.Bottom - Theme.F(8) - readoutH, W - Theme.F(17), readoutH);
            float faderBottom = ReadoutRect.Y - Theme.F(8);
            if (HideReadout) { ReadoutRect = RectangleF.Empty; faderBottom = PanelRect.Bottom - Theme.F(8); }   // 4.0: Wert unter dem Fader ausgeblendet

            float top = Theme.F(TopSection);
            FaderArea = new RectangleF(Theme.F(4), top, W - Theme.F(8), Math.Max(Theme.F(40), faderBottom - top));

            CapW = Theme.F(Narrow ? 26 : 32); CapH = Theme.F(Theme.CapHeightPx);   // 3.1: Kappen-Form
            float meterW = Theme.F(Narrow ? 5 : MeterBarWidth), gap = Theme.F(2);
            float grW = GrShown ? Theme.F(Narrow ? 4 : 5) : 0f, grGap = GrShown ? Theme.F(2) : 0f;
            float meterRight = PanelRect.Right - Theme.F(Narrow ? 6 : 8) - grW - grGap;
            float meterX = meterRight - meterW * 2 - gap;
            SlotX = (float)Math.Round(Theme.F(10) + Theme.F(Narrow ? 18 : 26) + CapW / 2 + Theme.F(FaderShift));   // 3.4.5: breite Zuege – Fader mehr zur Mitte

            TravelTop = FaderArea.Y + Theme.F(12) + CapH / 2 - Theme.F(4);
            TravelBottom = FaderArea.Bottom - CapH / 2 - Theme.F(2);
            if (TravelBottom < TravelTop + 10) TravelBottom = TravelTop + 10;
            SlotRect = new RectangleF(SlotX - Theme.F(3), TravelTop - Theme.F(5), Theme.F(6), TravelBottom - TravelTop + Theme.F(10));
            FaderHit = new RectangleF(SlotX - CapW / 2 - Theme.F(6), FaderArea.Y, CapW + Theme.F(12), FaderArea.Height);

            // Pegel: Segmente
            Theme.StripSegSize(out int segH, out int segGap);   // fein / normal / grob
            _segH = segH;
            _segGap = segGap;
            float mTop = FaderArea.Y + Theme.F(12);
            float mBottom = FaderArea.Bottom - Theme.F(2);
            _segCount = Math.Max(8, (int)((mBottom - mTop + _segGap) / (_segH + _segGap)));
            float mh = _segCount * (_segH + _segGap) - _segGap;
            float mx = (float)Math.Round(meterX), mw = (float)Math.Round(meterW);
            MeterL = new RectangleF(mx, (float)Math.Round(mBottom - mh), mw, mh);
            MeterR = new RectangleF(mx + mw + (float)Math.Round(gap), MeterL.Y, mw, mh);
            ClipRect = new RectangleF(MeterL.X, FaderArea.Y + Theme.F(2), MeterR.Right - MeterL.X, Theme.F(6));
            // GR haengt von oben herab und ist kuerzer als der Pegel (knapp die Haelfte) – so verwechselt man sie nicht
            _grSegs = Math.Max(6, (int)(_segCount * 0.45f));
            GrRect = GrShown ? new RectangleF(MeterR.Right + (float)Math.Round(grGap), MeterL.Y, Math.Max(2f, (float)Math.Round(grW)), _grSegs * (_segH + _segGap) - _segGap) : RectangleF.Empty;
            float meterEnd = GrShown ? GrRect.Right : MeterR.Right;
            float pw = Theme.F(Narrow ? 24 : 27);
            PeakRect = new RectangleF((float)Math.Round(meterEnd + Theme.F(1) - pw), (float)Math.Round(FaderArea.Y + Theme.F(0.5f)), (float)Math.Round(pw), (float)Math.Round(Theme.F(10.5f)));

            // SIG/PEAK-Leuchten: eigene Zeile zwischen MUTE und Fader
            float lx = Theme.F(8), lw = W - Theme.F(17), lg = Theme.F(5);
            float ly = Theme.F(264 + DynRow + RowOffset - (Compact ? CutLen : 0f)), lh = Theme.F(15);
            float half = (float)Math.Floor((lw - lg) / 2f);
            var full = new RectangleF((float)Math.Round(lx), (float)Math.Round(ly), (float)Math.Round(lw), (float)Math.Round(lh));
            if (SignalLeds && PeakLeds)
            {
                _sigRect = new RectangleF(full.X, full.Y, half, full.Height);
                _pkRect = new RectangleF((float)Math.Round(lx + lw - half), full.Y, half, full.Height);
            }
            else
            {
                // nur eine Leuchte: sie bekommt die ganze Breite
                _sigRect = SignalLeds ? full : RectangleF.Empty;
                _pkRect = PeakLeds ? full : RectangleF.Empty;
            }

            int z0 = Zones.Count;
            _topFull = true;
            try { LayoutTop(W); }
            finally { _topFull = false; }
            if (Compact)
                for (int i = Zones.Count - 1; i >= z0; i--)
                {
                    var m = MapTop(Zones[i].Rect);
                    if (m.IsEmpty) Zones.RemoveAt(i); else Zones[i].Rect = m;
                }
            if (SignalLeds) AddZone(_sigRect, "sig", false);
            if (PeakLeds) AddZone(_pkRect, "peak", PeakCountOn);
            if (GrShown) AddZone(RectangleF.Inflate(GrRect, Theme.F(2), 0), "gr", false);
            if (Meter.HoldsUntilClickHere) AddZone(RectangleF.Inflate(RectangleF.Union(MeterL, MeterR), Theme.F(2), Theme.F(2)), "meter");
            LayoutKnob400();   // 4.0: Drehknopf-Ansicht
            Zones.Add(new Zone(FaderHit, "fader", true));
            InvalidateChrome();
        }

        protected void AddZone(RectangleF r, string id, bool clickable = true) => Zones.Add(new Zone(r, id, clickable));

        /// <summary>
        /// 2.16: Pult nicht zu sehen (minimiert, versteckt, hinter einem Vollbild-Spiel), die Pegel werden aber gebraucht
        /// (Meterbridge im Fenster): die Zuege rechnen weiter, zeichnen aber nicht.
        /// </summary>
        public static bool Offscreen;
        protected new void Invalidate(Rectangle rc) { if (!Offscreen) base.Invalidate(rc); }
        protected new void Invalidate() { if (!Offscreen) base.Invalidate(); }

        public void InvalidateChrome()
        {
            _chromeDirty = true;
            Invalidate();
        }

        // --------------------------------------------------------------- Zeichnen
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 10 || Height < 10) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { PaintStrip(e); }
            finally
            {
                _updN = -1;   // gilt nur fuer dieses WM_PAINT (z. B. nicht fuer DrawToBitmap)
                FrameStats.AddPaint(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            }
        }

        // ---- 2.15.3: nur zeichnen, was Windows gleich wirklich auf den Schirm bringt
        private readonly Rectangle[] _upd = new Rectangle[24];
        private int _updN = -1;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x000F /* WM_PAINT */ && FastDraw) _updN = PerfNative.UpdateRects(Handle, _upd);
            base.WndProc(ref m);
        }

        /// <summary>Muss dieser Bereich in diesem Bild neu gezeichnet werden?</summary>
        protected bool IsDirty(Rectangle r)
        {
            if (!_paintClip.IntersectsWith(r)) return false;
            if (_updN <= 0) return true;
            for (int i = 0; i < _updN; i++) if (_upd[i].IntersectsWith(r)) return true;
            return false;
        }
        private Rectangle _paintClip;

        private void PaintStrip(PaintEventArgs e)
        {
            if (!_chrome.Fits(Width, Height) || _chromeDirty)
                RenderChrome();

            var g = e.Graphics;
            var clip = e.ClipRectangle;
            _paintClip = clip;
            if (!FastDraw) _updN = -1;
            if (_updN > 0) _chrome.DrawRects(g, _upd, _updN, clip);   // nur die wirklich neuen Stellen
            else _chrome.Draw(g, clip);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;

            var meterBounds = MeterArea;
            meterBounds = RectangleF.Union(meterBounds, PeakDb ? PeakRect : ClipRect);
            if (Theme.LedHalo) meterBounds = RectangleF.Inflate(meterBounds, Theme.F(5), Theme.F(4));
            if (IsDirty(Rectangle.Ceiling(meterBounds)))
                DrawMeterLive(g, clip);
            if (AnyLeds && IsDirty(LedBounds))
                DrawLeds(g);
            if (GlowOn && _glowStep > 0 && IsDirty(GlowBounds))
                DrawGlow(g);
            if (VuShown && !VuReplaced && IsDirty(VuBounds))
                if (Theme.VuDual) Theme.DrawVuNeedle2(g, VuRect, _vuP, _vuP2, Dimmed, VuPeakLed ? (_vuLedShown ? 2 : 1) : 0);   // 3.1: L und R
                else Theme.DrawVuNeedle(g, VuRect, _vuP, Dimmed, VuPeakNeedle ? _vuPk : -1f, VuPeakLed ? (_vuLedShown ? 2 : 1) : 0);
            if (_greetShown && GreetingRunning && IsDirty(ReadoutBounds)) DrawGreeting(g);
            if (BalanceOn && _balShown != int.MinValue && IsDirty(BalanceBounds)) DrawBalance(g);   // 3.4.6
            PaintLiveExtra(g, clip);
            if (_sparkN > 0 && IsDirty(SparkBounds)) DrawSparks(g);   // 3.3
            if (ReferenceEquals(_keyFocus, this)) DrawKeyFocus(g);    // 3.4: per Tastatur gewaehlt
            PaintOverlays350(g);   // 3.5: Auswahl, Szenen-Vorschau, Lauflicht
            PaintOverlays400(g);   // 4.0: Suche, Ziel beim App-Ziehen
            PaintFinal400(g);      // 4.0 finale: Leuchtspur, Lichtshow, Vorschau
            if (FaderGlowOn && IsDirty(FaderGlowBounds)) DrawFaderGlow(g);
            float liveFader = LiveFaderPos;
            if (liveFader >= 0f && IsDirty(FaderBounds))
            {
                var st = g.Save();
                Theme.Setup(g);
                if (KnobView && _knobR > 0f) DrawKnob400(g, liveFader);   // 4.0
                else { DrawFaderFill(g, liveFader); DrawFaderCap(g, liveFader); }
                g.Restore(st);
            }
            if (CapGlowOn && _faderGlowQ >= 0 && IsDirty(CapGlowBounds)) DrawCapGlow(g);   // 3.3
            // MUTE leuchtet (live gezeichnet, damit es blinken kann)
            if (MuteBlink && MuteLit && !_blinkDark && !MapTop(MuteKey).IsEmpty && IsDirty(MuteBounds))
            {
                Theme.Setup(g);
                Theme.DrawKey(g, MapTop(MuteKey), "MUTE", true, Theme.MuteRed, HoverZone == "mute");
            }

            if (HoverLight && _hot && !_moving)
            {
                // Hover-Licht: der Zug unter der Maus wird leicht angestrahlt
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var pr = RectangleF.Inflate(PanelRect, -Theme.F(0.75f), -Theme.F(0.75f));
                using (var path = Theme.Round(pr, Theme.PanelRad))
                {
                    using (var b = new SolidBrush(Theme.IsLight ? Color.FromArgb(16, PanelTint) : Color.FromArgb(10, 255, 255, 255)))
                        g.FillPath(b, path);
                    using (var pen = new Pen(Theme.Alpha(Theme.IsLight ? Theme.Darken(PanelTint, 0.1f) : Theme.Lighten(PanelTint, 0.15f), 150), Theme.F(1.5f)))
                        g.DrawPath(pen, path);
                }
            }
            if (_moving)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Accent, Theme.F(2)))
                using (var path = Theme.Round(RectangleF.Inflate(PanelRect, -Theme.F(1), -Theme.F(1)), Theme.PanelRad))
                    g.DrawPath(pen, path);
            }
        }

        private Point _lampOrigin;
        private int _lampVer = -1;

        /// <summary>Lampe an und der Zug hat sich verschoben (Scrollen, Umsortieren) oder die Lampe geaendert: neu zeichnen.</summary>
        private void CheckLamp()
        {
            if (_lampVer != Theme.LampVersion) { InvalidateChrome(); return; }
            if ((Theme.LampOn || Theme.BgImage != null || Theme.TiltOn || Theme.SunOn) && Theme.FormOrigin(this) != _lampOrigin) InvalidateChrome();
        }

        private void RenderChrome()
        {
            _chrome.Ensure(Width, Height);   // unter Windows eine echte Windows-Zeichenflaeche: dort wird die Schrift richtig gemischt
            _chromeDirty = false;
            ChromeVer420++;   // 4.2: Spiegelung im Tisch neu
            _chrome.Render(g =>
            {
                Theme.Setup(g);
                Theme.FillBody(g, new RectangleF(0, 0, Width, Height), Theme.FormOrigin(this), MixerForm.BodyColor);   // Pult-Grund (auch eigenes Bild)
                // abends: Flaeche dunkler, leuchtende Tasten bleiben hell (Pegel, LEDs und Zeiger kommen ohnehin live darueber);
                // die Pultlampe legt ihr Licht passend zur Lage des Zugs im Fenster darueber
                _lampOrigin = Theme.FormOrigin(this);
                _lampVer = Theme.LampVersion;
                Theme.ExtraDim = WholeDimAlpha;   // 3.1: stummer/eingeklappter Zug insgesamt dunkler
                try
                {
                    Theme.WithNightDimAndLamp(g, PanelRect, Theme.PanelRad, new RectangleF(0, 0, Width, Height), _lampOrigin, () =>
                    {
                        Theme.DrawPanel(g, PanelRect, Theme.PanelRad, Dimmed ? Theme.Darken(PanelTint, 0.45f) : PanelTint);
                        DrawPanelExtra400(g);   // 4.0 finale: Kanal-Bild ueber den ganzen Zug
                        if (Theme.Wear > 0) Theme.DrawWear(g, PanelRect, WearSeed);   // 3.3: Gebrauchsspuren
                        DrawTopMapped(g);
                        if (VuShown && !VuReplaced) Theme.DrawVuFace(g, VuRect, Dimmed);
                        DrawFaderSection(g);
                        DrawOverlay(g);
                        DrawStickers340(g);   // 3.4: Aufkleber
                        if (!ReadoutRect.IsEmpty) Theme.DrawLcd(g, ReadoutRect, _greetShown ? "" : ReadoutText, Dimmed ? Theme.Darken(Theme.LcdText, 0.45f) : (Color?)null);   // Gruss: leere Anzeige, Schrift kommt live
                    });
                }
                finally { Theme.ExtraDim = 0; }
            });
            RenderLit();
        }

        /// <summary>Bildspeicher mit allen LEDs leuchtend (Untergrund = derselbe Zug, damit die Luecken stimmen).</summary>
        private void RenderLit()
        {
            _litOk = false;
            if (!FastDraw || _segCount <= 0 || MeterL.IsEmpty) return;
            if (!_lit.Fits(Width, Height)) _lit.Ensure(Width, Height);
            var area = Rectangle.Inflate(Rectangle.Ceiling(MeterArea), 2, 2);
            _lit.Render(g =>
            {
                _chrome.Draw(g, area);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;
                DrawSegments(g, MeterL, _segCount, false, _segCount, -1);
                DrawSegments(g, MeterR, _segCount, false, _segCount, -1);
                if (GrShown) DrawGrSegments(g, false, _grSegs);
            });
            _litOk = _lit.Ready;
        }

        private void AddBlit(RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0 || _blitN >= _blit.Length) return;
            _blit[_blitN++] = Rectangle.FromLTRB((int)Math.Floor(r.X), (int)Math.Floor(r.Y), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
        }

        /// <summary>Unterste 'lit' Segmente eines Pegels (ein Rechteck mit den Luecken dazwischen).</summary>
        private RectangleF LitColumn(RectangleF bar, int lit)
        {
            if (lit <= 0) return RectangleF.Empty;
            lit = Math.Min(lit, _segCount);
            float step = _segH + _segGap;
            float top = bar.Bottom - lit * step + _segGap;
            return new RectangleF(bar.X, top, bar.Width, bar.Bottom - top);
        }

        private RectangleF SegRect(RectangleF bar, int i)
        {
            float step = _segH + _segGap;
            return new RectangleF(bar.X, bar.Bottom - (i + 1) * step + _segGap, bar.Width, _segH);
        }

        /// <summary>Pegel live – leuchtende Teile aus dem Bildspeicher (schnell).</summary>
        /// <summary>Oberhalb davon (dB-Wert bzw. Clip-Leuchte) zeichnet der Pegel nichts – so muss der dB-Wert nicht jedes Bild neu.</summary>
        private int MeterCapTop => Math.Min((int)Math.Ceiling(PeakDb ? PeakRect.Bottom : ClipRect.Bottom) + 1, (int)Math.Floor(MeterL.Y) - 1);

        private void DrawMeterLiveFast(Graphics g, Rectangle paintClip)
        {
            g.SmoothingMode = SmoothingMode.None;
            int topL = Math.Max(_litL, _pkL), topR = Math.Max(_litR, _pkR);
            var clip = Rectangle.Inflate(Rectangle.Ceiling(MeterArea), 2, 2);
            // 1) leuchtende Saeulen (und GR) kopieren
            _blitN = 0;
            AddBlit(LitColumn(MeterL, _litL));
            AddBlit(LitColumn(MeterR, _litR));
            if (GrShown && _grLit > 0 && !GrRect.IsEmpty)
            {
                int n = Math.Min(_grLit, _grSegs);
                AddBlit(new RectangleF(GrRect.X, GrRect.Y, GrRect.Width, n * (_segH + _segGap) - _segGap));
            }
            int cols = _blitN;
            _lit.DrawRects(g, _blit, _blitN, clip);
            // 2) Lichtschein drumherum (nicht ueber die eben kopierten Saeulen)
            if (Theme.LedHalo && (topL > 0 || topR > 0))
            {
                var st = g.Save();
                g.SetClip(new Rectangle(0, MeterCapTop, Width, Math.Max(0, Height - MeterCapTop)), CombineMode.Intersect);
                for (int i = 0; i < cols; i++) g.SetClip(_blit[i], CombineMode.Exclude);
                DrawHalo(g, MeterL, topL);
                DrawHalo(g, MeterR, topR);
                g.Restore(st);
            }
            // 3) Rest live: grau hinterlegt (vor Fader), Nachgluehen, halb helle Spitze
            DrawPreSegments(g, MeterL, _segCount, topL, _preL);
            DrawPreSegments(g, MeterR, _segCount, topR, _preR);
            if (Theme.LedAfterglow) { DrawAfterglow(g, MeterL, topL, _agL); DrawAfterglow(g, MeterR, topR, _agR); }
            if (_pkL > _litL) DrawPkSegments(g, MeterL, _litL, _pkL, _holdL);
            if (_pkR > _litR) DrawPkSegments(g, MeterR, _litR, _pkR, _holdR);
            // 4) stehende Spitze obendrauf (auch aus dem Bildspeicher)
            _blitN = 0;
            if (_holdL >= _litL && _holdL >= 0 && _holdL < _segCount) AddBlit(SegRect(MeterL, _holdL));
            if (_holdR >= _litR && _holdR >= 0 && _holdR < _segCount) AddBlit(SegRect(MeterR, _holdR));
            if (_blitN > 0) _lit.DrawRects(g, _blit, _blitN, clip);
            // dB-Wert / Clip-Leuchte nur, wenn sie gerade mit neu gezeichnet werden muessen
            if (PeakDb) { if (IsDirty(Rectangle.Inflate(Rectangle.Ceiling(PeakRect), 1, 1))) DrawPeakBox(g); }
            else if (_clipShown && IsDirty(Rectangle.Ceiling(ClipRect))) g.FillRectangle(Seg(Theme.MeterRed), ClipRect);
            g.SmoothingMode = SmoothingMode.None;
            Theme.DrawMeterGlass(g, RectangleF.Inflate(MeterArea, Theme.F(1), Theme.F(1)));
        }

        /// <summary>"Beides": Spitze ueber dem Mittelwert halb hell (ohne die stehende Spitze).</summary>
        private void DrawPkSegments(Graphics g, RectangleF bar, int from, int to, int hold)
        {
            int n = _segCount;
            for (int i = Math.Max(0, from); i < Math.Min(n, to); i++)
            {
                if (i == hold) continue;
                var r = SegRect(bar, i);
                Theme.FillSeg(g, Seg(Theme.Mix(Theme.SegOff, SegColor((i + 1f) / n), 0.5f)), r.X, r.Y, r.Width, r.Height);
            }
        }

        private void DrawFaderSection(Graphics g)
        {
            if (KnobView && _knobR > 0f) { DrawKnobSection(g, LiveFaderPos >= 0f); return; }   // 4.0
            // Skala
            var font = Theme.Ui(8.5f, FontStyle.Bold);
            using (var tickPen = new Pen(Theme.ScaleTick, Theme.F(1)))
            using (var labelBrush = new SolidBrush(Theme.ScaleLabel))
            using (var zeroBrush = new SolidBrush(Theme.Text))
            {
                foreach (var m in ScaleMarks)
                {
                    if (HideScale) break;   // 4.0: dB-Skala ausgeblendet
                    float y = PosToY(m.Key);
                    g.DrawLine(tickPen, SlotX - Theme.F(13), y, SlotX - Theme.F(6), y);
                    g.DrawLine(tickPen, SlotX + Theme.F(6), y, SlotX + Theme.F(13), y);
                    var lr = new RectangleF(PanelRect.X + Theme.F(2), y - Theme.F(7), SlotX - Theme.F(17) - PanelRect.X - Theme.F(2), Theme.F(14));
                    Theme.Str(g, m.Value, font, m.Value == "0" ? zeroBrush : labelBrush, lr, Theme.Right);
                }
            }

            // Faderschlitz
            Theme.DrawSlot(g, SlotRect);
            // Fuellung + Kappe kommen im Selbsttest live dazu (sonst muesste jedes Bild der ganze Zug neu gezeichnet werden)
            bool liveCap = LiveFaderPos >= 0f;
            if (!liveCap) DrawFaderFill(g, ShownFaderPos);
            DrawFaderMark(g);
            DrawFaderLimit(g);   // 3.2: Lautstaerke-Deckel

            // Pegel-Hintergrund (unbeleuchtete Segmente)
            Theme.DrawMeterBezel(g, RectangleF.Inflate(MeterArea, Theme.F(2), Theme.F(2)));
            if (GrShown) DrawGrSegments(g, true, 0);
            DrawSegments(g, MeterL, _segCount, true, 0, -1);
            DrawSegments(g, MeterR, _segCount, true, 0, -1);
            if (!PeakDb) Theme.FillRound(g, Theme.Mix(Theme.SegOff, Theme.MeterRed, 0.18f), ClipRect, Theme.F(1.5f));
            else if (!Narrow) DrawMeterScale(g);

            // Kappe
            if (!liveCap) DrawFaderCap(g, ShownFaderPos);
        }

        /// <summary>Fader-Marke: kleine Dreiecke links und rechts vom Schlitz in Kanalfarbe.</summary>
        private void DrawFaderMark(Graphics g)
        {
            float m = FaderMarkPos;
            if (float.IsNaN(m)) return;
            float y = (float)Math.Round(PosToY(Math.Max(0f, Math.Min(1f, m)))) + 0.5f;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = Theme.IsLight ? Theme.Darken(AccentColor, 0.15f) : Theme.Lighten(AccentColor, 0.1f);
            float s = Theme.F(4.2f), x1 = SlotX - Theme.F(14), x2 = SlotX + Theme.F(14);
            using (var b = new SolidBrush(c))
            {
                g.FillPolygon(b, new[] { new PointF(x1 - s, y - s), new PointF(x1 + Theme.F(0.5f), y), new PointF(x1 - s, y + s) });
                g.FillPolygon(b, new[] { new PointF(x2 + s, y - s), new PointF(x2 - Theme.F(0.5f), y), new PointF(x2 + s, y + s) });
            }
            using (var pen = new Pen(Theme.Alpha(c, 150), Math.Max(1f, Theme.F(1f))))
                g.DrawLine(pen, x1 + Theme.F(1), y, x2 - Theme.F(1), y);
            g.SmoothingMode = sm;
        }

        /// <summary>Fuellung im Schlitz in Kanalfarbe (bis zur Faderposition).</summary>
        private void DrawFaderFill(Graphics g, float pos)
        {
            float capY = PosToY(pos);
            var fill = new RectangleF(SlotX - Theme.F(1), capY, Theme.F(2), TravelBottom - capY + Theme.F(3));
            if (fill.Height > 0)
                using (var b = new SolidBrush(Theme.Alpha(AccentColor, Dimmed ? 60 : 150))) g.FillRectangle(b, fill);
        }

        private void DrawFaderCap(Graphics g, float pos)
        {
            float capY = PosToY(pos);
            var cap = new RectangleF(SlotX - CapW / 2, capY - CapH / 2, CapW, CapH);
            Theme.DrawFaderCap(g, cap, MasterCap, HoverZone == "fader" || _dragging, AccentColor);
            if (ColorCaps && !MasterCap && CapColor.A > 0)
            {
                if (Theme.TwoTone && CapColor2.A > 0) Theme.DrawCapInsert2(g, cap, CapColor, CapColor2);   // 3.4: zweifarbig
                else Theme.DrawCapInsert(g, cap, CapColor);
            }
            if (Theme.Wear > 0) Theme.DrawCapWear(g, cap);   // 3.3: abgegriffen
            DrawCapExtra420(g, cap);   // 4.2: App-Symbol auf der Kappe
        }

        private Rectangle FaderBounds => Rectangle.Ceiling(RectangleF.Inflate(FaderHit, Theme.F(6), Theme.F(6)));

        protected float PosToY(float pos) => TravelBottom - pos * (TravelBottom - TravelTop);
        private float YToPos(float y) => (TravelBottom - y) / (TravelBottom - TravelTop);

        /// <summary>Farbe der Einlage in der Faderkappe (Kanalfarbe); durchsichtig = keine.</summary>
        protected virtual Color CapColor => AccentColor;

        /// <summary>Farbe fuer Pegel in der Einstellung "Kanalfarbe".</summary>
        protected virtual Color MeterTint => AccentColor;
        public Color MeterTintColor => MeterTint;

        private Color SegColor(float frac) =>
            KScale == 1 ? Theme.SegColor(frac, MeterTint, MeterBallistics.Iec(-14f), MeterBallistics.Iec(-10f))   // 3.4.6: K-14 – gruen bis K0, gelb bis K+4
            : KScale == 2 ? Theme.SegColor(frac, MeterTint, MeterBallistics.Iec(-20f), MeterBallistics.Iec(-16f)) // K-20
            : Theme.SegColor(frac, MeterTint);

        // ---- 3.4.6 Pegel
        /// <summary>K-Skala dieses Zugs: 0 normal, 1 K-14, 2 K-20 (nur MASTER/Ausgaenge).</summary>
        protected virtual int KScale => 0;
        /// <summary>L/R-Balance unter den Pegeln (OPTIONEN → Leuchten &amp; Anzeigen).</summary>
        public static bool BalanceOn;
        /// <summary>Doppelklick auf einen Pegel oeffnet ihn gross (Pegel-Lupe).</summary>
        public static bool MeterLupeOn;
        /// <summary>Zuletzt angezeigter Pegel (linear, nach Fader) – fuer die Pegel-Lupe.</summary>
        public float LastPeakL, LastPeakR;
        /// <summary>Farbe fuer die Pegel-Lupe.</summary>
        public Color LupeColor => AccentColor;
        private float _bal;          // -1 links … +1 rechts (geglaettet)
        private int _balShown = int.MinValue;
        private Rectangle BalanceBounds => Rectangle.Ceiling(new RectangleF(MeterL.X - Theme.F(1), FaderArea.Bottom + Theme.F(1), MeterR.Right - MeterL.X + Theme.F(2), Theme.F(5)));
        /// <summary>Pegel vor Fader/Stumm (linear) aus dem Pegel-Takt – z. B. fuer die Reserve am Ausgang.</summary>
        protected virtual void OnPreLevel(float pre, float dt) { }

        private void UpdateBalance(float l, float r, float dt)
        {
            if (!BalanceOn) { if (_balShown != int.MinValue) { _balShown = int.MinValue; Invalidate(BalanceBounds); } return; }
            float target = 0f;
            float mx = Math.Max(l, r);
            if (mx > 0.003f)
            {
                float dl = 20f * (float)Math.Log10(Math.Max(1e-5f, l)), dr = 20f * (float)Math.Log10(Math.Max(1e-5f, r));
                target = Math.Max(-1f, Math.Min(1f, (dr - dl) / 12f));   // ±12 dB = ganz aussen
            }
            float k = Math.Min(1f, Math.Max(0f, dt) * (mx > 0.003f ? 6f : 2f));
            _bal += (target - _bal) * k;
            var bb = BalanceBounds;
            int px = (int)Math.Round(_bal * (bb.Width / 2f - Theme.F(3)));
            if (px != _balShown) { _balShown = px; Invalidate(bb); }
        }

        private void DrawBalance(Graphics g)
        {
            var bb = BalanceBounds;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(bb.X, bb.Y + Theme.F(1.5f), bb.Width, Theme.F(2));
            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(b, track);
            float cx = bb.X + bb.Width / 2f;
            using (var p = new Pen(Theme.ScaleTick, 1f)) g.DrawLine(p, cx, bb.Y, cx, bb.Bottom);
            float x = cx + (_balShown == int.MinValue ? 0 : _balShown);
            Color c = Math.Abs(_bal) > 0.5f ? Theme.SoloAmber : Theme.LedGreen;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x - Theme.F(2.5f), bb.Y, Theme.F(5), Theme.F(5));
            g.SmoothingMode = sm;
        }

        private static Color SegColorClassic(float frac)
        {
            if (frac <= 0.55f) return Theme.MeterGreen;   // bis -18 dBFS
            if (frac <= 0.85f) return Theme.MeterYellow;  // bis -6 dBFS
            return Theme.MeterRed;
        }

        /// <summary>Ein Pinsel fuer alle Segmente (nur im UI-Faden) – statt fuer jedes Segment jedes Bild einen neuen anzulegen.</summary>
        private static readonly SolidBrush SegBrush = new SolidBrush(Color.Black);

        private static SolidBrush Seg(Color c)
        {
            SegBrush.Color = c;
            return SegBrush;
        }

        /// <summary>Grau hinterlegte Segmente von 'from' bis 'to' (Pegel vor Fader/Stumm).</summary>
        private void DrawPreSegments(Graphics g, RectangleF bar, int n, int from, int to)
        {
            if (!Theme.GhostOn) return;
            float step = _segH + _segGap;
            for (int i = Math.Max(0, from); i < Math.Min(n, to); i++)
            {
                float y = bar.Bottom - (i + 1) * step + _segGap;
                Theme.FillSeg(g, Seg(Theme.Mix(Theme.MeterGhost, SegColor((i + 1f) / n), 0.14f)), bar.X, y, bar.Width, _segH);
            }
        }

        private void DrawSegments(Graphics g, RectangleF bar, int n, bool dim, int lit, int hold, int pk = 0)
        {
            float step = _segH + _segGap;
            for (int i = 0; i < n; i++)
            {
                bool on = !dim && i < lit;
                bool isHold = !dim && i == hold;
                bool isPk = !dim && !on && i < pk;   // "Beides": Spitze ueber dem Mittelwert, halb hell
                if (!dim && !on && !isHold && !isPk) continue;
                float y = bar.Bottom - (i + 1) * step + _segGap;
                var c = SegColor((i + 1f) / n);
                Theme.FillSeg(g, Seg(dim ? Theme.Mix(Theme.SegOff, c, Dimmed ? 0.06f : 0.13f) : isPk && !isHold ? Theme.Mix(Theme.SegOff, c, 0.5f) : c), bar.X, y, bar.Width, _segH);
            }
        }

        private void DrawMeterLive(Graphics g, Rectangle paintClip)
        {
            if (FastDraw && _litOk && _lit.Fits(Width, Height)) { DrawMeterLiveFast(g, paintClip); return; }
            g.SmoothingMode = SmoothingMode.None;
            if (Theme.LedHalo) { DrawHalo(g, MeterL, Math.Max(_litL, _pkL)); DrawHalo(g, MeterR, Math.Max(_litR, _pkR)); }
            DrawPreSegments(g, MeterL, _segCount, Math.Max(_litL, _pkL), _preL);
            DrawPreSegments(g, MeterR, _segCount, Math.Max(_litR, _pkR), _preR);
            if (Theme.LedAfterglow) { DrawAfterglow(g, MeterL, Math.Max(_litL, _pkL), _agL); DrawAfterglow(g, MeterR, Math.Max(_litR, _pkR), _agR); }
            DrawSegments(g, MeterL, _segCount, false, _litL, _holdL, _pkL);
            DrawSegments(g, MeterR, _segCount, false, _litR, _holdR, _pkR);
            if (PeakDb) DrawPeakBox(g);
            else if (_clipShown) g.FillRectangle(Seg(Theme.MeterRed), ClipRect);
            if (GrShown) DrawGrSegments(g, false, _grLit);
            g.SmoothingMode = SmoothingMode.None;
            Theme.DrawMeterGlass(g, RectangleF.Inflate(MeterArea, Theme.F(1), Theme.F(1)));
        }

        /// <summary>Lichtschein um den leuchtenden Teil eines Pegels (Farbe des obersten leuchtenden Segments).</summary>
        private void DrawHalo(Graphics g, RectangleF bar, int lit)
        {
            if (lit <= 0) return;
            float step = _segH + _segGap;
            float top = bar.Bottom - Math.Min(_segCount, lit) * step + _segGap;
            Theme.DrawLedHalo(g, new RectangleF(bar.X, top, bar.Width, bar.Bottom - top), SegColor(Math.Min(_segCount, lit) / (float)_segCount));
        }

        /// <summary>Nachgluehen: eben ausgegangene Segmente glimmen schwaecher werdend nach.</summary>
        private void DrawAfterglow(Graphics g, RectangleF bar, int lit, float ag)
        {
            int n = _segCount;
            float agN = ag * n;
            if (agN <= lit + 0.05f) return;
            float step = _segH + _segGap;
            int top = Math.Min(n, (int)Math.Ceiling(agN));
            for (int i = Math.Max(0, lit); i < top; i++)
            {
                float f = (agN - i) / Math.Max(1f, agN - lit);
                f *= Math.Min(1f, agN - i);
                if (f <= 0.02f) continue;
                float y = bar.Bottom - (i + 1) * step + _segGap;
                Theme.FillSeg(g, Seg(Theme.Mix(Theme.SegOff, SegColor((i + 1f) / n), 0.12f + 0.5f * f)), bar.X, y, bar.Width, _segH);
            }
        }

        // Nachgluehen (Anteil 0..1 je Seite) und was davon zuletzt gezeigt wurde
        private float _agL, _agR;
        private int _agShownL = -1, _agShownR = -1;

        // ---- Leuchtende Fader-Spur
        /// <summary>Faderschlitz leuchtet in der Kanalfarbe, heller mit dem Pegel (OPTIONEN → Aussehen).</summary>
        public static bool FaderGlowOn = true;
        private int _faderGlowQ = -1;
        private float _faderGlow;
        private Rectangle FaderGlowBounds => Rectangle.Ceiling(RectangleF.Inflate(SlotRect, Theme.F(4), Theme.F(3)));

        private void DrawFaderGlow(Graphics g)
        {
            if (SlotRect.IsEmpty || _faderGlowQ < 0) return;
            float lv = _faderGlow;
            Color c = Dimmed ? Theme.Darken(MeterTint, 0.4f) : MeterTint;
            float pos = LiveFaderPos >= 0f ? LiveFaderPos : ShownFaderPos;
            float capY = PosToY(pos);
            var cap = new RectangleF(SlotX - CapW / 2 - Theme.F(1), capY - CapH / 2 - Theme.F(1), CapW + Theme.F(2), CapH + Theme.F(6));
            var st = g.Save();
            g.SetClip(Rectangle.Ceiling(cap), CombineMode.Exclude);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int a = (int)(14 + 170 * lv);
            Theme.FillRound(g, Color.FromArgb(a / 5, c), RectangleF.Inflate(SlotRect, Theme.F(2.5f), Theme.F(1)), Theme.F(4));
            Theme.FillRound(g, Color.FromArgb(a / 3, c), SlotRect, Theme.F(3));
            // unter der Kappe (das, was "offen" ist) heller
            var below = new RectangleF(SlotRect.X + Theme.F(1), capY, SlotRect.Width - Theme.F(2), Math.Max(0f, SlotRect.Bottom - capY - Theme.F(1)));
            if (below.Height > 0) Theme.FillRound(g, Color.FromArgb(Math.Min(255, a + 40), Theme.Lighten(c, 0.2f)), below, Theme.F(2));
            g.SmoothingMode = sm;
            g.Restore(st);
        }

        // ---- 2.16: Pegel an den Ton anpassen (Funk-/Bluetooth-Kopfhoerer spielen spaeter ab)
        /// <summary>So viele ms zeigen die Pegel den Ton spaeter (0 = sofort).</summary>
        public static int MeterDelayMs;
        /// <summary>Dieser Zug zeigt immer sofort (Mikro – das hoert man nicht ueber die Kopfhoerer).</summary>
        protected virtual bool NoMeterDelay => false;
        private static readonly Stopwatch DelayClock = Stopwatch.StartNew();
        private const int DelayCap = 160;
        private readonly double[] _dlyT = new double[DelayCap];
        private readonly float[] _dlyV = new float[DelayCap * 4];
        private int _dlyHead, _dlyCount;

        /// <summary>Werte merken und die von vor MeterDelayMs zurueckgeben.</summary>
        private void DelayPeaks(ref float l, ref float r, ref float pl, ref float pr)
        {
            double now = DelayClock.Elapsed.TotalMilliseconds;
            int w = (_dlyHead + _dlyCount) % DelayCap;
            if (_dlyCount == DelayCap) { _dlyHead = (_dlyHead + 1) % DelayCap; _dlyCount--; }
            _dlyT[w] = now; _dlyV[w * 4] = l; _dlyV[w * 4 + 1] = r; _dlyV[w * 4 + 2] = pl; _dlyV[w * 4 + 3] = pr;
            _dlyCount++;
            double target = now - MeterDelayMs;
            // neuesten Eintrag nehmen, der alt genug ist; aeltere verwerfen
            int use = -1;
            for (int k = 0; k < _dlyCount; k++)
            {
                int i = (_dlyHead + k) % DelayCap;
                if (_dlyT[i] <= target) use = k; else break;
            }
            if (use < 0) { l = r = pl = pr = 0f; return; }   // noch nicht genug Vorlauf (gleich nach dem Start)
            int u = (_dlyHead + use) % DelayCap;
            l = _dlyV[u * 4]; r = _dlyV[u * 4 + 1]; pl = _dlyV[u * 4 + 2]; pr = _dlyV[u * 4 + 3];
            _dlyHead = u; _dlyCount -= use;
        }

        /// <summary>Pegel (L, R) und – bei Kanaelen – die GR-Spalte daneben.</summary>
        private RectangleF MeterArea => GrShown ? RectangleF.Union(RectangleF.Union(MeterL, MeterR), GrRect) : RectangleF.Union(MeterL, MeterR);

        /// <summary>Pegel aktualisieren (aus dem UI-Timer). Zeichnet nur neu, wenn sich sichtbar etwas aendert.</summary>
        public void UpdateMeter(float peakL, float peakR, float dt) => UpdateMeter(peakL, peakR, peakL, peakR, dt);

        /// <summary>Pegel nach (farbig) und vor Fader/Stumm (grau hinterlegt).</summary>
        public void UpdateMeter(float peakL, float peakR, float preL, float preR, float dt)
        {
            if (MeterDelayMs > 0 && !NoMeterDelay) DelayPeaks(ref peakL, ref peakR, ref preL, ref preR);
            if (PowerOffT >= 0f) { peakL = peakR = preL = preR = 0f; }   // 3.3: Ausschalt-Show – alles faellt
            OnMeterLevel(Math.Max(peakL, peakR), dt);   // 2.17: z. B. Pegel-Verlauf im Anzeigefeld
            OnPreLevel(Math.Max(Math.Max(preL, preR), Math.Max(peakL, peakR)), dt);   // 3.4.6: z. B. Reserve am Ausgang
            LastPeakL = peakL; LastPeakR = peakR;
            UpdateBalance(peakL, peakR, dt);
            // Selbsttest beim Start: Anzeige laeuft einmal durch, Fader fahren hoch
            float t = TestTime();
            float test = t >= 0f ? TestLevel(t) : -1f;
            _testS = test;
            if (t >= 0f || PowerOffT >= 0f)
            {
                float fp = PowerOffT >= 0f ? FaderPos * (1f - Ease(PowerOffT / 0.8f))   // 3.3: Fader fahren runter
                         : BigShow ? BigShowFader(t, FaderPos) : FaderPos * Ease((t - 0.08f) / 1.0f);   // 3.1: grosse Show – ganz hoch, dann an die Stelle
                if (!_testShown) { _testShown = true; _testFader = fp; InvalidateChrome(); }   // Kappe raus aus dem Bild-Cache
                else if (Math.Abs(fp - _testFader) > 0.0005f) { _testFader = fp; Invalidate(FaderBounds); }
            }
            else if (_testShown)
            {
                // Test vorbei: alles wieder mit echten Werten zeichnen
                _testShown = false; _testFader = -1f;
                _litL = -2; _peakShown = int.MinValue + 1;
                InvalidateChrome();
            }

            CheckLamp();
            // SIG/PEAK: zeigen, was ankommt – vor Fader und Stumm (wie am echten Mischpult)
            UpdateLeds(Math.Max(Math.Max(preL, preR), Math.Max(peakL, peakR)), Math.Max(peakL, peakR), dt, test);
            if (_hot && !_moving && !Capture)
            {
                // Maus hat den Zug verlassen, ohne dass es gemeldet wurde (z. B. ueber ein Eingabefeld hinaus)
                bool inside = true;
                try { inside = ClientRectangle.Contains(PointToClient(MousePosition)); } catch { }
                if (!inside) SetHot(false);
            }
            Meter.Update(peakL, peakR, dt);
            UpdatePeakAndBlink(dt);
            PreMeter.Update(Math.Max(preL, peakL), Math.Max(preR, peakR), dt);
            UpdateVu(peakL, peakR, dt, test);
            UpdateBridgeVu(peakL, peakR, dt, test);
            UpdateMotor(dt);
            UpdateGreeting();
            UpdateGr(dt, test);
            int n = _segCount;
            if (n <= 0) return;
            int litL = (int)(Meter.FracL * n + 0.5f), litR = (int)(Meter.FracR * n + 0.5f);
            int preLitL = (int)(PreMeter.FracL * n + 0.5f), preLitR = (int)(PreMeter.FracR * n + 0.5f);
            if (_ghostVersion != GhostVersion) { _ghostVersion = GhostVersion; _preL = -2; } // Helligkeit geaendert -> neu zeichnen
            int holdL = Meter.HoldFracL > 0.02f ? Math.Min(n - 1, (int)(Meter.HoldFracL * n + 0.5f) - 1) : -1;
            int holdR = Meter.HoldFracR > 0.02f ? Math.Min(n - 1, (int)(Meter.HoldFracR * n + 0.5f) - 1) : -1;
            int pkL = (int)(Meter.PeakFracL * n + 0.5f), pkR = (int)(Meter.PeakFracR * n + 0.5f);
            bool clip = Meter.Clip;
            if (test >= 0f)
            {
                litL = litR = preLitL = preLitR = (int)(test * n + 0.5f);
                holdL = holdR = -1;
                pkL = pkR = 0;
                clip = false;
            }
            UpdateGlow(test >= 0f ? test : Math.Max(Meter.FracL, Meter.FracR));
            // Nachgluehen: folgt nach oben sofort, sinkt langsam
            float topL = Math.Max(litL, pkL) / (float)n, topR = Math.Max(litR, pkR) / (float)n;
            if (Theme.LedAfterglow && test < 0f)
            {
                _agL = Math.Max(topL, _agL - 1.6f * Math.Min(dt, 0.1f));
                _agR = Math.Max(topR, _agR - 1.6f * Math.Min(dt, 0.1f));
            }
            else { _agL = topL; _agR = topR; }
            int agL = (int)(_agL * n * 4), agR = (int)(_agR * n * 4);
            // Fader-Spur: Helligkeit in Stufen
            if (_sparkN > 0) TickSparks(dt);   // 3.3
            if (FaderGlowOn || CapGlowOn)
            {
                float lv = test >= 0f ? test : Math.Max(Meter.FracL, Meter.FracR);
                _faderGlow += (lv - _faderGlow) * Math.Min(1f, dt * (lv > _faderGlow ? 20f : 5f));
                int q = (int)(_faderGlow * 16);
                if (q != _faderGlowQ) { _faderGlowQ = q; if (FaderGlowOn) Invalidate(FaderGlowBounds); if (CapGlowOn) Invalidate(CapGlowBounds); }
            }
            else if (_faderGlowQ >= 0) { _faderGlowQ = -1; Invalidate(FaderGlowBounds); }
            if (litL != _litL || litR != _litR || holdL != _holdL || holdR != _holdR || clip != _clipShown || preLitL != _preL || preLitR != _preR || pkL != _pkL || pkR != _pkR ||
                agL != _agShownL || agR != _agShownR)
            {
                bool clipChanged = clip != _clipShown;
                if (clipChanged && clip && SparksOn && test < 0f) SpawnSparks();   // 3.3: Funken
                _litL = litL; _litR = litR; _holdL = holdL; _holdR = holdR; _clipShown = clip; _preL = preLitL; _preR = preLitR; _pkL = pkL; _pkR = pkR;
                _agShownL = agL; _agShownR = agR;
                if (FastDraw)
                {
                    // nur der Pegel (mit Lichtschein) – der dB-Wert darueber meldet sich selbst, wenn er sich aendert
                    var r = Rectangle.Inflate(Rectangle.Ceiling(MeterArea), Theme.LedHalo ? Theme.Px(6) : 1, Theme.LedHalo ? Theme.Px(5) : 1);
                    int cap = MeterCapTop;
                    if (r.Top < cap) r = Rectangle.FromLTRB(r.Left, cap, r.Right, r.Bottom);
                    if (clipChanged) r = Rectangle.Union(r, Rectangle.Inflate(Rectangle.Ceiling(ClipRect), 1, 1));
                    Invalidate(r);
                }
                else
                {
                    var r = RectangleF.Union(MeterArea, ClipRect);
                    Invalidate(Rectangle.Inflate(Rectangle.Ceiling(r), Theme.LedHalo ? Theme.Px(6) : 1, Theme.LedHalo ? Theme.Px(5) : 1));
                }
            }
        }

        // --------------------------------------------------------------- SIG / PEAK
        // Wie am echten Mischpult: SIG (gruen) = hier kommt Signal an – auch wenn der Kanal stumm oder leise ist.
        // PEAK (rot) = dieser Kanal ist so laut, dass es gleich uebersteuert (ab -2 dBFS, nach dem Fader – also das,
        // was wirklich in den Mix geht). Bleibt kurz an, damit man es sicher sieht.

        private Rectangle LedBounds
        {
            get
            {
                var r = _sigRect.IsEmpty ? _pkRect : _pkRect.IsEmpty ? _sigRect : RectangleF.Union(_sigRect, _pkRect);
                return Rectangle.Ceiling(RectangleF.Inflate(r, Theme.F(5), Theme.F(5)));
            }
        }

        private void DrawLeds(Graphics g)
        {
            var sm = g.SmoothingMode;
            bool narrow = Math.Min(_sigRect.IsEmpty ? 999f : _sigRect.Width, _pkRect.IsEmpty ? 999f : _pkRect.Width) < Theme.F(40);
            float px = narrow ? 8f : 8.5f;
            if (SignalLeds && !_sigRect.IsEmpty) Theme.DrawLens(g, _sigRect, "SIG", Theme.KeyLit("SIG", Theme.LedGreen), _ledOn, px);
            if (PeakLeds && !_pkRect.IsEmpty) Theme.DrawLens(g, _pkRect, PeakLabel(g, px), Theme.KeyLit("PEAK", Theme.MeterRed), _peakLedOn, px);
            g.SmoothingMode = sm;
        }

        /// <summary>SIG an ab -50 dBFS (vor dem Fader), PEAK ab -2 dBFS (nach dem Fader). Beide gehen erst nach einer kurzen Pause aus, damit nichts flimmert.</summary>
        private void UpdateLeds(float peak, float post, float dt, float test)
        {
            if (dt > 0.2f) dt = 0.2f;
            bool sig, pk;
            if (test >= 0f) { sig = test > 0.06f; pk = test > 0.9f; _ledT = 0f; _peakLedT = 0f; }
            else
            {
                if (peak >= _sigLin) { sig = true; _ledT = 0.3f; }
                else { _ledT -= dt; sig = _ledT > 0; }
                if (post >= _peakLin) { pk = true; _peakLedT = 1.0f; }
                else { _peakLedT -= dt; pk = _peakLedT > 0; }
            }
            if (test < 0f && pk && !_peakLedOn && PeakCountOn && _peakCount < 99999) _peakCount++;   // neu zu laut: mitzaehlen (nicht im Selbsttest)
            if (test < 0f && pk && !_peakLedOn && ClipLogOn) Mixer.NoteClip(this, post);   // 3.5: Uebersteuerungs-Liste
            if (sig != _ledOn || pk != _peakLedOn)
            {
                _ledOn = sig; _peakLedOn = pk;
                if (AnyLeds) Invalidate(LedBounds);
            }
        }

        // --------------------------------------------------------------- PEAK-Zaehler

        private int _peakCount;
        /// <summary>Wie oft PEAK seit dem letzten Zuruecksetzen angegangen ist.</summary>
        public int PeakCount => _peakCount;

        /// <summary>Gehaltene Spitze loeschen (Pegel und Meterbridge zeigen danach wieder den aktuellen Wert).</summary>
        public void ResetMeterHold()
        {
            Meter.ResetHold();
            PreMeter.ResetHold();
            _holdL = _holdR = -2;
            _peakShown = int.MinValue + 1;
            Invalidate(Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(MeterArea, ClipRect)), 1, 1));
            if (PeakDb) Invalidate(Rectangle.Inflate(Rectangle.Ceiling(PeakRect), 1, 1));
        }

        public void ResetPeakCount()
        {
            if (_peakCount == 0) return;
            _peakCount = 0;
            if (AnyLeds) Invalidate(LedBounds);
        }

        /// <summary>Aufschrift der PEAK-Leuchte: "PEAK", mit Zaehler "PEAK 3" (bzw. nur "3", wenn es zu schmal ist).</summary>
        private string PeakLabel(Graphics g, float px)
        {
            if (!PeakCountOn || _peakCount == 0) return "PEAK";
            string n = _peakCount > 999 ? "999+" : _peakCount.ToString();
            var font = Theme.Ui(px, FontStyle.Bold);
            float room = _pkRect.Width - Theme.F(8);
            foreach (var t in new[] { "PEAK " + n, "PK " + n })
                if (Theme.TextWidth(g, t, font) <= room) return t;
            return n;
        }

        // --------------------------------------------------------------- GR (Absenkung)
        // Wie am echten Pult: orange LEDs neben dem Pegel, die von oben nach unten wachsen –
        // so viel dB machen Kompressor, AUTO, Ducking, "Spiel im Hintergrund leiser" und Sleep-Timer den Kanal gerade leiser.

        /// <summary>Hat dieser Zug eine GR-Spalte (nur Kanaele)?</summary>
        protected virtual bool HasGr => false;
        /// <summary>Aktuelle Absenkung in dB (positiv).</summary>
        protected virtual float GrTarget => 0f;
        /// <summary>Tooltip mit der Aufschluesselung.</summary>
        protected virtual string GrTooltip => null;
        private bool GrShown => GrOn && HasGr;
        protected RectangleF GrRect;
        private float _grDb;
        private int _grLit = -1, _grSegs = 8;
        /// <summary>So viel dB fuellen die ganze Spalte.</summary>
        public const float GrFull = 20f;
        private static readonly Color GrColor = Color.FromArgb(0xff, 0x96, 0x28);
        /// <summary>Angezeigte Absenkung (geglaettet).</summary>
        public float GrShownDb => _grDb;

        private Rectangle GrBounds => Rectangle.Ceiling(RectangleF.Inflate(GrRect, Theme.F(2), Theme.F(2)));

        private void UpdateGr(float dt, float test)
        {
            if (!GrShown) return;
            if (dt > 0.2f) dt = 0.2f;
            float target = test >= 0f ? test * GrFull : Math.Max(0f, GrTarget);
            if (float.IsNaN(target)) target = 0f;
            // sofort hoch, langsam zurueck (sonst flackert es bei jedem Kompressor-Ruck)
            if (target >= _grDb) _grDb = target;
            else _grDb = Math.Max(target, _grDb - 30f * dt);
            int n = _grSegs;
            int lit = _grDb < 0.5f ? 0 : Math.Max(1, Math.Min(n, (int)(_grDb / GrFull * n + 0.5f)));
            if (lit != _grLit)
            {
                _grLit = lit;
                Invalidate(GrBounds);
            }
        }

        /// <summary>GR-Segmente von oben nach unten (dim = unbeleuchtet, im Bild-Cache).</summary>
        private void DrawGrSegments(Graphics g, bool dim, int lit)
        {
            if (GrRect.IsEmpty || _grSegs <= 0) return;
            float step = _segH + _segGap;
            var b = Seg(dim ? Theme.Mix(Theme.SegOff, GrColor, Dimmed ? 0.06f : 0.16f) : GrColor);
            for (int i = 0; i < _grSegs; i++)
            {
                if (!dim && i >= lit) break;
                Theme.FillSeg(g, b, GrRect.X, GrRect.Y + i * step, GrRect.Width, _segH);
            }
        }

        // --------------------------------------------------------------- Leuchten im Takt
        // Das Namensschild bekommt einen Schein in der Kanalfarbe – je lauter der Kanal (nach dem Fader), desto heller.

        private int _glowStep;
        private const int GlowSteps = 12;
        private RectangleF PlateArea => new RectangleF(Theme.F(8), Theme.F(12), Width - Theme.F(17), Theme.F(24));
        private Rectangle GlowBounds => Rectangle.Ceiling(RectangleF.Inflate(PlateArea, Theme.F(7), Theme.F(6)));
        /// <summary>Aktuelle Leuchtstufe (0 = aus).</summary>
        public int GlowStep => _glowStep;

        private void UpdateGlow(float frac)
        {
            if (!GlowOn) { if (_glowStep != 0) { _glowStep = 0; Invalidate(GlowBounds); } return; }
            // erst ab etwa -30 dB, voll bei etwa -3 dB
            float s = Math.Max(0f, Math.Min(1f, (frac - 0.38f) / 0.5f));
            int step = (int)(s * GlowSteps + 0.5f);
            if (step != _glowStep)
            {
                _glowStep = step;
                Invalidate(GlowBounds);
            }
        }

        private void DrawGlow(Graphics g)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float a = _glowStep / (float)GlowSteps;
            Color col = Theme.IsLight ? Theme.Darken(PanelTint, 0.05f) : Theme.Lighten(PanelTint, 0.1f);
            var pr = PlateArea;
            // Ringe um das Schild (innen frei, damit die Schrift nicht verwaschen wird)
            using (var inner = Theme.Round(RectangleF.Inflate(pr, Theme.F(1), Theme.F(1)), Theme.F(4.5f)))
                for (int i = 4; i >= 1; i--)
                {
                    float d = Theme.F(i * 1.4f);
                    int alpha = (int)(a * (Theme.IsLight ? 40 : 34 + (4 - i) * 24));
                    if (alpha <= 0) continue;
                    using (var outer = Theme.Round(RectangleF.Inflate(pr, d + Theme.F(1), d + Theme.F(1)), Theme.F(4.5f) + d))
                    using (var ring = new GraphicsPath(FillMode.Alternate))
                    using (var b = new SolidBrush(Theme.Alpha(col, alpha)))
                    {
                        ring.AddPath(outer, false);
                        ring.AddPath(inner, false);
                        g.FillPath(b, ring);
                    }
                }
            g.SmoothingMode = sm;
        }

        // --------------------------------------------------------------- Kanalnummer

        /// <summary>Nummer des Zugs (1, 2, 3 …), 0 = keine.</summary>
        public int Number
        {
            get => _number;
            set { if (_number == value) return; _number = value; InvalidateChrome(); }
        }

        private bool NumberShown => ShowNumbers && _number > 0;

        /// <summary>Platz fuer den Namen im Schild (mit Nummer etwas nach rechts versetzt).</summary>
        protected RectangleF NameTextRect(RectangleF nr) => NumberShown
            ? new RectangleF(nr.X + Theme.F(_number >= 10 ? 24 : 20), nr.Y, nr.Width - Theme.F(_number >= 10 ? 26 : 22), nr.Height)
            : new RectangleF(nr.X + Theme.F(2), nr.Y, nr.Width - Theme.F(4), nr.Height);

        /// <summary>Breite, in die der Name passen muss.</summary>
        protected float NameFitWidth(RectangleF nr) => NameTextRect(nr).Width - Theme.F(4);

        /// <summary>Kanalnummer links im Namensschild (umgekehrte Farben, damit sie auf jeder Kanalfarbe gut lesbar ist).</summary>
        protected void DrawPlateNumber(Graphics g, RectangleF nr, Color plate)
        {
            if (!NumberShown) return;
            string t = _number.ToString();
            float w = Theme.F(t.Length > 1 ? 19 : 15), h = Theme.F(16);
            var r = new RectangleF((float)Math.Round(nr.X + Theme.F(4)), (float)Math.Round(nr.Y + (nr.Height - h) / 2f), (float)Math.Round(w), (float)Math.Round(h));
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fg = Theme.PlateText(plate);
            Theme.FillRound(g, Theme.Alpha(fg, Theme.Style == Theme.StyleNeon ? 60 : 225), r, Theme.F(3));
            g.SmoothingMode = sm;
            using (var b = new SolidBrush(Theme.DymoPlates ? Theme.DymoTape(plate) : Theme.TapePlates ? Theme.TapeColor : Theme.DigitalPlates ? Theme.DigitalPlateBg : Theme.Style == Theme.StyleNeon ? fg : plate))
                Theme.Str(g, t, Theme.Ui(10f, FontStyle.Bold), b, r, Theme.Center);
        }

        // --------------------------------------------------------------- VU-Meter (Zeiger)

        /// <summary>Platz fuer das VU-Meter (nur Ausgangszuege), sonst leer.</summary>
        protected virtual RectangleF VuRect => RectangleF.Empty;
        protected Rectangle VuBounds => Rectangle.Ceiling(RectangleF.Inflate(VuRect, Theme.F(1), Theme.F(1)));
        /// <summary>4.0: Zeiger-Feld zeigt gerade etwas anderes (Surround-Pegel).</summary>
        protected virtual bool VuReplaced => false;
        // Empfindlichkeit: Pegel (dBFS, Spitze je Bild) am linken Skalenende "20". Standard -28 dB, also 0 VU bei -8 dB:
        // laute Musik steht um 0, leiser Ton weit links. Weiter runter (z. B. -45) = leiser Ton bewegt den Zeiger schon gut.
        private static float _vuStartDb = -28f;
        /// <summary>Pegel fuer Vollausschlag (+3 VU), aus der Empfindlichkeit berechnet.</summary>
        private static float _vuFull = (float)Math.Pow(10, (-28f + 23f) / 20f);

        /// <summary>VU-Zeiger reagiert ab diesem Pegel (dBFS am linken Ende "20" der Skala; 0 auf der Skala = 20 dB mehr).</summary>
        public static float VuStartDb
        {
            get => _vuStartDb;
            set
            {
                if (float.IsNaN(value)) value = -28f;
                _vuStartDb = Math.Max(-60f, Math.Min(-24f, value));
                _vuFull = (float)Math.Pow(10, (_vuStartDb + 23f) / 20f);   // -20 VU .. +3 VU = 23 dB
            }
        }

        // Fuer "Auf jetzigen Ton einstellen": mittlerer Pegel der letzten Sekunden (nur solange Ton kommt)
        private float _vuMean, _vuHeard, _vuQuiet;

        /// <summary>Mittlerer Pegel (dBFS) der letzten Sekunden, NaN wenn gerade (fast) nichts zu hoeren ist.</summary>
        public float VuMeanDb => VuRect.IsEmpty || _vuHeard < 1.5f || _vuMean <= 0f ? float.NaN : 20f * (float)Math.Log10(_vuMean);

        /// <summary>
        /// Empfindlichkeit, bei der dieser Pegel (Mittelwert) gut auf der Skala steht: klassisch pendelt der Zeiger dann um 0,
        /// bei der dB-Skala (bis 0 dB) etwa in der Mitte.
        /// </summary>
        public static float VuStartFor(float meanDb) =>
            (float)Math.Round(Math.Max(-60f, Math.Min(-24f, Theme.VuDbScale ? meanDb * 2.07f : meanDb - 16f)));

        private void TrackVuMean(float a, float dt)
        {
            if (a > 0.0003f)   // ueber -70 dB: es kommt Ton
            {
                _vuQuiet = 0f;
                _vuHeard = Math.Min(10f, _vuHeard + dt);
                float k = _vuHeard <= dt * 1.5f ? 1f : 1f - (float)Math.Exp(-dt / 2.5f);
                _vuMean += (a - _vuMean) * k;
            }
            else if ((_vuQuiet += dt) > 4f) { _vuHeard = 0f; _vuMean = 0f; }   // lange Stille: von vorn messen
        }

        /// <summary>Zielstellung des VU-Zeigers (0..~1,15) fuer einen Pegel (Spitze je Bild, linear).</summary>
        public static float VuTarget(float a)
        {
            if (Theme.VuDbScale)
            {
                // dB-Skala: gleichmaessig von "reagiert ab" bis 0 dB – leiser Ton ist zu sehen, lauter knallt nicht an den Anschlag
                float db = a > 1e-6f ? 20f * (float)Math.Log10(a) : -120f;
                return Math.Max(-0.05f, Math.Min(1.15f, Theme.VuDbPos(db)));
            }
            return Math.Min(1.15f, a / _vuFull);   // klassisch: linear in der Spannung, 23 dB von "20" bis "+3"
        }

        /// <summary>Schwelle der PEAK-Leuchte (linear).</summary>
        public static float PeakLin => _peakLin;

        /// <summary>Zeiger wie bei einem echten VU-Meter: traege (ca. 300 ms), schwingt leicht ueber.</summary>
        // ---- 2.16 Zeiger fuer die Meterbridge (Zeigerinstrumente): wie das VU-Meter im Zug, fuer jeden Zug
        /// <summary>Meterbridge zeigt Zeigerinstrumente – dann rechnet jeder Zug seinen Zeiger mit.</summary>
        public static bool BridgeVuOn;
        private float _bvP, _bvV, _bvHold, _bvPk, _bvPkHold;
        public float BridgeVuPos => _bvP;
        public float BridgeVuPeak => _bvPk;

        private void UpdateBridgeVu(float l, float r, float dt, float test)
        {
            if (!BridgeVuOn) return;
            if (dt > 0.1f) dt = 0.1f;
            if (Theme.VuDual)
            {
                VuStep(ref _bvP, ref _bvV, ref _bvHold, test >= 0f ? test : VuTarget(l), dt);
                VuStep(ref _bvP2, ref _bvV2, ref _bvHold2, test >= 0f ? test : VuTarget(r), dt);
                _bvPk = Math.Max(_bvP, _bvP2);
                return;
            }
            float x = test >= 0f ? test : VuTarget((l + r) * 0.5f);
            float tau = VuTau();
            if (tau > 0f)
            {
                if (x >= _bvHold) _bvHold = x;
                else _bvHold += (x - _bvHold) * (1f - (float)Math.Exp(-dt / tau));
                x = _bvHold;
            }
            float w = VuOmega; const float z = 0.8f;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = w * w * (x - _bvP) - 2f * z * w * _bvV;
                _bvV += acc * h;
                _bvP += _bvV * h;
                if (_bvP < -0.015f) { _bvP = -0.015f; if (_bvV < 0) _bvV = 0; }
                if (_bvP > 1.06f) { _bvP = 1.06f; if (_bvV > 0) _bvV = -_bvV * 0.2f; }
            }
            if (test >= 0f) { _bvPk = _bvP; _bvPkHold = 0f; }
            else if (_bvP >= _bvPk) { _bvPk = _bvP; _bvPkHold = 1.6f; }
            else if (_bvPkHold > 0f) _bvPkHold -= dt;
            else _bvPk = Math.Max(_bvP, _bvPk - dt * 0.3f);
        }

        // ---- 3.1: Doppelzeiger – zweiter Zeiger (rechts); _vuP ist dann der linke
        private float _vuP2, _vuV2, _vuHold2, _vuShown2 = -9f;
        private float _bvP2, _bvV2, _bvHold2;
        public float BridgeVuPos2 => _bvP2;

        /// <summary>Ein Zeiger wie beim echten VU-Meter (traege, leicht ueberschwingend, langsamer Ruecklauf je nach Einstellung).</summary>
        private static void VuStep(ref float p, ref float v, ref float hold, float x, float dt)
        {
            float tau = VuTau();
            if (tau > 0f)
            {
                if (x >= hold) hold = x;
                else hold += (x - hold) * (1f - (float)Math.Exp(-dt / tau));
                x = hold;
            }
            float w = VuOmega; const float z = 0.8f;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = w * w * (x - p) - 2f * z * w * v;
                v += acc * h;
                p += v * h;
                if (p < -0.015f) { p = -0.015f; if (v < 0) v = 0; }
                if (p > 1.06f) { p = 1.06f; if (v > 0) v = -v * 0.2f; }
            }
        }

        private void UpdateVu(float l, float r, float dt, float test)
        {
            if (!VuOn || VuRect.IsEmpty) return;
            if (dt > 0.1f) dt = 0.1f;
            float a = (l + r) * 0.5f;
            if (test < 0f) TrackVuMean(a, dt);
            if (Theme.VuDual)
            {
                VuStep(ref _vuP, ref _vuV, ref _vuHold, test >= 0f ? test : VuTarget(l), dt);
                VuStep(ref _vuP2, ref _vuV2, ref _vuHold2, test >= 0f ? test : VuTarget(r), dt);
                bool led2 = VuPeakLed && _peakLedOn;
                if (Math.Abs(_vuP - _vuShown) > 0.0025f || Math.Abs(_vuP2 - _vuShown2) > 0.0025f || led2 != _vuLedShown)
                {
                    _vuShown = _vuP; _vuShown2 = _vuP2; _vuLedShown = led2;
                    if (!Compact) Invalidate(VuBounds);
                }
                return;
            }
            float x = test >= 0f ? test : VuTarget(a);
            // langsamer Ruecklauf: hoch geht es sofort, zurueck nur gemaechlich (der Zeiger "faellt" sanft)
            float tau = VuTau();
            if (tau > 0f)
            {
                if (x >= _vuHold) _vuHold = x;
                else _vuHold += (x - _vuHold) * (1f - (float)Math.Exp(-dt / tau));
                x = _vuHold;
            }
            float w = VuOmega; const float z = 0.8f;   // wie ein echtes VU-Meter: 99 % nach ca. 0,3 s, etwa 1,5 % Ueberschwingen
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = w * w * (x - _vuP) - 2f * z * w * _vuV;
                _vuV += acc * h;
                _vuP += _vuV * h;
                if (_vuP < -0.015f) { _vuP = -0.015f; if (_vuV < 0) _vuV = 0; }       // Anschlag links
                if (_vuP > 1.06f) { _vuP = 1.06f; if (_vuV > 0) _vuV = -_vuV * 0.2f; } // Anschlag rechts, federt leicht zurueck
            }
            // Schleppzeiger: wird vom Zeiger mitgenommen, bleibt kurz oben und sinkt dann langsam zurueck
            if (test >= 0f) { _vuPk = _vuP; _vuPkHold = 0f; }   // im Selbsttest nicht festhalten (sonst steht er danach im Roten)
            else if (_vuP >= _vuPk) { _vuPk = _vuP; _vuPkHold = 1.6f; }
            else if (_vuPkHold > 0f) _vuPkHold -= dt;
            else _vuPk = Math.Max(_vuP, _vuPk - dt * 0.3f);
            bool led = VuPeakLed && _peakLedOn;   // gleiche Schwelle wie die PEAK-Leuchte des Zugs
            if (Math.Abs(_vuP - _vuShown) > 0.0025f || (VuPeakNeedle && Math.Abs(_vuPk - _vuPkShown) > 0.0025f) || led != _vuLedShown)
            {
                _vuShown = _vuP; _vuPkShown = _vuPk; _vuLedShown = led;
                if (!Compact) Invalidate(VuBounds);
            }
        }

        // --------------------------------------------------------------- Selbsttest beim Start
        // Wie bei Hardware nach dem Einschalten: alle Pegel und Leuchten laufen einmal hoch und wieder runter,
        // die Fader fahren von unten an ihre Stelle. Nur Anzeige – am Ton aendert sich nichts.

        private static readonly Stopwatch _testClock = new Stopwatch();
        /// <summary>Nur Demo/Test: Selbsttest bei dieser Zeit anhalten (fuer Vorschaubilder).</summary>
        public static float FrozenTestTime = -1f;
        private static bool _testRunning;
        private const float TestLen = 1.55f, TestStagger = 0.35f;
        private float _testDelay = -1f;
        private bool _testShown, _testAbort;
        private static int _testGen;
        private int _myTestGen;
        private static bool _testArmed;
        private float _testFader = -1f;
        private float _testS = -1f;

        /// <summary>Selbsttest starten (beim ersten Anzeigen des Fensters).</summary>
        public static void StartSelfTest(bool force = false)
        {
            if (!SelfTestOn && !force) { _testArmed = false; return; }
            _testGen++;
            _testClock.Restart();
            _testRunning = true;
        }

        /// <summary>Vor dem ersten Anzeigen: Fader stehen schon unten (sonst springen sie erst runter und fahren dann hoch).</summary>
        public static void ArmSelfTest() => _testArmed = SelfTestOn;

        /// <summary>Gerade im Selbsttest?</summary>
        public static bool SelfTestRunning => _testRunning;

        /// <summary>Lokale Zeit im Selbsttest (Zuege weiter rechts etwas spaeter – eine Welle), -1 = vorbei.</summary>
        private float TestTime()
        {
            if (FrozenTestTime >= 0f) { _testDelay = 0f; return FrozenTestTime; }
            if (_myTestGen != _testGen) { _myTestGen = _testGen; _testAbort = false; _testDelay = -1f; }   // neuer Test (z. B. aus dem Menue)
            if (!_testRunning || _testAbort) return -1f;
            float t = (float)_testClock.Elapsed.TotalSeconds;
            if (t > TestLen + TestStagger + 0.2f) { _testRunning = false; _testArmed = false; return -1f; }
            if (_testDelay < 0f)
            {
                _testDelay = 0f;
                try
                {
                    var f = FindForm();
                    if (f != null && IsHandleCreated && f.ClientSize.Width > 0)
                        _testDelay = TestStagger * Math.Max(0, Math.Min(1, f.PointToClient(PointToScreen(Point.Empty)).X / (float)f.ClientSize.Width));
                }
                catch { }
            }
            t -= _testDelay;
            if (t < 0f) return 0f;
            return t > TestLen ? -1f : t;
        }

        private static float Ease(float u) { u = Math.Max(0, Math.Min(1, u)); return 1f - (1f - u) * (1f - u) * (1f - u); }

        // ---- 3.1: grosse Einschalt-Show (zum Selbsttest dazu): Fader fahren ganz hoch und dann an ihre Stelle, das Spektrum laeuft als Welle
        public static bool BigShow = true;

        private static float BigShowFader(float t, float target)
        {
            if (t < 0.08f) return 0f;
            if (t < 0.7f) return Ease((t - 0.08f) / 0.62f);
            if (t < 0.85f) return 1f;
            float u = Ease((t - 0.85f) / 0.6f);
            return 1f + (target - 1f) * u;
        }

        /// <summary>Zeit seit dem Start des Selbsttests (s), -1 = laeuft nicht.</summary>
        public static float SelfTestTime => FrozenTestTime >= 0f ? FrozenTestTime : _testRunning ? (float)_testClock.Elapsed.TotalSeconds : -1f;

        /// <summary>Pegel im Selbsttest zur Zeit t (fuer andere Anzeigen, z. B. das Spektrum als Welle).</summary>
        internal static float TestLevelAt(float t) => t < 0f ? 0f : t > 1.45f ? 0f : TestLevel(t);

        /// <summary>Pegel im Selbsttest: hoch (0,55 s), kurz oben, wieder runter.</summary>
        private static float TestLevel(float t)
        {
            if (t < 0.55f) return Ease(t / 0.55f);
            if (t < 0.8f) return 1f;
            if (t < 1.45f) return 1f - (t - 0.8f) / 0.65f;
            return 0f;
        }

        /// <summary>Faderstellung, wie sie gerade gezeichnet wird (im Selbsttest faehrt der Fader von unten hoch).</summary>
        protected float ShownFaderPos => _testFader >= 0f ? _testFader : _motorPos >= 0f ? _motorPos : _testArmed && !_testRunning && !_testAbort ? 0f : FaderPos;

        /// <summary>Fader wird gerade live gezeichnet (Selbsttest oder Motorfader)? Dann Position, sonst -1.</summary>
        private float LiveFaderPos => _testFader >= 0f ? _testFader : _motorPos;

        // --------------------------------------------------------------- Motorfader
        // Beim Laden einer Szene fahren die Fader wie bei einem Digitalpult mit Motorfadern an ihre neue Stelle,
        // statt zu springen. Nur Anzeige – der Ton blendet wie bisher kurz ueber.

        /// <summary>Wo der Fader gerade zu sehen ist (vor einer Aenderung merken, dann StartMotor).</summary>
        public float MotorFrom => ShownFaderPos;

        /// <summary>Fader von "from" aus an die aktuelle Stelle fahren lassen.</summary>
        public void StartMotor(float from)
        {
            if (!MotorFaders || Calm || _testFader >= 0f || _dragging || !Visible) return;   // 4.0: weniger Bewegung
            var form = FindForm();
            if (form == null || !form.Visible || form.WindowState == FormWindowState.Minimized) return;   // sonst faehrt er erst beim Wiederherstellen
            float to = FaderPos;
            float dist = Math.Abs(to - from);
            if (dist < 0.01f) return;
            bool wasLive = _motorPos >= 0f;
            _motorFrom = from; _motorT = 0f; _motorPos = from;
            _motorLen = MotorOverrideSec > 0f ? MotorOverrideSec : (0.22f + 0.38f * Math.Min(1f, dist)) * MotorSpeedFactor;   // kurze Wege schnell, ganz hoch/runter ca. 0,6 s (3.2: Tempo einstellbar)
            if (!wasLive) InvalidateChrome();   // Kappe raus aus dem Bild-Cache
            MotorStarted?.Invoke(_motorLen, dist);   // 4.0
        }

        /// <summary>Nur Demo/Test: Motorfader bei diesem Anteil (0..1) der Fahrt anhalten (fuer Vorschaubilder).</summary>
        public static float FrozenMotorT = -1f;

        /// <summary>Gerade in Fahrt?</summary>
        public bool MotorRunning => _motorPos >= 0f;

        private void StopMotor()
        {
            if (_motorPos < 0f) return;
            _motorPos = -1f;
            InvalidateChrome();
        }

        private void UpdateMotor(float dt)
        {
            if (_motorPos < 0f) return;
            if (dt > 0.1f) dt = 0.1f;
            _motorT += dt / Math.Max(0.05f, _motorLen);
            if (FrozenMotorT >= 0f) _motorT = Math.Min(_motorT, FrozenMotorT);
            if (_motorT >= 1f) { StopMotor(); return; }
            float u = _motorT, e = u * u * (3f - 2f * u);   // sanft anfahren und abbremsen
            float p = _motorFrom + (FaderPos - _motorFrom) * e;
            if (Math.Abs(p - _motorPos) > 0.0005f) { _motorPos = p; Invalidate(FaderBounds); }
        }

        // --------------------------------------------------------------- Werte fuer die Meterbridge

        /// <summary>Name fuer die Meterbridge.</summary>
        public virtual string DisplayName => "";
        /// <summary>2.16.2: fester Schluessel fuer die Meterbridge (Pegel ein-/ausblenden), null = immer zeigen.</summary>
        public virtual string BridgeKey => null;
        public Color DisplayColor => AccentColor;
        public bool IsHot => _hot && HoverLight;
        public bool IsDimmed => Dimmed;
        public float ShowL => _testS >= 0f ? _testS : Meter.FracL;
        public float ShowR => _testS >= 0f ? _testS : Meter.FracR;
        public float ShowHoldL => _testS >= 0f ? 0f : Meter.HoldFracL;
        /// <summary>Nachgluehen (Anteil 0..1, nur wenn eingeschaltet).</summary>
        public float ShowAgL => _testS >= 0f || !Theme.LedAfterglow ? 0f : _agL;
        public float ShowAgR => _testS >= 0f || !Theme.LedAfterglow ? 0f : _agR;
        /// <summary>Pegel-Art "Beides": Spitze ueber dem Mittelwert (0 = keine).</summary>
        public float ShowPkL => _testS >= 0f ? 0f : Meter.PeakFracL;
        public float ShowPkR => _testS >= 0f ? 0f : Meter.PeakFracR;
        public float ShowHoldR => _testS >= 0f ? 0f : Meter.HoldFracR;
        public float ShowPreL => _testS >= 0f ? 0f : PreMeter.FracL;
        public float ShowPreR => _testS >= 0f ? 0f : PreMeter.FracR;
        /// <summary>Spitzenwert in dB (gerundet), int.MinValue = nichts.</summary>
        public int ShowPeakDb => _testS >= 0f ? int.MaxValue : Meter.HoldDb <= -59.5f ? int.MinValue : (int)Math.Round(Meter.HoldDb);
        public bool ShowClip => _testS < 0f && Meter.Clip;
        public bool SigLit => _ledOn;
        public bool PeakLit => _peakLedOn;

        // --------------------------------------------------------------- Hover-Licht

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHot(true);
        }

        private void SetHot(bool on)
        {
            if (_hot == on) return;
            _hot = on;
            if (HoverLight) Invalidate();
        }

        // --------------------------------------------------------------- dB-Anzeige und MUTE-Blinken

        private Rectangle MuteBounds => Rectangle.Ceiling(RectangleF.Inflate(MapTop(MuteKey), Theme.F(7), Theme.F(7)));
        /// <summary>VU-Meter zu sehen (nicht in der Kompakt-Ansicht).</summary>
        protected bool VuShown => VuOn && !VuRect.IsEmpty && !Compact;

        // --------------------------------------------------------------- Einschalt-Gruss
        // Beim Start laeuft "MISCHPULT 2.12 · MADE BY D3R_JAN" als Laufschrift durch die Anzeigen aller Zuege –
        // als waeren sie ein langes Display (jede Anzeige zeigt das Stueck, das gerade ueber ihr steht).

        public static bool GreetingOn = true;
        private static readonly Stopwatch _greetClock = new Stopwatch();
        private const float GreetLen = 3.8f;
        private static string _greetText = "";
        private static int _greetLeft, _greetRight;
        private bool _greetShown;
        /// <summary>Nur Demo/Test: Gruss bei dieser Zeit anhalten (fuer Vorschaubilder).</summary>
        public static float FrozenGreetTime = -1f;

        /// <summary>Gruss starten. left/right = Bildschirmbereich, durch den die Schrift laeuft.</summary>
        public static void StartGreeting(string text, int screenLeft, int screenRight)
        {
            if (!GreetingOn || string.IsNullOrEmpty(text) || screenRight <= screenLeft || _tickerLoop) return;
            _greetText = text; _greetLeft = screenLeft; _greetRight = screenRight;
            _greetLenCur = GreetLen;
            _greetClock.Restart();
        }

        // ---- 3.1: eigene Laufschrift – laeuft immer wieder durch, bis sie gestoppt wird
        private static bool _tickerLoop;
        private static float _greetLenCur = GreetLen;
        /// <summary>Laeuft gerade die eigene Laufschrift?</summary>
        public static bool TickerRunning => _tickerLoop && _greetClock.IsRunning;

        /// <summary>Eigene Laufschrift starten (unabhaengig vom Einschalt-Gruss); laeuft, bis StopTicker kommt.</summary>
        public static void StartTicker(string text, int screenLeft, int screenRight)
        {
            if (string.IsNullOrEmpty(text) || screenRight <= screenLeft) return;
            _greetText = text; _greetLeft = screenLeft; _greetRight = screenRight;
            _tickerLoop = true;
            _greetLenCur = Math.Max(5f, Math.Min(16f, 3f + text.Length * 0.18f));   // laengerer Text: etwas mehr Zeit (gleich gut lesbar)
            _greetClock.Restart();
        }

        public static void StopTicker()
        {
            if (!_tickerLoop) return;
            _tickerLoop = false;
            _greetLenCur = GreetLen;
            _greetClock.Reset();
        }

        private static float GreetTime => FrozenGreetTime >= 0f ? FrozenGreetTime
            : !_greetClock.IsRunning ? -1f
            : _tickerLoop ? (float)(_greetClock.Elapsed.TotalSeconds % _greetLenCur)
            : (float)_greetClock.Elapsed.TotalSeconds;
        public static bool GreetingRunning { get { if (BannerText != null) return true; float t = GreetTime; return t >= 0f && (_tickerLoop || t < _greetLenCur); } }

        // ---- 3.2: feste Schrift quer ueber alle Anzeigen (Countdown) – hat Vorrang vor Laufschrift und Gruss
        /// <summary>Text, der mittig ueber allen Anzeigen steht (null = keiner). Mit SetBanner setzen.</summary>
        public static string BannerText { get; private set; }
        private static int _bannerVer;
        private int _bannerSeen = -1;

        public static void SetBanner(string text, int screenLeft, int screenRight)
        {
            if (text == BannerText && screenLeft == _greetLeft && screenRight == _greetRight) return;
            BannerText = text;
            if (text != null) { _greetLeft = screenLeft; _greetRight = screenRight; }
            _bannerVer++;
        }
        private Rectangle ReadoutBounds => Rectangle.Inflate(Rectangle.Ceiling(ReadoutRect), 1, 1);

        private void UpdateGreeting()
        {
            if (ReadoutRect.IsEmpty) return;
            if (GreetingRunning)
            {
                if (!_greetShown) { _greetShown = true; _bannerSeen = _bannerVer; InvalidateChrome(); }   // leere Anzeige in den Bild-Cache
                else if (BannerText == null) Invalidate(ReadoutBounds);
                else if (_bannerSeen != _bannerVer) { _bannerSeen = _bannerVer; Invalidate(ReadoutBounds); }   // 3.2: feste Schrift nur bei Aenderung neu
            }
            else if (_greetShown) { _greetShown = false; InvalidateChrome(); }
        }

        private void DrawGreeting(Graphics g)
        {
            float t = GreetTime;
            var st = g.Save();
            Theme.Setup(g);
            var font = Theme.Mono(11.5f);
            string text = BannerText ?? _greetText;
            if (BannerText != null)
                for (float px = 11.5f; px > 8f && Theme.TextWidth(g, text, font) > ReadoutRect.Width - Theme.F(6); px -= 0.5f) font = Theme.Mono(px);
            float tw = Theme.TextWidth(g, text, font);
            float lx;
            if (BannerText != null) lx = ReadoutRect.X + (ReadoutRect.Width - tw) / 2f;   // 3.2: feste Schrift in jeder Anzeige mittig
            else
            {
                float bx = _greetRight - t / _greetLenCur * (_greetRight - _greetLeft + tw);   // Bildschirm-x des Textanfangs
                int lcdScreenX;
                try { lcdScreenX = PointToScreen(new Point((int)ReadoutRect.X, 0)).X; } catch { lcdScreenX = 0; }
                lx = ReadoutRect.X + (bx - lcdScreenX);
            }
            if (lx < ReadoutRect.Right && lx + tw > ReadoutRect.X)
            {
                g.SetClip(RectangleF.Inflate(ReadoutRect, -Theme.F(2), -Theme.F(1)), System.Drawing.Drawing2D.CombineMode.Intersect);
                Theme.LcdColors(out _, out var fg);
                float th = font.GetHeight(g);
                using (var b = new SolidBrush(fg))
                    Theme.Str(g, text, font, b, (float)Math.Round(lx), (float)Math.Round(ReadoutRect.Y + (ReadoutRect.Height - th) / 2f), StringFormat.GenericTypographic);
            }
            g.Restore(st);
        }

        /// <summary>MUTE-Taste in den Bild-Cache. Ist sie an und darf blinken, kommt das Leuchten live dazu (OnPaint).</summary>
        protected void DrawMuteKey(Graphics g, RectangleF r, bool lit, bool half = false)
        {
            if (lit && MuteBlink) Theme.DrawKey(g, r, "MUTE", false, Theme.MuteRed, HoverZone == "mute", true);
            else Theme.DrawKey(g, r, "MUTE", lit, Theme.MuteRed, HoverZone == "mute", half);
        }

        /// <summary>dB-Skala neben den Pegel-LEDs (wie aufgedruckt).</summary>
        private void DrawMeterScale(Graphics g)
        {
            var font = Theme.Ui(7f, FontStyle.Bold);
            if (KScale > 0)
            {
                // 3.4.6: K-Skala – Zahlen relativ zu 0 K (= −14 bzw. −20 dBFS)
                int refDb = KScale == 1 ? 14 : 20;
                using (var b = new SolidBrush(Theme.ScaleLabel))
                using (var z = new SolidBrush(Theme.Text))
                    foreach (int k in KScale == 1 ? new[] { 14, 8, 4, 0, -4, -10, -20 } : new[] { 20, 12, 4, 0, -6, -12, -20 })
                    {
                        float y = MeterL.Bottom - MeterBallistics.Iec(k - refDb) * MeterL.Height;
                        Theme.Str(g, (k > 0 ? "+" : "") + k, font, k == 0 ? z : b, new RectangleF(MeterL.X - Theme.F(22), y - Theme.F(5), Theme.F(19), Theme.F(10)), Theme.Right);
                    }
                return;
            }
            using (var b = new SolidBrush(Theme.ScaleLabel))
                foreach (int db in new[] { -6, -12, -18, -30, -40, -60 })
                {
                    float y = MeterL.Bottom - MeterBallistics.Iec(db) * MeterL.Height;
                    Theme.Str(g, db.ToString(), font, b, new RectangleF(MeterL.X - Theme.F(22), y - Theme.F(5), Theme.F(19), Theme.F(10)), Theme.Right);
                }
        }

        /// <summary>Kleine Anzeige ueber dem Pegel: Spitzenwert in dB (rot bei Uebersteuerung).</summary>
        private void DrawPeakBox(Graphics g)
        {
            var r = PeakRect;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.LcdColors(out var lbg, out var lfg);
            Theme.FillRound(g, _peakClip ? Theme.MeterRed : lbg, r, Theme.F(2));
            bool none = _peakShown == int.MinValue;
            string t = _peakShown == int.MaxValue ? "88" : none ? "–" : (_peakShown > 0 ? "+" : "") + _peakShown.ToString();
            Color c = _peakClip ? Color.White : none ? Theme.LcdOffText() : lfg;
            using (var b = new SolidBrush(c))
                Theme.Str(g, t, Theme.Mono(8.5f), b, new RectangleF(r.X, r.Y + Theme.F(0.5f), r.Width, r.Height), Theme.Center);
            g.SmoothingMode = sm;
        }

        private void UpdatePeakAndBlink(float dt)
        {
            if (dt > 0.2f) dt = 0.2f;
            if (PeakDb)
            {
                float hold = Meter.HoldDb;
                int pv = hold <= -59.5f ? int.MinValue : (int)Math.Round(hold);
                bool clip = Meter.Clip;
                if (_testS >= 0f) { pv = _testS > 0.04f ? int.MaxValue : int.MinValue; clip = false; }   // Anzeigetest: "88"
                if (pv != _peakShown || clip != _peakClip)
                {
                    _peakShown = pv; _peakClip = clip;
                    Invalidate(Rectangle.Inflate(Rectangle.Ceiling(PeakRect), 1, 1));
                }
            }
            // stumm: MUTE blinkt (egal, ob gerade Ton ankommt)
            bool blink = MuteBlink && MuteLit && !MuteKey.IsEmpty;
            bool dark = false;
            if (blink) { _blinkT += dt; dark = _blinkT % 0.7f >= 0.4f; }
            else _blinkT = 0f;
            if (dark != _blinkDark)
            {
                _blinkDark = dark;
                Invalidate(MuteBounds);
            }
        }

        // --------------------------------------------------------------- Maus
        protected string ZoneAt(Point p)
        {
            for (int i = Zones.Count - 1; i >= 0; i--)
                if (Zones[i].Rect.Contains(p)) return Zones[i].Id;
            return null;
        }

        private bool IsClickable(string id)
        {
            foreach (var z in Zones) if (z.Id == id) return z.Clickable;
            return false;
        }

        private string _dragZone;

        // Verschieben per Maus
        private string _pressZone;
        private Point _pressScreen;
        private bool _moving, _pressFree;

        private StripHost MoveHost => Parent is StripHost h && h.CanMove(this) ? h : null;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HideTip();
            var renaming = ChannelStrip.Renaming;
            if (renaming != null && renaming != this) renaming.EndRename(true);
            string z = ZoneAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (_moving) { CancelMove(); return; }
                if (z == "peak" && ClipLogOn) { Mixer.ShowClipMenu(this, PointToScreen(e.Location)); return; }   // 3.5: zuletzt zu laut
                if (z == "sig" || z == "peak") { Mixer.ShowLedDialog(z == "peak"); return; }   // Rechtsklick auf SIG/PEAK: Schwelle einstellen
                OnZoneMouseDown(z ?? "", e);
                return;
            }
            if (e.Button == MouseButtons.Left && OnEdge(e.Location))
            {
                // 3.4.5: Breite ziehen
                _resizing = true;
                _resizeStartX = MousePosition.X;
                _resizeStartW = _resizeLast = ResizeBaseWidth;
                Capture = true;
                Cursor = Cursors.SizeWE;
                return;
            }
            if (e.Button == MouseButtons.Middle && MiddleMuteOn) { OnMiddleClick(); InvalidateChrome(); return; }   // 3.5: Mittelklick = stumm
            if (e.Button == MouseButtons.Left && MultiFaderOn && CanMultiSelect && (ModifierKeys & Keys.Control) != 0 && z == "name") { ToggleSelected(); return; }   // 3.5: Strg+Klick waehlt
            if (z == "peak" && e.Button == MouseButtons.Left && PeakCountOn) { ResetPeakCount(); return; }
            if (z == "meter" && e.Button == MouseButtons.Left) { ResetMeterHold(); return; }
            if (e.Button == MouseButtons.Left && z != null && KeyZones.Contains(z)) Mixer.KeyClick();   // Tasten klicken
            // Packen: am Griff (Name) oder an einer freien Stelle des Zugs
            bool free = z == null && PanelRect.Contains(e.Location);
            if (e.Button == MouseButtons.Left && (free || (z != null && IsMoveHandle(z))) && MoveHost != null)
            {
                _pressZone = z ?? "";
                _pressFree = free;
                _pressScreen = PointToScreen(e.Location);
                Capture = true;
                return;
            }
            if (z == "fader" && e.Button == MouseButtons.Left && FaderLocked) { OnLockedTouch(); return; }
            if (z == "fader" && e.Button == MouseButtons.Left)
            {
                StopMotor();   // Fader anfassen haelt den Motor an
                var cap = new RectangleF(SlotX - CapW / 2 - Theme.F(3), PosToY(ShownFaderPos) - CapH / 2 - Theme.F(3), CapW + Theme.F(6), CapH + Theme.F(6));
                _dragging = true;
                Capture = true;
                if (_testRunning || _testArmed) { _testAbort = true; InvalidateChrome(); }
                if (!cap.Contains(e.Location) && !KnobView)   // 4.0: am Drehknopf springt nichts, nur ziehen
                {
                    float p = DragSnap(Clamp(YToPos(e.Y)), (ModifierKeys & Keys.Shift) != 0);
                    float was = FaderPos;
                    OnFaderMoved(p, false);
                    MoveSelectedBy(was, p, false);   // 3.5
                }
                _dragVirtual = FaderPos;
                _lastY = e.Y;
                InvalidateChrome();
                return;
            }
            if (z != null && OnZoneMouseDown(z, e))
            {
                _dragZone = z;
                _lastY = e.Y;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_resizing)
            {
                int w = _resizeStartW + (int)Math.Round((MousePosition.X - _resizeStartX) / Theme.F(1));
                w = Math.Max(MixerSettings.DragWidthMin, Math.Min(MixerSettings.DragWidthMax, w));
                if (w != _resizeLast) { _resizeLast = w; OnResizeWidth(w, (ModifierKeys & Keys.Control) != 0, false); }
                return;
            }
            if (_pressZone == null && !_dragging && _dragZone == null)
            {
                bool edge = OnEdge(e.Location);
                if (edge != _onEdgeShown)
                {
                    _onEdgeShown = edge;
                    if (edge) { Cursor = Cursors.SizeWE; HideTip(); _tipZone = "#edge"; _tipPoint = e.Location; _tipTimer.Start(); if (HoverZone != null) { HoverZone = null; InvalidateChrome(); } return; }
                    Cursor = Cursors.Default; HoverZone = "#"; // erzwingt unten die normale Auswertung
                }
                if (edge) return;
            }
            if (_pressZone != null)
            {
                var p = MousePosition;
                if (!_moving)
                {
                    int ds = Math.Max(SystemInformation.DragSize.Width, Theme.Px(6));
                    int dx = Math.Abs(p.X - _pressScreen.X), dy = Math.Abs(p.Y - _pressScreen.Y);
                    if (dx <= ds && dy <= ds) return;
                    // an freien Stellen nur bei waagerechter Bewegung (senkrecht gezogen passiert nichts)
                    if (_pressFree && dx < dy) { _pressZone = null; Capture = false; return; }
                    var host = MoveHost;
                    if (host == null) { _pressZone = null; Capture = false; return; }
                    _moving = true;
                    HideTip();
                    Cursor = Cursors.SizeWE;
                    host.BeginMove(this, host.PointToClient(_pressScreen).X - Left);
                    Invalidate();
                }
                if (Parent is StripHost h) h.MoveTo(h.PointToClient(p).X);
                return;
            }
            if (_dragging)
            {
                bool fine = (ModifierKeys & Keys.Shift) != 0;
                float dy = e.Y - _lastY;
                _lastY = e.Y;
                _dragVirtual -= dy / (TravelBottom - TravelTop) * (fine ? 0.2f : 1f);
                float p = DragSnap(Clamp(_dragVirtual), fine);   // 3.4.6: einrasten (falls an)
                if (p != FaderPos) { float was = FaderPos; NoteTrail400(was, p); OnFaderMoved(p, false); MoveSelectedBy(was, p, false); InvalidateChrome(); }
                return;
            }
            if (_dragZone != null)
            {
                int dy = e.Y - _lastY;
                _lastY = e.Y;
                if (dy != 0 || _dragZone == "apps") OnZoneDrag(_dragZone, dy, (ModifierKeys & Keys.Shift) != 0);   // 4.0: App ziehen auch waagerecht
                return;
            }

            string z = ZoneAt(e.Location);
            if (z != HoverZone)
            {
                HoverZone = z;
                Cursor = z != null && IsClickable(z) ? Cursors.Hand : Cursors.Default;
                InvalidateChrome();
                HideTip();
                _tipZone = z;
                if (z != null) _tipTimer.Start();
            }
            _tipPoint = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_resizing) { EndResize(); return; }
            if (_pressZone != null)
            {
                string z = _pressZone;
                bool moved = _moving;
                _pressZone = null;
                _moving = false;
                Capture = false;
                if (moved)
                {
                    Cursor = Cursors.Default;
                    Invalidate();
                    (Parent as StripHost)?.EndMove(true);
                }
                else if (!_pressFree) OnHandleClick(z, e);
                return;
            }
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                OnFaderMoved(FaderPos, true);
                MoveSelectedBy(FaderPos, FaderPos, true);   // 3.5: die anderen gewaehlten auch festschreiben
                InvalidateChrome();
            }
            if (_dragZone != null)
            {
                var z = _dragZone;
                _dragZone = null;
                Capture = false;
                OnZoneDragEnd(z);
            }
        }

        /// <summary>Verschieben abbrechen (Rechtsklick oder Maus verloren, z. B. Alt+Tab).</summary>
        private void CancelMove()
        {
            if (!_moving) return;
            _moving = false;
            _pressZone = null;
            Cursor = Cursors.Default;
            if (Capture) Capture = false;
            Invalidate();
            (Parent as StripHost)?.EndMove(false);
        }

        private void EndResize()
        {
            if (!_resizing) return;
            _resizing = false;
            if (Capture) Capture = false;
            OnResizeWidth(_resizeLast, (ModifierKeys & Keys.Control) != 0, true);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _resizing) EndResize();
            if (!Capture)
            {
                if (_moving) CancelMove();
                else _pressZone = null;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && OnEdge(e.Location)) { _resizing = false; OnResizeReset((ModifierKeys & Keys.Control) != 0); return; }   // 3.4.5
            string z = ZoneAt(e.Location);
            if (z == "meter" && e.Button == MouseButtons.Left && MeterLupeOn) { Mixer.ShowMeterLupe(this); return; }   // 3.4.6
            if (TypeValueOn && e.Button == MouseButtons.Left && ReadoutRect.Contains(e.Location) && !FaderLocked) { Mixer.AskFaderValue(this); return; }   // 3.5
            if (z == "fader" && e.Button == MouseButtons.Left)
            {
                if (FaderLocked) { OnLockedTouch(); return; }
                float target = !float.IsNaN(FaderMarkPos) ? FaderMarkPos : DefaultFaderPos;   // Doppelklick: zur Marke, sonst 0 dB
                if (!float.IsNaN(target))
                {
                    _dragging = false;
                    Capture = false;
                    float from = ShownFaderPos;
                    OnFaderMoved(Clamp(target), true);
                    InvalidateChrome();
                    StartMotor(from);   // sichtbar hinfahren (Motorfader)
                }
                return;
            }
            if (z != null) OnZoneDoubleClick(z, e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HideTip();
            // nur aus, wenn die Maus wirklich draussen ist (nicht nur ueber einem Eingabefeld im Zug)
            bool inside = false;
            try { inside = ClientRectangle.Contains(PointToClient(MousePosition)); } catch { }
            if (!inside) SetHot(false);
            if (HoverZone != null && !_dragging)
            {
                HoverZone = null;
                Cursor = Cursors.Default;
                InvalidateChrome();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Shift) != 0 && Parent is StripHost host)
            {
                host.ScrollBy(-e.Delta);
                return;
            }
            string z = ZoneAt(e.Location);
            int wd = WheelInvert ? -e.Delta : e.Delta;   // 3.5: Mausrad-Richtung umkehren
            if (z != null && z != "fader" && OnZoneWheel(z, wd)) return;
            if (FaderLocked) { OnLockedTouch(); return; }   // gesperrt: Mausrad aendert nichts
            float step = WheelStep * ((ModifierKeys & Keys.Control) != 0 ? 0.25f : 1f);
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            float p = (DbFader ? WheelDb : WheelPct) > 0f ? WheelTarget(FaderPos, wd, ctrl)   // 3.4.6: feste Schritte
                    : Clamp(FaderPos + wd / 120f * step);
            if (p != FaderPos)
            {
                float was = FaderPos;
                OnFaderMoved(p, true);
                MoveSelectedBy(was, p, true);   // 3.5
                InvalidateChrome();
            }
        }

        protected static float Clamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>"-50 dB" (ganzzahlig, mit echtem Minuszeichen).</summary>
        public static string DbText(float db) => ((int)Math.Round(db)).ToString(System.Globalization.CultureInfo.InvariantCulture).Replace("-", "−") + " dB";

        // --------------------------------------------------------------- Tooltips
        private void ShowTip()
        {
            _tipTimer.Stop();
            if (_tipZone == null || _dragging || !Visible) return;
            string text = _tipZone == "#edge" ? "Ziehen: breiter oder schmaler  ·  Strg: alle Kanäle gleich\nDoppelklick: wieder normal breit"
                : _tipZone == "fader" && FaderLocked ? "Fader gesperrt – Ziehen und Mausrad ändern nichts\nRechtsklick → Fader entsperren"
                : _tipZone == "fader" ? "Ziehen = Lautstärke  ·  Mausrad  ·  Shift = fein" + FaderTipSuffix +
                    (float.IsNaN(FaderMarkPos) ? "" : "\nDoppelklick: zur Fader-Marke")
                : _tipZone == "sig" ? "SIG – grün: hier kommt Ton an (ab " + DbText(SigDb) + ")\nLeuchtet auch, wenn der Kanal stumm oder leise ist\nRechtsklick: einstellen, ab wann"
                : _tipZone == "peak" ? "PEAK – rot: so laut, dass es gleich übersteuert (ab " + DbText(PeakDbLimit) + ")\nLeuchtet es oft, den Fader etwas zurücknehmen" +
                    (PeakCountOn ? "\n" + (_peakCount == 0 ? "Noch nie zu laut seit dem Zurücksetzen" : _peakCount + "× zu laut seit dem Zurücksetzen") + "  ·  Klick: Zähler auf 0" : "") +
                    "\nRechtsklick: einstellen, ab wann"
                : _tipZone == "gr" ? GrTooltip
                : _tipZone == "meter" ? "Pegel – die Spitze bleibt stehen, bis du klickst\nKlick: Spitze löschen"
                : GetTooltip(_tipZone);
            if (string.IsNullOrEmpty(text)) return;
            try { _tip.Show(text, this, _tipPoint.X + Theme.Px(14), _tipPoint.Y + Theme.Px(18), 6000); } catch { }
        }

        protected virtual string FaderTipSuffix => "";

        /// <summary>Weitere Anzeigen, die live (ueber dem Bild-Cache) gezeichnet werden – z. B. Lautheit im MASTER.</summary>
        protected virtual void PaintLiveExtra(Graphics g, Rectangle clip) { }
        /// <summary>2.17: hoerbarer Pegel (linear) aus dem Pegel-Takt – fuer Zuege, die ihn sich merken.</summary>
        protected virtual void OnMeterLevel(float peak, float dt) { }

        protected void HideTip()
        {
            _tipTimer.Stop();
            try { _tip.Hide(this); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tipTimer.Dispose();
                _tip.Dispose();
                _chrome.Dispose();
                _lit.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
