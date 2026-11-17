using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Mischpult.Audio;

namespace Mischpult.UI
{
    /// <summary>
    /// Ausgangszug. Ohne feste Geraete-ID: MASTER, folgt dem Windows-Standardgeraet (umschaltbar).
    /// Mit fester Geraete-ID (Modus "Mehrere Ausgaenge"): ein Zug pro Geraet.
    /// </summary>
    internal sealed partial class OutputStrip : StripBase
    {
        public string DeviceId { get; private set; }
        private DeviceState _dev;
        private float _localVol = -1f;
        private readonly Stopwatch _localUntil = new Stopwatch();
        private List<AppView> _apps = new List<AppView>();

        private RectangleF _nameRect, _infoRect, _devBtn, _appsRect, _capBtn, _muteBtn, _vuRect, _dimBtn, _loudBtn, _lufsRect, _monoBtn;
        /// <summary>3.4: MONO-Taste am MASTER (neben GEHÖRSCHUTZ).</summary>
        public static bool MonoKeyOn;
        private bool MonoShown => MonoKeyOn && !Fixed;
        public override string StickerOwner => Fixed ? "out:" + FixedDeviceId : "master";

        // ---- Lautheit (LUFS) im MASTER bzw. im Zug des Standardgeraets – 2.16.1: auf Wunsch in jedem Ausgang (eigene Messung)
        /// <summary>Lautheit anzeigen (OPTIONEN → Leuchten &amp; Anzeigen).</summary>
        public static bool LufsOn = true;
        /// <summary>2.16.1: Lautheit auch in den anderen Ausgaengen (Modus "Mehrere Ausgaenge"), jeder misst sein Geraet.</summary>
        public static bool LufsEach = true;
        /// <summary>0 Momentan (0,4 s), 1 Kurzzeit (3 s), 2 Integriert (seit dem Zuruecksetzen).</summary>
        public static int LufsMode = 1;
        public static readonly string[] LufsModeNames = { "Momentan (0,4 s)", "Kurzzeit (3 s)", "Integriert (seit Zurücksetzen)" };
        private static readonly string[] LufsLetters = { "M", "S", "I" };
        /// <summary>Buchstabe der gewaehlten Lautheit (M, S, I).</summary>
        public static string LufsLetter => LufsLetters[Math.Max(0, Math.Min(2, LufsMode))];
        private string _lufsText;
        private bool _lufsQuiet = true;

        /// <summary>Zeigt dieser Zug die Lautheit? (MASTER, der Zug des Windows-Standardgeraets – oder jeder Ausgang)</summary>
        public bool ShowsLufs => LufsOn && _dev != null && (!Fixed || _dev.IsDefault || LufsEach);
        /// <summary>Misst das Standardgeraet (MASTER oder der Zug des Standardgeraets) – sonst das eigene Geraet.</summary>
        public bool UsesDefaultLoudness => !Fixed || (_dev?.IsDefault ?? false);
        /// <summary>Name fuer Menues ("MASTER" bzw. Geraetename).</summary>
        public string LufsSourceName => Fixed ? _dev?.Name ?? "Ausgang" : "MASTER";

        /// <summary>Nur Vorschau: Pegel-Menue des MASTER zeigen.</summary>
        public void ShowOutMeterMenuDemo() => Mixer.ShowOutMeterMenu(PointToScreen(new Point((int)MeterL.X - Theme.Px(150), (int)(MeterL.Y + MeterL.Height * 0.3f))));
        /// <summary>Nur Vorschau: Lautheits-Menue zeigen.</summary>
        public void ShowLufsMenuDemo() { if (ShowsLufs) Mixer.ShowLufsMenu(PointToScreen(new Point((int)_lufsRect.X, (int)_lufsRect.Bottom + 2)), this); }

        /// <summary>Aus dem UI-Takt: [Momentan, Kurzzeit, Integriert] in LUFS (null = keine Messung).</summary>
        public void SetLoudness(float[] v)
        {
            if (!ShowsLufs || Compact) { _lufsText = null; return; }
            float x = v != null && v.Length > LufsMode ? v[Math.Max(0, Math.Min(2, LufsMode))] : float.NegativeInfinity;
            _lufsVal = v != null && v.Length > 1 && !float.IsInfinity(v[1]) ? v[1] : float.NaN;   // 3.4.6: Kurzzeit fuer Ziel und Dynamik
            bool quiet = float.IsNaN(x) || float.IsInfinity(x) || x < -70f;
            string t = LufsLetters[Math.Max(0, Math.Min(2, LufsMode))] + "  " + (quiet ? "–" : x.ToString("0.0", CultureInfo.InvariantCulture).Replace("-", "\u2212")) + " LUFS";
            if (t == _lufsText && quiet == _lufsQuiet) return;
            _lufsText = t; _lufsQuiet = quiet;
            Invalidate(Rectangle.Inflate(Rectangle.Ceiling(_lufsRect), 1, 1));
        }

