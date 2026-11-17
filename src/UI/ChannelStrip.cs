using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Mischpult.UI
{
    internal sealed class AppView
    {
        public string Key, Name, Path;
        public bool Running, Active;
    }

    /// <summary>Ein Kanalzug: Name, Apps, 3-Band-EQ, Balance, Solo/Mute, Fader, Pegel.</summary>
    internal sealed partial class ChannelStrip : StripBase
    {
        public readonly ChannelConfig Config;
        public List<AppView> Apps = new List<AppView>();
        private bool _implicitMute;
        private bool _eqAvailable = true;
        private string _eqStatus;
        private string _routeLabel = "Standard", _routeTip = "";
        private bool _routeMissing;
        private RectangleF _routeBtn;

        private RectangleF _nameRect, _appsRect, _appsBtn, _eqBtn, _compBtn, _autoBtn, _presetBtn, _panZone, _strZone, _soloBtn, _muteBtn, _fadeBtn;

        /// <summary>FADE-Taste zwischen SOLO und MUTE (nur bei normaler Breite; schmal: Shift+Klick auf MUTE).</summary>
        public static bool FadeKeys = true;
        public static readonly Color FadeColor = Color.FromArgb(0x5a, 0xa9, 0xff);
        private bool FadeKeyShown => FadeKeys && !Narrow;
        private PointF _strCenter;
        private readonly List<KeyValuePair<RectangleF, string>> _iconRects = new List<KeyValuePair<RectangleF, string>>();
        private readonly RectangleF[] _eqZones = new RectangleF[3];
        private readonly PointF[] _eqCenters = new PointF[3];
        private PointF _knobCenter;
        private TextBox _rename;

        private bool _ducked, _bgQuiet;
        private string _lcdOverride;
        private readonly Stopwatch _lcdTimer = new Stopwatch();

        private static readonly string[] EqNames = { "LO", "MID", "HI" };
        private static readonly string[] EqZoneIds = { "eqlo", "eqmid", "eqhi" };
        private static readonly string[] EqLong = { "Bässe (120 Hz)", "Mitten (1 kHz)", "Höhen (6 kHz)" };

        /// <summary>Gerade offenes Umbenennen-Feld (oder null).</summary>
        public static ChannelStrip Renaming { get; private set; }
        public IntPtr RenameHandle => _rename != null && _rename.IsHandleCreated ? _rename.Handle : IntPtr.Zero;

        public ChannelStrip(MixerForm mixer, ChannelConfig config) : base(mixer)
        {
            Config = config;
        }

        // 3.2: Breite pro Kanal (0 wie alle, 1 schmal, 2 normal, 3 breit)
        private int OwnWidth => Mixer.ChannelWidthOf(Config);
        private int DragW => Mixer.DragWidthOf(Config);   // 3.4.5: mit der Maus gezogen (0 = nicht)
        public override int StripWidth => Theme.Px(Folded ? 86 : DragW > 0 ? DragW : OwnWidth == 1 ? 86 : OwnWidth == 2 ? 108 : OwnWidth == 3 ? 132 : Mixer.Narrow ? 86 : 108);
        protected override bool Narrow => Folded || (DragW > 0 ? DragW < 98 : OwnWidth == 0 ? Mixer.Narrow : OwnWidth == 1);
        protected override float MeterBarWidth => Folded ? base.MeterBarWidth : DragW > 0 ? Math.Min(12f, Math.Max(base.MeterBarWidth, 7f + (DragW - 120) / 28f)) : OwnWidth == 3 ? 8f : base.MeterBarWidth;
        protected override bool CanResizeWidth => Mixer.DragWidthEnabled && !Folded;
        protected override float FaderShift => DragW > 140 ? (DragW - 140) * 0.45f : 0f;
        protected override void OnResizeWidth(int logicalPx, bool all, bool final) => Mixer.SetDragWidth(Config, logicalPx, all, final);
        protected override void OnResizeReset(bool all) => Mixer.SetDragWidth(Config, 0, all, true);
        protected override float FaderLimitPos => Mixer.CapPosOf(Config);

        // ---- 3.1: stumm abdunkeln, SOLO hebt hervor, inaktiv einklappen
        public static bool MuteDimOn = true, SoloDimOn = true;
        /// <summary>Eingeklappt (Programm spielt laenger nicht): schmal und etwas dunkler.</summary>
        public bool Folded;
        protected override int WholeDimAlpha => (Config.Mute && MuteDimOn) || (_implicitMute && SoloDimOn) ? 105 : Folded ? 55 : 0;
        private bool StreamOn => Mixer.StreamOn;
        protected override float FaderPos => StreamLayer ? StreamPos(Config) : Config.Fader;
        protected override Color AccentColor => StreamLayer ? StreamColor : _tint ?? Color.FromArgb(Config.Color);

        /// <summary>4.0 (finale): Fader-Ebene "Stream" – die Fader stellen den Anteil im Stream-Mix.</summary>
        public static bool StreamLayer;
        private static float StreamPos(ChannelConfig c) => c.StreamOff || c.StreamDb <= StreamOffDb ? 0f : FaderLaw.DbToPos(Math.Min(0f, c.StreamDb));

        // ---- 3.1: Kanalfarbe automatisch aus dem App-Symbol (nur Anzeige – die eigene Farbe bleibt gespeichert)
        public static bool AutoIconColor;
        private Color? _tint;
        private string _tintKey = "";

        /// <summary>Farbe aus dem Symbol der ersten zugeordneten App neu bestimmen (nach neuer App-Liste). true = geaendert.</summary>
        public bool RefreshTint()
        {
            AppView pick = null;
            if (AutoIconColor && !Config.IsCatchAll)
            {
                foreach (var k in Config.Apps)
                {
                    pick = Apps.Find(a => string.Equals(a.Key, k, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(a.Path));
                    if (pick != null) break;
                }
            }
            string key = pick == null ? "" : pick.Key + "|" + pick.Path;
            if (key == _tintKey) return false;
            _tintKey = key;
            var old = _tint;
            _tint = pick == null ? null : Mixer.Icons.Dominant(pick.Key, pick.Name, pick.Path);
            return old != _tint;
        }

        /// <summary>Farbe, in der der Kanal gerade gezeigt wird.</summary>
        public Color ChannelColor => AccentColor;
        public override string DisplayName => Config.Name;
        public override string BridgeKey => "ch:" + Config.Id;
        protected override bool HasGr => true;

        protected override float GrTarget
        {
            get
            {
                Mixer.ReductionOf(Config, out float comp, out float auto, out float duck, out float quiet, out float sleep);
                return comp + auto + duck + quiet + sleep;
            }
        }

        protected override string GrTooltip
        {
            get
            {
                Mixer.ReductionOf(Config, out float comp, out float auto, out float duck, out float quiet, out float sleep);
                var sb = new System.Text.StringBuilder();
                Action<string, float> add = (n, v) => { if (v >= 0.5f) sb.Append("\n• ").Append(n).Append(": ").Append(StripBase.DbText(-v)); };
                add("Kompressor (COMP)", comp);
                add("Lautstärke-Ausgleich (AUTO)", auto);
                add("Ducking", duck);
                add("Spiel im Hintergrund leiser", quiet);
                add("Sleep-Timer", sleep);
                float sum = comp + auto + duck + quiet + sleep;
                return sb.Length == 0
                    ? "GR (orange) – gerade wird nichts leiser gemacht.\nLeuchtet, wenn COMP, AUTO, Ducking, „Spiel im Hintergrund leiser“\noder der Sleep-Timer diesen Kanal leiser machen."
                    : "GR (orange) – so viel leiser macht das Mischpult diesen Kanal gerade: " + StripBase.DbText(-sum) + sb;
            }
        }
        protected override float DefaultFaderPos => 1f;
        protected override bool FaderLocked => Config.FaderLock;
        protected override float FaderMarkPos => Config.FaderMark ?? float.NaN;
        protected override void OnLockedTouch() { ShowLcd("GESPERRT"); InvalidateChrome(); }
        protected override RectangleF MuteKey => _muteBtn;
        protected override bool MuteLit => Config.Mute;
        protected override bool Dimmed => Config.Mute || _implicitMute;
        protected override string FaderTipSuffix => Config.FaderMark.HasValue ? "" : "  ·  Doppelklick = 0 dB";

        protected override string ReadoutText =>
            _lcdOverride != null && _lcdTimer.IsRunning && _lcdTimer.ElapsedMilliseconds < 1500
                ? _lcdOverride : StreamLayer ? FormatStream(Config) : FaderLaw.FormatDb(Config.Fader);

        protected override bool DbFader => true;

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                yield return new KeyValuePair<float, string>(1f, "0");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-5), "-5");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-10), "-10");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-20), "-20");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-30), "-30");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-40), "-40");
                yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(-60), "-60");
                yield return new KeyValuePair<float, string>(0f, "∞");
            }
        }

        public void SetState(bool implicitMute, bool eqAvailable, string eqStatus, string routeLabel, string routeTip, bool routeMissing)
        {
            if (implicitMute == _implicitMute && eqAvailable == _eqAvailable && eqStatus == _eqStatus &&
                routeLabel == _routeLabel && routeTip == _routeTip && routeMissing == _routeMissing) return;
            _implicitMute = implicitMute; _eqAvailable = eqAvailable; _eqStatus = eqStatus;
            _routeLabel = routeLabel; _routeTip = routeTip; _routeMissing = routeMissing;
            InvalidateChrome();
        }

        public void SetDucked(bool on)
        {
            if (on == _ducked) return;
            _ducked = on;
            InvalidateChrome();
        }

        /// <summary>Spiel ist nicht im Vordergrund und laeuft deshalb leiser.</summary>
        public void SetBgQuiet(bool on)
        {
            if (on == _bgQuiet) return;
            _bgQuiet = on;
            InvalidateChrome();
        }

        /// <summary>2.19: Schildchen FX (Hall/Echo an) und CUE (vorhoeren) zeigen.</summary>
        public static bool FxShown = true, CueShown;

        protected override void DrawOverlay(Graphics g)
        {
            float x = PanelRect.X + Theme.F(Narrow ? 4 : 6);
            if (_ducked) x = DrawPill(g, x, "DUCK", Theme.SoloAmber, Theme.F(Narrow ? 30 : 34));
            if (_bgQuiet) x = DrawPill(g, x, Narrow ? "LEISE" : "LEISER", Color.FromArgb(0x5a, 0xa9, 0xff), Theme.F(Narrow ? 34 : 40));
            if (CueShown && Config.Cue && !HidFx) x = DrawPill(g, x, "CUE", MixerForm.CueColor, Theme.F(Narrow ? 26 : 28));   // 2.19: vorhoeren
            if (FxShown && Config.FxType > 0 && !HidFx) x = DrawPill(g, x, "FX", MixerForm.FxColor, Theme.F(20));             // 2.19: Hall/Echo
            if (Config.FaderLock)
            {
                // Schloss: Fader gesperrt
                var r = new RectangleF(x, FaderArea.Y + Theme.F(1), Theme.F(15), Theme.F(11));
                Theme.FillRound(g, Theme.IsLight ? Theme.Darken(Theme.PanelBottom, 0.35f) : Color.FromArgb(0x5a, 0x60, 0x6a), r, Theme.F(3));
                DrawPadlock(g, new RectangleF(r.X + Theme.F(4), r.Y + Theme.F(1.5f), Theme.F(7), Theme.F(8.5f)), Theme.IsLight ? Color.White : Color.FromArgb(0xe8, 0xeb, 0xef));
            }
        }

        private float DrawPill(Graphics g, float x, string text, Color c, float w)
        {
            var r = new RectangleF(x, FaderArea.Y + Theme.F(1), w, Theme.F(11));
            Theme.FillRound(g, c, r, Theme.F(3));
            using (var b = new SolidBrush(Theme.Darken(c, 0.88f)))
                Theme.Str(g, text, Theme.Ui(8f, FontStyle.Bold), b, r, Theme.Center);
            return r.Right + Theme.F(3);
        }

        /// <summary>Vom UI-Timer: LCD nach dem Drehen eines Reglers wieder auf dB zurueckstellen.</summary>
        public void CheckLcdTimeout()
        {
            if (_lcdOverride != null && _lcdTimer.ElapsedMilliseconds >= 1500)
            {
                _lcdOverride = null;
                InvalidateChrome();
            }
        }

        private void ShowLcd(string text)
        {
            _lcdOverride = text;
            _lcdTimer.Restart();
        }

        protected override void OnFaderMoved(float pos, bool final)
        {
            if (StreamLayer)
            {
                // 4.0 (finale): Anteil im Stream-Mix
                float db = pos <= 0.001f ? StreamOffDb : Math.Max(StreamOffDb, FaderLaw.PosToDb(pos));
                db = (float)Math.Round(db * 2) / 2f;
                Config.StreamOff = db <= StreamOffDb;
                Config.StreamDb = db;
                _lcdOverride = null;
                Mixer.OnChannelChanged(Config, false);
                return;
            }
            float cap = Mixer.CapPosOf(Config);   // 3.2: Lautstaerke-Deckel
            if (!float.IsNaN(cap) && pos > cap) pos = cap;
            Config.Fader = pos;
            _lcdOverride = null;
            Mixer.OnChannelChanged(Config, false);
        }

        // --------------------------------------------------------------- Layout
        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _appsRect = new RectangleF(x, Theme.F(42), iw, Theme.F(44));
            _appsBtn = new RectangleF(x, Theme.F(90), iw, Theme.F(20));
            _routeBtn = new RectangleF(x, Theme.F(114), iw, Theme.F(20));

            // EQ + Presets
            float ky = Theme.F(121 + RowOffset), eqW = Theme.F(Narrow ? 26 : 30);
            _eqBtn = new RectangleF(x, ky, eqW, Theme.F(17));
            _presetBtn = new RectangleF(x + eqW + Theme.F(3), ky, iw - eqW - Theme.F(3), Theme.F(17));
            float cell = iw / 3f;
            for (int i = 0; i < 3; i++)
            {
                _eqCenters[i] = new PointF((float)Math.Round(x + cell * i + cell / 2), Theme.F(160 + RowOffset));
                _eqZones[i] = new RectangleF(x + cell * i, Theme.F(142 + RowOffset), cell, Theme.F(42));
            }

            // Dynamik: Kompressor, Lautstaerke-Ausgleich
            float bw = (iw - Theme.F(4)) / 2;
            _compBtn = new RectangleF(x, Theme.F(193 + RowOffset), bw, Theme.F(17));
            _autoBtn = new RectangleF(x + bw + Theme.F(4), Theme.F(193 + RowOffset), bw, Theme.F(17));

            // Balance (und Anteil im Stream-Mix)
            float py = Theme.F(209 + DynRow + RowOffset);
            if (StreamOn)
            {
                _knobCenter = new PointF((float)Math.Round(x + iw * 0.27f), py);
                _strCenter = new PointF((float)Math.Round(x + iw * 0.73f), py);
                _panZone = new RectangleF(x, py - Theme.F(19), iw / 2, Theme.F(40));
                _strZone = new RectangleF(x + iw / 2, py - Theme.F(19), iw / 2, Theme.F(40));
            }
            else
            {
                _knobCenter = new PointF((float)Math.Round(PanelRect.X + PanelRect.Width / 2), py);
                _panZone = new RectangleF(_knobCenter.X - Theme.F(22), py - Theme.F(19), Theme.F(44), Theme.F(40));
                _strZone = RectangleF.Empty;
            }

            float ky2 = Theme.F(237 + DynRow + RowOffset);
            if (FadeKeyShown)
            {
                // SOLO | FADE | MUTE – MUTE etwas breiter (wird am meisten gedrueckt)
                float gap = Theme.F(3), avail = iw - 2 * gap;
                float sw = (float)Math.Round(avail * 0.31f), fw = (float)Math.Round(avail * 0.31f), mw = avail - sw - fw;
                _soloBtn = new RectangleF(x, ky2, sw, Theme.F(22));
                _fadeBtn = new RectangleF(x + sw + gap, ky2, fw, Theme.F(22));
                _muteBtn = new RectangleF(x + sw + fw + 2 * gap, ky2, mw, Theme.F(22));
            }
            else
            {
                _soloBtn = new RectangleF(x, ky2, bw, Theme.F(22));
                _muteBtn = new RectangleF(x + bw + Theme.F(4), ky2, bw, Theme.F(22));
                _fadeBtn = RectangleF.Empty;
            }

            LayoutCustom400();   // 4.0: Kanalzug anpassen (ausgeblendete Teile, das App-Feld waechst)
            AddZone(_nameRect, "name");
            AddZone(_appsRect, "apps");
            AddZone(_appsBtn, "appsbtn");
            if (Mixer.MultiOutput) AddZone(_routeBtn, "route");
            AddZone(_eqBtn, "eq");
            AddZone(_presetBtn, "preset");
            for (int i = 0; i < 3; i++) AddZone(_eqZones[i], EqZoneIds[i]);
            AddZone(_compBtn, "comp");
            AddZone(_autoBtn, "auto");
            AddZone(_panZone, "pan");
            if (StreamOn) AddZone(_strZone, "str");
            AddZone(_soloBtn, "solo");
            if (!_fadeBtn.IsEmpty) AddZone(_fadeBtn, "fade");
            AddZone(_muteBtn, "mute");
            LayoutTop330();
        }

        private float GetEq(int i) => i == 0 ? Config.EqLow : i == 1 ? Config.EqMid : Config.EqHigh;

        /// <summary>EQ-Kurve im PRESET-Feld anzeigen (OPTIONEN → Leuchten &amp; Anzeigen).</summary>
        public static bool EqCurveOn = true;
        private float[] _curve;
        private string _curveKey;

        /// <summary>Kurve zu den aktuellen EQ-Werten (nur neu gerechnet, wenn sich etwas geaendert hat).</summary>
        private float[] CurveDb()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Config.EqLow).Append('|').Append(Config.EqMid).Append('|').Append(Config.EqHigh);
            if (Config.Geq != null) foreach (var v in Config.Geq) sb.Append('|').Append(v);
            string key = sb.ToString();
            if (key != _curveKey || _curve == null)
            {
                _curveKey = key;
                _curve = EqCurve.Response(Config.EqLow, Config.EqMid, Config.EqHigh, Config.Geq);
            }
            return _curve;
        }

        private void SetEq(int i, float v)
        {
            v = Math.Max(-MixerSettings.EqRange, Math.Min(MixerSettings.EqRange, (float)Math.Round(v * 2) / 2f));
            if (i == 0) Config.EqLow = v; else if (i == 1) Config.EqMid = v; else Config.EqHigh = v;
        }

        private static string FormatEq(float v) =>
            Math.Abs(v) < 0.05f ? "0 dB" : (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + " dB";

        // --------------------------------------------------------------- Zeichnen
        protected override void DrawTop(Graphics g)
        {
            Color col = ChannelColor;
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);

            // Farbkante oben
            Theme.DrawColorEdge(g, PanelRect, col);
            DrawGroupBracket330(g);   // 3.3: Gruppen-Klammer

            // Scribble-Strip (Name)
            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Theme.Darken(col, 0.45f) : col;
            Theme.DrawNamePlate(g, nr, bg);
            if (Theme.TwoTone && Config.Color2 != 0) Theme.DrawTwoToneOverlay(g, nr, Dimmed ? Theme.Darken(Color.FromArgb(255, Color.FromArgb(Config.Color2)), 0.45f) : Color.FromArgb(255, Color.FromArgb(Config.Color2)), Theme.F(3.5f));   // 3.4
            Theme.DrawNameGloss(g, nr);
            DrawPlateNumber(g, nr, bg);
            var ntr = NameTextRect(nr);
            float nfw = NameFitWidth(nr);
            DrawSymbol330(g, nr, bg, ref ntr, ref nfw);   // 3.3: Symbol im Namensschild
            using (var b = new SolidBrush(Theme.PlateText(bg)))
                Theme.Str(g, Config.Name, Theme.FitPlate(g, Config.Name, nfw, 12.5f, 8.5f), b, ntr, Theme.Center);
            DrawNote330(g);   // 3.3: Notizzettel

            if (!HidField) DrawApps(g);   // 4.0

            string appsText = Config.IsCatchAll ? "ALLE ÜBRIGEN" : (Apps.Count == 0 ? "APPS WÄHLEN" : Apps.Count + (Apps.Count == 1 ? " APP" : " APPS"));
            Theme.DrawDisplayButton(g, _appsBtn, appsText, HoverZone == "appsbtn" || HoverZone == "apps", Theme.Text);
            if (Mixer.MultiOutput)
                Theme.DrawDisplayButton(g, _routeBtn, _routeLabel, HoverZone == "route", _routeMissing ? Theme.MuteRed : Theme.Text, true, "→");

            Theme.Engrave(g, left, right, Theme.F(116 + RowOffset) + _shApps);

            // ---- EQ
            bool eqOn = Config.EqOn && _eqAvailable;
            if (!HidEq) {   // 4.0
            Theme.DrawKey(g, _eqBtn, EqAbOn && Config.EqIsB ? "EQ B" : "EQ", eqOn, Theme.LedGreen, HoverZone == "eq", Config.EqOn && !_eqAvailable);   // 3.4: EQ A/B
            bool geq = Config.Geq != null && !Mischpult.Audio.GraphicEq.IsFlat(Config.Geq);
            bool hoverPreset = HoverZone == "preset";
            if (EqCurveOn && !hoverPreset)
            {
                // EQ-Kurve im PRESET-Feld (unter der Maus steht wieder "PRESET")
                Theme.DrawDisplayButton(g, _presetBtn, "", false, Theme.TextDim);
                var cr = new RectangleF(_presetBtn.X + Theme.F(4), _presetBtn.Y + Theme.F(2), _presetBtn.Width - Theme.F(17), _presetBtn.Height - Theme.F(4));
                EqCurve.Draw(g, cr, CurveDb(), Theme.IsLight ? Theme.Darken(col, 0.2f) : Theme.Lighten(col, 0.15f), eqOn);
            }
            else
                Theme.DrawDisplayButton(g, _presetBtn, Narrow ? "" : "PRESET", hoverPreset,
                    geq && eqOn ? Theme.OkText : Theme.TextDim);

            Color ring = eqOn ? (Dimmed ? Theme.Darken(col, 0.4f) : col) : Theme.Disabled;
            for (int i = 0; i < 3; i++)
            {
                float v = ShownEq(i) / MixerSettings.EqRange;   // 4.0 finale: Motor-Potis
                if (Narrow) Theme.DrawKnob(g, _eqCenters[i], Theme.F(7f), v, ring, true, HoverZone == EqZoneIds[i], 3.5f, 9);
                else Theme.DrawKnob(g, _eqCenters[i], Theme.F(9.5f), v, ring, true, HoverZone == EqZoneIds[i], 5f, 11);
                if (Theme.BigConsole) Theme.DrawKnobCap(g, _eqCenters[i], Theme.F(Narrow ? 7f : 9.5f), Theme.ConsoleCapColor(EqNames[i]));   // 4.0 finale: Grosskonsole
                else if (StripBase.ColorCaps) Theme.DrawKnobCap(g, _eqCenters[i], Theme.F(Narrow ? 7f : 9.5f), ChannelColor);
                Theme.Label(g, EqNames[i], new RectangleF(_eqZones[i].X, _eqCenters[i].Y + Theme.F(14), _eqZones[i].Width, Theme.F(11)), Theme.Center,
                    Math.Abs(GetEq(i)) > 0.05f && eqOn ? Theme.Text : Theme.TextFaint, 8f);
            }
            Theme.Engrave(g, left, right, Theme.F(188 + RowOffset) + _shEq);
            }

            // ---- Dynamik
            bool compOn = Config.CompOn && _eqAvailable;
            bool night = Mixer.NightMode && _eqAvailable && !Config.CompOn;
            Theme.DrawKey(g, _compBtn, "COMP", compOn, Theme.Accent, HoverZone == "comp", night || (Config.CompOn && !_eqAvailable));
            Theme.DrawKey(g, _autoBtn, "AUTO", Config.AutoOn && _eqAvailable, AutoColor, HoverZone == "auto", Config.AutoOn && !_eqAvailable);

            Theme.Engrave(g, left, right, Theme.F(188 + DynRow + RowOffset) + _shEq);

            // ---- Balance (+ Stream-Anteil)
            Color knobCol = Dimmed ? Theme.Darken(col, 0.4f) : col;
            if (HidPan) { }   // 4.0: ausgeblendet
            else if (StreamOn)
            {
                float kr = Theme.F(Narrow ? 8f : 9.5f);
                Theme.DrawKnob(g, _knobCenter, kr, ShownPan, knobCol, true, HoverZone == "pan", Narrow ? 3.5f : 5f, Narrow ? 9 : 11);
                if (Theme.BigConsole) Theme.DrawKnobCap(g, _knobCenter, kr, Theme.ConsoleCapColor("PAN"));
                else if (StripBase.ColorCaps) Theme.DrawKnobCap(g, _knobCenter, kr, ChannelColor);
                bool sOff = Config.StreamOff || Config.StreamDb <= StreamOffDb;
                Theme.DrawKnob(g, _strCenter, kr, sOff ? 0f : (Config.StreamDb - StreamOffDb) / (StreamMaxDb - StreamOffDb), sOff ? Theme.Disabled : StreamColor, false, HoverZone == "str", Narrow ? 3.5f : 5f, Narrow ? 9 : 11);
                if (Theme.BigConsole) Theme.DrawKnobCap(g, _strCenter, kr, Theme.ConsoleCapColor("STREAM"));
                Theme.Label(g, "PAN", new RectangleF(_panZone.X, _knobCenter.Y + Theme.F(12), _panZone.Width, Theme.F(11)), Theme.Center, Theme.TextFaint, 8f);
                Theme.Label(g, "STREAM", new RectangleF(_strZone.X - Theme.F(4), _strCenter.Y + Theme.F(12), _strZone.Width + Theme.F(8), Theme.F(11)), Theme.Center,
                    sOff ? Theme.TextFaint : StreamColor, 7.5f);
            }
            else
            {
                if (!Narrow)
                {
                    Theme.Label(g, "PAN", new RectangleF(Theme.F(6), _knobCenter.Y - Theme.F(18), Theme.F(30), Theme.F(12)), Theme.Center, Theme.TextFaint, 8.5f);
                    string pv = PanText(Config.Pan);
                    using (var b = new SolidBrush(Math.Abs(Config.Pan) < 0.005f ? Theme.TextDim : Theme.ValueText))
                        Theme.Str(g, pv, Theme.Mono(10.5f), b, new RectangleF(PanelRect.Right - Theme.F(34), _knobCenter.Y - Theme.F(19), Theme.F(30), Theme.F(14)), Theme.Center);
                }
                Theme.DrawKnob(g, _knobCenter, Theme.F(Narrow ? 9f : 11f), ShownPan, knobCol, true, HoverZone == "pan");
                if (Theme.BigConsole) Theme.DrawKnobCap(g, _knobCenter, Theme.F(Narrow ? 9f : 11f), Theme.ConsoleCapColor("PAN"));
                else if (StripBase.ColorCaps) Theme.DrawKnobCap(g, _knobCenter, Theme.F(Narrow ? 9f : 11f), ChannelColor);
                Theme.Label(g, "L", new RectangleF(_knobCenter.X - Theme.F(24), _knobCenter.Y + Theme.F(9), Theme.F(10), Theme.F(12)), Theme.Center, Theme.TextFaint, 8f);
                Theme.Label(g, "R", new RectangleF(_knobCenter.X + Theme.F(14), _knobCenter.Y + Theme.F(9), Theme.F(10), Theme.F(12)), Theme.Center, Theme.TextFaint, 8f);
            }

            if (!HidPan || !HidKeys) Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset) + _shPan);

            if (!HidKeys)   // 4.0
            {
                Theme.DrawKey(g, _soloBtn, "SOLO", Config.Solo, Theme.SoloAmber, HoverZone == "solo");
                if (!_fadeBtn.IsEmpty) Theme.DrawKey(g, _fadeBtn, "FADE", Mixer.IsFading(Config), FadeColor, HoverZone == "fade");
                DrawMuteKey(g, _muteBtn, Config.Mute, _implicitMute && !Config.Mute);
            }
            DrawExtras350(g);   // 3.5: Verzoegerung

            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        public static readonly Color AutoColor = Color.FromArgb(0x5a, 0xa9, 0xff);
        public static readonly Color StreamColor = Color.FromArgb(0xb4, 0x7c, 0xff);
        public const float StreamOffDb = -40f, StreamMaxDb = 6f;

        private static string FormatStream(ChannelConfig c) =>
            c.StreamOff || c.StreamDb <= StreamOffDb ? "STR AUS" : "STR " + (c.StreamDb > 0 ? "+" : "") + c.StreamDb.ToString("0.0", CultureInfo.InvariantCulture);

        private void SetStreamDb(float db)
        {
            db = Math.Max(StreamOffDb, Math.Min(StreamMaxDb, (float)Math.Round(db * 2) / 2f));
            if (db > StreamOffDb) Config.StreamOff = false;
            Config.StreamDb = db;
            ShowLcd(FormatStream(Config));
            Mixer.OnChannelChanged(Config, false);
            InvalidateChrome();
        }

        /// <summary>2.16: eigene Kanal-Bilder zeigen (OPTIONEN → Aussehen).</summary>
        public static bool ImagesOn = true;

        // ---- 2.17 Anzeigefeld: Apps, Lautheit, Spektrum, Pegel-Verlauf oder Album-Cover
        /// <summary>Anzeigefeld fuer alle Kanaele ohne eigene Wahl (OPTIONEN → Aussehen).</summary>
        public static int GlobalField;
        /// <summary>Was im Feld steht (eigene Wahl des Kanals oder die fuer alle).</summary>
        public int FieldMode => ChannelField.Clamp(Config.Field ?? GlobalField);
        private bool FieldLive => (FieldMode != ChannelField.Apps || GifLive) && !Compact;   // 4.0: bewegtes GIF wird auch live gezeichnet
        private Rectangle FieldBounds => Rectangle.Inflate(Rectangle.Ceiling(_appsRect), 1, 1);
        private float _fLufs = float.NaN;
        private string _fLufsText;
        private readonly SpectrumModel _fSpec = new SpectrumModel();
        private int _fSpecVer = -1;
        private readonly LevelHistory _fHist = new LevelHistory();
        private System.Drawing.Bitmap _fCover;
        private string _fCoverText;
        private bool _fPlaying;
        /// <summary>Pegel-Verlauf (fuer Tests/Vorschau).</summary>
        public LevelHistory FieldHistory => _fHist;

        /// <summary>Lautheit [M, S, I] in LUFS nach dem Fader (null = nichts gemessen / stumm).</summary>
        public void SetFieldLoudness(float[] v, int mode)
        {
            if (FieldMode != ChannelField.Loud) { _fLufsText = null; return; }
            float x = v != null && v.Length > mode ? v[Math.Max(0, Math.Min(2, mode))] : float.NaN;
            if (float.IsInfinity(x) || x < -70f) x = float.NaN;
            string t = (float.IsNaN(x) ? "–" : ChannelField.LufsText(x)) + mode;
            if (t == _fLufsText) return;
            _fLufsText = t; _fLufs = x;
            if (FieldLive) Invalidate(FieldBounds);
        }

        /// <summary>Spektrum (dB je Band, schon mit Fader verrechnet; null = still).</summary>
        public void SetFieldSpectrum(float[] db, float dt)
        {
            if (FieldMode != ChannelField.Spec) return;
            _fSpec.Update(db, dt);
            if (_fSpec.Version != _fSpecVer) { _fSpecVer = _fSpec.Version; if (FieldLive) Invalidate(FieldBounds); }
        }

        /// <summary>Album-Cover (was gerade laeuft).</summary>
        public void SetFieldCover(System.Drawing.Bitmap cover, string text, bool playing)
        {
            if (FieldMode != ChannelField.Cover) return;
            if (ReferenceEquals(cover, _fCover) && text == _fCoverText && playing == _fPlaying) return;
            _fCover = cover; _fCoverText = text; _fPlaying = playing;
            if (FieldLive) Invalidate(FieldBounds);
        }

        protected override void OnMeterLevel(float peak, float dt)
        {
            StatsSample(peak, dt);   // 3.5
            if (_fHist.Add(peak, dt) && FieldMode == ChannelField.Hist && FieldLive) Invalidate(FieldBounds);
        }

        /// <summary>Feld wurde umgestellt: neu zeichnen.</summary>
        public void FieldChanged()
        {
            _fLufsText = null; _fSpecVer = -1; _fCover = null; _fCoverText = null;
            _fSpec.Reset();
            InvalidateChrome();
        }

        protected override void PaintLiveExtra(Graphics g, Rectangle clip)
        {
            if (!FieldLive || !IsDirty(FieldBounds)) return;
            var r = _appsRect;
            Theme.Setup(g);
            Theme.DrawWell(g, r, HoverZone == "apps");
            var st = g.Save();
            using (var path = Theme.Round(RectangleF.Inflate(r, -Theme.F(1), -Theme.F(1)), Theme.F(4))) g.SetClip(path, CombineMode.Intersect);
            Color col = ChannelColor;
            switch (FieldMode)
            {
                case ChannelField.Loud:
                    ChannelField.DrawLoud(g, r, _fLufs, float.IsNaN(_fLufs), OutputStrip.LufsLetter);
                    break;
                case ChannelField.Spec:
                    ChannelField.DrawSpec(g, r, _fSpec, Dimmed ? Theme.Darken(col, 0.4f) : col);
                    break;
                case ChannelField.Hist:
                    ChannelField.DrawHist(g, r, _fHist, Dimmed ? Theme.Darken(col, 0.4f) : col);
                    break;
                case ChannelField.Cover:
                    ChannelField.DrawCoverField(g, r, _fCover, _fCoverText, _fPlaying);
                    break;
                case ChannelField.Apps:   // 4.0: bewegtes GIF
                    _gifShown = ChannelImages.FrameIndex(Config.ImagePath, GifT);
                    DrawAppsBody(g, r, HoverZone == "apps", ChannelImages.Frame(Config.ImagePath, _gifShown));
                    break;
            }
            g.Restore(st);
        }

        private void DrawApps(Graphics g)
        {
            var r = _appsRect;
            bool hover = HoverZone == "apps";
            Theme.DrawWell(g, r, hover);
            if (FieldLive) return;   // 2.17: das Feld wird live gezeichnet
            DrawAppsBody(g, r, hover, ImagesOn ? ChannelImages.Get(Config.ImagePath) : null);
        }

        /// <summary>Inhalt des App-Felds: Bild (auch ein Bild aus einem GIF) und App-Symbole.</summary>
        private void DrawAppsBody(Graphics g, RectangleF r, bool hover, System.Drawing.Bitmap cover)
        {
            DrawAppsBodyCore(g, r, hover, cover);
            DrawWinTitle(g, r);   // 4.0: was laeuft
        }

        private void DrawAppsBodyCore(Graphics g, RectangleF r, bool hover, System.Drawing.Bitmap cover)
        {
            if (cover != null) ChannelImages.DrawCover(g, RectangleF.Inflate(r, -Theme.F(1), -Theme.F(1)), cover, Theme.F(4), Apps.Count == 0 ? 25 : 90);

            if (Apps.Count == 0)
            {
                if (cover != null) return;   // das Bild ist der Inhalt
                string t = Config.IsCatchAll ? "keine weiteren\nApps aktiv" : "+ Apps\nhinzufügen";
                using (var b = new SolidBrush(hover ? Theme.TextDim : Theme.TextFaint))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    Theme.Str(g, t, Theme.Ui(9.5f), b, r, sf);
                return;
            }

            int size = Theme.Px(20), gap = Theme.Px(4);
            int cols = Math.Max(1, (int)((r.Width - Theme.F(4) + gap) / (size + gap)));
            int maxShown = cols * 2;
            float totalW = cols * size + (cols - 1) * gap;
            float x0 = r.X + (r.Width - totalW) / 2, y0 = r.Y + (r.Height - (size * 2 + gap)) / 2;

            var list = new List<AppView>(Apps);
            list.Sort((a, b) =>
            {
                int ra = a.Active ? 0 : a.Running ? 1 : 2, rb = b.Active ? 0 : b.Running ? 1 : 2;
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            _iconRects.Clear();
            int shown = list.Count > maxShown ? maxShown - 1 : list.Count;
            for (int i = 0; i < shown; i++)
            {
                var a = list[i];
                float x = x0 + (i % cols) * (size + gap), y = y0 + (i / cols) * (size + gap);
                var e = Mixer.Icons.Get(a.Key, a.Name, a.Path);
                var img = a.Active ? e.Normal : a.Running ? e.Dim : e.Off;
                g.DrawImage(img, new RectangleF(x, y, size, size));
                _iconRects.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, size, size), a.Key));
                // eigene Lautstaerke der App: kleiner Balken unter dem Symbol
                float ap = Mixer.AppPos(a.Key);
                if (ap < 0.9995f)
                {
                    var bar = new RectangleF(x, y + size - Theme.F(3), size, Theme.F(3));
                    using (var b = new SolidBrush(Color.FromArgb(220, 0x05, 0x06, 0x07))) g.FillRectangle(b, bar);
                    using (var b = new SolidBrush(ap <= FaderLaw.MinPos ? Theme.MuteRed : Theme.LcdText))
                        g.FillRectangle(b, bar.X, bar.Y + Theme.F(0.5f), ap <= FaderLaw.MinPos ? bar.Width : Math.Max(Theme.F(2), bar.Width * ap), bar.Height - Theme.F(1));
                }
                if (a.Active) Theme.DrawLed(g, x + size - Theme.F(1), y + size - Theme.F(1), Theme.F(2.2f), Theme.LedGreen, true);
            }
            if (list.Count > maxShown)
            {
                int i = maxShown - 1;
                float x = x0 + (i % cols) * (size + gap), y = y0 + (i / cols) * (size + gap);
                var rr = new RectangleF(x, y, size, size);
                Theme.FillRound(g, Theme.Chip, rr, Theme.F(4));
                Theme.Label(g, "+" + (list.Count - shown), rr, Theme.Center, Theme.Text, 9f);
            }
        }

        public static string PanText(float pan)
        {
            if (Math.Abs(pan) < 0.005f) return "C";
            int v = (int)Math.Round(Math.Abs(pan) * 100);
            return (pan < 0 ? "L" : "R") + v.ToString(CultureInfo.InvariantCulture);
        }

        private static int EqIndex(string zone) => Array.IndexOf(EqZoneIds, zone);

        // --------------------------------------------------------------- Interaktion
        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (zone == "fade") { Mixer.ShowFadeMenu(this, PointToScreen(new Point((int)_fadeBtn.X, (int)_fadeBtn.Bottom + 2))); return false; }
                Mixer.ShowChannelMenu(this, PointToScreen(e.Location));
                return false;
            }
            if (e.Button != MouseButtons.Left) return false;
            switch (zone)
            {
                case "note":
                    Mixer.EditNote(Config);   // 3.3
                    return false;
                case "apps":
                    if (TryBeginAppDrag(e)) return true;   // 4.0: App packen (Klick ohne Ziehen: wie bisher)
                    if (Apps.Count > 0) { Mixer.ShowAppMix(this); return false; }
                    Mixer.ShowAppMenu(this, PointToScreen(new Point((int)_appsBtn.X, (int)_appsBtn.Bottom + 2)));
                    return false;
                case "appsbtn":
                    Mixer.ShowAppMenu(this, PointToScreen(new Point((int)_appsBtn.X, (int)_appsBtn.Bottom + 2)));
                    return false;
                case "route":
                    Mixer.ShowRouteMenu(this, PointToScreen(new Point((int)_routeBtn.X, (int)_routeBtn.Bottom + 2)));
                    return false;
                case "eq":
                    if (!_eqAvailable && !Config.EqOn)
                    {
                        MessageBox.Show(FindForm(), _eqStatus ?? "Der EQ ist auf diesem System nicht verfügbar.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                    Config.EqOn = !Config.EqOn;
                    ShowLcd(Config.EqOn ? "EQ AN" : "EQ AUS");
                    Mixer.OnChannelChanged(Config, false);
                    InvalidateChrome();
                    return false;
                case "comp":
                    if (!_eqAvailable && !Config.CompOn)
                    {
                        MessageBox.Show(FindForm(), _eqStatus ?? "Der Kompressor ist auf diesem System nicht verfügbar.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                    Config.CompOn = !Config.CompOn;
                    ShowLcd(Config.CompOn ? "COMP AN" : "COMP AUS");
                    Mixer.OnChannelChanged(Config, false);
                    InvalidateChrome();
                    return false;
                case "preset":
                    Mixer.ShowEqPresetMenu(this, PointToScreen(new Point((int)_presetBtn.Right, (int)_presetBtn.Bottom + 2)));
                    return false;
                case "auto":
                    if (!_eqAvailable && !Config.AutoOn)
                    {
                        MessageBox.Show(FindForm(), _eqStatus ?? "Der Lautstärke-Ausgleich ist auf diesem System nicht verfügbar.", "Mischpult", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                    Config.AutoOn = !Config.AutoOn;
                    ShowLcd(Config.AutoOn ? "AUTO AN" : "AUTO AUS");
                    Mixer.OnChannelChanged(Config, false);
                    InvalidateChrome();
                    return false;
                case "str":
                    ShowLcd(FormatStream(Config));
                    InvalidateChrome();
                    return true;
                case "solo":
                    Config.Solo = !Config.Solo;
                    Mixer.OnChannelChanged(Config, false);
                    InvalidateChrome();
                    return false;
                case "mute":
                    if ((ModifierKeys & Keys.Shift) != 0) { Mixer.ToggleFade(Config, false); ShowLcd(Mixer.IsFadingOut(Config) ? "AUSBLENDEN" : "EINBLENDEN"); InvalidateChrome(); return false; }
                    Mixer.CancelFade(Config);   // MUTE bricht ein laufendes Ein-/Ausblenden ab
                    Config.Mute = !Config.Mute;
                    Mixer.OnChannelChanged(Config, false);
                    InvalidateChrome();
                    return false;
                case "fade":
                    Mixer.ToggleFade(Config, false);
                    ShowLcd(Mixer.IsFadingOut(Config) ? "AUSBLENDEN" : "EINBLENDEN");
                    InvalidateChrome();
                    return false;
                case "pan":
                    return true;
            }
            if (EqIndex(zone) >= 0)
            {
                ShowLcd(EqNames[EqIndex(zone)] + " " + FormatEq(GetEq(EqIndex(zone))).Replace(" dB", ""));
                InvalidateChrome();
                return true;
            }
            return false;
        }

        /// <summary>EQ-Preset uebernehmen (Comp nur, wenn das Preset ihn vorsieht).</summary>
        public void ApplyPreset(EqPreset p)
        {
            Config.EqLow = p.Low; Config.EqMid = p.Mid; Config.EqHigh = p.High;
            bool flat = p.Low == 0 && p.Mid == 0 && p.High == 0;
            if (!flat && _eqAvailable) Config.EqOn = true;
            if (p.Comp && _eqAvailable) Config.CompOn = true;
            ShowLcd(flat ? "EQ FLAT" : "PRESET");
            Mixer.OnChannelChanged(Config, false);
            InvalidateChrome();
        }

        /// <summary>Am EQ drehen schaltet den EQ automatisch ein.</summary>
        private void ChangeEq(int i, float value)
        {
            float before = GetEq(i);
            SetEq(i, value);
            float after = GetEq(i);
            if (after == before) { ShowLcd(EqNames[i] + " " + FormatEq(after).Replace(" dB", "")); InvalidateChrome(); return; }
            if (!Config.EqOn && _eqAvailable) Config.EqOn = true;
            ShowLcd(EqNames[i] + " " + FormatEq(after).Replace(" dB", ""));
            Mixer.OnChannelChanged(Config, false);
            InvalidateChrome();
        }

        private float _eqDragVirtual;

        protected override bool OnZoneDrag(string zone, int dy, bool fine)
        {
            if (zone == "apps" && _dragApp != null) { AppDragMove(); return true; }   // 4.0
            int ei = EqIndex(zone);
            if (ei >= 0)
            {
                if (_eqDragVirtual == float.MinValue) _eqDragVirtual = GetEq(ei);
                _eqDragVirtual -= dy * (fine ? 0.05f : 0.2f);
                _eqDragVirtual = Math.Max(-MixerSettings.EqRange, Math.Min(MixerSettings.EqRange, _eqDragVirtual));
                ChangeEq(ei, _eqDragVirtual);
                return true;
            }
            if (zone == "str")
            {
                if (_strDragVirtual == float.MinValue) _strDragVirtual = Config.StreamOff ? StreamOffDb : Config.StreamDb;
                _strDragVirtual = Math.Max(StreamOffDb, Math.Min(StreamMaxDb, _strDragVirtual - dy * (fine ? 0.05f : 0.3f)));
                SetStreamDb(_strDragVirtual);
                return true;
            }
            if (zone != "pan") return false;
            float np = Math.Max(-1f, Math.Min(1f, Config.Pan - dy / Theme.F(fine ? 600f : 120f)));
            if (Math.Abs(np) < 0.02f && Math.Abs(Config.Pan) >= 0.02f) np = 0f;
            if (np != Config.Pan)
            {
                Config.Pan = np;
                ShowLcd("PAN " + PanText(np));
                Mixer.OnChannelChanged(Config, false);
                InvalidateChrome();
            }
            return true;
        }

        private float _strDragVirtual = float.MinValue;

        protected override void OnZoneDragEnd(string zone) { _eqDragVirtual = float.MinValue; _strDragVirtual = float.MinValue; if (zone == "apps") AppDragEnd(); }

        protected override bool IsMoveHandle(string zone) => zone == "name";

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _eqDragVirtual = float.MinValue;
            _strDragVirtual = float.MinValue;
            base.OnMouseDown(e);
        }

        protected override bool OnZoneDoubleClick(string zone, MouseEventArgs e)
        {
            int ei = EqIndex(zone);
            if (ei >= 0)
            {
                ChangeEq(ei, 0f);
                return true;
            }
            if (zone == "str")
            {
                SetStreamDb(0f);
                return true;
            }
            if (zone == "pan")
            {
                Config.Pan = 0f;
                ShowLcd("PAN C");
                Mixer.OnChannelChanged(Config, false);
                InvalidateChrome();
                return true;
            }
            if (zone == "name" && e.Button == MouseButtons.Left)
            {
                BeginRename();
                return true;
            }
            return false;
        }

        /// <summary>App-Symbol an dieser Stelle (oder null).</summary>
        private string IconAt(Point p)
        {
            foreach (var kv in _iconRects)
                if (RectangleF.Inflate(kv.Key, Theme.F(2), Theme.F(2)).Contains(p)) return kv.Value;
            return null;
        }

        protected override bool OnZoneWheel(string zone, int delta)
        {
            if (zone == "apps")
            {
                // Mausrad ueber einem Symbol: nur diese App lauter/leiser
                string key = IconAt(PointToClient(Cursor.Position));
                if (key == null) return false;
                float pos = Mixer.NudgeApp(key, delta, (ModifierKeys & Keys.Control) != 0);
                var app = Apps.Find(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
                string nm = (app?.Name ?? key).ToUpperInvariant();
                if (nm.Length > 5) nm = nm.Substring(0, 5);
                ShowLcd(nm + " " + FaderLaw.FormatDb(pos).Replace(" dB", ""));
                InvalidateChrome();
                return true;
            }
            int ei = EqIndex(zone);
            if (ei >= 0)
            {
                float step = (ModifierKeys & Keys.Control) != 0 ? 0.5f : 1f;
                ChangeEq(ei, GetEq(ei) + delta / 120f * step);
                return true;
            }
            if (zone == "str")
            {
                float cur = Config.StreamOff ? StreamOffDb : Config.StreamDb;
                SetStreamDb(cur + delta / 120f * ((ModifierKeys & Keys.Control) != 0 ? 0.5f : 1f));
                return true;
            }
            if (zone != "pan") return false;
            float pstep = (ModifierKeys & Keys.Control) != 0 ? 0.01f : 0.05f;
            float np = Math.Max(-1f, Math.Min(1f, Config.Pan + delta / 120f * pstep));
            if (Math.Abs(np) < 0.001f) np = 0;
            Config.Pan = (float)Math.Round(np, 3);
            ShowLcd("PAN " + PanText(Config.Pan));
            Mixer.OnChannelChanged(Config, false);
            InvalidateChrome();
            return true;
        }

        protected override string GetTooltip(string zone)
        {
            int ei = EqIndex(zone);
            if (ei >= 0)
                return EqLong[ei] + ": " + FormatEq(GetEq(ei)) + "\nZiehen/Mausrad · Doppelklick = 0 dB" +
                       (Config.EqOn ? "" : "\n(Drehen schaltet den EQ ein)");
            switch (zone)
            {
                case "note": return "Notiz: " + (Config.Note ?? "") + "\nKlicken: ändern oder löschen";
                case "name": return "Ziehen: Kanal verschieben (geht auch an jeder freien Stelle)\nDoppelklick: umbenennen  ·  Rechtsklick: Farbe, Gruppe, löschen …" +
                                    (MultiFaderOn ? "\nStrg+Klick: mit anderen Kanälen zusammen bewegen" : "") + (StatsOn ? "\n" + StatsText() : "") +
                                    (_latencyMs > 0 ? "\nLäuft durchs Mischpult: etwa " + _latencyMs + " ms später als direkt" : "");
                case "apps" when FieldLive:
                    return "Anzeigefeld: " + ChannelField.Names[FieldMode] + " – " + ChannelField.Hints[FieldMode] +
                           (FieldMode == ChannelField.Loud && !float.IsNaN(_fLufs) ? "\nGerade: " + ChannelField.LufsText(_fLufs) + " LUFS (" + OutputStrip.LufsModeNames[Math.Max(0, Math.Min(2, OutputStrip.LufsMode))] + ")" : "") +
                           "\nRechtsklick auf den Kanal → Anzeigefeld: umstellen  ·  Klicken: Apps";
                case "apps":
                case "appsbtn":
                    if (Apps.Count == 0) return Config.IsCatchAll ? "Dieser Kanal steuert automatisch alle Apps, die keinem anderen Kanal zugeordnet sind." : "Klicken, um Apps zuzuordnen";
                    var lines = new List<string>();
                    foreach (var a in Apps)
                    {
                        float ap = Mixer.AppPos(a.Key);
                        lines.Add((a.Active ? "● " : a.Running ? "○ " : "   ") + a.Name + (a.Running ? Mixer.AppLevelInfo(a.Key) : "  (nicht gestartet)") +
                                  (ap < 0.9995f ? "  · eigene Lautstärke " + FaderLaw.FormatDb(ap) : ""));
                    }
                    if (zone == "apps") lines.Add("\nKlicken: Lautstärke pro App  ·  Mausrad über einem Symbol: diese App lauter/leiser");
                    return string.Join("\n", lines);
                case "eq":
                    if (!_eqAvailable) return _eqStatus ?? "EQ nicht verfügbar";
                    return (Config.EqOn ? "EQ ist an" : "EQ ist aus") +
                           "\nMit EQ läuft der Ton dieses Kanals durch das Mischpult (ca. 30–50 ms Verzögerung)." +
                           (_eqStatus != null ? "\n\n" + _eqStatus : "");
                case "comp":
                    if (!_eqAvailable) return _eqStatus ?? "Kompressor nicht verfügbar";
                    return "Kompressor " + (Config.CompOn ? "an" : "aus") + " – gleicht laut und leise aus (z. B. nachts)" +
                           (Mixer.NightMode && !Config.CompOn ? "\nZurzeit durch den Nachtmodus eingeschaltet." : "") +
                           "\nLäuft wie der EQ durch das Mischpult (ca. 30–50 ms Verzögerung).";
                case "preset": return (EqCurveOn ? "EQ-Kurve: so ist der EQ gerade eingestellt (Bass links, Höhen rechts)\n" : "") +
                                      "Klick: EQ-Presets (Stimmen klarer, Schritte hören, Mehr Bass …) und großer EQ mit 10 Bändern und Spektrum";
                case "auto":
                    if (!_eqAvailable) return _eqStatus ?? "Lautstärke-Ausgleich nicht verfügbar";
                    float lg = Mixer.LevelerGainOf(Config.Id);
                    return "Lautstärke-Ausgleich " + (Config.AutoOn ? "an" : "aus") + " – hält den Kanal gleich laut (±12 dB)" +
                           (Config.AutoOn && !float.IsNaN(lg) ? "\nZurzeit: " + (lg >= 0 ? "+" : "") + lg.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : "") +
                           "\nLäuft wie der EQ durch das Mischpult (ca. 30–50 ms Verzögerung).";
                case "str":
                    return "Anteil im Stream-Mix: " + FormatStream(Config).Replace("STR ", "") + (Config.StreamOff || Config.StreamDb <= StreamOffDb ? "" : " dB") +
                           "\n(nach deinem Fader – 0 dB = so wie du es hörst)\nZiehen/Mausrad · Doppelklick = 0 dB · ganz runter = aus";
                case "route": return _routeTip;
                case "pan": return "Balance " + PanText(Config.Pan) + "  ·  Ziehen/Mausrad  ·  Doppelklick = Mitte";
                case "solo": return "Nur diesen Kanal (und andere Solo-Kanäle) hören";
                case "mute": return (_implicitMute && !Config.Mute ? "Stumm, weil ein anderer Kanal auf Solo steht" : "Kanal stummschalten") +
                        "\nShift+Klick: langsam " + (Config.Mute ? "einblenden" : "ausblenden") + " (" + MixerForm.FadeSecText(Mixer.FadeSec) + ")";
                case "fade":
                    return Mixer.IsFading(Config)
                        ? (Mixer.IsFadingOut(Config) ? "Blendet gerade aus – klicken: wieder einblenden" : "Blendet gerade ein – klicken: wieder ausblenden")
                        : (Config.Mute ? "FADE: langsam einblenden" : "FADE: langsam ausblenden, danach stumm") + " (" + MixerForm.FadeSecText(Mixer.FadeSec) + ")\nRechtsklick: Dauer einstellen";
            }
            return null;
        }

        // --------------------------------------------------------------- Umbenennen
        public void BeginRename()
        {
            if (_rename != null) return;
            HideTip();
            var r = Rectangle.Round(_nameRect);
            _rename = new TextBox
            {
                Text = Config.Name,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                TextAlign = HorizontalAlignment.Center,
                Font = Theme.Ui(12.5f, FontStyle.Bold),
                MaxLength = 14,
                CharacterCasing = CharacterCasing.Upper,
            };
            _rename.SetBounds(r.X + 2, r.Y + (r.Height - _rename.PreferredHeight) / 2, r.Width - 4, _rename.PreferredHeight);
            Controls.Add(_rename);
            Renaming = this;
            _rename.Focus();
            _rename.SelectAll();
            _rename.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EndRename(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndRename(false); }
            };
            _rename.LostFocus += (s, e) => EndRename(true);
        }

        public void EndRename(bool commit)
        {
            var tb = _rename;
            if (tb == null) return;
            _rename = null;
            if (Renaming == this) Renaming = null;
            string name = tb.Text.Trim();
            Controls.Remove(tb);
            tb.Dispose();
            if (commit && name.Length > 0 && name != Config.Name)
            {
                Config.Name = name;
                Mixer.OnChannelChanged(Config, false);
            }
            InvalidateChrome();
        }
    }
}