        protected override void PaintLiveExtra(Graphics g, Rectangle clip)
        {
            PaintSurround400(g);   // 4.0: Surround-Pegel
            PaintMeasure(g);   // 3.4.6
            if (!ShowsLufs || Compact || _lufsRect.IsEmpty || !IsDirty(Rectangle.Inflate(Rectangle.Ceiling(_lufsRect), 1, 1))) return;
            Theme.Setup(g);
            Theme.LcdColors(out var bg, out var fg);
            if (Theme.EffLcd == 0) fg = Theme.LcdText;
            Theme.DrawLcd(g, _lufsRect, "", null);
            string t = _lufsText ?? (LufsLetters[Math.Max(0, Math.Min(2, LufsMode))] + "  – LUFS");
            // 3.4.6: Lautheits-Ziel – gruen im Ziel (±1 LU), gelb lauter, sonst normal
            Color tc = _lufsQuiet ? Theme.LcdOffText() : fg;
            if (!_lufsQuiet && LufsTargetDb < 0f && !float.IsNaN(_lufsVal))
                tc = Math.Abs(_lufsVal - LufsTargetDb) <= 1f ? Theme.LedGreen : _lufsVal > LufsTargetDb + 1f ? Theme.SoloAmber : fg;
            using (var b = new SolidBrush(tc))
                Theme.Str(g, t, Theme.Mono(9.5f), b, new RectangleF(_lufsRect.X, _lufsRect.Y + Theme.F(0.5f), _lufsRect.Width, _lufsRect.Height), Theme.Center);
            if (LufsTargetDb < 0f)
            {
                // kleiner Strich unten: so weit liegt die Lautheit vom Ziel (Mitte = im Ziel, ±6 LU = Rand)
                float d = float.IsNaN(_lufsVal) || _lufsQuiet ? 0f : Math.Max(-1f, Math.Min(1f, (_lufsVal - LufsTargetDb) / 6f));
                float cx = _lufsRect.X + _lufsRect.Width / 2f, half = _lufsRect.Width / 2f - Theme.F(4);
                using (var p = new Pen(Color.FromArgb(120, tc), Theme.F(1)))
                    g.DrawLine(p, cx, _lufsRect.Bottom - Theme.F(2), cx + d * half, _lufsRect.Bottom - Theme.F(2));
            }
        }

        // ---------------------------------------------------------------- 3.4.6 Messwerte: True-Peak, Dynamik, Reserve
        public static bool TruePeakOn, DynamicsOn, HeadroomOn;
        public static int KScaleMode;
        public static float LufsTargetDb;
        protected override int KScale => KScaleMode;
        private float _lufsVal = float.NaN;
        private float _tpDb = float.NegativeInfinity, _spDb = float.NegativeInfinity;
        private readonly float[] _hrSeg = new float[30];
        private float _hrCur, _hrT;
        private int _hrPos;
        private string _measureText;
        private bool _measureHot;
        /// <summary>Zeigt dieser Zug Messwerte (statt der Apps)?</summary>
        public bool ShowsMeasure => !Compact && ((UsesDefaultLoudness && (TruePeakOn || DynamicsOn)) || HeadroomOn);
        private RectangleF MeasureRect => RectangleF.Inflate(_appsRect, -Theme.F(2), -Theme.F(2));

        /// <summary>Aus dem UI-Takt: [True-Peak, Spitze] in dB (null = nicht gemessen).</summary>
        public void SetMasterPeaks(float[] p)
        {
            _tpDb = p != null && p.Length > 0 ? p[0] : float.NegativeInfinity;
            _spDb = p != null && p.Length > 1 ? p[1] : float.NegativeInfinity;
            UpdateMeasureText();
        }

        protected override void OnPreLevel(float pre, float dt)
        {
            if (!HeadroomOn) return;
            if (pre > _hrCur) _hrCur = pre;
            _hrT += dt;
            if (_hrT >= 0.1f)   // 30 x 100 ms = 3 s
            {
                _hrT = 0f;
                _hrSeg[_hrPos] = _hrCur; _hrPos = (_hrPos + 1) % _hrSeg.Length; _hrCur = 0f;
                UpdateMeasureText();
            }
        }

        private void UpdateMeasureText()
        {
            if (!ShowsMeasure) { _measureText = null; return; }
            var parts = new List<string>();
            var ci = CultureInfo.InvariantCulture;
            bool hot = false;
            if (UsesDefaultLoudness && TruePeakOn)
            {
                bool none = float.IsNegativeInfinity(_tpDb) || _tpDb < -70f;
                parts.Add("TP " + (none ? "–" : _tpDb.ToString("0.0", ci).Replace("-", "\u2212")));
                if (!none && _tpDb > -1f) hot = true;
            }
            if (UsesDefaultLoudness && DynamicsOn)
            {
                float st = _lufsVal;
                bool none = float.IsNaN(st) || float.IsNegativeInfinity(_spDb) || st < -70f;
                parts.Add("DYN " + (none ? "–" : Math.Max(0, Math.Round(_spDb - st)).ToString("0", ci)));
            }
            if (HeadroomOn)
            {
                float pk = _hrCur; foreach (var v in _hrSeg) if (v > pk) pk = v;
                bool none = pk < 0.0001f;
                float hr = none ? 0f : -20f * (float)Math.Log10(Math.Max(1e-6f, pk));
                parts.Add("LUFT " + (none ? "–" : Math.Max(0f, hr).ToString("0.0", ci)));
                if (!none && hr < 1f) hot = true;
            }
            string t = string.Join("\n", parts.Count > 2 ? new[] { parts[0] + "  " + parts[1], parts[2] } : parts.Count == 2 && _appsRect.Height < Theme.F(30) ? new[] { parts[0] + "  " + parts[1] } : parts.ToArray());
            if (t == _measureText && hot == _measureHot) return;
            _measureText = t; _measureHot = hot;
            Invalidate(Rectangle.Inflate(Rectangle.Ceiling(MeasureRect), 1, 1));
        }

        private void PaintMeasure(Graphics g)
        {
            if (!ShowsMeasure || !IsDirty(Rectangle.Inflate(Rectangle.Ceiling(MeasureRect), 1, 1))) return;
            Theme.Setup(g);
            var r = MeasureRect;
            Theme.LcdColors(out var bg, out var fg);
            if (Theme.EffLcd == 0) fg = Theme.LcdText;
            Theme.DrawLcd(g, r, "", null);
            var lines = (_measureText ?? "–").Split('\n');
            float lh = r.Height / lines.Length;
            float pt = lines.Length > 1 ? 8f : 9f;
            using (var b = new SolidBrush(_measureHot ? Theme.MeterRed : fg))
                for (int i = 0; i < lines.Length; i++)
                    Theme.Str(g, lines[i], Theme.Mono(pt), b, new RectangleF(r.X, r.Y + i * lh, r.Width, lh), Theme.Center);
        }
        public static readonly Color LoudColor = Color.FromArgb(0x4c, 0xd9, 0x7b);

        public readonly string FixedDeviceId;

        public OutputStrip(MixerForm mixer, string fixedDeviceId = null) : base(mixer)
        {
            FixedDeviceId = fixedDeviceId;
            Meter.Output = true;      // eigene Pegel-Einstellung fuer MASTER/Ausgaenge (falls gewaehlt)
            PreMeter.Output = true;
        }

        private bool Fixed => FixedDeviceId != null;

        // 3.4.6: Ausgaenge breiter ziehen
        private string WidthKey => Fixed ? FixedDeviceId : "master";
        private int DragW => Mixer.OutWidthOf(WidthKey);
        public override int StripWidth => Theme.Px(DragW > 0 ? DragW : 118);
        protected override bool CanResizeWidth => Mixer.OutDragEnabled;
        protected override int ResizeBaseWidth => DragW > 0 ? DragW : 118;
        protected override void OnResizeWidth(int logicalPx, bool all, bool final) => Mixer.SetOutWidth(WidthKey, logicalPx, all, final);
        protected override void OnResizeReset(bool all) => Mixer.SetOutWidth(WidthKey, 0, all, true);
        protected override float FaderShift => DragW > 150 ? (DragW - 150) * 0.45f : 0f;
        protected override bool MasterCap => true;
        protected override Color AccentColor => Color.FromArgb(0xd8, 0xdc, 0xe1);
        protected override Color PanelTint => EdgeColor;
        protected override RectangleF MuteKey => _muteBtn;
        public override string DisplayName => Fixed ? (_dev?.Name ?? "AUSGANG").ToUpperInvariant() : "MASTER";
        public override string BridgeKey => Fixed ? "out:" + FixedDeviceId : "master";
        protected override RectangleF VuRect => _vuRect;
        protected override bool MuteLit => _dev != null && _dev.Mute;
        private Color EdgeColor => Fixed && !(_dev?.IsDefault ?? false) ? Color.FromArgb(0x9a, 0xa1, 0xab) : Theme.Accent;
        protected override float MeterBarWidth => DragW > 140 ? Math.Min(11f, 7f + (DragW - 140) / 30f) : 7f;
        protected override bool Dimmed => _dev != null && _dev.Mute;
        protected override Color MeterTint => Theme.Accent;

        private bool LocalActive => _localVol >= 0 && _localUntil.IsRunning && _localUntil.ElapsedMilliseconds < 700;

        /// <summary>
        /// 3.4.3: Ausgaenge in dB wie am Mischpult (Standard aus): Skala 0 / −5 / −10 … −60 / ∞ wie bei den Kanaelen, Anzeige in dB,
        /// der Fader stellt die Geraete-Lautstaerke direkt in dB ein (0 dB oben = volle Lautstaerke des Geraets).
        /// </summary>
        public static bool DbMode;

        /// <summary>Faderstellung zu einer Geraete-Lautstaerke (dB-Modus).</summary>
        internal static float DbPos(DeviceState d)
        {
            if (d == null || d.Volume <= 0.0005f || d.VolumeDb <= d.MinDb + 0.01f) return 0f;
            return FaderLaw.DbToPos(Math.Min(0f, d.VolumeDb));
        }

        internal static string OutDbText(float db) =>
            float.IsNegativeInfinity(db) ? "-∞ dB" : db > -0.05f ? "0.0 dB" : db.ToString("0.0", CultureInfo.InvariantCulture) + " dB";

        protected override float FaderPos => LocalActive ? _localVol : DbMode ? DbPos(_dev) : _dev?.Volume ?? 0f;
        protected override bool DbFader => DbMode;
        protected override void OnMiddleClick()   // 3.5: Mittelklick = stumm
        {
            if (_dev == null) return;
            bool m = !_dev.Mute;
            _dev.Mute = m;
            Mixer.Backend.SetDeviceMute(DeviceId, m);
        }

        protected override string ReadoutText =>
            _dev == null ? "--"
            : DbMode ? (LocalActive ? FaderLaw.FormatDb(_localVol) : DbPos(_dev) <= 0f ? "-∞ dB" : OutDbText(_dev.VolumeDb))
            : ((int)Math.Round(FaderPos * 100)).ToString(CultureInfo.InvariantCulture) + " %";

        protected override string FaderTipSuffix =>
            _dev == null ? ""
            : DbMode ? "  ·  Windows: " + ((int)Math.Round(_dev.Volume * 100)).ToString(CultureInfo.InvariantCulture) + " %"
            : "  ·  " + _dev.VolumeDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";

        protected override IEnumerable<KeyValuePair<float, string>> ScaleMarks
        {
            get
            {
                if (DbMode)
                {
                    yield return new KeyValuePair<float, string>(1f, "0");
                    foreach (var db in new[] { -5f, -10f, -20f, -30f, -40f, -60f })
                        yield return new KeyValuePair<float, string>(FaderLaw.DbToPos(db), db.ToString(CultureInfo.InvariantCulture));
                    yield return new KeyValuePair<float, string>(0f, "∞");
                    yield break;
                }
                yield return new KeyValuePair<float, string>(1f, "100");
                yield return new KeyValuePair<float, string>(0.75f, "75");
                yield return new KeyValuePair<float, string>(0.5f, "50");
                yield return new KeyValuePair<float, string>(0.25f, "25");
                yield return new KeyValuePair<float, string>(0f, "0");
            }
        }

        /// <summary>dev = aktuelles Standardgeraet (oder null, wenn keins da ist).</summary>
        public void SetDevice(DeviceState dev, List<AppView> apps, bool structural)
        {
            bool changed = structural || (dev == null) != (_dev == null) ||
                (dev != null && (dev.Id != _dev.Id || dev.Mute != _dev.Mute || dev.Name != _dev.Name ||
                                 Math.Abs(dev.Volume - _dev.Volume) > 0.0005f));
            if (dev != null && DeviceId != null && dev.Id != DeviceId) _localVol = -1f;
            _dev = dev;
            DeviceId = dev?.Id;
            if (structural && apps != null) _apps = apps;
            if (!LocalActive && _localVol >= 0) { _localVol = -1f; changed = true; }
            if (changed) InvalidateChrome();
        }

        protected override void OnFaderMoved(float pos, bool final)
        {
            if (DeviceId == null) return;
            _localVol = pos;
            _localUntil.Restart();
            if (DbMode)
            {
                // 3.4.3: in dB; ganz unten bzw. unter dem kleinsten Wert des Geraets = aus (0 %)
                float db = FaderLaw.PosToDb(pos);
                if (pos <= FaderLaw.MinPos || float.IsNegativeInfinity(db) || (_dev != null && db <= _dev.MinDb)) Mixer.Backend.SetDeviceVolume(DeviceId, 0f);
                else Mixer.Backend.SetDeviceVolumeDb(DeviceId, db);
                return;
            }
            Mixer.Backend.SetDeviceVolume(DeviceId, pos);
        }

        protected override void LayoutTop(float w)
        {
            float x = Theme.F(8), iw = w - Theme.F(17);
            _nameRect = new RectangleF(x, Theme.F(12), iw, Theme.F(24));
            _infoRect = new RectangleF(x, Theme.F(42), iw, Theme.F(44));
            _devBtn = new RectangleF(x, Theme.F(90), iw, Theme.F(20));
            // Mit VU-Meter: Apps in einer Reihe, GEHÖRSCHUTZ rueckt hoch – so bekommt das Instrument mehr Hoehe
            _appsRect = new RectangleF(x, Theme.F(138), iw, Theme.F(VuOn ? 26 : 44));
            _lufsRect = new RectangleF(x, Theme.F(119), iw, Theme.F(16));   // statt "SPIELT GERADE", wenn die Lautheit hier steht
            _capBtn = new RectangleF(x, Theme.F(VuOn ? 174 : 193), iw, Theme.F(17));
            _monoBtn = RectangleF.Empty;
            if (MonoShown)
            {
                // 3.4: GEHÖRSCHUTZ | MONO
                float mw = (float)Math.Round(iw * 0.36f), gap = Theme.F(3);
                _monoBtn = new RectangleF(x + iw - mw, _capBtn.Y, mw, _capBtn.Height);
                _capBtn = new RectangleF(x, _capBtn.Y, iw - mw - gap, _capBtn.Height);
            }
            // DIM | LOUD | MUTE – wie am Studio-Monitor-Controller
            {
                float ky = Theme.F(237 + DynRow + RowOffset), gap = Theme.F(3), avail = iw - 2 * gap;
                float dw = (float)Math.Round(avail * 0.29f), lw = (float)Math.Round(avail * 0.33f), mw = avail - dw - lw;
                _dimBtn = new RectangleF(x, ky, dw, Theme.F(22));
                _loudBtn = new RectangleF(x + dw + gap, ky, lw, Theme.F(22));
                _muteBtn = new RectangleF(x + dw + lw + 2 * gap, ky, mw, Theme.F(22));
            }
            // VU-Meter zwischen GEHÖRSCHUTZ und MUTE
            float vt = (float)Math.Round(Theme.F(198)), vb = (float)Math.Round(Theme.F(230 + DynRow + RowOffset));
            _vuRect = new RectangleF((float)Math.Round(x + Theme.F(1)), vt, (float)Math.Round(iw - Theme.F(2)), vb - vt);
            AddZone(_nameRect, "device");
            AddZone(_infoRect, "device");
            AddZone(_devBtn, "device");
            AddZone(_appsRect, "apps", false);
            if (LufsOn) AddZone(_lufsRect, "lufs");
            AddZone(_capBtn, "cap");
            if (!_monoBtn.IsEmpty) AddZone(_monoBtn, "mono");
            AddZone(_dimBtn, "dim");
            AddZone(_loudBtn, "loud");
            AddZone(_muteBtn, "mute");
            if (VuOn) AddZone(_vuRect, "vu");
        }

        protected override void DrawTop(Graphics g)
        {
            var dev = _dev;
            float left = PanelRect.X + Theme.F(6), right = PanelRect.Right - Theme.F(6);
            Theme.DrawColorEdge(g, PanelRect, EdgeColor);

            // Name
            var nr = _nameRect;
            Theme.DrawPlateRim(g, nr);
            Color bg = Dimmed ? Color.FromArgb(0x6a, 0x6f, 0x76) : Color.FromArgb(0xdb, 0xdf, 0xe4);
            Theme.DrawNamePlate(g, nr, bg);
            string title = Fixed ? (dev?.Name ?? "AUSGANG").ToUpperInvariant() : "MASTER";
            var ntr = NameTextRect(nr);
            float nfw = NameFitWidth(nr);
            if (ShowsPhase) { float pw = (nr.Height - Theme.F(6)) * 1.35f + Theme.F(5); ntr.Width -= pw; nfw -= pw; }   // 3.5: Platz fuer das Phasen-Zeichen
            using (var b = new SolidBrush(Theme.PlateText(bg)))
                Theme.Str(g, title, Theme.FitPlate(g, title, nfw, 12.5f, 8f), b, ntr, Theme.Center);
            DrawPhase350(g, nr);   // 3.5: Phasen-Warnung

            // Geraete-Info (LCD-artig)
            var ir = _infoRect;
            bool hoverDev = HoverZone == "device";
            Theme.DrawWell(g, ir, hoverDev);
            if (dev != null)
            {
                string sub = dev.FullName ?? "";
                int p = sub.IndexOf('(');
                if (p > 0 && sub.EndsWith(")")) sub = sub.Substring(p + 1, sub.Length - p - 2);
                sub = DeviceSubText(sub);   // 3.5: Format des Geraets
                float isp = DeviceIconSpace;   // 3.5: Geraete-Bild
                DrawDeviceIcon350(g, ir);
                using (var b = new SolidBrush(Theme.Text))
                    Theme.Str(g, dev.Name, Theme.Fit(g, dev.Name, ir.Width - Theme.F(8) - isp, 11.5f, 8.5f), b,
                        new RectangleF(ir.X + Theme.F(3) + isp, ir.Y + Theme.F(4), ir.Width - Theme.F(6) - isp, Theme.F(18)), Theme.Center);
                using (var b = new SolidBrush(Theme.TextDim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    Theme.Str(g, sub, Theme.Ui(9f), b, new RectangleF(ir.X + Theme.F(3), ir.Y + Theme.F(23), ir.Width - Theme.F(6), Theme.F(16)), sf);
            }
            else
            {
                using (var b = new SolidBrush(Theme.MuteRed))
                    Theme.Str(g, "Kein Gerät", Theme.Ui(10f), b, ir, Theme.Center);
            }

            string btn = !Fixed ? "GERÄT WÄHLEN" : (dev != null && dev.IsDefault ? "STANDARD ✓" : "ALS STANDARD");
            Theme.DrawDisplayButton(g, _devBtn, btn, hoverDev, Fixed && dev != null && dev.IsDefault ? Theme.OkText : Theme.Text);

            Theme.Engrave(g, left, right, Theme.F(116));

            // Apps, die gerade spielen
            if (ShowsLufs) Theme.FillRound(g, Theme.LcdBg, _lufsRect, Theme.F(3));   // Platzhalter – die Anzeige kommt live darueber
            else Theme.Label(g, "SPIELT GERADE", new RectangleF(_appsRect.X, Theme.F(121), _appsRect.Width, Theme.F(14)), Theme.Center, Theme.TextFaint, 8.5f);
            var r = _appsRect;
            Theme.DrawWell(g, r, false);
            if (ShowsMeasure) Theme.FillRound(g, Theme.LcdBg, MeasureRect, Theme.F(3));   // 3.4.6: Messwerte statt Apps (live darueber)
            else if (_apps.Count == 0)
            {
                using (var b = new SolidBrush(Theme.TextFaint))
                    Theme.Str(g, "Stille", Theme.Ui(9.5f), b, r, Theme.Center);
            }
            else
            {
                int size = Theme.Px(20), gap = Theme.Px(4);
                int cols = Math.Max(1, (int)((r.Width - Theme.F(4) + gap) / (size + gap)));
                int rows = r.Height >= size * 2 + gap + Theme.F(2) ? 2 : 1;
                int max = cols * rows;
                float totalW = cols * size + (cols - 1) * gap;
                float x0 = r.X + (r.Width - totalW) / 2, y0 = r.Y + (r.Height - (size * rows + gap * (rows - 1))) / 2;
                int shown = _apps.Count > max ? max - 1 : _apps.Count;
                for (int i = 0; i < shown; i++)
                {
                    var a = _apps[i];
                    var e = Mixer.Icons.Get(a.Key, a.Name, a.Path);
                    g.DrawImage(e.Normal, new RectangleF(x0 + (i % cols) * (size + gap), y0 + (i / cols) * (size + gap), size, size));
                }
                if (_apps.Count > max)
                {
                    int i = max - 1;
                    var rr = new RectangleF(x0 + (i % cols) * (size + gap), y0 + (i / cols) * (size + gap), size, size);
                    Theme.FillRound(g, Theme.Chip, rr, Theme.F(4));
                    Theme.Label(g, "+" + (_apps.Count - shown), rr, Theme.Center, Theme.Text, 9f);
                }
            }

            Theme.Engrave(g, left, right, Theme.F(VuOn ? 169 : 188));
            string capLabel = !_monoBtn.IsEmpty && Theme.TextWidth(g, "GEHÖRSCHUTZ", Theme.UiSemi(8f)) > _capBtn.Width - Theme.F(4) ? "SCHUTZ" : "GEHÖRSCHUTZ";
            Theme.DrawKey(g, _capBtn, capLabel, Mixer.HearingCapDb < 0, Theme.SoloAmber, HoverZone == "cap");
            if (!_monoBtn.IsEmpty) Theme.DrawKey(g, _monoBtn, "MONO", Mixer.MasterMono, Color.FromArgb(0x5a, 0xb4, 0xff), HoverZone == "mono");   // 3.4
            if (!VuOn) Theme.Engrave(g, left, right, Theme.F(188 + DynRow));
            if (!VuOn)
            {
                var font = Theme.UiSemi(12f);
                float tw = Theme.MeasureSpaced(g, "OUTPUT", font, Theme.F(3));
                Theme.DrawSpaced(g, "OUTPUT", font, Theme.EngraveText, PanelRect.X + (PanelRect.Width - tw) / 2, Theme.F(202 + DynRow + RowOffset / 2), Theme.F(3));
                Theme.Engrave(g, left, right, Theme.F(232 + DynRow + RowOffset));
            }
            Theme.DrawKey(g, _dimBtn, "DIM", Mixer.DimOn, Theme.SoloAmber, HoverZone == "dim");
            Theme.DrawKey(g, _loudBtn, "LOUD", Mixer.LoudOn, LoudColor, HoverZone == "loud", Mixer.LoudUnavailable);
            DrawMuteKey(g, _muteBtn, dev != null && dev.Mute);
            Theme.Engrave(g, left, right, Theme.F(TopSection - 3));
        }

        protected override bool OnZoneMouseDown(string zone, MouseEventArgs e)
        {
            if (zone == "dim")
            {
                if (e.Button == MouseButtons.Right) Mixer.ShowDimMenu(PointToScreen(new Point((int)_dimBtn.X, (int)_dimBtn.Bottom + 2)));
                else if (e.Button == MouseButtons.Left) Mixer.ToggleDim(false);
                return false;
            }
            if (zone == "loud")
            {
                if (e.Button == MouseButtons.Right) Mixer.ShowLoudMenu(PointToScreen(new Point((int)_loudBtn.X, (int)_loudBtn.Bottom + 2)));
                else if (e.Button == MouseButtons.Left) Mixer.ToggleLoud(false);
                return false;
            }
            if (zone == "vu" && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                Mixer.ShowVuMenu(PointToScreen(new Point((int)_vuRect.X, (int)_vuRect.Bottom + 4)));
                return false;
            }
            if (zone == "lufs" && ShowsLufs && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                Mixer.ShowLufsMenu(PointToScreen(new Point((int)_lufsRect.X, (int)_lufsRect.Bottom + 2)), this);
                return false;
            }
            if (zone == "mono" && e.Button == MouseButtons.Left)
            {
                Mixer.ToggleMasterMono();   // 3.4
                InvalidateChrome();
                return false;
            }
            if (zone == "cap" && e.Button == MouseButtons.Left)
            {
                Mixer.ShowHearingMenu(PointToScreen(new Point((int)_capBtn.X, (int)_capBtn.Bottom + 2)));
                return false;
            }
            if (e.Button == MouseButtons.Right && RectangleF.Inflate(RectangleF.Union(MeterL, MeterR), Theme.F(4), Theme.F(4)).Contains(e.Location))
            {
                Mixer.ShowOutMeterMenu(PointToScreen(e.Location));   // Rechtsklick auf den Pegel: Ruecklauf, Spitze, Pegel-Art fuer MASTER/Ausgaenge
                return false;
            }
            if (Fixed && (e.Button == MouseButtons.Right || (zone == "device" && e.Button == MouseButtons.Left)))
            {
                Mixer.ShowOutputMenu(this, PointToScreen(zone == "device" ? new Point((int)_devBtn.X, (int)_devBtn.Bottom + 2) : e.Location));
                return false;
            }
            if (zone == "device" && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                Mixer.ShowDeviceMenu(PointToScreen(new Point((int)_devBtn.X, (int)_devBtn.Bottom + 2)));
                return false;
            }
            if (zone == "mute" && e.Button == MouseButtons.Left && _dev != null)
            {
                bool m = !_dev.Mute;
                _dev.Mute = m; // sofort anzeigen
                Mixer.Backend.SetDeviceMute(DeviceId, m);
                InvalidateChrome();
            }
            return false;
        }

        /// <summary>Nur Demo/Vorschau: Menue wie beim Klick aufs VU-Meter.</summary>
        public void ShowVuMenuDemo()
        {
            if (VuOn) Mixer.ShowVuMenu(PointToScreen(new Point((int)_vuRect.X, (int)_vuRect.Bottom + 4)));
        }

        protected override string GetTooltip(string zone)
        {
            switch (zone)
            {
                case "device":
                    if (Fixed)
                        return (_dev?.FullName ?? "") + DeviceKindTip + (_dev != null && _dev.IsDefault ? "\nWindows-Standardgerät" : "\nKlicken: als Standard setzen oder ausblenden") + PhaseTip;
                    return (_dev?.FullName ?? "Kein Ausgabegerät") + DeviceKindTip + "\nKlicken, um das Windows-Ausgabegerät zu wechseln" + PhaseTip;
                case "apps":
                    if (_apps.Count == 0) return "Gerade spielt keine App.";
                    var names = new List<string>();
                    foreach (var a in _apps) names.Add(a.Name);
                    return "Spielt gerade:\n" + string.Join("\n", names);
                case "mute": return "Ausgang stummschalten (wie in Windows)";
                case "lufs":
                    if (!ShowsLufs) return null;
                    return (Fixed && !UsesDefaultLoudness ? "Lautheit von „" + (_dev?.Name ?? "") + "“ in LUFS – wie laut es sich anfühlt\n" : "Lautheit in LUFS – wie laut es sich anfühlt (wie bei Radio, YouTube, Spotify)\n") +
                           "Spotify/YouTube spielen um −14 LUFS, Fernsehen um −23 LUFS · höher = lauter\n" +
                           "Gerade: " + LufsModeNames[Math.Max(0, Math.Min(2, LufsMode))] + " · Klick: umschalten, Integriert neu beginnen";
                case "dim":
                    return (Mixer.DimOn ? "DIM an: alles " : "DIM: alles auf Knopfdruck ") + "um " + ((int)Math.Round(Mixer.DimDb)).ToString(CultureInfo.InvariantCulture) +
                           " dB leiser – nochmal klicken = wie vorher\nDie Fader bleiben, wo sie sind · Rechtsklick: wie viel leiser";
                case "loud":
                {
                    string now = Mixer.LoudOn ? "\nGerade: " + Mixer.LoudNowText(DeviceId) : Mixer.LoudUnavailable ? "\nAuf diesem Windows nicht verfügbar (braucht Windows 10 Version 2004 oder neuer)" : "";
                    return "Loudness " + (Mixer.LoudOn ? "an" : "aus") + ": bei leisem Hören Bass und Höhen anheben – wie bei alten Hi-Fi-Anlagen" + now +
                           "\nLäuft wie der EQ durch das Mischpult · Rechtsklick: Stärke";
                }
                case "vu": return "VU-Meter: zeigt die Lautstärke wie ein altes Zeigerinstrument –\nträge, so wie man es hört (Rot = sehr laut)\n" +
                        (Theme.VuDbScale ? "Skala in dB: von " + DbText(VuStartDb) + " bis 0 dB, rot ab " + DbText(Theme.VuRedDb)
                                         : "Reagiert ab " + DbText(VuStartDb) + ", die 0 steht für " + DbText(VuStartDb + 20f)) +
                        "\nKlicken: einstellen, ab wann der Zeiger reagiert (für leisen Ton)";
                case "mono": return Mixer.MasterMono ? "MONO ist an – beide Seiten gleich (zum Gegenhören). Klick: wieder Stereo" : "MONO: kurz in Mono gegenhören (z. B. ob etwas nur auf einer Seite spielt)";
                case "cap": return Mixer.HearingCapDb < 0
                        ? "Gehörschutz an: höchstens " + Mixer.HearingCapDb.ToString("0", CultureInfo.InvariantCulture) + " dB – fängt plötzlich laute Geräusche ab\nKlicken: Stufe ändern"
                        : "Gehörschutz aus – klicken, um ihn einzuschalten\n(fängt plötzlich laute Geräusche ab)";
            }
            return null;
        }
    }
}
